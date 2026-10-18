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
