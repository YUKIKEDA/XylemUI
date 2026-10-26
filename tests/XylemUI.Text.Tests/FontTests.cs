namespace XylemUI.Text.Tests;

// Opening and closing a font (S8a, design §4.5).
public sealed class FontTests
{
    /// <summary>
    /// A font's ascent, descent, and line gap are HarfBuzz's horizontal extents, which read the hhea table, or OS/2's typographic
    /// values when the font sets USE_TYPO_METRICS (design §3.2, "Text leaves").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font (hhea 1160, -288, 0; no USE_TYPO_METRICS) and the bundled font (OS/2 typographic 1069, -293, 0, with
    ///   USE_TYPO_METRICS), read with fontTools.
    ///
    /// Steps:
    /// - Read Ascent, Descent, and LineGap of each.
    ///
    /// Expected:
    /// - 1160, 288, 0 and 1069, 293, 0: the descent positive below the baseline.
    /// </remarks>
    [Fact]
    public void Extents_AreTheFontsLineMetrics()
    {
        using Font font = TestFont.Open();
        Font bundled = FontFallback.Bundled;

        Assert.Equal((1160, 288, 0), (font.Ascent, font.Descent, font.LineGap));
        Assert.Equal((1069, 293, 0), (bundled.Ascent, bundled.Descent, bundled.LineGap));
    }

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
    /// - <see cref="InvalidOperationException"/> (HarfBuzz finds no glyphs in them).
    /// </remarks>
    [Fact]
    public void Open_BytesThatAreNotAFont_Throws()
    {
        byte[] garbage = Enumerable.Range(0, 256).Select(i => (byte)(i * 37)).ToArray();

        Assert.Throws<InvalidOperationException>(() => new Font(garbage));
    }

    /// <summary>
    /// The face index reaches HarfBuzz: a face the file does not have is refused, and a negative index is an argument error.
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
        var atlas = new GlyphAtlas(new TestRasterizer(), 64, 32);

        font.Dispose();

        Assert.Throws<ObjectDisposedException>(() => shaper.Shape(font, "A", TestFont.Japanese).ToArray());
        Assert.Throws<ObjectDisposedException>(() => atlas.TryGet(new GlyphKey(font, 41, 32, 0), out _));
    }

    /// <summary>
    /// A font file opened by mapping it shapes as the same bytes opened from memory, and disposing the font lets the file go.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A copy of the test font in a temporary file.
    ///
    /// Steps:
    /// - Open the file with Font.FromFile and the bytes with the constructor; shape "Hello, World! 日本語" with both. Dispose the
    ///   mapped font, then delete the file.
    ///
    /// Expected:
    /// - The same glyphs; the same family and units per em; the file can be deleted after the dispose (no mapping holds it).
    /// </remarks>
    [Fact]
    public void FromFile_ShapesAsTheBytesAndReleasesTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xylemui-font-{Guid.NewGuid():N}.ttf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"), path);
        try
        {
            using var shaper = new TextShaper();
            using Font fromBytes = TestFont.Open();
            ShapedGlyph[] expected = shaper.Shape(fromBytes, "Hello, World! 日本語", TestFont.Japanese).ToArray();
            Font mapped = Font.FromFile(path);
            ShapedGlyph[] glyphs = shaper.Shape(mapped, "Hello, World! 日本語", TestFont.Japanese).ToArray();
            (string, int) names = (mapped.FamilyName, mapped.UnitsPerEm);
            mapped.Dispose();

            Assert.Equal(expected, glyphs);
            Assert.Equal((fromBytes.FamilyName, fromBytes.UnitsPerEm), names);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.False(File.Exists(path));
    }
}
