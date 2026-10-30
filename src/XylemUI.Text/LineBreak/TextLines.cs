namespace XylemUI.Text.LineBreak;

/// <summary>
/// A glyph of shaped text, in logical pixels: the shaped glyph's values times the size of its font run over the font's units.
/// </summary>
/// <param name="GlyphId">The glyph in its run's font.</param>
/// <param name="Cluster">The UTF-16 offset in the text of the first code unit of the glyph's cluster.</param>
/// <param name="Advance">How far the pen moves after the glyph.</param>
/// <param name="OffsetX">The horizontal shift of the glyph from the pen.</param>
/// <param name="OffsetY">The vertical shift of the glyph from the pen, up positive.</param>
/// <param name="Run">The font run the glyph was shaped with (its index in <see cref="ShapingResult"/>).</param>
internal readonly record struct LaidGlyph(uint GlyphId, int Cluster, float Advance, float OffsetX, float OffsetY, int Run)
{
    /// <summary>
    /// The glyph of the room an inline object takes (design §4.8): no font has it (OpenType counts glyphs in 16 bits), its advance
    /// is the object's width, and no glyph is drawn for it.
    /// </summary>
    public const uint Placeholder = uint.MaxValue;

    /// <summary>
    /// Gets a value indicating whether the glyph is the room of an inline object.
    /// </summary>
    public bool IsPlaceholder => GlyphId == Placeholder;
}

/// <summary>
/// One line of a text: where it starts and ends, and how it ends.
/// </summary>
/// <param name="Start">The first code unit of the line.</param>
/// <param name="End">The code unit after the line's last one; the next line starts here.</param>
/// <param name="Hyphenated">Whether the line ends at a soft hyphen, drawn as a hyphen.</param>
/// <param name="Mandatory">Whether the line ends at a hard line break or the end of the text.</param>
/// <param name="Emergency">Whether the line was cut at a grapheme cluster boundary because no break opportunity fit.</param>
internal readonly record struct LineSpan(int Start, int End, bool Hyphenated, bool Mandatory, bool Emergency);

/// <summary>
/// One line of a text laid out: its span, its glyphs, and its widths.
/// </summary>
/// <param name="Span">Where the line starts and ends.</param>
/// <param name="GlyphStart">The line's first glyph in <see cref="LineLayoutResult.Glyphs"/>.</param>
/// <param name="GlyphCount">The number of the line's glyphs.</param>
/// <param name="Width">The width without the spaces and hard line breaks at the line's end, which hang past it.</param>
/// <param name="WidthWithSpaces">The width with them.</param>
internal readonly record struct LaidLine(LineSpan Span, int GlyphStart, int GlyphCount, float Width, float WidthWithSpaces);

/// <summary>
/// The line breaks of a text at one width (design §4.1, Pass1; cached by width, §3.2's <c>LineBreakCache</c>). Reused: a text
/// broken again into as many lines allocates nothing.
/// </summary>
internal sealed class LineBreakResult
{
    // Empty until the first line, then grown to the larger of the need and twice its size (design §3.2, the memory of a leaf).
    private LineSpan[] _lines = [];

    /// <summary>
    /// Gets the width the text was broken at.
    /// </summary>
    public float Width { get; private set; }

    /// <summary>
    /// Gets the lines, in order.
    /// </summary>
    public ReadOnlySpan<LineSpan> Lines => _lines.AsSpan(0, Count);

    private int Count { get; set; }

    /// <summary>
    /// Forgets the lines, for a new width.
    /// </summary>
    /// <param name="width">The width.</param>
    public void Reset(float width)
    {
        Width = width;
        Count = 0;
    }

    /// <summary>
    /// Appends a line.
    /// </summary>
    /// <param name="line">The line.</param>
    public void Add(in LineSpan line)
    {
        if (Count == _lines.Length)
        {
            Array.Resize(ref _lines, Math.Max(Count + 1, Count * 2));
        }

        _lines[Count++] = line;
    }
}

/// <summary>
/// The lines of a text laid out for drawing (design §4.1, Pass2): each line's glyphs, its ends shaped again where the break changes
/// the shaping, and a hyphen at a soft hyphen it ends at. Reused: a steady state allocates nothing.
/// </summary>
internal sealed class LineLayoutResult
{
    // Empty until the first layout, which makes them at the size of its lines and glyphs (design §3.2, the memory of a leaf).
    private LaidLine[] _lines = [];
    private LaidGlyph[] _glyphs = [];

    /// <summary>
    /// Gets the lines, in order.
    /// </summary>
    public ReadOnlySpan<LaidLine> Lines => _lines.AsSpan(0, LineCount);

    /// <summary>
    /// Gets the glyphs of every line, line after line.
    /// </summary>
    public ReadOnlySpan<LaidGlyph> Glyphs => _glyphs.AsSpan(0, GlyphCount);

    private int LineCount { get; set; }

    private int GlyphCount { get; set; }

    /// <summary>
    /// Forgets the lines and glyphs, and makes room for as many as a layout is expected to write.
    /// </summary>
    /// <param name="glyphs">The glyphs expected.</param>
    /// <param name="lines">The lines expected.</param>
    public void Reset(int glyphs, int lines)
    {
        LineCount = 0;
        GlyphCount = 0;
        if (_glyphs.Length < glyphs)
        {
            _glyphs = new LaidGlyph[Math.Max(glyphs, _glyphs.Length * 2)];
        }

        if (_lines.Length < lines)
        {
            _lines = new LaidLine[Math.Max(lines, _lines.Length * 2)];
        }
    }

    /// <summary>
    /// Appends a glyph to the line being written.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    public void AddGlyph(in LaidGlyph glyph)
    {
        if (GlyphCount == _glyphs.Length)
        {
            Array.Resize(ref _glyphs, Math.Max(GlyphCount + 1, GlyphCount * 2));
        }

        _glyphs[GlyphCount++] = glyph;
    }

    /// <summary>
    /// Ends a line: the glyphs appended since the last line are its glyphs.
    /// </summary>
    /// <param name="span">The line's span.</param>
    /// <param name="glyphStart">The line's first glyph.</param>
    /// <param name="width">The width without the hanging spaces.</param>
    /// <param name="widthWithSpaces">The width with them.</param>
    public void EndLine(in LineSpan span, int glyphStart, float width, float widthWithSpaces)
    {
        if (LineCount == _lines.Length)
        {
            Array.Resize(ref _lines, Math.Max(LineCount + 1, LineCount * 2));
        }

        _lines[LineCount++] = new LaidLine(span, glyphStart, GlyphCount - glyphStart, width, widthWithSpaces);
    }

    /// <summary>
    /// Gets the index the next glyph is written at.
    /// </summary>
    public int NextGlyph => GlyphCount;

    /// <summary>
    /// Forgets the glyphs written from an index on, for a line laid out again.
    /// </summary>
    /// <param name="glyph">The index of the first glyph to forget.</param>
    public void Rewind(int glyph) => GlyphCount = glyph;
}
