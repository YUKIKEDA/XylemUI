namespace XylemUI.Text.Tests;

// The end of the font fallback (design §4.5, roadmap row F1): the bundled font and the split of a text into font runs. The
// bundled font's glyph ids and advances were read with fontTools (TTFont.getGlyphID, hmtx; src/XylemUI.Text/Fonts/README.md),
// not with HarfBuzz.
public sealed class FontFallbackTests
{
    /// <summary>
    /// The bundled font holds printable ASCII, and every other character shapes to its .notdef, 1 em wide.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bundled font (FontFallback.Bundled).
    ///
    /// Steps:
    /// - Shape "A", " ", "W", U+0378 (unassigned), "€", and "あ", one at a time.
    ///
    /// Expected:
    /// - Glyph 34 with advance 639, glyph 1 with 260, glyph 56 with 930; the other three are glyph 0 (.notdef) with advance 1000,
    ///   the font's units per em. The family is "XylemUI Fallback".
    /// </remarks>
    [Fact]
    public void Bundled_HoldsAsciiAndANotdefOneEmWide()
    {
        Font font = FontFallback.Bundled;
        using var shaper = new TextShaper();

        string[] texts = ["A", " ", "W", "͸", "€", "あ"];
        (uint, int)[] glyphs = texts
            .Select(text => shaper.Shape(font, text, TestFont.Japanese).ToArray().Single())
            .Select(glyph => (glyph.GlyphId, glyph.XAdvance))
            .ToArray();

        Assert.Equal<(uint, int)>([(34, 639), (1, 260), (56, 930), (0, 1000), (0, 1000), (0, 1000)], glyphs);
        Assert.Equal(1000, font.UnitsPerEm);
        Assert.Equal("XylemUI Fallback", font.FamilyName);
    }

