using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Platform.Windows.Com;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Draws color glyphs with Direct2D into a CPU bitmap (WIC, premultiplied BGRA), as the apps of Windows draw them (design §4.6,
/// §5.0.3, "Color glyphs", roadmap row T6): every format DirectWrite splits a color glyph into, COLRv1 paint trees included.
/// </summary>
/// <remarks>
/// <para>
/// The runs come from <c>IDWriteFactory8::TranslateColorGlyphRun</c> with the COLRv1 paint feature level when Direct2D can draw
/// paint trees (<c>ID2D1DeviceContext7::GetPaintFeatureLevel</c>), and from <c>IDWriteFactory4::TranslateColorGlyphRun</c>
/// without paint trees otherwise, which gives the COLRv0 layers. Skia reads COLRv1 with <c>IDWriteFontFace7::CreatePaintReader</c>
/// and draws the tree itself; GPUI draws COLRv0 only (Zed v1.24.3, <c>crates/gpui_windows/src/direct_write.rs</c>, lines 938 to
/// 955).
/// </para>
/// <para>
/// The WIC factory needs COM on the calling thread; the platform initializes OLE on the UI thread (design §5.0.3). A thread of a
/// .NET process that did not initialize COM is in the implicit multithreaded apartment, since the process has one, and makes the
/// factory too (<c>ColorGlyphTests</c>). The renderer is
/// made the first time a color glyph is asked for, belongs to the thread of its rasterizer, and keeps one bitmap, grown to the
/// largest glyph so far.
/// </para>
/// </remarks>
internal sealed unsafe class ColorGlyphRenderer : IDisposable
{
    /// <summary>
    /// The formats a color glyph is split into, but COLRv1 paint trees: outlines, COLRv0 layers, SVG, and bitmaps.
    /// </summary>
    /// <remarks>
    /// A run's format can hold several flags: DirectWrite gives a COLRv0 layer as <c>TRUETYPE | COLR</c> (Windows 11 26300, Segoe
    /// UI Emoji), and GPUI takes the TrueType flag off before it compares (<c>direct_write.rs</c>, line 954). <see cref="KindOf"/>
    /// reads the flags.
    /// </remarks>
    internal const DWRITE_GLYPH_IMAGE_FORMATS Formats =
        DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TRUETYPE
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_CFF
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8;

    private IDWriteFactory4* _factory4;
    private IDWriteFactory8* _factory8;
    private ID2D1Factory1* _d2d;
    private IWICImagingFactory* _wic;
    private IWICBitmap* _bitmap;
    private ID2D1RenderTarget* _target;
    private ID2D1DeviceContext4* _context4;
    private ID2D1DeviceContext7* _context7;
    private ID2D1SolidColorBrush* _brush;
    private int _side;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorGlyphRenderer"/> class.
    /// </summary>
    /// <param name="factory">The rasterizer's DirectWrite factory; the renderer adds a reference.</param>
    /// <exception cref="InvalidOperationException">Direct2D or WIC could not create their factories.</exception>
    public ColorGlyphRenderer(IDWriteFactory5* factory)
    {
        try
        {
            IDWriteFactory4* factory4;
            Guid iid4 = IID.IID_IDWriteFactory4;
            ThrowIfFailed(factory->QueryInterface(&iid4, (void**)&factory4), "IDWriteFactory5::QueryInterface(IDWriteFactory4)");
            _factory4 = factory4;
            IDWriteFactory8* factory8;
            Guid iid8 = IID.IID_IDWriteFactory8;
            _factory8 = factory->QueryInterface(&iid8, (void**)&factory8) >= 0 ? factory8 : null;

            ID2D1Factory1* d2d;
            Guid d2dId = IID.IID_ID2D1Factory1;
            ThrowIfFailed(D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, &d2dId, null, (void**)&d2d), "D2D1CreateFactory");
            _d2d = d2d;

            IWICImagingFactory* wic;
            Guid wicClass = CLSID.CLSID_WICImagingFactory;
            Guid wicId = IID.IID_IWICImagingFactory;
            ThrowIfFailed(
                CoCreateInstance(&wicClass, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &wicId, (void**)&wic),
                "CoCreateInstance(CLSID_WICImagingFactory)"
            );
            _wic = wic;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether COLRv1 paint trees are drawn where Direct2D can; <see langword="false"/> takes the
    /// COLRv0 layers, the path of a Windows without COLRv1, for its tests.
    /// </summary>
    public bool UsePaintTrees { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether the last <see cref="Draw"/> drew a COLRv1 paint tree.
    /// </summary>
    public bool DrewPaintTree { get; private set; }

    /// <summary>
    /// Draws a glyph in its colors on a whole pixel and returns the box of its ink; <see cref="Copy"/> reads the pixels.
    /// </summary>
    /// <param name="face">The glyph's font face.</param>
    /// <param name="key">The glyph; its <see cref="GlyphKey.TextColor"/> paints the layers in the text's color, or black.</param>
    /// <param name="box">The box of the ink, from the pen on the baseline.</param>
    public void Draw(IDWriteFontFace3* face, in GlyphKey key, out GlyphBox box)
    {
        // A square that holds a glyph from one em left of the pen to two right, and two above the baseline to one below.
        int side = (int)MathF.Ceiling(key.PixelSize * 3) + 4;
        EnsureBitmap(side);
        int originX = _side / 3;
        int originY = _side * 2 / 3;

        ushort index = checked((ushort)key.GlyphId);
        float advance = 0;
        DWRITE_GLYPH_OFFSET offset = default;
        DWRITE_GLYPH_RUN run = RunOf(face, &index, &advance, &offset, key.PixelSize);
        uint text = key.TextColor;
        var textColor = new DXGI_RGBA
        {
            r = (text & 0xFF) / 255f,
            g = ((text >> 8) & 0xFF) / 255f,
            b = ((text >> 16) & 0xFF) / 255f,
            a = 1,
        };

        DrewPaintTree = false;
        _target->BeginDraw();
        var transparent = default(DXGI_RGBA);
        _target->Clear(&transparent);
        IDWriteColorGlyphRunEnumerator1* runs = Translate(&run, new D2D_POINT_2F(originX, originY), out bool paintTrees);
        if (runs is null)
        {
            _brush->SetColor(&textColor);
            _target->DrawGlyphRun(new D2D_POINT_2F(originX, originY), &run, (ID2D1Brush*)_brush, DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
        }
        else
        {
            try
            {
                DrawRuns(runs, paintTrees, textColor);
            }
            finally
            {
                _ = runs->Release();
            }
        }

        ThrowIfFailed(_target->EndDraw(null, null), "ID2D1RenderTarget::EndDraw");
        box = InkBox(originX, originY);
    }

    /// <summary>
    /// Copies the box <see cref="Draw"/> returned, premultiplied BGRA, into <paramref name="destination"/>, rows
    /// <paramref name="stride"/> bytes apart.
    /// </summary>
    /// <param name="box">The box from <see cref="Draw"/>.</param>
    /// <param name="destination">The room for the box.</param>
    /// <param name="stride">The bytes from one row to the next.</param>
    public void Copy(in GlyphBox box, Span<byte> destination, int stride)
    {
        int originX = _side / 3;
        int originY = _side * 2 / 3;
        var rect = new WICRect
        {
            X = 0,
            Y = 0,
            Width = _side,
            Height = _side,
        };
        IWICBitmapLock* bitmapLock;
        ThrowIfFailed(_bitmap->Lock(&rect, (uint)WICBitmapLockFlags.WICBitmapLockRead, &bitmapLock), "IWICBitmap::Lock");
        try
        {
            uint lockStride;
            uint size;
            byte* pixels;
            ThrowIfFailed(bitmapLock->GetStride(&lockStride), "IWICBitmapLock::GetStride");
            ThrowIfFailed(bitmapLock->GetDataPointer(&size, &pixels), "IWICBitmapLock::GetDataPointer");
            int left = originX + box.Left;
            int top = originY - box.Top;
            for (int row = 0; row < box.Height; row++)
            {
                new ReadOnlySpan<byte>(pixels + ((long)(top + row) * lockStride) + (left * 4), box.Width * 4).CopyTo(
                    destination.Slice(row * stride, box.Width * 4)
                );
            }
        }
        finally
        {
            _ = bitmapLock->Release();
        }
    }

    /// <summary>
    /// Releases the bitmap, the render target, and the factories.
    /// </summary>
    public void Dispose()
    {
        ReleaseTarget();
        Release(ref _wic);
        Release(ref _d2d);
        Release(ref _factory8);
        Release(ref _factory4);
    }

    private static DWRITE_GLYPH_RUN RunOf(IDWriteFontFace3* face, ushort* index, float* advance, DWRITE_GLYPH_OFFSET* offset, float pixelSize) =>
        new()
        {
            fontFace = (IDWriteFontFace*)face,
            fontEmSize = pixelSize,
            glyphCount = 1,
            glyphIndices = index,
            glyphAdvances = advance,
            glyphOffsets = offset,
            isSideways = BOOL.FALSE,
            bidiLevel = 0,
        };

    // The color runs of the glyph run, or null for a glyph with no color (DWRITE_E_NOCOLOR). paintTrees says whether they may hold
    // COLRv1 paint trees.
    private IDWriteColorGlyphRunEnumerator1* Translate(DWRITE_GLYPH_RUN* run, D2D_POINT_2F origin, out bool paintTrees)
    {
        IDWriteColorGlyphRunEnumerator1* runs;
        HRESULT result;
        paintTrees = UsePaintTrees && _factory8 is not null && CanDrawPaintTrees();
        if (paintTrees)
        {
            result = _factory8->TranslateColorGlyphRun(
                origin,
                run,
                null,
                Formats | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE,
                DWRITE_PAINT_FEATURE_LEVEL.DWRITE_PAINT_FEATURE_LEVEL_COLR_V1,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                null,
                0,
                &runs
            );
        }
        else
        {
            result = _factory4->TranslateColorGlyphRun(
                origin,
                run,
                null,
                Formats,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                null,
                0,
                &runs
            );
        }

        if (result == DWRITE.DWRITE_E_NOCOLOR)
        {
            return null;
        }

        ThrowIfFailed(result, "IDWriteFactory::TranslateColorGlyphRun");
        return runs;
    }

    // Direct2D draws COLRv1 when its device context has the interface and the feature level. The bitmap target is made first.
    private bool CanDrawPaintTrees()
    {
        EnsureBitmap(64);
        return _context7 is not null && _context7->GetPaintFeatureLevel() >= DWRITE_PAINT_FEATURE_LEVEL.DWRITE_PAINT_FEATURE_LEVEL_COLR_V1;
    }

    // Each run with the call that draws its format.
    private void DrawRuns(IDWriteColorGlyphRunEnumerator1* runs, bool paintTrees, DXGI_RGBA textColor)
    {
        BOOL more;
        while (runs->MoveNext(&more) >= 0 && more)
        {
            DWRITE_COLOR_GLYPH_RUN1* color;
            ThrowIfFailed(runs->GetCurrentRun(&color), "IDWriteColorGlyphRunEnumerator1::GetCurrentRun");
            var origin = new D2D_POINT_2F(color->Base.baselineOriginX, color->Base.baselineOriginY);
            DWRITE_GLYPH_RUN* glyphRun = &color->Base.glyphRun;
            DWRITE_MEASURING_MODE measuring = color->measuringMode;
            switch (KindOf(color->glyphImageFormat))
            {
                case DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE when paintTrees:
                    _brush->SetColor(&textColor);
                    _context7->DrawPaintGlyphRun(origin, glyphRun, (ID2D1Brush*)_brush, 0, measuring);
                    DrewPaintTree = true;
                    break;
                case DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR:
                    DXGI_RGBA layer = color->Base.paletteIndex == 0xFFFF ? textColor : color->Base.runColor;
                    _brush->SetColor(&layer);
                    _target->DrawGlyphRun(origin, glyphRun, (ID2D1Brush*)_brush, measuring);
                    break;
                case DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG:
                    _brush->SetColor(&textColor);
                    _context4->DrawSvgGlyphRun(origin, glyphRun, (ID2D1Brush*)_brush, null, 0, measuring);
                    break;
                case DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
                or DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG
                or DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF
                or DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8:
                    _context4->DrawColorBitmapGlyphRun(
                        KindOf(color->glyphImageFormat),
                        origin,
                        glyphRun,
                        measuring,
                        D2D1_COLOR_BITMAP_GLYPH_SNAP_OPTION.D2D1_COLOR_BITMAP_GLYPH_SNAP_OPTION_DEFAULT
                    );
                    break;
                default:
                    // An outline (TrueType or CFF): the parts of the glyph that have no color, in the text's color.
                    _brush->SetColor(&textColor);
                    _target->DrawGlyphRun(origin, glyphRun, (ID2D1Brush*)_brush, measuring);
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the one format a color run is drawn as: a paint tree, a COLRv0 layer, SVG, or a bitmap format, before an outline
    /// flag that comes with it; <c>TRUETYPE</c> for an outline alone.
    /// </summary>
    /// <param name="format">The run's format flags.</param>
    /// <returns>The format to draw.</returns>
    internal static DWRITE_GLYPH_IMAGE_FORMATS KindOf(DWRITE_GLYPH_IMAGE_FORMATS format)
    {
        ReadOnlySpan<DWRITE_GLYPH_IMAGE_FORMATS> order =
        [
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE,
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR,
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG,
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG,
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG,
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF,
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8,
        ];
        foreach (DWRITE_GLYPH_IMAGE_FORMATS kind in order)
        {
            if ((format & kind) != 0)
            {
                return kind;
            }
        }

        return DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TRUETYPE;
    }

    // The box of the pixels with any alpha, from the origin.
    private GlyphBox InkBox(int originX, int originY)
    {
        var rect = new WICRect
        {
            X = 0,
            Y = 0,
            Width = _side,
            Height = _side,
        };
        IWICBitmapLock* bitmapLock;
        ThrowIfFailed(_bitmap->Lock(&rect, (uint)WICBitmapLockFlags.WICBitmapLockRead, &bitmapLock), "IWICBitmap::Lock");
        try
        {
            uint stride;
            uint size;
            byte* pixels;
            ThrowIfFailed(bitmapLock->GetStride(&stride), "IWICBitmapLock::GetStride");
            ThrowIfFailed(bitmapLock->GetDataPointer(&size, &pixels), "IWICBitmapLock::GetDataPointer");
            int left = _side;
            int right = -1;
            int top = _side;
            int bottom = -1;
            for (int y = 0; y < _side; y++)
            {
                byte* row = pixels + ((long)y * stride);
                for (int x = 0; x < _side; x++)
                {
                    if (row[(x * 4) + 3] != 0)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            return right < 0
                ? new GlyphBox(0, 0, 0, 0, IsColor: true)
                : new GlyphBox(left - originX, originY - top, right - left + 1, bottom - top + 1, IsColor: true);
        }
        finally
        {
            _ = bitmapLock->Release();
        }
    }

    // One bitmap of at least `side` pixels square, with its render target, its device contexts, and a brush.
    private void EnsureBitmap(int side)
    {
        if (_bitmap is not null && side <= _side)
        {
            return;
        }

        ReleaseTarget();
        side = Math.Max(side, _side);
        IWICBitmap* bitmap;
        Guid format = GUID.GUID_WICPixelFormat32bppPBGRA;
        ThrowIfFailed(
            _wic->CreateBitmap((uint)side, (uint)side, &format, WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, &bitmap),
            "IWICImagingFactory::CreateBitmap"
        );
        _bitmap = bitmap;
        _side = side;
        var properties = new D2D1_RENDER_TARGET_PROPERTIES
        {
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
            },
            dpiX = 96,
            dpiY = 96,
        };
        ID2D1RenderTarget* target;
        ThrowIfFailed(((ID2D1Factory*)_d2d)->CreateWicBitmapRenderTarget(_bitmap, &properties, &target), "ID2D1Factory::CreateWicBitmapRenderTarget");
        _target = target;
        _target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE.D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        ID2D1DeviceContext4* context4;
        Guid id4 = IID.IID_ID2D1DeviceContext4;
        ThrowIfFailed(_target->QueryInterface(&id4, (void**)&context4), "ID2D1RenderTarget::QueryInterface(ID2D1DeviceContext4)");
        _context4 = context4;
        ID2D1DeviceContext7* context7;
        Guid id7 = IID.IID_ID2D1DeviceContext7;
        _context7 = _target->QueryInterface(&id7, (void**)&context7) >= 0 ? context7 : null;
        ID2D1SolidColorBrush* brush;
        var black = new DXGI_RGBA { a = 1 };
        ThrowIfFailed(_target->CreateSolidColorBrush(&black, null, &brush), "ID2D1RenderTarget::CreateSolidColorBrush");
        _brush = brush;
    }

    private void ReleaseTarget()
    {
        Release(ref _brush);
        Release(ref _context7);
        Release(ref _context4);
        Release(ref _target);
        Release(ref _bitmap);
    }
}
