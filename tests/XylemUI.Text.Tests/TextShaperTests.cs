namespace XylemUI.Text.Tests;

// Shaping with HarfBuzz (S8a, design §4.1). The expected glyph ids and advances were read from the test font with fontTools
// (Fonts/README.md): the advance is hmtx plus the GPOS kern pair value with the next glyph, so they do not come from HarfBuzz.
public sealed class TextShaperTests
{
    /// <summary>
    /// Latin text shapes to the font's glyphs with their advances, kerning included.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font.
    ///
    /// Steps:
    /// - Shape "Hello, World!".
    ///
    /// Expected:
    /// - Glyph ids 41 70 77 77 80 13 1 56 80 83 77 69 2; advances 728 554 284 284 592 278 224 878 606 388 284 620 323 (the "o"
    ///   before "," is 606 in hmtx, kerned by −14); offsets 0; clusters 0 to 12 in order.
    /// </remarks>
    [Fact]
    public void Shape_Latin_MatchesTheFontsGlyphsAndKernedAdvances()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();

        ShapedGlyph[] glyphs = shaper.Shape(font, "Hello, World!", TestFont.Japanese).ToArray();

        Assert.Equal<uint>([41, 70, 77, 77, 80, 13, 1, 56, 80, 83, 77, 69, 2], glyphs.Select(g => g.GlyphId));
        Assert.Equal([728, 554, 284, 284, 592, 278, 224, 878, 606, 388, 284, 620, 323], glyphs.Select(g => g.XAdvance));
        Assert.All(glyphs, g => Assert.Equal((0, 0), (g.XOffset, g.YOffset)));
        Assert.Equal(Enumerable.Range(0, 13), glyphs.Select(g => g.Cluster));
        Assert.Equal(1000, font.UnitsPerEm);
    }

    /// <summary>
    /// Japanese text shapes to the font's glyphs: full-width advances, with the font's kerning between katakana.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font.
    ///
    /// Steps:
    /// - Shape "日本語のテキスト".
    ///
    /// Expected:
    /// - Glyph ids 240 241 242 104 107 105 106 108; advances 1000 except 970 for "キ" before "ス" (kern −30).
    /// </remarks>
    [Fact]
    public void Shape_Japanese_MatchesTheFontsGlyphs()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();

        ShapedGlyph[] glyphs = shaper.Shape(font, "日本語のテキスト", TestFont.Japanese).ToArray();

        Assert.Equal<uint>([240, 241, 242, 104, 107, 105, 106, 108], glyphs.Select(g => g.GlyphId));
        Assert.Equal([1000, 1000, 1000, 1000, 1000, 970, 1000, 1000], glyphs.Select(g => g.XAdvance));
    }

    /// <summary>
    /// A character the font lacks becomes glyph 0 (.notdef) with the .notdef advance, and the glyphs after it keep their ids.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, which has no euro sign.
    ///
    /// Steps:
    /// - Shape "A€B".
    ///
    /// Expected:
    /// - Glyph ids 34, 0, 35; advances 608, 1000 (the .notdef advance), 657; clusters 0, 1, 2.
    /// </remarks>
    [Fact]
    public void Shape_MissingCharacter_IsNotdefAndTheRestIsKept()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();

        ShapedGlyph[] glyphs = shaper.Shape(font, "A€B", TestFont.Japanese).ToArray();

        Assert.Equal<uint>([34, 0, 35], glyphs.Select(g => g.GlyphId));
        Assert.Equal([608, 1000, 657], glyphs.Select(g => g.XAdvance));
        Assert.Equal([0, 1, 2], glyphs.Select(g => g.Cluster));
    }

    /// <summary>
    /// A character outside the Basic Multilingual Plane is one cluster of two UTF-16 code units; the next cluster starts after both.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, which lacks U+1F600 (an emoji, a surrogate pair in UTF-16).
    ///
    /// Steps:
    /// - Shape "A😀B".
    ///
    /// Expected:
    /// - Three glyphs: 34, 0, 35; clusters 0, 1, 3 (UTF-16 offsets, design §4.3).
    /// </remarks>
    [Fact]
    public void Shape_SurrogatePair_ClustersAreUtf16Offsets()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();

        ShapedGlyph[] glyphs = shaper.Shape(font, "A\U0001F600B", TestFont.Japanese).ToArray();

        Assert.Equal<uint>([34, 0, 35], glyphs.Select(g => g.GlyphId));
        Assert.Equal([0, 1, 3], glyphs.Select(g => g.Cluster));
    }

    /// <summary>
    /// Shaping again reuses the shaper's buffers: a longer text than the first array grows it, and the results stay right.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and one shaper.
    ///
    /// Steps:
    /// - Shape "Hello, World!" repeated 10 times (130 glyphs, more than the 64 the shaper starts with), then "A".
    ///
    /// Expected:
    /// - 130 glyphs whose ids repeat the 13 of "Hello, World!"; then one glyph, 34.
    /// </remarks>
    [Fact]
    public void Shape_Again_ReusesAndGrowsTheBuffers()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();

        uint[] long10 = shaper
            .Shape(font, string.Concat(Enumerable.Repeat("Hello, World!", 10)), TestFont.Japanese)
            .ToArray()
            .Select(g => g.GlyphId)
            .ToArray();
        ShapedGlyph[] single = shaper.Shape(font, "A", TestFont.Japanese).ToArray();

        Assert.Equal(130, long10.Length);
        Assert.Equal(long10.Take(13), long10.Skip(117));
        Assert.Equal(34u, Assert.Single(single).GlyphId);
    }

    /// <summary>
    /// The given language picks the font's language forms (locl), whatever the process locale is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font. Its Latin script has a default language system whose locl maps the digits to other glyphs ("one" 18 →
    ///   356), and a Japanese one (JAN) whose locl maps the quotes instead ("quotedbl" 3 → 349) (read with fontTools).
    ///
    /// Steps:
    /// - Shape "A1\"" in "ja", then in "en".
    ///
    /// Expected:
    /// - "ja": glyph ids 34 18 349. "en": 34 356 3.
    /// </remarks>
    [Fact]
    public void Shape_Language_PicksTheFontsLanguageForms()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();

        uint[] japanese = shaper.Shape(font, "A1\"", TextLanguage.FromTag("ja")).ToArray().Select(g => g.GlyphId).ToArray();
        uint[] english = shaper.Shape(font, "A1\"", TextLanguage.FromTag("en")).ToArray().Select(g => g.GlyphId).ToArray();

        Assert.Equal<uint>([34, 18, 349], japanese);
        Assert.Equal<uint>([34, 356, 3], english);
    }

    /// <summary>
    /// A language is the same value for the same tag, in any case, and keeps its tag; the default value is refused by shaping.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font.
    ///
    /// Steps:
    /// - Make "ja-JP" and "JA-jp"; make "en"; shape "A" with the default language; make a blank tag and a non-ASCII tag.
    ///
    /// Expected:
    /// - The first two are equal, with tag "ja-jp"; "en" differs from them; the default language and both tags throw
    ///   ArgumentException.
    /// </remarks>
    [Fact]
    public void FromTag_SameTag_IsTheSameLanguage()
    {
        using Font font = TestFont.Open();
        using var shaper = new TextShaper();

        TextLanguage a = TextLanguage.FromTag("ja-JP");
        TextLanguage b = TextLanguage.FromTag("JA-jp");

        Assert.Equal(a, b);
        Assert.Equal("ja-jp", a.Tag);
        Assert.NotEqual(a, TextLanguage.FromTag("en"));
        Assert.Throws<ArgumentException>(() => shaper.Shape(font, "A", default).ToArray());
        Assert.Throws<ArgumentException>(() => TextLanguage.FromTag(" "));
        Assert.Throws<ArgumentException>(() => TextLanguage.FromTag("日本"));
    }
}
