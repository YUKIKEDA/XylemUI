using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.DirectX.DirectX;
using static XylemUI.Platform.Windows.Com;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Draws glyphs with DirectWrite, as the other apps of Windows draw them (design §5.0.3, roadmap row T4): the rendering mode and
/// grid fitting DirectWrite recommends for the font and size, grayscale or ClearType coverage (the key's
/// <see cref="GlyphKey.Coverage"/>), and the glyph shifted right by its subpixel step (<c>baselineOriginX</c>). GPUI draws its
/// glyphs the same way (Zed v1.24.3, <c>crates/gpui_windows/src/direct_write.rs</c>; ClearType at lines 731 to 735 and 759 to
/// 763). A color glyph (an emoji) is drawn from its COLRv0 layers (design §5.0.3, "Color glyphs", roadmap row T6). A bilevel key
/// (<see cref="GlyphKey.Aliased"/>) is drawn in the aliased rendering mode, its color layers too, so its coverage is 0 or 255
/// (design §5.0.3, "When fonts are not smoothed", roadmap row T8).
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="Font"/> gets one DirectWrite font face, made the first time and kept until the font is disposed or the
/// rasterizer is: from the font's file (<see cref="Font.FilePath"/>) for a font of the OS, from its bytes in place
/// (<c>IDWriteInMemoryFontFileLoader</c>, without a copy) for a font opened from bytes.
/// </para>
/// <para>
/// DirectWrite shares what it makes of a glyph at a size with the other processes for a font file, and not for bytes in memory.
/// Drawing a glyph that no process has drawn at that size takes 78 to 98 µs, nearly all in <c>CreateGlyphRunAnalysis</c>; once
/// any process has, 2 to 3 µs (Yu Gothic, 2,000 glyphs, NativeAOT, Windows 11 26300, <c>docs/benchmarks/t4-glyphs.md</c>).
/// </para>
/// <para>
/// <see cref="Measure"/> makes the glyph's analysis and keeps it, so that <see cref="Rasterize"/> of the same key, which the
/// atlas calls next, does not make it again. Used on one thread, as the atlas is.
/// </para>
/// <para>
/// A color glyph is told apart in a color font (<c>IDWriteFontFace2::IsColorFont</c>) by <c>TranslateColorGlyphRun</c>, as GPUI
/// does (<c>direct_write.rs</c>, lines 1562 and 1925 to 1952). Its layers come from <c>IDWriteFactory4::TranslateColorGlyphRun</c>
/// with the COLRv0 format; each is drawn as grayscale coverage with the analysis GPUI uses for a layer (natural symmetric, default
/// grid fit, lines 956 to 967) and laid over the ones before in its color, premultiplied, on the CPU. A layer in the text's color
/// (palette index 0xFFFF, or an outline with no color) takes <see cref="GlyphKey.TextColor"/>. A glyph without COLRv0 layers that
/// has a PNG bitmap (sbix, CBDT) is that bitmap, read by <c>PngDecoder</c> and scaled to the size by <c>BitmapScaler</c> (design
/// §5.0.3, "PNG bitmaps", roadmap row T9); a broken PNG leaves the glyph to its outline. COLRv1 paint trees and SVG are not drawn:
/// Direct2D drew a COLRv1 emoji in 4 ms and added 8.5 MB to the process (roadmap row T11).
/// </para>
/// </remarks>
public sealed unsafe class DirectWriteGlyphRasterizer : IGlyphRasterizer, IDisposable
{
    // The formats asked of TranslateColorGlyphRun: COLRv0 layers, and the outlines of the parts without color. DirectWrite gives a
    // layer as TRUETYPE | COLR (Windows 11 26300, Segoe UI Emoji), so the formats are read as flags, as GPUI reads them (line 954).
    private const DWRITE_GLYPH_IMAGE_FORMATS LayerFormats =
        DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TRUETYPE
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_CFF
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR;

    private readonly Dictionary<Font, nint> _faces = [];
    private readonly Dictionary<Font, bool> _colorFonts = [];
    private readonly List<Layer> _layers = [];
    private readonly PngDecoder _png = new();
    private readonly BitmapScaler _scaler = new();
    private IDWriteFactory5* _factory;
    private IDWriteInMemoryFontFileLoader* _loader;
    private IDWriteGlyphRunAnalysis* _analysis;
    private GlyphKey _analysisKey;
    private RECT _bounds;
    private DWRITE_TEXTURE_TYPE _texture;
    private byte[] _coverage = [];
    private IDWriteFactory4* _factory4;
    private GlyphKey _colorKey;
    private GlyphBox _colorBox;
    private byte[] _layerCoverage = [];
    private byte[] _colorPixels = [];

