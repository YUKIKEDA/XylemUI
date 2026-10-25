using XylemUI.Text.Unicode;

namespace XylemUI.Text.LineBreak;

/// <summary>
/// A text shaped once, run by run of its fonts, with its line break opportunities (design §4.1, "Shaping and line breaking", and
/// "Line breaking and grapheme clusters"; cached while the text does not change, §3.2's <c>ShapedTextCache</c>). It breaks the text
/// into lines at a width (<see cref="Wrap"/>, Pass1), lays the lines out for drawing (<see cref="Lay"/>, Pass2), and gives the
/// text's content widths (<see cref="IntrinsicWidths"/>).
/// </summary>
/// <remarks>
/// The text is shaped once and broken again for each width, as skparagraph, Avalonia's <c>TextRunCache</c>, and GPUI do. The
/// position of every code unit is kept as a running sum of the advances, so the width of any part of the text is one
/// subtraction. Arrays are kept and grow only when a text needs more: a steady state allocates nothing. An instance serves one
/// thread, as the <see cref="TextShaper"/> and the <see cref="LineBreaker"/> it is used with do.
/// </remarks>
internal sealed class ShapingResult
{
    private const char SoftHyphen = '\u00AD';
    private const string Hyphen = "-";

    private char[] _text = new char[64];
    private char[] _scratch = new char[64];
    private FontRun[] _runs = new FontRun[4];
    private float[] _scales = new float[4];
    private float[] _hyphens = new float[4];
    private LaidGlyph[] _glyphs = new LaidGlyph[64];
    private bool[] _unsafe = new bool[64];
    private int[] _glyphAt = new int[65];
    private float[] _x = new float[65];
    private LineBreakOpportunity[] _breaks = new LineBreakOpportunity[16];
    private int _runCount;
    private int _glyphCount;
    private int _breakCount;
    private TextLanguage _language;

    /// <summary>
    /// Gets the number of code units of the text.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Gets the font runs the text was shaped with.
    /// </summary>
    public ReadOnlySpan<FontRun> Runs => _runs.AsSpan(0, _runCount);

    /// <summary>
    /// Gets the size, in logical pixels per em, the text was shaped at (a run's scale multiplies it).
    /// </summary>
    public float Size { get; private set; }

    /// <summary>
    /// Shapes a text, run by run of its fonts, and finds its line break opportunities.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="runs">Its font runs, from <see cref="FontFallback.Split"/>: in order, covering the text.</param>
    /// <param name="size">The size in logical pixels per em.</param>
    /// <param name="language">The language of the text.</param>
    /// <param name="shaper">The shaper, of this thread.</param>
    /// <param name="breaker">The line breaker, of this thread.</param>
    /// <exception cref="ArgumentException">The runs do not cover the text in order.</exception>
    public void Shape(ReadOnlySpan<char> text, ReadOnlySpan<FontRun> runs, float size, TextLanguage language, TextShaper shaper, LineBreaker breaker)
    {
        ArgumentNullException.ThrowIfNull(shaper);
        ArgumentNullException.ThrowIfNull(breaker);
        Length = text.Length;
        Size = size;
        _language = language;
        Ensure(ref _text, text.Length);
        text.CopyTo(_text);
        Ensure(ref _x, text.Length + 1);
        Ensure(ref _glyphAt, text.Length + 1);
        Array.Fill(_x, 0, 0, text.Length + 1);
        Array.Fill(_glyphAt, -1, 0, text.Length + 1);
        _runCount = 0;
        _glyphCount = 0;
        int covered = 0;
        foreach (FontRun run in runs)
        {
            if (run.Start != covered || run.Length <= 0)
            {
                throw new ArgumentException("The font runs do not cover the text in order.", nameof(runs));
            }

            covered += run.Length;
            AddRun(run);
            float scale = Scale(run);
            foreach (ShapedGlyph glyph in shaper.ShapeRange(run.Font, text, run.Start, run.Length, language))
            {
                AddGlyph(
                    new LaidGlyph(glyph.GlyphId, glyph.Cluster, glyph.XAdvance * scale, glyph.XOffset * scale, glyph.YOffset * scale, _runCount - 1),
                    glyph.UnsafeToBreak
                );
            }
        }

        if (covered != text.Length)
        {
            throw new ArgumentException("The font runs do not cover the text in order.", nameof(runs));
        }

        // The advance of each cluster goes to its first code unit; the running sum puts every code unit at the left of the next
        // cluster after it, so the width of a range between clusters is one subtraction.
        for (int g = 0; g < _glyphCount; g++)
        {
            _x[_glyphs[g].Cluster + 1] += _glyphs[g].Advance;
        }

        for (int i = 1; i <= text.Length; i++)
        {
            _x[i] += _x[i - 1];
        }

        ReadOnlySpan<LineBreakOpportunity> breaks = breaker.Find(text);
        Ensure(ref _breaks, breaks.Length);
        breaks.CopyTo(_breaks);
        _breakCount = breaks.Length;
    }

