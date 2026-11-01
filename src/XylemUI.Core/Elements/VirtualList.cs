namespace XylemUI;

/// <summary>
/// How a virtualized list makes its rows (design §3.4, "The shape of P02e").
/// </summary>
public sealed record VirtualOptions
{
    /// <summary>
    /// Gets the length, in logical pixels, before and after the viewport whose rows are made and laid out but not drawn; 250 by
    /// default, as Flutter's viewport.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public float CacheExtent
    {
        get;
        init => field = float.IsFinite(value) && value >= 0 ? value : throw OutOfRange(nameof(CacheExtent), value, "at or above 0");
    } = 250;

    /// <summary>
    /// Gets the length of every row along the axis that scrolls, margins included, when the rows all have one: the rows are given
    /// it and are not measured, and a position is found by a division. With <see langword="null"/>, the default, each row takes its
    /// content's length, which is measured.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite length above 0.</exception>
    public float? FixedExtent
    {
        get;
        init => field = value is not { } v || (float.IsFinite(v) && v > 0) ? value : throw OutOfRange(nameof(FixedExtent), value, "above 0");
    }

    /// <summary>
    /// Gets the length taken for a row not measured yet; with <see langword="null"/>, the default, the mean of the rows measured.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite length above 0.</exception>
    public float? EstimatedExtent
    {
        get;
        init => field = value is not { } v || (float.IsFinite(v) && v > 0) ? value : throw OutOfRange(nameof(EstimatedExtent), value, "above 0");
    }

    private static ArgumentOutOfRangeException OutOfRange(string name, float? value, string bound) =>
        new(name, value, $"The {name} option is a finite length {bound}.");
}

/// <summary>
/// A range of the indices of a list's items: from <see cref="Start"/> up to, and not including, <see cref="End"/>.
/// </summary>
/// <param name="Start">The first index.</param>
/// <param name="End">The index after the last.</param>
public readonly record struct IndexRange(int Start, int End)
{
    /// <summary>
    /// Gets the number of indices in the range.
    /// </summary>
    public int Count => End - Start;
}

/// <summary>
/// The state of a virtualized list that the app reads and moves (design §3.4, "The shape of P02e"): its box's scroll state and the
/// rows that show.
/// </summary>
public sealed class VirtualListState
{
    private readonly Signal<IndexRange> _visibleRange = Signal.Create(default(IndexRange));

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualListState"/> class, with a scroll state of its own.
    /// </summary>
    public VirtualListState()
        : this(new ScrollState()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualListState"/> class with a scroll state the box has already.
    /// </summary>
    /// <param name="scroll">The scroll state.</param>
    internal VirtualListState(ScrollState scroll) => Scroll = scroll;

    /// <summary>
    /// Gets the scroll state of the list's box (design §3.4.1). Its position is found from the row the list keeps at the top of the
    /// viewport, and moves when the rows above that row are measured.
    /// </summary>
    public ScrollState Scroll { get; }

    /// <summary>
    /// Gets the indices of the rows that show in the viewport, written after the layout pass and when the position changes.
    /// </summary>
    public IReadOnlySignal<IndexRange> VisibleRange => _visibleRange;

    /// <summary>
    /// Writes the rows that show, when they changed.
    /// </summary>
    /// <param name="range">The rows.</param>
    internal void SetVisibleRange(IndexRange range)
    {
        if (range != _visibleRange.Peek())
        {
            _visibleRange.Set(range);
        }
    }
}
