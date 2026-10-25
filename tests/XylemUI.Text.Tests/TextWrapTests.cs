using XylemUI.Text.LineBreak;

namespace XylemUI.Text.Tests;

// The wrapping of shaped text and its content widths (roadmap row T1b, design §4.1, "Shaping and line breaking" and "Line breaking
// and grapheme clusters"). The test font is shaped at 1,000 px per em, so a font unit is a pixel. The expected widths come from the
// font's hmtx and kern values read with fontTools (n 610, x 498, the hyphen 347, T 599, the space 224; the kerning of the hyphen
// before T is -55, of T before o -76), not from HarfBuzz.
public sealed class TextWrapTests : IDisposable
{
    private const float Size = 1000;
    private const int N = 610;
    private const int Hyphen = 347;
    private const int T = 599;
    private const int Space = 224;
    private const int HyphenBeforeT = -55;

    private static readonly float[] Widths = [3000, 7000];

    private readonly Font _font = TestFont.Open();
    private readonly TextShaper _shaper = new();
    private readonly LineBreaker _breaker = new();
    private readonly ShapingResult _shaped = new();
    private readonly LineBreakResult _lines = new();
    private readonly LineLayoutResult _laid = new();

    public void Dispose()
    {
        _shaper.Dispose();
        _font.Dispose();
    }

    /// <summary>
    /// A glyph whose cluster a break would change the shaping before is marked unsafe to break: T after a kerned hyphen; n after n,
    /// with no kerning, is not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font.
    ///
    /// Steps:
    /// - Shape "-T" and "nn".
    ///
    /// Expected:
    /// - The T glyph is marked UnsafeToBreak, the hyphen not; neither n is.
    /// </remarks>
    [Fact]
    public void Shape_KernedPair_MarksTheSecondGlyphUnsafeToBreak()
    {
        ShapedGlyph[] kerned = _shaper.Shape(_font, "-T", TestFont.Japanese).ToArray();
        ShapedGlyph[] plain = _shaper.Shape(_font, "nn", TestFont.Japanese).ToArray();

        Assert.Equal([false, true], kerned.Select(g => g.UnsafeToBreak));
        Assert.Equal([false, false], plain.Select(g => g.UnsafeToBreak));
        Assert.Equal(Hyphen + HyphenBeforeT, kerned[0].XAdvance);
    }

    /// <summary>
    /// Spaces at a line's end hang past it: the line ends after them, and its width leaves them out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn nn" shaped at 1,000 px.
    ///
    /// Steps:
    /// - Wrap at 1,300 px and lay the lines out.
    ///
    /// Expected:
    /// - Two lines, [0, 3) and [3, 5). The first is 1,220 px wide without its space and 1,444 with it; the second is 1,220.
    /// </remarks>
    [Fact]
    public void Wrap_SpacesAtTheEnd_HangPastTheLine()
    {
        LaidLine[] lines = Lay("nn nn", 1300);

        Assert.Equal([(0, 3), (3, 5)], lines.Select(l => (l.Span.Start, l.Span.End)));
        Assert.Equal(2 * N, lines[0].Width);
        Assert.Equal((2 * N) + Space, lines[0].WidthWithSpaces);
        Assert.Equal(2 * N, lines[1].Width);
    }

    /// <summary>
    /// A word wider than the line is cut at grapheme cluster boundaries, as many as fit and one at least.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nnnn" shaped at 1,000 px; at 300 px, also "n".
    ///
    /// Steps:
    /// - Wrap "nnnn" at 1,300 px; wrap "n" at 300 px (narrower than its one cluster).
    ///
    /// Expected:
    /// - Lines [0, 2), cut in an emergency, and [2, 4), which fits and ends the text. "n" is one line [0, 1), cut in an emergency at
    ///   the end of the text, mandatory.
    /// </remarks>
    [Fact]
    public void Wrap_WordWiderThanTheLine_IsCutAtGraphemes()
    {
        _ = Lay("nnnn", 1300);
        LineSpan[] word = [.. _lines.Lines];
        _ = Lay("n", 300);

        Assert.Equal([new LineSpan(0, 2, false, false, true), new LineSpan(2, 4, false, true, false)], word);
        Assert.Equal([new LineSpan(0, 1, false, true, true)], _lines.Lines.ToArray());
    }