    // The bitmap glyph whose PNG _png holds: its font, glyph, and the strike DirectWrite returned (0 for none).
    private (Font? Font, uint Glyph, uint Strike) _decoded;
    private Bitmap _bitmap;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectWriteGlyphRasterizer"/> class.
    /// </summary>
    /// <exception cref="InvalidOperationException">DirectWrite could not create the factory or the in-memory loader (Windows 10
    /// version 1703 and later have them).</exception>
    public DirectWriteGlyphRasterizer()
    {
        try
        {
            IDWriteFactory5* factory;
            Guid iid = IID.IID_IDWriteFactory5;
            ThrowIfFailed(DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &iid, (IUnknown**)&factory), "DWriteCreateFactory");
            _factory = factory;
            IDWriteInMemoryFontFileLoader* loader;
            ThrowIfFailed(_factory->CreateInMemoryFontFileLoader(&loader), "IDWriteFactory5::CreateInMemoryFontFileLoader");
            _loader = loader;
            ThrowIfFailed(_factory->RegisterFontFileLoader((IDWriteFontFileLoader*)_loader), "IDWriteFactory::RegisterFontFileLoader");
            IDWriteFactory4* factory4;
            Guid iid4 = IID.IID_IDWriteFactory4;
            ThrowIfFailed(_factory->QueryInterface(&iid4, (void**)&factory4), "IDWriteFactory5::QueryInterface(IDWriteFactory4)");
            _factory4 = factory4;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets or sets a rendering mode that replaces DirectWrite's recommendation, for the tests of the aliased mode, which no font
    /// of Windows gets (see <see cref="TextureOf"/>); <see langword="null"/> takes the recommendation.
    /// </summary>
    internal DWRITE_RENDERING_MODE1? RenderingMode { get; set; }

    /// <inheritdoc />
    public GlyphBox Measure(in GlyphKey glyph)
    {
        if (glyph.Coverage == GlyphCoverage.Color)
        {
            DrawColor(glyph);
            return _colorBox;
        }

        if (IsColorGlyph(glyph, out bool usesTextColor))
        {
            return new GlyphBox(0, 0, 0, 0, IsColor: true, usesTextColor);
        }

        Analyze(glyph);
        return new GlyphBox(_bounds.left, -_bounds.top, _bounds.right - _bounds.left, _bounds.bottom - _bounds.top);
    }

    /// <inheritdoc />
    public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride)
    {
        if (glyph.Coverage == GlyphCoverage.Color)
        {
            DrawColor(glyph);
            GlyphBox box = _colorBox;
            if (box.Width <= 0 || box.Height <= 0)
            {
                return;
            }

            if (stride < box.Width * 4 || destination.Length < ((box.Height - 1) * stride) + (box.Width * 4))
            {
                throw new ArgumentException(
                    $"The destination holds less than {box.Height} rows of {box.Width} pixels, {stride} bytes apart.",
                    nameof(destination)
                );
            }

            for (int row = 0; row < box.Height; row++)
            {
                _colorPixels.AsSpan(row * box.Width * 4, box.Width * 4).CopyTo(destination.Slice(row * stride, box.Width * 4));
            }

            return;
        }

        Analyze(glyph);
        int width = _bounds.right - _bounds.left;
        int height = _bounds.bottom - _bounds.top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        bool rgb = glyph.Coverage == GlyphCoverage.Rgb;
        int rowBytes = rgb ? width * 4 : width;
        if (stride < rowBytes || destination.Length < ((height - 1) * stride) + rowBytes)
        {
            throw new ArgumentException(
                $"The destination holds less than {height} rows of {width} pixels, {stride} bytes apart.",
                nameof(destination)
            );
        }

        // DirectWrite writes the rows packed (3 bytes a pixel for its ClearType texture), so the coverage goes through a buffer
        // kept for the next glyph.
        int channels = _texture == DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1 ? 3 : 1;
        int size = width * height * channels;
        if (_coverage.Length < size)
        {
            _coverage = GC.AllocateUninitializedArray<byte>(Math.Max(size, _coverage.Length * 2));
        }

        RECT bounds = _bounds;
        fixed (byte* coverage = _coverage)
        {
            ThrowIfFailed(_analysis->CreateAlphaTexture(_texture, &bounds, coverage, (uint)size), "IDWriteGlyphRunAnalysis::CreateAlphaTexture");
        }

        if (!rgb)
        {
            for (int row = 0; row < height; row++)
            {
                _coverage.AsSpan(row * width, width).CopyTo(destination.Slice(row * stride, width));
            }

            return;
        }

        // Red, green, and blue into the first three bytes of each 4-byte pixel, as GPUI pads them (direct_write.rs, lines 864 to
        // 880); the fourth is left as it is. An aliased glyph has one value a pixel, the same for the three.
        for (int row = 0; row < height; row++)
        {
            ReadOnlySpan<byte> source = _coverage.AsSpan(row * width * channels, width * channels);
            Span<byte> target = destination.Slice(row * stride, rowBytes);
            for (int x = 0; x < width; x++)
            {
                int at = x * channels;
                target[x * 4] = source[at];
                target[(x * 4) + 1] = source[at + ((channels - 1) / 2)];
                target[(x * 4) + 2] = source[at + channels - 1];
            }
        }
    }

