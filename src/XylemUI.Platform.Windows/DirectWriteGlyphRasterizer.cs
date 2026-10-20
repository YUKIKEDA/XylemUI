using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.DirectX.DirectX;
using static XylemUI.Platform.Windows.Com;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Draws glyphs with DirectWrite, as the other apps of Windows draw them (design §5.0.3, roadmap row T4): the rendering mode and
/// grid fitting DirectWrite recommends for the font and size, grayscale coverage, and the glyph shifted right by its subpixel
/// step (<c>baselineOriginX</c>). GPUI draws its glyphs the same way (Zed v1.24.3, <c>crates/gpui_windows/src/direct_write.rs</c>).
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="Font"/> gets one DirectWrite font face, made the first time from the font's bytes in place
/// (<c>IDWriteInMemoryFontFileLoader</c>, without a copy) and kept until the font is disposed or the rasterizer is.
/// </para>
/// <para>
/// <see cref="Measure"/> makes the glyph's analysis and keeps it, so that <see cref="Rasterize"/> of the same key, which the
/// atlas calls next, does not make it again. Used on one thread, as the atlas is.
/// </para>
/// </remarks>
public sealed unsafe class DirectWriteGlyphRasterizer : IGlyphRasterizer, IDisposable
{
    private const DWRITE_TEXTURE_TYPE Texture = DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1;

    private readonly Dictionary<Font, nint> _faces = [];
    private IDWriteFactory5* _factory;
    private IDWriteInMemoryFontFileLoader* _loader;
    private IDWriteGlyphRunAnalysis* _analysis;
    private GlyphKey _analysisKey;
    private RECT _bounds;
    private byte[] _coverage = [];

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
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public GlyphBox Measure(in GlyphKey glyph)
    {
        Analyze(glyph);
        return new GlyphBox(_bounds.left, -_bounds.top, _bounds.right - _bounds.left, _bounds.bottom - _bounds.top);
    }

    /// <inheritdoc />
    public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride)
    {
        Analyze(glyph);
        int width = _bounds.right - _bounds.left;
        int height = _bounds.bottom - _bounds.top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (stride < width || destination.Length < ((height - 1) * stride) + width)
        {
            throw new ArgumentException(
                $"The destination holds less than {height} rows of {width} pixels, {stride} bytes apart.",
                nameof(destination)
            );
        }

        // DirectWrite writes the rows packed, so the coverage goes through a buffer kept for the next glyph.
        int size = width * height;
        if (_coverage.Length < size)
        {
            _coverage = GC.AllocateUninitializedArray<byte>(Math.Max(size, _coverage.Length * 2));
        }

        RECT bounds = _bounds;
        fixed (byte* coverage = _coverage)
        {
            ThrowIfFailed(_analysis->CreateAlphaTexture(Texture, &bounds, coverage, (uint)size), "IDWriteGlyphRunAnalysis::CreateAlphaTexture");
        }

        for (int row = 0; row < height; row++)
        {
            _coverage.AsSpan(row * width, width).CopyTo(destination.Slice(row * stride, width));
        }
    }

    /// <summary>
    /// Releases the font faces, the loader, and the factory.
    /// </summary>
    public void Dispose()
    {
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

        // At 96 DPI a DIP is a pixel, so the size and the origin are in pixels.
        IDWriteGlyphRunAnalysis* analysis;
        ThrowIfFailed(
            _factory->CreateGlyphRunAnalysis(
                &run,
                null,
                mode,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                gridFit,
                DWRITE_TEXT_ANTIALIAS_MODE.DWRITE_TEXT_ANTIALIAS_MODE_GRAYSCALE,
                key.SubpixelX / (float)GlyphPosition.SubpixelSteps,
                0,
                &analysis
            ),
            "IDWriteFactory3::CreateGlyphRunAnalysis"
        );
        _analysis = analysis;
        _analysisKey = key;
        RECT bounds;
        ThrowIfFailed(_analysis->GetAlphaTextureBounds(Texture, &bounds), "IDWriteGlyphRunAnalysis::GetAlphaTextureBounds");
        _bounds = bounds;
    }

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
            fixed (byte* bytes = data)
            {
                // The bytes are pinned or mapped by the font until it is disposed, so DirectWrite may keep their address.
                ThrowIfFailed(
                    _loader->CreateInMemoryFontFileReference((IDWriteFactory*)_factory, bytes, (uint)data.Length, FontDataOwner.Instance, &file),
                    "IDWriteInMemoryFontFileLoader::CreateInMemoryFontFileReference"
                );
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
            }
        }
    }
}