    /// <summary>
    /// Threads that ask for the bundled font at once get the same font (Issue #303).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 16 threads held at a barrier. When this test is the first of the process to ask, the threads race to open the font; when
    ///   another test asked before, they read it.
    ///
    /// Steps:
    /// - Release the threads; each reads FontFallback.Bundled.
    ///
    /// Expected:
    /// - The 16 fonts are one instance, which is not disposed.
    /// </remarks>
    [Fact]
    public void Bundled_AskedByManyThreadsAtOnce_IsOneFont()
    {
        const int Threads = 16;
        var fonts = new Font[Threads];
        using var barrier = new Barrier(Threads);
        var threads = new Thread[Threads];
        for (int i = 0; i < Threads; i++)
        {
            int index = i;
            threads[i] = new Thread(() =>
            {
                barrier.SignalAndWait();
                fonts[index] = FontFallback.Bundled;
            });
            threads[i].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.All(fonts, font => Assert.Same(fonts[0], font));
        Assert.False(fonts[0].IsDisposed);
    }

    /// <summary>
    /// The split takes each match of the provider in order, joins neighbouring matches of the same font and scale, and puts a
    /// range without a font into the bundled font.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A provider that answers from a script: 2 characters in font A, 1 in A, 2 in A at scale 1.5, 1 without a font, 1 without
    ///   a font, 3 in B. The text is 10 characters long.
    ///
    /// Steps:
    /// - Split the text into a list that holds an old run.
    ///
    /// Expected:
    /// - Four runs: A 0 + 3 at 1, A 3 + 2 at 1.5, the bundled font 5 + 2 at 1, B 7 + 3 at 1. The old run is gone, and the provider
    ///   was asked at 0, 2, 3, 5, 6, and 7.
    /// </remarks>
    [Fact]
    public void Split_JoinsMatchesOfOneFontAndScale()
    {
        using Font a = TestFont.Open();
        using Font b = TestFont.Open();
        var provider = new ScriptedProvider([
            new FontMatch(a, 2, 1),
            new FontMatch(a, 1, 1),
            new FontMatch(a, 2, 1.5f),
            new FontMatch(null, 1, 1),
            new FontMatch(null, 1, 1),
            new FontMatch(b, 3, 1),
        ]);
        var runs = new List<FontRun> { new(b, 9, 9, 9) };

        FontFallback.Split(provider, "0123456789", TestFont.Japanese, FontRequest.Default, runs);

        Assert.Equal(
            [new FontRun(a, 0, 3, 1), new FontRun(a, 3, 2, 1.5f), new FontRun(FontFallback.Bundled, 5, 2, 1), new FontRun(b, 7, 3, 1)],
            runs
        );
        Assert.Equal([0, 2, 3, 5, 6, 7], provider.Starts);
    }

    /// <summary>
    /// A provider that matches no character, or more characters than are left, stops the split instead of looping or reading past
    /// the text.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two providers: one that answers a length of 0, one that answers 5 for a text of 3.
    ///
    /// Steps:
    /// - Split "abc" with each.
    ///
    /// Expected:
    /// - Both throw InvalidOperationException.
    /// </remarks>
    [Fact]
    public void Split_ProviderOutOfBounds_Throws()
    {
        using Font font = TestFont.Open();
        var runs = new List<FontRun>();

        Assert.Throws<InvalidOperationException>(() =>
            FontFallback.Split(new ScriptedProvider([new FontMatch(font, 0, 1)]), "abc", TestFont.Japanese, FontRequest.Default, runs)
        );
        Assert.Throws<InvalidOperationException>(() =>
            FontFallback.Split(new ScriptedProvider([new FontMatch(font, 5, 1)]), "abc", TestFont.Japanese, FontRequest.Default, runs)
        );
    }

    /// <summary>
    /// A weight's default is the regular weight, and the bounds of CSS's font-weight hold.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Read default(FontWeight); make weights of 1, 999, 0, and 1000.
    ///
    /// Expected:
    /// - The default equals FontWeight.Normal (400); 1 and 999 keep their values; 0 and 1000 throw ArgumentOutOfRangeException.
    /// </remarks>
    [Fact]
    public void FontWeight_DefaultIsNormalAndBoundsHold()
    {
        Assert.Equal(FontWeight.Normal, default);
        Assert.Equal(400, default(FontWeight).Value);
        Assert.Equal((1, 999), (new FontWeight(1).Value, new FontWeight(999).Value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FontWeight(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FontWeight(1000));
    }

    // Answers the matches in order, and records where it was asked.
    /// <summary>
    /// A match that ends inside a grapheme cluster is taken on to the cluster's end, so the font of the cluster's base character
    /// draws the whole cluster, and the provider is asked again after it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A provider that answers from a script: 1 character in font A, then 1 in B, then 3 in B. The text is "e" + U+0301 (a
    ///   combining acute) + "x" + the family emoji 👨‍👩‍👧 (8 code units, joined by ZWJ), cut by the script inside both clusters.
    ///
    /// Steps:
    /// - Split the text.
    ///
    /// Expected:
    /// - A 0 + 2 (the e and its accent), B 2 + 9 (x, and the family, which the second match of 3 ends inside). The provider was
    ///   asked at 0, 2, and 3.
    /// </remarks>
    [Fact]
    public void Split_MatchEndingInsideACluster_TakesTheWholeCluster()
    {
        using Font a = TestFont.Open();
        using Font b = TestFont.Open();
        var provider = new ScriptedProvider([new(a, 1, 1), new(b, 1, 1), new(b, 3, 1)]);
        var runs = new List<FontRun>();

        FontFallback.Split(provider, "e\u0301x👨‍👩‍👧", TestFont.Japanese, FontRequest.Default, runs);

        Assert.Equal([new FontRun(a, 0, 2, 1), new FontRun(b, 2, 9, 1)], runs);
        Assert.Equal([0, 2, 3], provider.Starts);
    }

    private sealed class ScriptedProvider(FontMatch[] script) : IFontProvider
    {
        private int _next;

        public List<int> Starts { get; } = [];

        public FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request)
        {
            Starts.Add(start);
            return script[_next++];
        }
    }
}
