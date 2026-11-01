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
