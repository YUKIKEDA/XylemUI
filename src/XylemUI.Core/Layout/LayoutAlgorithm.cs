namespace XylemUI;

/// <summary>
/// How a node lays out its children (design §3.2). Created when a style is set, never per frame.
/// </summary>
internal abstract record LayoutAlgorithm
{
    private LayoutAlgorithm() { }

    /// <summary>
    /// Stacks the children along the vertical axis (design §3.2: no margin collapsing, no floats, no inline flow).
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
    /// Places the children along one axis and divides the free space by their grow and shrink factors.
    /// </summary>
    /// <param name="Direction">The main axis.</param>
    public sealed record Flex(FlexDirection Direction) : LayoutAlgorithm;
}