    /// <summary>
    /// A hard line break always ends a line, and hangs past it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn\nnn" shaped at 1,000 px.
    ///
    /// Steps:
    /// - Wrap at 10,000 px.
    ///
    /// Expected:
    /// - Lines [0, 3) and [3, 5), both mandatory; the first is 1,220 px wide, without its line feed.
    /// </remarks>
    [Fact]
    public void Wrap_HardLineBreak_EndsTheLine()
    {
        LaidLine[] lines = Lay("nn\nnn", 10_000);

        Assert.Equal([new LineSpan(0, 3, false, true, false), new LineSpan(3, 5, false, true, false)], lines.Select(l => l.Span));
        Assert.Equal(2 * N, lines[0].Width);
    }

    /// <summary>
    /// A soft hyphen the line breaks at is drawn as a hyphen and counted in the fit; one the line does not break at is not drawn and
    /// has no width.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn" + U+00AD + "nn" shaped at 1,000 px (the test font has no glyph for the soft hyphen).
    ///
    /// Steps:
    /// - Wrap at 1,600 px and lay out; wrap at 3,000 px and lay out.
    ///
    /// Expected:
    /// - At 1,600: lines [0, 3), hyphenated, and [3, 5). The first is 1,567 px wide (two n and the hyphen) and its last glyph is the
    ///   hyphen's. At 3,000: one line [0, 5), 2,440 px wide, with no hyphen glyph.
    /// </remarks>
    [Fact]
    public void Wrap_SoftHyphen_IsAHyphenOnlyWhereTheLineBreaks()
    {
        uint hyphenGlyph = _shaper.Shape(_font, "-", TestFont.Japanese)[0].GlyphId;

        LaidLine[] broken = Lay("nn\u00ADnn", 1600);
        uint lastGlyph = _laid.Glyphs[broken[0].GlyphStart + broken[0].GlyphCount - 1].GlyphId;
        LaidLine[] whole = Lay("nn\u00ADnn", 3000);
        uint[] wholeGlyphs = [.. _laid.Glyphs.ToArray().Select(g => g.GlyphId)];

        Assert.Equal([new LineSpan(0, 3, true, false, false), new LineSpan(3, 5, false, true, false)], broken.Select(l => l.Span));
        Assert.Equal((2 * N) + Hyphen, broken[0].Width);
        Assert.Equal(hyphenGlyph, lastGlyph);
        Assert.Equal([new LineSpan(0, 5, false, true, false)], whole.Select(l => l.Span));
        Assert.Equal(4 * N, whole[0].Width);
        Assert.DoesNotContain(hyphenGlyph, wholeGlyphs);
    }

    /// <summary>
    /// A soft hyphen whose hyphen does not fit is not taken: the line ends at the opportunity before it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "n nn" + U+00AD + "nn" shaped at 1,000 px.
    ///
    /// Steps:
    /// - Wrap at 2,200 px: the first line up to the soft hyphen is 2,054 px without the hyphen and 2,401 with it.
    ///
    /// Expected:
    /// - Lines [0, 2), [2, 5) hyphenated (1,567 px with the hyphen), and [5, 7): the first line does not take the soft hyphen, and is
    ///   not cut in an emergency.
    /// </remarks>
    [Fact]
    public void Wrap_SoftHyphenThatDoesNotFit_FallsBackToTheOpportunityBefore()
    {
        _ = Lay("n nn\u00ADnn", 2200);

        Assert.Equal(
            [new LineSpan(0, 2, false, false, false), new LineSpan(2, 5, true, false, false), new LineSpan(5, 7, false, true, false)],
            _lines.Lines.ToArray()
        );
    }

