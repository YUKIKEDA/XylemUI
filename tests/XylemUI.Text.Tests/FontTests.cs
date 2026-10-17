namespace XylemUI.Text.Tests;

// Opening and closing a font (S8a, design §4.5).
public sealed class FontTests
{
    /// <summary>
    /// Bytes that are not a font are refused with an exception, not opened as an empty font.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 256 bytes of a repeating pattern, which is not a font file.
    ///
    /// Steps:
    /// - Open them as a font.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/> from FreeType.
    /// </remarks>
    [Fact]
    public void Open_BytesThatAreNotAFont_Throws()
    {
        byte[] garbage = Enumerable.Range(0, 256).Select(i => (byte)(i * 37)).ToArray();

        Assert.Throws<InvalidOperationException>(() => new Font(garbage));
    }

    /// <summary>
    /// The face index reaches FreeType: a face the file does not have is refused, and a negative index is an argument error.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a single font (one face, index 0).
    ///
    /// Steps:
    /// - Open it with face index 0, 1, and −1.
    ///
    /// Expected:
    /// - 0 opens; 1 throws <see cref="InvalidOperationException"/>; −1 throws <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    [Fact]
    public void Open_FaceIndex_SelectsTheFace()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));

        using (new Font(bytes, faceIndex: 0)) { }

        Assert.Throws<InvalidOperationException>(() => new Font(bytes, faceIndex: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Font(bytes, faceIndex: -1));
    }

    /// <summary>
    /// A disposed font is refused by shaping and by rasterizing a glyph that is not cached yet.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, and an atlas.
    ///
    /// Steps:
    /// - Dispose the font; shape "A" with it; get glyph 41 from the atlas with it.
    ///
    /// Expected:
    /// - Both throw <see cref="ObjectDisposedException"/>.
    /// </remarks>
    [Fact]
    public void Disposed_IsRefusedByShapingAndRasterizing()
    {
        Font font = TestFont.Open();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(64, 64);

        font.Dispose();

        Assert.Throws<ObjectDisposedException>(() => shaper.Shape(font, "A", TestFont.Japanese).ToArray());
        Assert.Throws<ObjectDisposedException>(() => atlas.TryGet(font, 41, 32, out _));
    }
}
