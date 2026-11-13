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
    private static readonly float[] Sizes = [11, 12.5f, 13.3f, 14, 15.7f, 17.3f];

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
    /// A glyph whose cluster a break would change the shaping before is marked unsafe to break, in the flags the shaper writes for
    /// the line breaking: T after a kerned hyphen; n after n, with no kerning, is not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font; two empty flag arrays.
    ///
    /// Steps:
    /// - Shape "-T" and "nn" with the flags written from index 0.
    ///
    /// Expected:
    /// - The flags of "-T" are [false, true] (the T marked, the hyphen not), and those of "nn" [false, false]; the hyphen keeps its
    ///   kerning, 347 − 55 units.
    /// </remarks>
    [Fact]
    public void ShapeRange_KernedPair_MarksTheSecondGlyphUnsafeToBreak()
    {
        bool[] kerned = [];
        bool[] plain = [];

        ShapedGlyph[] glyphs = _shaper.ShapeRange(_font, "-T", 0, 2, TestFont.Japanese, null, ref kerned, 0).ToArray();
        _ = _shaper.ShapeRange(_font, "nn", 0, 2, TestFont.Japanese, null, ref plain, 0);

        Assert.Equal([false, true], kerned);
        Assert.Equal([false, false], plain);
        Assert.Equal(Hyphen + HyphenBeforeT, glyphs[0].XAdvance);
    }

    /// <summary>
    /// The shaper writes the flags of a part's glyphs after the flags at the index it is given: an array too short grows and keeps
    /// the flags before, and one long enough is written in place, its other flags kept.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The flag arrays [true] and [true, true, true, true].
    ///
    /// Steps:
    /// - Shape "-T" with the flags written from index 1 into each.
    ///
    /// Expected:
    /// - The short array became [true, false, true]; the long one is the same array, [true, false, true, true].
    /// </remarks>
    [Fact]
    public void ShapeRange_FlagsFromAnIndex_KeepTheOtherFlags()
    {
        bool[] shortFlags = [true];
        bool[] longFlags = [true, true, true, true];
        bool[] longBefore = longFlags;

        _ = _shaper.ShapeRange(_font, "-T", 0, 2, TestFont.Japanese, null, ref shortFlags, 1);
        _ = _shaper.ShapeRange(_font, "-T", 0, 2, TestFont.Japanese, null, ref longFlags, 1);

        Assert.Equal([true, false, true], shortFlags);
        Assert.Same(longBefore, longFlags);
        Assert.Equal([true, false, true, true], longFlags);
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
    /// The flags of a font run after the first are those of its own glyphs: a break before a kerned glyph of the second run is
    /// shaped again, as one in a text of one run is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nnnn-TT" shaped at 1,000 px as two font runs of the test font, [0, 2) and [2, 7): the T after the hyphen, kerned by -55,
    ///   is the sixth glyph and the fourth of the second run.
    ///
    /// Steps:
    /// - Wrap at 2,800 px (Pass1 fits "nnnn-" at 2,732) and lay out.
    ///
    /// Expected:
    /// - Lines [0, 5) and [5, 7). The first is 2,787 px wide (four n and the hyphen without its kerning); the second is 1,198 (two T).
    /// </remarks>
    [Fact]
    public void Lay_KernedPairInTheSecondRun_ShapesTheLineEndAgain()
    {
        const string Text = "nnnn-TT";
        var style = new RunStyle(Size, TestFont.Japanese, 0, null);
        _shaped.Shape(Text, [new FontRun(_font, 0, 2, 1), new FontRun(_font, 2, 5, 1)], [style, style], _shaper, _breaker);
        _shaped.Wrap(2800, _shaper, _lines);
        _shaped.Lay(_lines, _shaper, _laid);
        LaidLine[] lines = _laid.Lines.ToArray();

        Assert.Equal([(0, 5), (5, 7)], lines.Select(l => (l.Span.Start, l.Span.End)));
        Assert.Equal((4 * N) + Hyphen, lines[0].Width);
        Assert.Equal(2 * T, lines[1].Width);
    }

    /// <summary>
    /// The room of an inline object is safe to break before, whatever flag its glyph's index held for an earlier text: a line that
    /// starts at it takes its room, not a glyph shaped again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "n-T" shaped at 1,000 px, which marks its third glyph, the T, unsafe to break; then, with the same result, "n " + U+FFFC
    ///   with a room of 500 px at 2, a font run of its own, so the room is the third glyph.
    ///
    /// Steps:
    /// - Wrap the second text at 1,000 px (the whole is 1,334 px) and lay out.
    ///
    /// Expected:
    /// - Lines [0, 2) and [2, 3). The second has one glyph, the room's placeholder, 500 px wide.
    /// </remarks>
    [Fact]
    public void Lay_RoomAtAnIndexAnEarlierTextMarked_IsNotShapedAgain()
    {
        const string Text = "n ￼";
        Shape("n-T");
        var style = new RunStyle(Size, TestFont.Japanese, 0, null);
        _shaped.Shape(Text, [new FontRun(_font, 0, 2, 1), new FontRun(_font, 2, 1, 1)], [style, style], _shaper, _breaker, [2], [500]);
        _shaped.Wrap(1000, _shaper, _lines);
        _shaped.Lay(_lines, _shaper, _laid);
        LaidLine[] lines = _laid.Lines.ToArray();
        LaidGlyph room = _laid.Glyphs[lines[1].GlyphStart];

        Assert.Equal([(0, 2), (2, 3)], lines.Select(l => (l.Span.Start, l.Span.End)));
        Assert.Equal(1, lines[1].GlyphCount);
        Assert.True(room.IsPlaceholder);
        Assert.Equal(500, room.Advance);
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
    /// A text broken at its own most content width at a fractional size keeps its lines: Pass2 measures each line as a sum from its
    /// start where Pass1 measures a difference of running sums, and the last bit they differ in does not cut the widest line.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The texts "nn\nnnnnnnnnnn", "n nn\nnnnn nnnnnnn", and "日本語\nのテキスト日本語", each shaped at 11, 12.5, 13.3, 14, 15.7, and
    ///   17.3 px.
    ///
    /// Steps:
    /// - Read each text's most content width; wrap at it and lay the lines out.
    ///
    /// Expected:
    /// - The laid lines are the wrapped lines, and none is cut in an emergency.
    /// </remarks>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("nn\nnnnnnnnnnn")]
    [InlineData("n nn\nnnnn nnnnnnn")]
    [InlineData("日本語\nのテキスト日本語")]
    public void Lay_AtTheMostContentWidth_KeepsTheLines(string text)
    {
        foreach (float size in Sizes)
        {
            _shaped.Shape(text, [new FontRun(_font, 0, text.Length, 1)], [new RunStyle(size, TestFont.Japanese, 0, null)], _shaper, _breaker);
            (_, float max) = _shaped.IntrinsicWidths(_shaper);
            _shaped.Wrap(max, _shaper, _lines);
            _shaped.Lay(_lines, _shaper, _laid);

            Assert.Equal(_lines.Lines.ToArray(), _laid.Lines.ToArray().Select(l => l.Span));
            Assert.DoesNotContain(_laid.Lines.ToArray(), l => l.Span.Emergency);
        }
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
    /// A width less than a line by no more than the tolerance (1/64 px) still fits the line, in Pass1 and in the emergency cut: a
    /// width that came back from a layout less its padding can be one bit under the line (PR #301's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - "nn nn" (1,220 + 224 + 1,220 px) and "nnnnnn" (6 × 610 px) at 1,000 px.
    ///
    /// Steps:
    /// - Wrap "nn nn" at 2,664 − 0.01 px, and "nnnnnn" at 1,830 − 0.01 px and at 1,830 − 0.1 px.
    ///
    /// Expected:
    /// - "nn nn" is one line. "nnnnnn" is cut after 3 clusters at 1,830 − 0.01 px, and after 2 at 1,830 − 0.1 px (past the tolerance).
    /// </remarks>
    [Fact]
    public void Wrap_WidthUnderTheLineWithinTheTolerance_StillFits()
    {
        Shape("nn nn");
        _shaped.Wrap((4 * N) + Space - 0.01f, _shaper, _lines);
        Assert.Equal([(0, 5)], _lines.Lines.ToArray().Select(l => (l.Start, l.End)));

        Shape("nnnnnn");
        _shaped.Wrap((3 * N) - 0.01f, _shaper, _lines);
        Assert.Equal(3, _lines.Lines[0].End);
        _shaped.Wrap((3 * N) - 0.1f, _shaper, _lines);
        Assert.Equal(2, _lines.Lines[0].End);
    }

    /// <summary>
    /// Wrapping and laying out the same shaped text at alternating widths allocate nothing once the arrays have grown.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Japanese and English text with spaces, a soft hyphen, and a kerned break, shaped once and laid out at both widths once.
    ///
    /// Steps:
    /// - 1,000 times, in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: wrap at 3,000 px and at 7,000 px, laying out each.
    ///
    /// Expected:
    /// - 0 bytes on this thread in every window. A failure lists each window's bytes, the methods compiled on this thread, and the
    ///   collections (Issue #92).
    /// </remarks>
    [Fact]
    public void SteadyState_WrapAndLay_AllocateNothing()
    {
        const string Text = "日本語のテキスト nn-TT and nn\u00ADnn 日本語のテキスト nn-TT and more text";
        Shape(Text);

        void Run(int index)
        {
            foreach (float width in Widths)
            {
                _shaped.Wrap(width, _shaper, _lines);
                _shaped.Lay(_lines, _shaper, _laid);
            }
        }

        Run(0);
        PerformanceAssert.DoesNotAllocate(1_000, Run);
        Assert.True(_laid.Lines.Length > 1);
    }

    private void Shape(string text)
    {
        FontRun[] runs = [new FontRun(_font, 0, text.Length, 1)];
        _shaped.Shape(text, runs, [new RunStyle(Size, TestFont.Japanese, 0, null)], _shaper, _breaker);
    }

    private LaidLine[] Lay(string text, float width)
    {
        Shape(text);
        _shaped.Wrap(width, _shaper, _lines);
        _shaped.Lay(_lines, _shaper, _laid);
        return _laid.Lines.ToArray();
    }
}