    /// <summary>
    /// Releases the font faces, the loader, and the factory.
    /// </summary>
    public void Dispose()
    {
        Release(ref _factory4);
        Release(ref _analysis);
        foreach (nint face in _faces.Values)
        {
            _ = ((IDWriteFontFace3*)face)->Release();
        }

        _faces.Clear();
        if (_loader is not null && _factory is not null)
        {
            _ = _factory->UnregisterFontFileLoader((IDWriteFontFileLoader*)_loader);
        }

        Release(ref _loader);
        Release(ref _factory);
    }

    // Draws the color glyph of the key, unless the last call drew it: from its COLRv0 layers, each layer's grayscale coverage in its
    // color, premultiplied BGRA, laid over the layers before it; or, without layers, from its PNG bitmap.
    private void DrawColor(in GlyphKey key)
    {
        ObjectDisposedException.ThrowIf(_factory is null, this);
        ObjectDisposedException.ThrowIf(key.Font.IsDisposed, key.Font);
        if (_colorKey == key && _colorKey.Font is not null)
        {
            return;
        }

        IDWriteFontFace3* face = FaceOf(key.Font);
        uint text = key.TextColor == 0 ? 0xFF000000 : key.TextColor;
        var textColor = new DXGI_RGBA
        {
            r = (text & 0xFF) / 255f,
            g = ((text >> 8) & 0xFF) / 255f,
            b = ((text >> 16) & 0xFF) / 255f,
            a = 1,
        };
        ushort index = checked((ushort)key.GlyphId);
        float advance = 0;
        DWRITE_GLYPH_OFFSET offset = default;
        DWRITE_GLYPH_RUN run = RunOf(face, &index, &advance, &offset, key.PixelSize);
        _layers.Clear();
        int used = 0;

        // A bilevel color glyph draws its layers bilevel too, as Skia does (SkScalerContext_win_dw.cpp, line 2204).
        DWRITE_RENDERING_MODE1 mode = key.Aliased
            ? DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_ALIASED
            : DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC;
        IDWriteColorGlyphRunEnumerator1* runs = TranslateLayers(&run);
        if (runs is null && TryReadBitmap(face, key))
        {
            DrawBitmap(key);
            _colorKey = key;
            return;
        }

        if (runs is null)
        {
            used = AddLayer(&run, 0, 0, textColor, mode, used);
        }
        else
        {
            try
            {
                BOOL more;
                while (runs->MoveNext(&more) >= 0 && more)
                {
                    DWRITE_COLOR_GLYPH_RUN1* layer;
                    ThrowIfFailed(runs->GetCurrentRun(&layer), "IDWriteColorGlyphRunEnumerator1::GetCurrentRun");
                    bool inTextColor =
                        (layer->glyphImageFormat & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR) == 0
                        || layer->Base.paletteIndex == 0xFFFF;
                    used = AddLayer(
                        &layer->Base.glyphRun,
                        layer->Base.baselineOriginX,
                        layer->Base.baselineOriginY,
                        inTextColor ? textColor : layer->Base.runColor,
                        mode,
                        used
                    );
                }
            }
            finally
            {
                _ = runs->Release();
            }
        }

        _colorKey = key;
        _colorBox = Composite();
    }

