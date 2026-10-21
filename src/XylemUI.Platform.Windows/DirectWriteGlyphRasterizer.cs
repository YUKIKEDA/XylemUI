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
/// 763).
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
/// </remarks>
public sealed unsafe class DirectWriteGlyphRasterizer : IGlyphRasterizer, IDisposable
{
    private readonly Dictionary<Font, nint> _faces = [];
    private readonly Dictionary<Font, bool> _colorFonts = [];
    private IDWriteFactory5* _factory;
    private IDWriteInMemoryFontFileLoader* _loader;
    private IDWriteGlyphRunAnalysis* _analysis;
    private GlyphKey _analysisKey;
    private RECT _bounds;
    private DWRITE_TEXTURE_TYPE _texture;
    private byte[] _coverage = [];
    private IDWriteFactory4* _factory4;
    private IDWriteFactory8* _factory8;
    private ColorGlyphRenderer? _colors;
    private GlyphKey _colorKey;
    private GlyphBox _colorBox;
    private bool _usePaintTrees = true;

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
            IDWriteFactory8* factory8;
            Guid iid8 = IID.IID_IDWriteFactory8;
            _factory8 = _factory->QueryInterface(&iid8, (void**)&factory8) >= 0 ? factory8 : null;
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

    /// <summary>
    /// Gets or sets a value indicating whether color glyphs are drawn from COLRv1 paint trees where Direct2D can draw them;
    /// <see langword="false"/> draws the COLRv0 layers, as a Windows without COLRv1 does, for the tests of that path.
    /// </summary>
    internal bool UsePaintTrees
    {
        get => _usePaintTrees;
        set => SetUsePaintTrees(value);
    }

    /// <summary>
    /// Gets a value indicating whether the last color glyph drawn came from a COLRv1 paint tree.
    /// </summary>
    internal bool DrewPaintTree => _colors?.DrewPaintTree ?? false;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Direct2D or WIC could not draw a color glyph.</exception>
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
    /// <exception cref="InvalidOperationException">Direct2D or WIC could not draw a color glyph.</exception>
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

            _colors!.Copy(box, destination, stride);
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
        _colors?.Dispose();
        _colors = null;
        Release(ref _factory8);
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

    private void SetUsePaintTrees(bool value)
    {
        _usePaintTrees = value;
        _colorKey = default;
        _colors?.UsePaintTrees = value;
    }

    // Draws the color glyph of the key in its colors, unless the last call drew it. The renderer (Direct2D and WIC) is made the
    // first time.
    private void DrawColor(in GlyphKey key)
    {
        ObjectDisposedException.ThrowIf(_factory is null, this);
        ObjectDisposedException.ThrowIf(key.Font.IsDisposed, key.Font);
        if (_colors is not null && _colorKey == key && _colorKey.Font is not null)
        {
            return;
        }

        _colors ??= new ColorGlyphRenderer(_factory) { UsePaintTrees = _usePaintTrees };
        _colors.Draw(FaceOf(key.Font), key, out GlyphBox box);
        _colorKey = key;
        _colorBox = box;
    }

    // Whether DirectWrite gives the glyph color runs (not DWRITE_E_NOCOLOR), as GPUI's is_color_glyph decides
    // (direct_write.rs, lines 1925 to 1952), asked only for a color font (IDWriteFontFace2::IsColorFont, line 1562). A layer in the
    // text's color is a COLRv0 layer with palette index 0xFFFF, an outline layer, an SVG glyph, or a COLRv1 paint tree whose
    // attributes say so (DWRITE_PAINT_ATTRIBUTES_USES_TEXT_COLOR).
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
        var run = new DWRITE_GLYPH_RUN
        {
            fontFace = (IDWriteFontFace*)face,
            fontEmSize = key.PixelSize,
            glyphCount = 1,
            glyphIndices = &index,
            glyphAdvances = &advance,
            glyphOffsets = &offset,
        };
        IDWriteColorGlyphRunEnumerator1* runs;
        HRESULT result =
            _factory8 is not null && _usePaintTrees
                ? _factory8->TranslateColorGlyphRun(
                    default,
                    &run,
                    null,
                    ColorGlyphRenderer.Formats | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE,
                    DWRITE_PAINT_FEATURE_LEVEL.DWRITE_PAINT_FEATURE_LEVEL_COLR_V1,
                    DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                    null,
                    0,
                    &runs
                )
                : _factory4->TranslateColorGlyphRun(
                    default,
                    &run,
                    null,
                    ColorGlyphRenderer.Formats,
                    DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                    null,
                    0,
                    &runs
                );
        if (result == DWRITE.DWRITE_E_NOCOLOR)
        {
            return false;
        }

        ThrowIfFailed(result, "IDWriteFactory::TranslateColorGlyphRun");
        try
        {
            BOOL more;
            while (runs->MoveNext(&more) >= 0 && more)
            {
                DWRITE_COLOR_GLYPH_RUN1* color;
                ThrowIfFailed(runs->GetCurrentRun(&color), "IDWriteColorGlyphRunEnumerator1::GetCurrentRun");
                usesTextColor |= ColorGlyphRenderer.KindOf(color->glyphImageFormat) switch
                {
                    DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR => color->Base.paletteIndex == 0xFFFF,
                    DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE => PaintTreeUsesTextColor(
                        face,
                        color->Base.glyphRun.glyphIndices[0]
                    ),
                    DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
                    or DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG
                    or DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF
                    or DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8 => false,
                    _ => true,
                };
            }
        }
        finally
        {
            _ = runs->Release();
        }

        return true;
    }

    // Whether a COLRv1 paint tree paints anywhere in the text's color, from its attributes.
    private static bool PaintTreeUsesTextColor(IDWriteFontFace3* face, ushort glyph)
    {
        IDWriteFontFace7* face7;
        Guid iid = IID.IID_IDWriteFontFace7;
        if (face->QueryInterface(&iid, (void**)&face7) < 0)
        {
            return true;
        }

        IDWritePaintReader* reader = null;
        try
        {
            ThrowIfFailed(
                face7->CreatePaintReader(
                    DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE,
                    DWRITE_PAINT_FEATURE_LEVEL.DWRITE_PAINT_FEATURE_LEVEL_COLR_V1,
                    &reader
                ),
                "IDWriteFontFace7::CreatePaintReader"
            );
            DWRITE_PAINT_ELEMENT element;
            D2D_RECT_F clip;
            DWRITE_PAINT_ATTRIBUTES attributes;
            ThrowIfFailed(
                reader->SetCurrentGlyph(glyph, &element, (uint)sizeof(DWRITE_PAINT_ELEMENT), &clip, &attributes),
                "IDWritePaintReader::SetCurrentGlyph"
            );
            return (attributes & DWRITE_PAINT_ATTRIBUTES.DWRITE_PAINT_ATTRIBUTES_USES_TEXT_COLOR) != 0;
        }
        finally
        {
            Release(ref reader);
            _ = face7->Release();
        }
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
}
