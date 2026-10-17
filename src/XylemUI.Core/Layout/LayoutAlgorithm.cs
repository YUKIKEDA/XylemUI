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
    public sealed record Block : LayoutAlgorithm
    {
        private Block() { }

        /// <summary>
        /// Gets the one instance.
        /// </summary>
        public static Block Instance { get; } = new();
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
/// The properties of a flex container (design §3.2). The values L1b adds (wrapping, alignment other than CSS's initial
/// values, gaps) come with that row.
/// </summary>
internal readonly record struct FlexProps
{
    /// <summary>
    /// Gets the main axis.
    /// </summary>
    public FlexDirection Direction { get; init; }
}