    // The color runs of a glyph run as COLRv0 layers and outlines, or null for a glyph with no color (DWRITE_E_NOCOLOR).
    private IDWriteColorGlyphRunEnumerator1* TranslateLayers(DWRITE_GLYPH_RUN* run)
    {
        IDWriteColorGlyphRunEnumerator1* runs;
        HRESULT result = _factory4->TranslateColorGlyphRun(
            default,
            run,
            null,
            LayerFormats,
            DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
            null,
            0,
            &runs
        );
        if (result == DWRITE.DWRITE_E_NOCOLOR)
        {
            return null;
        }

        ThrowIfFailed(result, "IDWriteFactory4::TranslateColorGlyphRun");
        return runs;
    }

    // One layer's grayscale (or bilevel) coverage, after the layers before it in _layerCoverage. Returns the bytes used so far.
    private int AddLayer(DWRITE_GLYPH_RUN* run, float originX, float originY, DXGI_RGBA color, DWRITE_RENDERING_MODE1 mode, int used)
    {
        IDWriteGlyphRunAnalysis* analysis;
        ThrowIfFailed(
            _factory->CreateGlyphRunAnalysis(
                run,
                null,
                mode,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                DWRITE_GRID_FIT_MODE.DWRITE_GRID_FIT_MODE_DEFAULT,
                DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_GRAYSCALE,
                originX,
                originY,
                &analysis
            ),
            "IDWriteFactory3::CreateGlyphRunAnalysis"
        );
        try
        {
            RECT bounds;
            ThrowIfFailed(
                analysis->GetAlphaTextureBounds(DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1, &bounds),
                "IDWriteGlyphRunAnalysis::GetAlphaTextureBounds"
            );
            int size = (bounds.right - bounds.left) * (bounds.bottom - bounds.top);
            if (bounds.right <= bounds.left || bounds.bottom <= bounds.top)
            {
                return used;
            }

            if (_layerCoverage.Length < used + size)
            {
                Array.Resize(ref _layerCoverage, Math.Max(used + size, _layerCoverage.Length * 2));
            }

            fixed (byte* coverage = &_layerCoverage[used])
            {
                ThrowIfFailed(
                    analysis->CreateAlphaTexture(DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1, &bounds, coverage, (uint)size),
                    "IDWriteGlyphRunAnalysis::CreateAlphaTexture"
                );
            }

            _layers.Add(new Layer(bounds, color, used));
            return used + size;
        }
        finally
        {
            _ = analysis->Release();
        }
    }

    // Reads the PNG bitmap of a glyph without COLRv0 layers (design §5.0.3, "PNG bitmaps"), unless _png holds it already: the
    // strike DirectWrite returns for the size rounded up (the smallest strike at least that size, else the largest: with strikes
    // of 20 and 64 ppem, 1 to 20 gave 20 and 21 to 300 gave 64, Windows 11 26300). Returns false for no PNG or a broken one, which
    // is then drawn as its outline.
    private bool TryReadBitmap(IDWriteFontFace3* face, in GlyphKey key)
    {
        IDWriteFontFace4* face4;
        Guid iid = IID.IID_IDWriteFontFace4;
        if (face->QueryInterface(&iid, (void**)&face4) < 0)
        {
            return false;
        }

        try
        {
            ushort glyph = checked((ushort)key.GlyphId);
            uint ppem = (uint)Math.Clamp(MathF.Ceiling(key.PixelSize), 1, ushort.MaxValue);

            // The formats of every strike: asked for one size, DirectWrite gives only the strikes inside the range (10 to 10 ppem gave
            // no PNG for strikes of 20 and 64 ppem), while GetGlyphImageData returns the nearest strike.
            DWRITE_GLYPH_IMAGE_FORMATS formats;
            ThrowIfFailed(face4->GetGlyphImageFormats(glyph, 1, ushort.MaxValue, &formats), "IDWriteFontFace4::GetGlyphImageFormats");
            if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG) == 0)
            {
                return false;
            }

            DWRITE_GLYPH_IMAGE_DATA data;
            void* context;
            ThrowIfFailed(
                face4->GetGlyphImageData(glyph, ppem, DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG, &data, &context),
                "IDWriteFontFace4::GetGlyphImageData"
            );
            if (data.imageDataSize == 0)
            {
                return false;
            }

