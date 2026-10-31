using XylemUI.Text.LineBreak;

namespace XylemUI;

/// <summary>
/// The state of a text leaf (design §3.2, "Text leaves"): its text, its style, and the two caches of its measurement. Only a
/// text leaf has one; <see cref="UiNode.Text"/> is <see langword="null"/> on every other node, so they do not grow by it.
/// </summary>
internal sealed class TextLeaf
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextLeaf"/> class, with no text.
    /// </summary>
    /// <param name="style">The style.</param>
    public TextLeaf(TextStyle style) => Style = style;

    /// <summary>
    /// Gets the text's buffer: <see langword="null"/> until a text is written, then reused and grown only when a text needs more.
    /// </summary>
    public char[]? Buffer { get; private set; }

    /// <summary>
    /// Gets the length of the text, in UTF-16 code units.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public ReadOnlySpan<char> Text => Buffer.AsSpan(0, Length);

    /// <summary>
    /// Gets the style.
    /// </summary>
    public TextStyle Style { get; private set; }

    /// <summary>
    /// Gets or sets the leaf's own color for its text; <see langword="null"/> at first, which takes the color its ancestors resolve
    /// (<see cref="NodeVisual.Foreground"/>, design §5.0, "Opacity, transforms, and pseudo-states"). It is no part of
    /// <see cref="Style"/>: the layout does not read it.
    /// </summary>
    public Color? Color { get; set; }

    /// <summary>
    /// Gets or sets the padding and border widths the leaf's last layout run resolved, in logical pixels: the draw stage places the
    /// lines in the content box, the border box less them, and does not resolve the style again (design §5.0, "The draw stage and
    /// the frame drive"). Only a text leaf keeps them, so that no other node grows by them.
    /// </summary>
    public Edges PaddingBorder { get; set; }

    /// <summary>
    /// Gets or sets the text shaped once (the first cache); <see langword="null"/> until the leaf is first measured.
    /// </summary>
    public ShapedTextCache? Shaped { get; set; }

    /// <summary>
    /// Gets or sets the line breaks by width (the second cache); <see langword="null"/> until the leaf is first measured.
    /// </summary>
    public LineBreakCache? LineBreaks { get; set; }

    /// <summary>
    /// Copies <paramref name="text"/> into the buffer. When it differs from the text there, both caches and the ranges, which
    /// covered the old text, are dropped.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool SetContent(ReadOnlySpan<char> text)
    {
        if (text.SequenceEqual(Text))
        {
            return false;
        }

        if (Buffer is null || Buffer.Length < text.Length)
        {
            Buffer = new char[Math.Max(text.Length, (Buffer?.Length ?? 0) * 2)];
        }

        text.CopyTo(Buffer);
        Length = text.Length;
        Ranges = null;
        DropCaches();
        return true;
    }

    /// <summary>
    /// Takes the first <paramref name="length"/> characters of a text writer's buffer as the text when they differ from it (design
    /// §2.13, "TextBuilder"): the leaf keeps the written buffer and gives its old one back, so that neither is copied and the next
    /// write reuses it. Both caches are then dropped. When the text is the same, the written buffer is given back.
    /// </summary>
    /// <param name="written">The buffer the writer wrote.</param>
    /// <param name="length">The characters the writer wrote.</param>
    /// <param name="released">The buffer the leaf does not keep: its old one, or <paramref name="written"/>; <see langword="null"/>
    /// when the leaf had none.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool TakeWritten(char[] written, int length, out char[]? released)
    {
        if (written.AsSpan(0, length).SequenceEqual(Text))
        {
            released = written;
            return false;
        }

        released = Buffer;
        Buffer = written;
        Length = length;
        DropCaches();
        return true;
    }

    /// <summary>
    /// Gets the ranges of the text (design §4.8): <see langword="null"/> for a text no writer gave a range.
    /// </summary>
    public TextRanges? Ranges { get; private set; }

    /// <summary>
    /// Takes the ranges a text writer wrote when they differ from the leaf's (design §4.8): the leaf keeps the written ranges and
    /// gives its old ones to the writer, so that neither is copied. When the layout's ranges differ, both caches are dropped; when
    /// only the look's do, the layout stays. A write with ranges after one without allocates the writer's new ranges, and a write
    /// without them after one with drops the leaf's: a builder that adds and removes a style by a condition allocates at each
    /// change. Writes that keep having ranges allocate nothing.
    /// </summary>
    /// <param name="writer">The writer of the ranges, its write finished.</param>
    /// <param name="layout"><see langword="true"/> when the layout's ranges changed.</param>
    /// <returns><see langword="true"/> when the ranges changed.</returns>
    public bool TakeRanges(TextRangeWriter writer, out bool layout)
    {
        if (!writer.Used)
        {
            layout = Ranges is not null;
            if (!layout)
            {
                return false;
            }

            Ranges = null;
        }
        else if (Ranges is null)
        {
            layout = true;
            Ranges = writer.Ranges;
            writer.Ranges = new TextRanges();
        }
        else
        {
            layout = !Ranges.LayoutEquals(writer.Ranges);
            if (!layout && Ranges.LookEquals(writer.Ranges))
            {
                return false;
            }

            writer.Ranges.TakeStateOf(Ranges);
            (Ranges, writer.Ranges) = (writer.Ranges, Ranges);
        }

        if (layout)
        {
            DropCaches();
        }

        return true;
    }

    /// <summary>
    /// Replaces the style. When it differs from the style there, both caches are dropped.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <returns><see langword="true"/> when the style changed.</returns>
    public bool SetStyle(TextStyle style)
    {
        if (style == Style)
        {
            return false;
        }

        Style = style;
        DropCaches();
        return true;
    }

    // The caches keep their arrays for the next measurement; only their results are forgotten.
    private void DropCaches()
    {
        Shaped?.Drop();
        LineBreaks?.Drop();
    }
}

