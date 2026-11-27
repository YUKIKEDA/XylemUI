using System.Collections.Immutable;

namespace XylemUI;

/// <summary>
/// How a node lays out its children (design §3.2). Created when a style is set, never per frame.
/// </summary>
internal abstract record LayoutAlgorithm
{
    private LayoutAlgorithm() { }

    // The two writes below are virtual, and each container type answers for itself, so that NativeAOT keeps a container's code
    // only when the app makes that container: a type test with a constructor in a shared method made every app that sets a gap or
    // an alignment carry the grid's layout (114,936 bytes in the map of a Counter with Fluent's button; Issue #952).

    /// <summary>
    /// Returns this layout with a gap between its children, or <see langword="null"/> when it has none (a block).
    /// </summary>
    /// <param name="gap">The gap, on both axes.</param>
    /// <returns>The layout with the gap, or <see langword="null"/>.</returns>
    internal virtual LayoutAlgorithm? WithGap(LengthPercentage gap) => null;

    /// <summary>
    /// Returns this layout with the container's alignments replaced where a value is given, this same object when nothing changes,
    /// or <see langword="null"/> when it has no container alignment (a block or a virtualized list).
    /// </summary>
    /// <param name="justify">The new <c>justify-content</c>, or <see langword="null"/> to keep it.</param>
    /// <param name="align">The new <c>align-items</c>, or <see langword="null"/> to keep it.</param>
    /// <returns>The layout with the alignments, this object, or <see langword="null"/>.</returns>
    internal virtual LayoutAlgorithm? WithContainerAlignment(ContentAlignment? justify, ItemAlignment? align) => null;

    /// <summary>
    /// Stacks the children along the vertical axis (design §3.2: CSS block layout without margin collapsing, floats, or inline
    /// flow).
    /// </summary>
    /// <param name="Props">The block's alignment of its children.</param>
    public sealed record Block(BlockProps Props) : LayoutAlgorithm
    {
        /// <summary>
        /// Gets a block with CSS's initial values.
        /// </summary>
        public static Block Instance { get; } = new(default(BlockProps));
    }

    /// <summary>
    /// Lays the children out by CSS Flexbox §9 (design §3.2).
    /// </summary>
    /// <param name="Props">The container's flex properties.</param>
    public sealed record Flex(FlexProps Props) : LayoutAlgorithm
    {
        /// <summary>
        /// Gets a flex row with CSS's initial values.
        /// </summary>
        public static Flex Row { get; } = new(new FlexProps { Direction = FlexDirection.Row });

        /// <summary>
        /// Gets a flex column with CSS's initial values.
        /// </summary>
        public static Flex Column { get; } = new(new FlexProps { Direction = FlexDirection.Column });

        /// <inheritdoc/>
        internal override LayoutAlgorithm WithGap(LengthPercentage gap) => new Flex(Props with { RowGap = gap, ColumnGap = gap });

        /// <inheritdoc/>
        internal override LayoutAlgorithm WithContainerAlignment(ContentAlignment? justify, ItemAlignment? align)
        {
            FlexProps props = Props with { JustifyContent = justify ?? Props.JustifyContent, AlignItems = align ?? Props.AlignItems };
            return props == Props ? this : new Flex(props);
        }
    }

    /// <summary>
    /// Lays the children out in a grid by CSS Grid (design §3.3, "The shape of P02f", ported from Taffy 0.15.1).
    /// </summary>
    /// <param name="Props">The grid container's properties.</param>
    public sealed record Grid(GridProps Props) : LayoutAlgorithm
    {
        /// <inheritdoc/>
        internal override LayoutAlgorithm WithGap(LengthPercentage gap) => new Grid(Props with { RowGap = gap, ColumnGap = gap });

        /// <inheritdoc/>
        internal override LayoutAlgorithm WithContainerAlignment(ContentAlignment? justify, ItemAlignment? align)
        {
            GridProps props = Props with { JustifyContent = justify ?? Props.JustifyContent, AlignItems = align ?? Props.AlignItems };
            return props == Props ? this : new Grid(props);
        }
    }

    /// <summary>
    /// Places the rows a virtualized list made along the axis that scrolls, each at the position its index has among all the items
    /// (design §3.4, "The shape of P02e"): the rows fill the content box across, take their content's length along, and are apart
    /// by the gap.
    /// </summary>
    /// <param name="Props">The list's axis and gap.</param>
    public sealed record Virtual(VirtualProps Props) : LayoutAlgorithm
    {
        /// <inheritdoc/>
        internal override LayoutAlgorithm WithGap(LengthPercentage gap) => new Virtual(Props with { Gap = gap });
    }
}