    /// <summary>
    /// Breaks the text into lines no wider than a width (Pass1): at each line, the last break opportunity that fits. Spaces and hard
    /// line breaks at a line's end do not count; a soft hyphen counts its hyphen. With no opportunity that fits (a word wider than the
    /// line), the line is cut at the last grapheme cluster boundary that fits, and after one cluster at least. A hard line break
    /// always ends a line.
    /// </summary>
    /// <param name="width">The width, in logical pixels.</param>
    /// <param name="shaper">The shaper, of this thread, for the width of a hyphen.</param>
    /// <param name="result">The lines; reset first.</param>
    public void Wrap(float width, TextShaper shaper, LineBreakResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Reset(width);
        int lineStart = 0;
        int lastFit = -1;
        bool lastFitHyphen = false;
        int k = 0;
        while (k < _breakCount)
        {
            LineBreakOpportunity opportunity = _breaks[k];
            bool hyphen = !opportunity.Mandatory && EndsWithSoftHyphen(opportunity.Position);
            float lineWidth = TrimmedWidth(lineStart, opportunity.Position) + (hyphen ? HyphenWidth(opportunity.Position - 1, shaper) : 0);
            if (lineWidth <= width)
            {
                if (opportunity.Mandatory)
                {
                    result.Add(new LineSpan(lineStart, opportunity.Position, false, true, false));
                    lineStart = opportunity.Position;
                    lastFit = -1;
                }
                else
                {
                    lastFit = opportunity.Position;
                    lastFitHyphen = hyphen;
                }

                k++;
                continue;
            }

            if (lastFit > lineStart)
            {
                result.Add(new LineSpan(lineStart, lastFit, lastFitHyphen, false, false));
                lineStart = lastFit;
                lastFit = -1;
                continue;
            }

            // No opportunity fits: cut at a grapheme cluster boundary. A cut at the opportunity itself (one cluster wider than the
            // line) ends the line there, as the opportunity would.
            int cut = EmergencyCut(lineStart, opportunity.Position, width);
            bool atOpportunity = cut == opportunity.Position;
            result.Add(new LineSpan(lineStart, cut, false, atOpportunity && opportunity.Mandatory, true));
            lineStart = cut;
            lastFit = -1;
            if (atOpportunity)
            {
                k++;
            }
        }
    }

