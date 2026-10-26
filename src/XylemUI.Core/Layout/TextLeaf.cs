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
    /// Gets or sets the color the text is drawn in; opaque black at first. It is no part of <see cref="Style"/>: the layout does not
    /// read it (design §5.0, "The draw stage and the frame drive").
    /// </summary>
    public Color Color { get; set; } = Color.FromRgb(0x000000);

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
    /// Copies <paramref name="text"/> into the buffer. When it differs from the text there, both caches are dropped.
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
        DropCaches();
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

    /// <summary>
    /// Gets the text shaped once; <see langword="null"/> for an empty text, which has no glyphs.
    /// </summary>
    public ShapingResult? Result { get; private set; }

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
    public ShapingResult ResultToFill() => Result ??= new ShapingResult();

    /// <summary>
    /// Records the content widths of a text just shaped into <see cref="Result"/>.
    /// </summary>
    public void Fill(float minWidth, float maxWidth)
    {
        MinWidth = minWidth;
        MaxWidth = maxWidth;
        Valid = true;
    }

    /// <summary>
    /// Records the line of an empty text.
    /// </summary>
    public void FillEmpty(LineMetrics line)
    {
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
/// The vertical metrics of a line, in logical pixels: the largest ascent, descent, and line gap of the fonts on it.
/// </summary>
/// <param name="Ascent">The distance from the baseline up.</param>
/// <param name="Descent">The distance from the baseline down.</param>
/// <param name="LineGap">The gap the fonts ask for between lines.</param>
internal readonly record struct LineMetrics(float Ascent, float Descent, float LineGap);

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
    public LineBreakEntry this[int index] => _entries[index]!;

    /// <summary>
    /// Returns the size and the first baseline the lines of a kept entry make.
    /// </summary>
    public MeasureResult ResultOf(int index) => _sizes[index];

    /// <summary>
    /// Records the size and the first baseline the lines of an entry just filled make.
    /// </summary>
    public void SetResult(int index, MeasureResult result) => _sizes[index] = result;

    /// <summary>
    /// Returns the entry of a width when it is kept.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="index">The entry's index.</param>
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