/// <summary>
/// The properties of a flex container (design §3.2), with CSS's initial values by default.
/// </summary>
internal readonly record struct FlexProps
{
    /// <summary>
    /// Gets the main axis.
    /// </summary>
    public FlexDirection Direction { get; init; }

    /// <summary>
    /// Gets whether the items break into lines.
    /// </summary>
    public FlexWrap Wrap { get; init; }

    /// <summary>
    /// Gets how the free space of a line is distributed along the main axis.
    /// </summary>
    public ContentAlignment JustifyContent { get; init; }

    /// <summary>
    /// Gets where the items go across their line, unless an item's <see cref="LayoutStyle.AlignSelf"/> says otherwise.
    /// </summary>
    public ItemAlignment AlignItems { get; init; }

    /// <summary>
    /// Gets how the lines share the container's cross size.
    /// </summary>
    public ContentAlignment AlignContent { get; init; }

    /// <summary>
    /// Gets the space between columns: between the items of a row, between the lines of a column.
    /// </summary>
    public LengthPercentage ColumnGap { get; init; }

    /// <summary>
    /// Gets the space between rows: between the lines of a row, between the items of a column.
    /// </summary>
    public LengthPercentage RowGap { get; init; }
}

/// <summary>
/// The properties of a grid container (design §3.2 and §3.3), with CSS's initial values by default. A track list is compared by
/// reference: the same array given again is no change.
/// </summary>
internal readonly record struct GridProps
{
    /// <summary>
    /// Gets the explicit grid's columns (<c>grid-template-columns</c>); empty for none.
    /// </summary>
    public ImmutableArray<GridTrack> Columns { get; init; }

    /// <summary>
    /// Gets the explicit grid's rows (<c>grid-template-rows</c>); empty for none.
    /// </summary>
    public ImmutableArray<GridTrack> Rows { get; init; }

    /// <summary>
    /// Gets the sizes of the columns the implicit grid adds, in turn (<c>grid-auto-columns</c>); empty for <c>auto</c>.
    /// </summary>
    public ImmutableArray<TrackSize> AutoColumns { get; init; }

    /// <summary>
    /// Gets the sizes of the rows the implicit grid adds, in turn (<c>grid-auto-rows</c>); empty for <c>auto</c>.
    /// </summary>
    public ImmutableArray<TrackSize> AutoRows { get; init; }

    /// <summary>
    /// Gets the order in which the auto-placement algorithm fills the cells.
    /// </summary>
    public GridAutoFlow AutoFlow { get; init; }

    /// <summary>
    /// Gets where the items go along the width of their cells, unless an item's <see cref="LayoutStyle.JustifySelf"/> says
    /// otherwise. <c>normal</c> stretches an item without a width.
    /// </summary>
    public ItemAlignment JustifyItems { get; init; }

    /// <summary>
    /// Gets where the items go along the height of their cells, unless an item's <see cref="LayoutStyle.AlignSelf"/> says
    /// otherwise. <c>normal</c> stretches an item without a height or an aspect ratio.
    /// </summary>
    public ItemAlignment AlignItems { get; init; }

    /// <summary>
    /// Gets where the columns go in the content box; <c>normal</c> behaves as <c>stretch</c>, which grows the <c>auto</c> columns.
    /// </summary>
    public ContentAlignment JustifyContent { get; init; }

    /// <summary>
    /// Gets where the rows go in the content box; <c>normal</c> behaves as <c>stretch</c>, which grows the <c>auto</c> rows.
    /// </summary>
    public ContentAlignment AlignContent { get; init; }

    /// <summary>
    /// Gets the space between columns.
    /// </summary>
    public LengthPercentage ColumnGap { get; init; }

    /// <summary>
    /// Gets the space between rows.
    /// </summary>
    public LengthPercentage RowGap { get; init; }
}

/// <summary>
/// Where a grid item goes in one axis: its start and end lines (CSS's <c>grid-column</c> and <c>grid-row</c>), <c>auto</c> by
/// default.
/// </summary>
/// <param name="Start">The start line.</param>
/// <param name="End">The end line.</param>
internal readonly record struct GridPlacement(GridLine Start, GridLine End);

/// <summary>
/// The properties of a block container (design §3.2, CSS Box Alignment §5 and §6), with CSS's initial values by default.
/// </summary>
internal readonly record struct BlockProps
{
    /// <summary>
    /// Gets where the children go along the width, unless a child's <see cref="LayoutStyle.JustifySelf"/> says otherwise.
    /// </summary>
    public ItemAlignment JustifyItems { get; init; }

    /// <summary>
    /// Gets where the children, taken as one, go along the height.
    /// </summary>
    public ContentAlignment AlignContent { get; init; }
}

/// <summary>
/// The properties of a virtualized list's layout (design §3.4, "The shape of P02e").
/// </summary>
internal readonly record struct VirtualProps
{
    /// <summary>
    /// Gets a value indicating whether the rows go along the horizontal axis; along the vertical one otherwise.
    /// </summary>
    public bool Horizontal { get; init; }

    /// <summary>
    /// Gets the gap between two rows.
    /// </summary>
    public LengthPercentage Gap { get; init; }
}
