namespace XylemUI;

/// <summary>
/// The layout properties of a node (design §3.2), with CSS's meaning and initial values. Sizes are border-box sizes: a fixed
/// size includes the padding and the border.
/// </summary>
internal readonly record struct LayoutStyle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutStyle"/> struct with the block algorithm, every length auto, and a flex
    /// shrink of 1.
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
    /// Gets the smallest width. <see cref="Length.Auto"/> is the automatic minimum size of a flex item (CSS Flexbox §4.5) and 0
    /// elsewhere.
    /// </summary>
    public LengthPercentageAuto MinWidth { get; init; }

    /// <summary>
    /// Gets the smallest height, with the same meaning of <see cref="Length.Auto"/> as <see cref="MinWidth"/>.
    /// </summary>
    public LengthPercentageAuto MinHeight { get; init; }

    /// <summary>
    /// Gets the largest width; <see cref="Length.Auto"/> sets no limit.
    /// </summary>
    public LengthPercentageAuto MaxWidth { get; init; }

    /// <summary>
    /// Gets the largest height; <see cref="Length.Auto"/> sets no limit.
    /// </summary>
    public LengthPercentageAuto MaxHeight { get; init; }

    /// <summary>
    /// Gets the space between the border and the children.
    /// </summary>
    public EdgeInsets<LengthPercentage> Padding { get; init; }

    /// <summary>
    /// Gets the width of the border on each side: the space it takes in the layout. Its color and corners are painting (V1).
    /// </summary>
    public EdgeInsets<LengthPercentage> Border { get; init; }

    /// <summary>
    /// Gets the space around the border box, inside the parent.
    /// </summary>
    public EdgeInsets<LengthPercentage> Margin { get; init; }

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
    /// structural boundary). A parent that reads the node's content still takes the change (design §2.4.1).
    /// </summary>
    public bool IsStructuralBoundary => Width.IsPx && Height.IsPx;

    /// <summary>
    /// Gets a value indicating whether a length of the node resolves against the width of its parent's content box: a width or
    /// a minimum or maximum width in percent, or a padding, border, or margin in percent (CSS resolves those against the width
    /// on every side).
    /// </summary>
    public bool UsesPercentOfParentWidth =>
        Width.IsPercent
        || MinWidth.IsPercent
        || MaxWidth.IsPercent
        || EdgeInsets.UsesPercent(Padding)
        || EdgeInsets.UsesPercent(Border)
        || EdgeInsets.UsesPercent(Margin);

    /// <summary>
    /// Gets a value indicating whether a length of the node resolves against the height of its parent's content box.
    /// </summary>
    public bool UsesPercentOfParentHeight => Height.IsPercent || MinHeight.IsPercent || MaxHeight.IsPercent;
}