            try
            {
                if (_decoded == (key.Font, key.GlyphId, data.pixelsPerEm))
                {
                    return _bitmap.Width > 0;
                }

                // A side past four times the strike, or past 4,096, is not a glyph's bitmap (the design's limit on untrusted input).
                int maxSide = (int)Math.Min(4096u, 4u * Math.Max(1u, data.pixelsPerEm));
                var png = new ReadOnlySpan<byte>(data.imageData, (int)data.imageDataSize);
                bool read = _png.TryDecode(png, maxSide, out int width, out int height);
                _decoded = (key.Font, key.GlyphId, data.pixelsPerEm);
                _bitmap = read ? new Bitmap(width, height, data.pixelsPerEm, data.horizontalLeftOrigin.x, data.horizontalLeftOrigin.y) : default;
                return read;
            }
            finally
            {
                face4->ReleaseGlyphImageData(context);
            }
        }
        finally
        {
            _ = face4->Release();
        }
    }

    // Scales the bitmap _png holds to the key's size, the ratio of the size to its strike, into _colorPixels: down by the average
    // over each pixel's area, up bilinearly; its origin scaled by the same ratio (design §5.0.3, "PNG bitmaps"). A bilevel key
    // draws the same picture.
    private void DrawBitmap(in GlyphKey key)
    {
        float ratio = key.PixelSize / _bitmap.Strike;
        int width = Math.Max(1, (int)MathF.Round(_bitmap.Width * ratio, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)MathF.Round(_bitmap.Height * ratio, MidpointRounding.AwayFromZero));
        if (_colorPixels.Length < width * height * 4)
        {
            _colorPixels = GC.AllocateUninitializedArray<byte>(Math.Max(width * height * 4, _colorPixels.Length * 2));
        }

        _scaler.Scale(_png.Pixels, _bitmap.Width, _bitmap.Height, _colorPixels, width, height, width * 4);
        int left = (int)MathF.Round(-_bitmap.OriginX * ratio, MidpointRounding.AwayFromZero);
        int top = (int)MathF.Round(_bitmap.OriginY * ratio, MidpointRounding.AwayFromZero);
        _colorBox = new GlyphBox(left, top, width, height, IsColor: true);
    }

    // Lays the layers, in order, over each other in _colorPixels (premultiplied BGRA, "over"), and returns the box of their union.
    private GlyphBox Composite()
    {
        if (_layers.Count == 0)
        {
            return new GlyphBox(0, 0, 0, 0, IsColor: true);
        }

        RECT union = _layers[0].Bounds;
        foreach (Layer layer in _layers)
        {
            union.left = Math.Min(union.left, layer.Bounds.left);
            union.top = Math.Min(union.top, layer.Bounds.top);
            union.right = Math.Max(union.right, layer.Bounds.right);
            union.bottom = Math.Max(union.bottom, layer.Bounds.bottom);
        }

        int width = union.right - union.left;
        int height = union.bottom - union.top;
        if (_colorPixels.Length < width * height * 4)
        {
            _colorPixels = GC.AllocateUninitializedArray<byte>(Math.Max(width * height * 4, _colorPixels.Length * 2));
        }

        Span<byte> pixels = _colorPixels.AsSpan(0, width * height * 4);
        pixels.Clear();
        foreach (Layer layer in _layers)
        {
            int layerWidth = layer.Bounds.right - layer.Bounds.left;
            for (int y = layer.Bounds.top; y < layer.Bounds.bottom; y++)
            {
                ReadOnlySpan<byte> coverage = _layerCoverage.AsSpan(layer.Offset + ((y - layer.Bounds.top) * layerWidth), layerWidth);
                Span<byte> row = pixels.Slice((((y - union.top) * width) + (layer.Bounds.left - union.left)) * 4, layerWidth * 4);
                for (int x = 0; x < layerWidth; x++)
                {
                    if (coverage[x] == 0)
                    {
                        continue;
                    }

                    float alpha = coverage[x] / 255f * layer.Color.a;
                    float keep = 1 - alpha;
                    Span<byte> pixel = row.Slice(x * 4, 4);
                    pixel[0] = (byte)((layer.Color.b * alpha * 255) + (pixel[0] * keep) + 0.5f);
                    pixel[1] = (byte)((layer.Color.g * alpha * 255) + (pixel[1] * keep) + 0.5f);
                    pixel[2] = (byte)((layer.Color.r * alpha * 255) + (pixel[2] * keep) + 0.5f);
                    pixel[3] = (byte)((alpha * 255) + (pixel[3] * keep) + 0.5f);
                }
            }
        }

        return new GlyphBox(union.left, -union.top, width, height, IsColor: true);
    }

    // A decoded bitmap: its size and its strike, and the pen's origin from the image's top left, in the strike's pixels.
    private readonly record struct Bitmap(int Width, int Height, uint Strike, int OriginX, int OriginY);

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

    // Whether DirectWrite gives the glyph COLRv0 layers (not DWRITE_E_NOCOLOR), asked only for a color font. A layer in the text's
    // color is one with palette index 0xFFFF, or an outline with no color.
    private bool IsColorGlyph(in GlyphKey key, out bool usesTextColor)
    {
        ObjectDisposedException.ThrowIf(_factory is null, this);
        ObjectDisposedException.ThrowIf(key.Font.IsDisposed, key.Font);
        usesTextColor = false;
        IDWriteFontFace3* face = FaceOf(key.Font);
        if (!_colorFonts.TryGetValue(key.Font, out bool colorFont))
        {
            colorFont = face->IsColorFont() != BOOL.FALSE;
            _colorFonts.Add(key.Font, colorFont);
        }

        if (!colorFont)
        {
            return false;
        }

        ushort index = checked((ushort)key.GlyphId);
        float advance = 0;
        DWRITE_GLYPH_OFFSET offset = default;
        DWRITE_GLYPH_RUN run = RunOf(face, &index, &advance, &offset, key.PixelSize);
        IDWriteColorGlyphRunEnumerator1* runs = TranslateLayers(&run);
        if (runs is null)
        {
            return TryReadBitmap(face, key);
        }

        try
        {
            BOOL more;
            while (runs->MoveNext(&more) >= 0 && more)
            {
                DWRITE_COLOR_GLYPH_RUN1* layer;
                ThrowIfFailed(runs->GetCurrentRun(&layer), "IDWriteColorGlyphRunEnumerator1::GetCurrentRun");
                usesTextColor |=
                    (layer->glyphImageFormat & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR) == 0 || layer->Base.paletteIndex == 0xFFFF;
            }
        }
        finally
        {
            _ = runs->Release();
        }

        return true;
    }

    // Makes the analysis of the key and its texture bounds, unless the last call made them.
    private void Analyze(in GlyphKey key)
    {
        ObjectDisposedException.ThrowIf(_factory is null, this);
        ObjectDisposedException.ThrowIf(key.Font.IsDisposed, key.Font);
        if (_analysis is not null && _analysisKey == key)
        {
            return;
        }

        Release(ref _analysis);
        IDWriteFontFace3* face = FaceOf(key.Font);
        DWRITE_RENDERING_MODE1 mode;
        DWRITE_GRID_FIT_MODE gridFit;
        ThrowIfFailed(
            face->GetRecommendedRenderingMode(
                key.PixelSize,
                96,
                96,
                null,
                BOOL.FALSE,
                DWRITE_OUTLINE_THRESHOLD.DWRITE_OUTLINE_THRESHOLD_ANTIALIASED,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                null,
                &mode,
                &gridFit
            ),
            "IDWriteFontFace3::GetRecommendedRenderingMode"
        );

        // An outline is what DirectWrite recommends for large text it would draw as a path; the analysis needs a rendering mode,
        // and GPUI takes the symmetric one there (direct_write.rs, lines 726 to 729).
        if (mode == DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_OUTLINE)
        {
            mode = DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC;
        }

        // A bilevel key is drawn aliased, with the same measuring mode (design §5.0.3, "When fonts are not smoothed": GDI_CLASSIC
        // drew the same pixels at 100, 125, and 150 %, PR #200).
        if (key.Aliased)
        {
            mode = DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_ALIASED;
        }

        mode = RenderingMode ?? mode;

        ushort index = checked((ushort)key.GlyphId);
        float advance = 0;
        DWRITE_GLYPH_OFFSET offset = default;
        var run = new DWRITE_GLYPH_RUN
        {
            fontFace = (IDWriteFontFace*)face,
            fontEmSize = key.PixelSize,
            glyphCount = 1,
            glyphIndices = &index,
            glyphAdvances = &advance,
            glyphOffsets = &offset,
            isSideways = BOOL.FALSE,
            bidiLevel = 0,
        };

        DWRITE_TEXT_ANTIALIAS_MODE antialias =
            key.Coverage == GlyphCoverage.Rgb
                ? DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_CLEARTYPE
                : DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_GRAYSCALE;

        // At 96 DPI a DIP is a pixel, so the size and the origin are in pixels.
        IDWriteGlyphRunAnalysis* analysis;
        ThrowIfFailed(
            _factory->CreateGlyphRunAnalysis(
                &run,
                null,
                mode,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                gridFit,
                antialias,
                key.SubpixelX / (float)GlyphPosition.SubpixelSteps,
                0,
                &analysis
            ),
            "IDWriteFactory3::CreateGlyphRunAnalysis"
        );
        _analysis = analysis;
        _analysisKey = key;
        RECT bounds;
        _texture = TextureOf(key.Coverage, mode);
        ThrowIfFailed(_analysis->GetAlphaTextureBounds(_texture, &bounds), "IDWriteGlyphRunAnalysis::GetAlphaTextureBounds");
        _bounds = bounds;
    }

    /// <summary>
    /// Returns the texture to read the analysis as. An aliased rendering mode has only the 1x1 texture: its ClearType 3x1 texture is
    /// empty, and the glyph would have no ink. Skia pairs them the same way (<c>src/ports/SkScalerContext_win_dw.cpp</c>, lines 333 to
    /// 393); GPUI does not (<c>direct_write.rs</c>, lines 726 to 763). No face of the fonts of Windows 11 (391 faces, 6 to 32 px), nor
    /// the test font with a gasp table that asks for no smoothing, got the aliased mode from <c>GetRecommendedRenderingMode</c> as
    /// <see cref="Analyze"/> calls it; the API may still return it.
    /// </summary>
    /// <param name="coverage">The kind of coverage of the key.</param>
    /// <param name="mode">The rendering mode of the analysis.</param>
    /// <returns>The texture type.</returns>
    internal static DWRITE_TEXTURE_TYPE TextureOf(GlyphCoverage coverage, DWRITE_RENDERING_MODE1 mode) =>
        coverage == GlyphCoverage.Rgb && mode != DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_ALIASED
            ? DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1
            : DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1;

    private IDWriteFontFace3* FaceOf(Font font)
    {
        if (_faces.TryGetValue(font, out nint known))
        {
            return (IDWriteFontFace3*)known;
        }

        ReleaseFacesOfDisposedFonts();
        ReadOnlySpan<byte> data = font.Data;
        IDWriteFontFile* file = null;
        IDWriteFontFaceReference* reference = null;
        try
        {
            if (font.FilePath is { } path)
            {
                fixed (char* name = path)
                {
                    ThrowIfFailed(_factory->CreateFontFileReference(name, null, &file), "IDWriteFactory::CreateFontFileReference");
                }
            }
            else
            {
                fixed (byte* bytes = data)
                {
                    // The bytes are pinned by the font until it is disposed, so DirectWrite may keep their address.
                    ThrowIfFailed(
                        _loader->CreateInMemoryFontFileReference((IDWriteFactory*)_factory, bytes, (uint)data.Length, FontDataOwner.Instance, &file),
                        "IDWriteInMemoryFontFileLoader::CreateInMemoryFontFileReference"
                    );
                }
            }

            ThrowIfFailed(
                _factory->CreateFontFaceReference(file, (uint)font.FaceIndex, DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE, &reference),
                "IDWriteFactory3::CreateFontFaceReference"
            );
            IDWriteFontFace3* face;
            ThrowIfFailed(reference->CreateFontFace(&face), "IDWriteFontFaceReference::CreateFontFace");
            _faces.Add(font, (nint)face);
            return face;
        }
        finally
        {
            Release(ref reference);
            Release(ref file);
        }
    }

    // A disposed font's face reads bytes that are gone; it is never asked for again, so its reference goes.
    private void ReleaseFacesOfDisposedFonts()
    {
        foreach ((Font font, nint face) in _faces)
        {
            if (font.IsDisposed)
            {
                _ = ((IDWriteFontFace3*)face)->Release();
                _ = _faces.Remove(font);
                _ = _colorFonts.Remove(font);
            }
        }
    }

    // A COLRv0 layer: the box of its coverage, from the pen, its color, and where its coverage starts in _layerCoverage.
    private readonly record struct Layer(RECT Bounds, DXGI_RGBA Color, int Offset);
}
