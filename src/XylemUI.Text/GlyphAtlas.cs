using System.Runtime.InteropServices;
using XylemUI.Text.Interop;

namespace XylemUI.Text;

/// <summary>
/// The CPU side of the glyph atlas: 8-bit coverage of rasterized glyphs, packed into one pinned array (design §5.0.2, §5.0.3).
/// </summary>
/// <remarks>
/// <para>
/// A glyph is rasterized by FreeType the first time it is asked for, at one pixel size, and kept: the next request returns the
/// same region without rasterizing. The regions filled since the last <see cref="ClearDirtyRegions"/> are listed in
/// <see cref="DirtyRegions"/>, for the GPU upload after the display list is built (design §5.0.3).
/// </para>
/// <para>
/// Glyphs are packed on shelves: left to right along a row as tall as its tallest glyph, then a new row below, with one pixel of
/// space around each glyph so that sampling does not bleed. A full atlas refuses new glyphs; evicting old ones comes with the
/// GPU texture (roadmap row S8b). The array is pinned, so FreeType's output is copied straight in and the GPU upload reads it in
/// place. One atlas belongs to one thread.
/// </para>
/// </remarks>
public sealed unsafe class GlyphAtlas
{
    private const int Padding = 1;

    private readonly byte[] _pixels;
    private readonly Dictionary<(int Font, uint Glyph, int Size), AtlasGlyph> _glyphs = [];
    private readonly List<(int X, int Y, int Width, int Height)> _dirty = [];
    private int _shelfX = Padding;
    private int _shelfY = Padding;
    private int _shelfHeight;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphAtlas"/> class.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public GlyphAtlas(int width = 1024, int height = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _pixels = GC.AllocateArray<byte>(width * height, pinned: true);
    }

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the coverage, one byte per pixel, row after row (the stride is <see cref="Width"/>).
    /// </summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>
    /// Gets the regions filled since the last <see cref="ClearDirtyRegions"/>, as (x, y, width, height).
    /// </summary>
    public ReadOnlySpan<(int X, int Y, int Width, int Height)> DirtyRegions => CollectionsMarshal.AsSpan(_dirty);

    /// <summary>
    /// Gets the number of glyphs rasterized so far; a cached glyph does not add to it.
    /// </summary>
    public int RasterizeCount { get; private set; }

    /// <summary>
    /// Forgets the dirty regions, after they were uploaded.
    /// </summary>
    public void ClearDirtyRegions() => _dirty.Clear();

    /// <summary>
    /// Returns the region of a glyph, rasterizing it into the atlas the first time.
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="glyphId">The glyph, from shaping.</param>
    /// <param name="pixelSize">The size in pixels per em.</param>
    /// <param name="glyph">The glyph's region and bearings.</param>
    /// <returns>
    /// <see langword="false"/> when the glyph is new and the atlas has no room for it. A glyph with no ink always fits.
    /// </returns>
    /// <exception cref="InvalidOperationException">FreeType could not render the glyph.</exception>
    /// <exception cref="ObjectDisposedException">The font was disposed and the glyph is not in the atlas.</exception>
    public bool TryGet(Font font, uint glyphId, int pixelSize, out AtlasGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        if (_glyphs.TryGetValue((font.Id, glyphId, pixelSize), out glyph))
        {
            return true;
        }

        return TryRasterize(font, glyphId, pixelSize, out glyph);
    }

    private bool TryRasterize(Font font, uint glyphId, int pixelSize, out AtlasGlyph glyph)
    {
        FreeType.GlyphSlotRec* slot = font.Render(glyphId, pixelSize);
        RasterizeCount++;
        FreeType.Bitmap bitmap = slot->Bitmap;
        int width = (int)bitmap.Width;
        int height = (int)bitmap.Rows;
        if (bitmap.PixelMode != FreeType.PixelModeGray && width > 0)
        {
            throw new InvalidOperationException($"FreeType rendered glyph {glyphId} in pixel mode {bitmap.PixelMode}, not gray.");
        }

        if (!TryPlace(width, height, out int x, out int y))
        {
            glyph = default;
            return false;
        }

        // The bitmap has `rows` rows of `width` bytes, `pitch` bytes apart (negative pitch: bottom-up). The region was checked to
        // lie inside the atlas by TryPlace.
        for (int row = 0; row < height; row++)
        {
            var source = new ReadOnlySpan<byte>(bitmap.Buffer + ((long)row * bitmap.Pitch), width);
            source.CopyTo(_pixels.AsSpan(((y + row) * Width) + x, width));
        }

        glyph = new AtlasGlyph(x, y, width, height, slot->BitmapLeft, slot->BitmapTop);
        _glyphs.Add((font.Id, glyphId, pixelSize), glyph);
        if (width > 0 && height > 0)
        {
            _dirty.Add((x, y, width, height));
        }

        return true;
    }

    // Shelf packing: the next spot on the current row, or the start of a new row below it.
    private bool TryPlace(int width, int height, out int x, out int y)
    {
        x = 0;
        y = 0;

        // A glyph with no ink (a space) needs no room, so it fits even in a full atlas.
        if (width == 0 || height == 0)
        {
            return true;
        }

        // Too wide for any row: refuse before moving to a new row, which would waste the rest of the current one.
        if (width + (2 * Padding) > Width)
        {
            return false;
        }

        if (_shelfX + width + Padding > Width)
        {
            _shelfX = Padding;
            _shelfY += _shelfHeight + Padding;
            _shelfHeight = 0;
        }

        if (_shelfY + height + Padding > Height)
        {
            return false;
        }

        x = _shelfX;
        y = _shelfY;
        _shelfX += width + Padding;
        _shelfHeight = Math.Max(_shelfHeight, height);
        return true;
    }
}
