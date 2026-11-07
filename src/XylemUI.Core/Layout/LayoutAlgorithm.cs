using System.Collections.Immutable;

namespace XylemUI;

/// <summary>
/// How a node lays out its children (design §3.2). Created when a style is set, never per frame.
/// </summary>
internal abstract record LayoutAlgorithm
{
    private LayoutAlgorithm() { }

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
    }

    /// <summary>
    /// Lays the children out in a grid by CSS Grid (design §3.3, "The shape of P02f", ported from Taffy 0.15.1).
    /// </summary>
    /// <param name="Props">The grid container's properties.</param>
    public sealed record Grid(GridProps Props) : LayoutAlgorithm;

    /// <summary>
    /// Places the rows a virtualized list made along the axis that scrolls, each at the position its index has among all the items
    /// (design §3.4, "The shape of P02e"): the rows fill the content box across, take their content's length along, and are apart
    /// by the gap.
    /// </summary>
    /// <param name="Props">The list's axis and gap.</param>
    public sealed record Virtual(VirtualProps Props) : LayoutAlgorithm;
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
    public AlignContent JustifyContent { get; init; }

    /// <summary>
    /// Gets where the items go across their line, unless an item's <see cref="LayoutStyle.AlignSelf"/> says otherwise.
    /// </summary>
    public AlignItems AlignItems { get; init; }

    /// <summary>
    /// Gets how the lines share the container's cross size.
    /// </summary>
    public AlignContent AlignContent { get; init; }

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
    public AlignItems JustifyItems { get; init; }

    /// <summary>
    /// Gets where the items go along the height of their cells, unless an item's <see cref="LayoutStyle.AlignSelf"/> says
    /// otherwise. <c>normal</c> stretches an item without a height or an aspect ratio.
    /// </summary>
    public AlignItems AlignItems { get; init; }

    /// <summary>
    /// Gets where the columns go in the content box; <c>normal</c> behaves as <c>stretch</c>, which grows the <c>auto</c> columns.
    /// </summary>
    public AlignContent JustifyContent { get; init; }

    /// <summary>
    /// Gets where the rows go in the content box; <c>normal</c> behaves as <c>stretch</c>, which grows the <c>auto</c> rows.
    /// </summary>
    public AlignContent AlignContent { get; init; }

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
    public AlignItems JustifyItems { get; init; }

    /// <summary>
    /// Gets where the children, taken as one, go along the height.
    /// </summary>
    public AlignContent AlignContent { get; init; }
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