/// <summary>
/// The first cache of a text leaf (design §3.2): its text shaped once, with its line break opportunities and content widths.
/// It does not depend on the width.
/// </summary>
internal sealed class ShapedTextCache
{
    /// <summary>
    /// Gets a value indicating whether the results are those of the leaf's text and style.
    /// </summary>
    public bool Valid { get; private set; }

    // The result a text is shaped into, kept with its arrays while the text is empty, so that a text typed again reuses them.
    private ShapingResult? _result;
    private bool _empty;

    /// <summary>
    /// Gets the text shaped once; <see langword="null"/> for an empty text, which has no glyphs.
    /// </summary>
    public ShapingResult? Result => _empty ? null : _result;

    /// <summary>
    /// Gets the min-content width.
    /// </summary>
    public float MinWidth { get; private set; }

    /// <summary>
    /// Gets the max-content width.
    /// </summary>
    public float MaxWidth { get; private set; }

    /// <summary>
    /// Gets the line of an empty text: the metrics of the font a space takes in the style.
    /// </summary>
    public LineMetrics EmptyLine { get; private set; }

    /// <summary>
    /// Returns the result to shape a text into, made the first time.
    /// </summary>
    /// <returns><see cref="Result"/>, which later calls return again.</returns>
    public ShapingResult ResultToFill()
    {
        _empty = false;
        return _result ??= new ShapingResult();
    }

    /// <summary>
    /// Records the content widths of a text just shaped into <see cref="Result"/>.
    /// </summary>
    /// <param name="minWidth">The min-content width.</param>
    /// <param name="maxWidth">The max-content width.</param>
    public void Fill(float minWidth, float maxWidth)
    {
        MinWidth = minWidth;
        MaxWidth = maxWidth;
        Valid = true;
    }

    /// <summary>
    /// Records the line of an empty text.
    /// </summary>
    /// <param name="line">The metrics of the font a space takes in the style.</param>
    public void FillEmpty(LineMetrics line)
    {
        // A text emptied after it was shaped had its glyphs drawn again: the last text's result stood as the empty text's (row
        // P02b, the todo field emptied by an add).
        _empty = true;
        EmptyLine = line;
        MinWidth = 0;
        MaxWidth = 0;
        Valid = true;
    }

    /// <summary>
    /// Forgets the results, keeping the arrays.
    /// </summary>
    public void Drop() => Valid = false;
}

/// <summary>
/// The vertical metrics of a line, in logical pixels: the largest ascent, descent, and line gap of the fonts on it, and the
/// largest size of its ranges.
/// </summary>
/// <param name="Ascent">The distance from the baseline up.</param>
/// <param name="Descent">The distance from the baseline down.</param>
/// <param name="LineGap">The gap the fonts ask for between lines.</param>
/// <param name="Size">The largest size asked for on the line, in logical pixels per em, which a line height multiplies.</param>
internal readonly record struct LineMetrics(float Ascent, float Descent, float LineGap, float Size);