    /// <summary>
    /// A break where the shaping changes is shaped again at both ends of the lines: the hyphen before T loses its kerning at the end
    /// of its line, and T keeps no kerning at the start of the next.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn-TT" shaped at 1,000 px: the opportunity after the hyphen, where the hyphen is kerned against T by -55.
    ///
    /// Steps:
    /// - Wrap at 1,600 px and lay out.
    ///
    /// Expected:
    /// - Lines [0, 3) and [3, 5). The first is 1,567 px wide (Pass1 counted 1,512, with the kerning); the second is 1,198 (two T).
    /// </remarks>
    [Fact]
    public void Lay_BreakAcrossAKernedPair_ShapesBothEndsAgain()
    {
        LaidLine[] lines = Lay("nn-TT", 1600);

        Assert.Equal([(0, 3), (3, 5)], lines.Select(l => (l.Span.Start, l.Span.End)));
        Assert.Equal((2 * N) + Hyphen, lines[0].Width);
        Assert.Equal(2 * T, lines[1].Width);
    }

    /// <summary>
    /// A line that Pass2 makes wider than the width is cut once at a grapheme cluster boundary, and the text is not broken again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn-TT" shaped at 1,000 px.
    ///
    /// Steps:
    /// - Wrap at 1,540 px (Pass1 fits "nn-" at 1,512) and lay out.
    ///
    /// Expected:
    /// - Pass1 gives [0, 3) and [3, 5). Pass2 makes the first 1,567 px, so it is cut after "nn": the laid lines are [0, 2) (an
    ///   emergency cut, 1,220 px), [2, 3) (the hyphen, 347 px), and [3, 5) (1,198 px).
    /// </remarks>
    [Fact]
    public void Lay_LineWiderAfterPass2_IsCutOnce()
    {
        LaidLine[] lines = Lay("nn-TT", 1540);

        Assert.Equal([(0, 3), (3, 5)], _lines.Lines.ToArray().Select(l => (l.Start, l.End)));
        Assert.Equal([(0, 2, true), (2, 3, false), (3, 5, false)], lines.Select(l => (l.Span.Start, l.Span.End, l.Span.Emergency)));
        Assert.Equal([2 * N, Hyphen, 2 * T], lines.Select(l => l.Width));
    }

    /// <summary>
    /// The least content width is the widest piece between opportunities, a soft hyphen counting its hyphen; the most is the widest
    /// line between hard line breaks; spaces at their ends do not count.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn nnn" + U+00AD + "nn\nn" shaped at 1,000 px.
    ///
    /// Steps:
    /// - Read the content widths.
    ///
    /// Expected:
    /// - The least is 2,177 px ("nnn" and the hyphen); the most is 4,494 ("nn nnn" + U+00AD + "nn", without the line feed).
    /// </remarks>
    [Fact]
    public void IntrinsicWidths_PiecesAndLines_AreTheWidest()
    {
        const string Text = "nn nnn\u00ADnn\nn";
        Shape(Text);

        (float min, float max) = _shaped.IntrinsicWidths(_shaper);

        Assert.Equal((3 * N) + Hyphen, min);
        Assert.Equal((2 * N) + Space + (5 * N), max);
    }

    /// <summary>
    /// Wrapping and laying out the same shaped text at alternating widths allocate nothing once the arrays have grown.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Japanese and English text with spaces, a soft hyphen, and a kerned break, shaped once and laid out at both widths once.
    ///
    /// Steps:
    /// - 1,000 times: wrap at 3,000 px and at 7,000 px, laying out each.
    ///
    /// Expected:
    /// - 0 bytes on this thread.
    /// </remarks>
    [Fact]
    public void SteadyState_WrapAndLay_AllocateNothing()
    {
        const string Text = "日本語のテキスト nn-TT and nn\u00ADnn 日本語のテキスト nn-TT and more text";
        Shape(Text);

        void Run()
        {
            foreach (float width in Widths)
            {
                _shaped.Wrap(width, _shaper, _lines);
                _shaped.Lay(_lines, _shaper, _laid);
            }
        }

        Run();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            Run();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(_laid.Lines.Length > 1);
    }

    private void Shape(string text)
    {
        FontRun[] runs = [new FontRun(_font, 0, text.Length, 1)];
        _shaped.Shape(text, runs, Size, TestFont.Japanese, _shaper, _breaker);
    }

    private LaidLine[] Lay(string text, float width)
    {
        Shape(text);
        _shaped.Wrap(width, _shaper, _lines);
        _shaped.Lay(_lines, _shaper, _laid);
        return _laid.Lines.ToArray();
    }
}