    /// <summary>
    /// Lays the lines of a break out for drawing (Pass2): each line takes the glyphs of the text shaped once, except at its ends,
    /// which are shaped again where the break changes the shaping (a glyph marked <see cref="ShapedGlyph.UnsafeToBreak"/>, or a break
    /// inside a cluster), and a soft hyphen it ends at, shaped as a hyphen. A line that this makes wider than the break's width is cut
    /// once at a grapheme cluster boundary, and the text is not broken again (design §4.1, the contract of monotonicity).
    /// </summary>
    /// <param name="lines">The lines, from <see cref="Wrap"/>.</param>
    /// <param name="shaper">The shaper, of this thread.</param>
    /// <param name="result">The laid out lines; reset first.</param>
    public void Lay(LineBreakResult lines, TextShaper shaper, LineLayoutResult result)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(result);
        result.Reset();
        foreach (LineSpan line in lines.Lines)
        {
            int start = result.NextGlyph;
            (float width, float withSpaces) = LayLine(line, shaper, result);
            if (width <= lines.Width || line.Emergency || line.End - line.Start <= 1)
            {
                result.EndLine(line, start, width, withSpaces);
                continue;
            }

            // Wider after Pass2: one emergency cut, each part laid out once more and never cut again.
            int cut = EmergencyCut(line.Start, line.End, lines.Width);
            if (cut >= line.End)
            {
                result.EndLine(line, start, width, withSpaces);
                continue;
            }

            result.Rewind(start);
            var head = new LineSpan(line.Start, cut, false, false, true);
            (width, withSpaces) = LayLine(head, shaper, result);
            result.EndLine(head, start, width, withSpaces);
            var tail = line with { Start = cut };
            start = result.NextGlyph;
            (width, withSpaces) = LayLine(tail, shaper, result);
            result.EndLine(tail, start, width, withSpaces);
        }
    }

    /// <summary>
    /// Returns the content widths of the text: the least, the widest piece between two break opportunities (a soft hyphen counting
    /// its hyphen); the most, the widest line between hard line breaks. Spaces at a piece's or a line's end do not count, as CSS does
    /// not count the spaces that wrap at a line's end.
    /// </summary>
    /// <param name="shaper">The shaper, of this thread, for the width of a hyphen.</param>
    /// <returns>The least and the most content width, in logical pixels.</returns>
    public (float Min, float Max) IntrinsicWidths(TextShaper shaper)
    {
        float min = 0;
        float max = 0;
        int piece = 0;
        int line = 0;
        for (int k = 0; k < _breakCount; k++)
        {
            LineBreakOpportunity opportunity = _breaks[k];
            bool hyphen = !opportunity.Mandatory && EndsWithSoftHyphen(opportunity.Position);
            min = MathF.Max(min, TrimmedWidth(piece, opportunity.Position) + (hyphen ? HyphenWidth(opportunity.Position - 1, shaper) : 0));
            piece = opportunity.Position;
            if (opportunity.Mandatory)
            {
                max = MathF.Max(max, TrimmedWidth(line, opportunity.Position));
                line = opportunity.Position;
            }
        }

        return (min, max);
    }

    // One line's glyphs: the head and the tail shaped again where needed, the rest as shaped once. Returns its widths.
    private (float Width, float WithSpaces) LayLine(in LineSpan line, TextShaper shaper, LineLayoutResult result)
    {
        int headEnd = line.Start;
        if (line.Start > 0 && IsUnsafe(line.Start))
        {
            headEnd = NextSafe(line.Start, line.End);
        }

        int tailStart = line.End;
        if (line.Hyphenated)
        {
            tailStart = line.End - 1;
        }

        if (line.End < Length && IsUnsafe(line.End))
        {
            tailStart = Math.Min(tailStart, PreviousSafe(line.End, line.Start));
        }

        tailStart = Math.Max(tailStart, headEnd);
        int trimmed = TrimEnd(line.Start, line.End);
        float width = 0;
        float withSpaces = 0;

        // The head: shaped from the line's start, with no text before it as context.
        if (headEnd > line.Start)
        {
            ShapeAgain(
                line.Start,
                Math.Min(headEnd, tailStart),
                contextStart: line.Start,
                contextEnd: line.End,
                hyphenAt: -1,
                shaper,
                result,
                trimmed,
                ref width,
                ref withSpaces
            );
        }

        // The middle, as shaped once.
        int from = Math.Max(headEnd, line.Start);
        for (int g = FirstGlyphAt(from); g < _glyphCount && _glyphs[g].Cluster < tailStart; g++)
        {
            Append(_glyphs[g], result, trimmed, ref width, ref withSpaces);
        }

        // The tail: shaped up to the line's end, with no text after it as context, and its soft hyphen as a hyphen.
        if (tailStart < line.End)
        {
            int hyphenAt = line.Hyphenated ? line.End - 1 : -1;
            ShapeAgain(
                tailStart,
                line.End,
                contextStart: line.Start,
                contextEnd: line.End,
                hyphenAt,
                shaper,
                result,
                trimmed,
                ref width,
                ref withSpaces
            );
        }

        return (width, withSpaces);
    }

    private void ShapeAgain(
        int start,
        int end,
        int contextStart,
        int contextEnd,
        int hyphenAt,
        TextShaper shaper,
        LineLayoutResult result,
        int trimmed,
        ref float width,
        ref float withSpaces
    )
    {
        // A part of one font run at a time: a run's font shapes only its own text.
        for (int position = start; position < end; )
        {
            int run = RunAt(position);
            int runEnd = Math.Min(end, _runs[run].Start + _runs[run].Length);
            int length = contextEnd - contextStart;
            Ensure(ref _scratch, length);
            _text.AsSpan(contextStart, length).CopyTo(_scratch);
            if (hyphenAt >= position && hyphenAt < runEnd)
            {
                _scratch[hyphenAt - contextStart] = Hyphen[0];
            }

            float scale = _scales[run];
            foreach (
                ShapedGlyph glyph in shaper.ShapeRange(
                    _runs[run].Font,
                    _scratch.AsSpan(0, length),
                    position - contextStart,
                    runEnd - position,
                    _language
                )
            )
            {
                var laid = new LaidGlyph(
                    glyph.GlyphId,
                    glyph.Cluster + contextStart,
                    glyph.XAdvance * scale,
                    glyph.XOffset * scale,
                    glyph.YOffset * scale,
                    run
                );
                Append(laid, result, trimmed, ref width, ref withSpaces);
            }

            position = runEnd;
        }
    }

    private static void Append(in LaidGlyph glyph, LineLayoutResult result, int trimmed, ref float width, ref float withSpaces)
    {
        result.AddGlyph(glyph);
        withSpaces += glyph.Advance;
        if (glyph.Cluster < trimmed)
        {
            width += glyph.Advance;
        }
    }

    // Whether breaking at a position changes the shaping: no glyph starts a cluster there, or the one that does is marked.
    private bool IsUnsafe(int position) => _glyphAt[position] < 0 || _unsafe[_glyphAt[position]];

    // The first position after `position`, before `limit`, where a break would not change the shaping; `limit` when none.
    private int NextSafe(int position, int limit)
    {
        for (int p = position + 1; p < limit; p++)
        {
            if (_glyphAt[p] >= 0 && !_unsafe[_glyphAt[p]])
            {
                return p;
            }
        }

        return limit;
    }

    // The last position before `position`, after `limit`, where a break would not change the shaping; `limit` when none.
    private int PreviousSafe(int position, int limit)
    {
        for (int p = position - 1; p > limit; p--)
        {
            if (_glyphAt[p] >= 0 && !_unsafe[_glyphAt[p]])
            {
                return p;
            }
        }

        return limit;
    }

    private int FirstGlyphAt(int position)
    {
        for (int p = position; p < Length; p++)
        {
            if (_glyphAt[p] >= 0)
            {
                return _glyphAt[p];
            }
        }

        return _glyphCount;
    }

    private float TrimmedWidth(int start, int end) => _x[TrimEnd(start, end)] - _x[start];

    // The end of a line without the spaces and hard line breaks at it, which hang past the line (UAX #14's SP, BK, CR, LF, NL).
    private int TrimEnd(int start, int end)
    {
        while (
            end > start
            && UnicodeData.LineBreak(UnicodeData.Properties(_text[end - 1]))
                is LineBreakClass.SP
                    or LineBreakClass.BK
                    or LineBreakClass.CR
                    or LineBreakClass.LF
                    or LineBreakClass.NL
        )
        {
            end--;
        }

        return end;
    }

    private bool EndsWithSoftHyphen(int position) => position > 0 && position < Length && _text[position - 1] == SoftHyphen;

    // The width of a hyphen in the font of the run at `position`, shaped once per run.
    private float HyphenWidth(int position, TextShaper shaper)
    {
        int run = RunAt(position);
        if (float.IsNaN(_hyphens[run]))
        {
            float advance = 0;
            foreach (ShapedGlyph glyph in shaper.Shape(_runs[run].Font, Hyphen, _language))
            {
                advance += glyph.XAdvance;
            }

            _hyphens[run] = advance * _scales[run];
        }

        return _hyphens[run];
    }

    // The last grapheme cluster boundary after `start` and before `limit` whose part fits the width; the first boundary after
    // `start` when none fits, so a line always takes one cluster.
    private int EmergencyCut(int start, int limit, float width)
    {
        ReadOnlySpan<char> text = _text.AsSpan(0, Length);
        int first = Graphemes.NextBoundary(text, start);
        int last = first;
        for (int boundary = first; boundary < limit; )
        {
            if (_x[boundary] - _x[start] > width)
            {
                break;
            }

            last = boundary;
            boundary = Graphemes.NextBoundary(text, boundary);
        }

        return Math.Min(last, limit);
    }

    private int RunAt(int position)
    {
        for (int r = _runCount - 1; r > 0; r--)
        {
            if (_runs[r].Start <= position)
            {
                return r;
            }
        }

        return 0;
    }

    private float Scale(in FontRun run) => Size * run.Scale / run.Font.UnitsPerEm;

    private void AddRun(in FontRun run)
    {
        if (_runCount == _runs.Length)
        {
            int size = _runCount * 2;
            Array.Resize(ref _runs, size);
            Array.Resize(ref _scales, size);
            Array.Resize(ref _hyphens, size);
        }

        _runs[_runCount] = run;
        _scales[_runCount] = Scale(run);
        _hyphens[_runCount] = float.NaN;
        _runCount++;
    }

    private void AddGlyph(in LaidGlyph glyph, bool unsafeToBreak)
    {
        if (_glyphCount == _glyphs.Length)
        {
            Array.Resize(ref _glyphs, _glyphCount * 2);
            Array.Resize(ref _unsafe, _glyphCount * 2);
        }

        if (_glyphAt[glyph.Cluster] < 0)
        {
            _glyphAt[glyph.Cluster] = _glyphCount;
        }

        _glyphs[_glyphCount] = glyph;
        _unsafe[_glyphCount] = unsafeToBreak;
        _glyphCount++;
    }

    private static void Ensure<T>(ref T[] array, int length)
    {
        if (array.Length < length)
        {
            array = new T[Math.Max(length, array.Length * 2)];
        }
    }
}