/// <summary>
/// The second cache of a text leaf (design §3.2): the lines of its text at up to four widths. When it is full, a new width takes
/// the oldest entry but the committed one, which the last layout used and drawing reads.
/// </summary>
/// <remarks>
/// The widths, the times of use, and the sizes the lines make are kept here, in the cache's own object, so a hit reads no entry:
/// with the widths in the entries, the lookup alone took 9.8 % of the samples of resizing 10,101 text leaves, the entries of
/// 10,100 leaves being scattered on the heap (row V1a).
/// </remarks>
internal sealed class LineBreakCache
{
    /// <summary>
    /// The number of widths kept.
    /// </summary>
    public const int Capacity = 4;

    private LineBreakEntry?[] _entries = [];
    private Widths _widths;
    private Stamps _used;
    private Sizes _sizes;
    private long _clock;

    /// <summary>
    /// Gets the number of widths kept.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the index of the entry the last layout (its commit) used; -1 when none.
    /// </summary>
    public int Committed { get; private set; } = -1;

    /// <summary>
    /// Returns a kept entry.
    /// </summary>
    /// <param name="index">The entry's index, below <see cref="Count"/>.</param>
    public LineBreakEntry this[int index] => _entries[index]!;

    /// <summary>
    /// Returns the size and the first baseline the lines of a kept entry make.
    /// </summary>
    /// <param name="index">The entry's index, below <see cref="Count"/>.</param>
    /// <returns>The result recorded by <see cref="SetResult"/>.</returns>
    public MeasureResult ResultOf(int index) => _sizes[index];

    /// <summary>
    /// Records the size and the first baseline the lines of an entry just filled make.
    /// </summary>
    /// <param name="index">The entry's index, below <see cref="Count"/>.</param>
    /// <param name="result">The size and the first baseline.</param>
    public void SetResult(int index, MeasureResult result) => _sizes[index] = result;

    /// <summary>
    /// Returns the entry of a width when it is kept.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="index">The entry's index, or -1 when the width is not kept.</param>
    /// <returns><see langword="true"/> when the width is kept; otherwise <see langword="false"/>.</returns>
    public bool TryGet(float width, out int index)
    {
        for (int i = 0; i < Count; i++)
        {
            if (_widths[i] == width)
            {
                _used[i] = ++_clock;
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// Returns an entry to fill for a new width: a new one while there is room, otherwise the oldest but the committed one. It
    /// is made the first time, and reused after.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="index">The entry's index.</param>
    /// <returns>The entry, its width set to <paramref name="width"/>.</returns>
    public LineBreakEntry Take(float width, out int index)
    {
        if (Count < Capacity)
        {
            index = Count++;
            if (_entries.Length < Count)
            {
                Array.Resize(ref _entries, Count);
            }
        }
        else
        {
            index = -1;
            for (int i = 0; i < Count; i++)
            {
                if (i != Committed && (index < 0 || _used[i] < _used[index]))
                {
                    index = i;
                }
            }
        }

        _widths[index] = width;
        _used[index] = ++_clock;
        LineBreakEntry entry = _entries[index] ??= new LineBreakEntry();
        entry.Width = width;
        return entry;
    }

    /// <summary>
    /// Marks the entry the layout committed: it is not overwritten until another one is committed.
    /// </summary>
    /// <param name="index">The entry's index, below <see cref="Count"/>.</param>
    public void Commit(int index) => Committed = index;

    /// <summary>
    /// Forgets every width, keeping the entries' arrays.
    /// </summary>
    public void Drop()
    {
        // The entries stay in _entries to be reused; Count says how many hold a width.
        Count = 0;
        Committed = -1;
    }

    [System.Runtime.CompilerServices.InlineArray(Capacity)]
    private struct Widths
    {
        private float _first;
    }

    [System.Runtime.CompilerServices.InlineArray(Capacity)]
    private struct Stamps
    {
        private long _first;
    }

    [System.Runtime.CompilerServices.InlineArray(Capacity)]
    private struct Sizes
    {
        private MeasureResult _first;
    }
}

/// <summary>
/// The lines of a text leaf at one width: the breaks (Pass1) and the lines laid out (Pass2). The size they make is kept in the
/// cache (<see cref="LineBreakCache.ResultOf"/>).
/// </summary>
internal sealed class LineBreakEntry
{
    /// <summary>
    /// Gets or sets the width the lines were broken at: the key.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Gets the breaks.
    /// </summary>
    public LineBreakResult Lines { get; } = new();

    /// <summary>
    /// Gets the lines laid out.
    /// </summary>
    public LineLayoutResult Layout { get; } = new();
}
