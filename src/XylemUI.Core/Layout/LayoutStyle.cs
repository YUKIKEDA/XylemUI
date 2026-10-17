namespace XylemUI;

/// <summary>
/// The layout properties of a node (design §3.2). Sizes are border-box sizes: a fixed size includes the padding.
/// </summary>
internal readonly record struct LayoutStyle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutStyle"/> struct with the block algorithm and every length auto.
    /// </summary>
    public LayoutStyle() { }

    /// <summary>
    /// Gets how the node lays out its children.
    /// </summary>
    public LayoutAlgorithm Algorithm { get; init; } = LayoutAlgorithm.Block.Instance;

    /// <summary>
    /// Gets the width.
    /// </summary>
    public Length Width { get; init; }

    /// <summary>
    /// Gets the height.
    /// </summary>
    public Length Height { get; init; }

    /// <summary>
    /// Gets the space between the border box and the children.
    /// </summary>
    public EdgeInsets Padding { get; init; }

    /// <summary>
    /// Gets the space around the border box, inside the parent.
    /// </summary>
    public EdgeInsets Margin { get; init; }

    /// <summary>
    /// Gets the share of a flex container's free space this node takes when there is room.
    /// </summary>
    public float FlexGrow { get; init; }

    /// <summary>
    /// Gets the share of a flex container's overflow this node gives up, weighted by its basis.
    /// </summary>
    public float FlexShrink { get; init; } = 1;

    /// <summary>
    /// Gets the main size this node starts from in a flex container; <see cref="Length.Auto"/> takes the main-axis size, then
    /// the content.
    /// </summary>
    public Length FlexBasis { get; init; }

    /// <summary>
    /// Gets a value indicating whether the node's size cannot depend on its content: both sides fixed (design §2.4, the
    /// structural boundary). An algorithm sized by its parent joins this with the algorithm that needs it.
    /// </summary>
    public bool IsStructuralBoundary => Width.IsFixed && Height.IsFixed;
}
