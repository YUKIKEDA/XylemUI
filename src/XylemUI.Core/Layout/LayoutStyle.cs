using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The layout properties of a node (design §3.2), with CSS's meaning and initial values. Sizes are border-box sizes: a fixed
/// size includes the padding and the border.
/// </summary>
internal readonly record struct LayoutStyle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutStyle"/> struct with the block algorithm, every length auto, no margin,
    /// and a flex shrink of 1.
    /// </summary>
    public LayoutStyle() { }

    /// <summary>
    /// Gets how the node lays out its children.
    /// </summary>
    public LayoutAlgorithm Algorithm { get; init; } = LayoutAlgorithm.Block.Instance;

    /// <summary>
    /// Gets whether the node takes part in its parent's layout or is placed by its <see cref="Inset"/> against the parent's
    /// padding box (row L2: the parent, not CSS's nearest positioned ancestor).
    /// </summary>
    public Position Position { get; init; }

    /// <summary>
    /// Gets the distances of the left, top, right, and bottom sides: from the parent's padding box for an absolutely
    /// positioned node, and the shift after the layout for a relatively positioned one. <see cref="LengthPercentageAuto.Auto"/>
    /// on every side by default.
    /// </summary>
    public EdgeInsets<LengthPercentageAuto> Inset { get; init; }

    /// <summary>
    /// Gets the width divided by the height that an automatic size follows from the other axis (CSS's <c>aspect-ratio</c>);
    /// NaN, the default, or a value that is not positive sets none.
    /// </summary>
    public float AspectRatio { get; init; } = float.NaN;

    /// <summary>
    /// Gets a value indicating whether the node and its subtree take no part in the layout (CSS's <c>display: none</c>, row
    /// L2): they are not laid out, take no room, and get an empty rectangle. Leaving it out of the algorithm keeps the
    /// algorithm for when the node is shown again.
    /// </summary>
    public bool Hidden { get; init; }

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
    /// elsewhere. A content size or <see cref="Length.Stretch"/> resolves as for <see cref="Width"/>.
    /// </summary>
    public Length MinWidth { get; init; }

    /// <summary>
    /// Gets the smallest height, with the same meaning of <see cref="Length.Auto"/> as <see cref="MinWidth"/>. A content size
    /// counts only on a column's main axis and is auto elsewhere (Chrome).
    /// </summary>
    public Length MinHeight { get; init; }

    /// <summary>
    /// Gets the largest width; <see cref="Length.Auto"/> sets no limit. A content size or <see cref="Length.Stretch"/> resolves
    /// as for <see cref="Width"/>.
    /// </summary>
    public Length MaxWidth { get; init; }

    /// <summary>
    /// Gets the largest height; <see cref="Length.Auto"/> sets no limit. A content size counts as for <see cref="MinHeight"/>.
    /// </summary>
    public Length MaxHeight { get; init; }

    /// <summary>
    /// Gets the space between the border and the children.
    /// </summary>
    public EdgeInsets<LengthPercentage> Padding { get; init; }

    /// <summary>
    /// Gets the width of the border on each side: the space it takes in the layout. Its color and corners are painting (V1).
    /// </summary>
    public EdgeInsets<LengthPercentage> Border { get; init; }

    /// <summary>
    /// Gets the space around the border box, inside the parent: 0 on every side by default (CSS's initial value), not
    /// <see cref="LengthPercentageAuto.Auto"/>, the default of the side's type.
    /// </summary>
    public EdgeInsets<LengthPercentageAuto> Margin { get; init; } = EdgeInsets.Margin(0);

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
    /// Gets where the node goes across its flex line, or <see langword="null"/> (CSS's <c>auto</c>) to follow the container's
    /// <see cref="FlexProps.AlignItems"/>.
    /// </summary>
    public AlignItems? AlignSelf { get; init; }

    /// <summary>
    /// Gets where the node goes along its block's width, or <see langword="null"/> (CSS's <c>auto</c>) to follow the block's
    /// <see cref="BlockProps.JustifyItems"/>.
    /// </summary>
    public AlignItems? JustifySelf { get; init; }

    /// <summary>
    /// Gets what the node does with content that overflows its width (design §3.4.1, "The scroll container").
    /// </summary>
    public Overflow OverflowX { get; init; }

    /// <summary>
    /// Gets what the node does with content that overflows its height.
    /// </summary>
    public Overflow OverflowY { get; init; }

    /// <summary>
    /// Gets the room a scrollbar takes inside the padding box on an axis whose overflow is <see cref="Overflow.Scroll"/>, in
    /// logical pixels: the width of a vertical one at the end of a row, the height of a horizontal one at the bottom (CSS's
    /// <c>scrollbar-width</c>, Taffy's <c>scrollbar_width</c>). 0 by default: the scrollbars are drawn over the content.
    /// </summary>
    public float ScrollbarWidth { get; init; }

    /// <summary>
    /// Gets a value indicating whether the node is a scroll container: its overflow is <see cref="Overflow.Hidden"/> or
    /// <see cref="Overflow.Scroll"/> on an axis (Taffy's <c>is_scroll_container</c>).
    /// </summary>
    public bool IsScrollContainer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => OverflowX >= Overflow.Hidden || OverflowY >= Overflow.Hidden;
    }

    /// <summary>
    /// Gets a value indicating whether the node clips its children to its padding box on an axis.
    /// </summary>
    public bool ClipsChildren
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => OverflowX != Overflow.Visible || OverflowY != Overflow.Visible;
    }

    /// <summary>
    /// Gets the room the scrollbars take inside the padding box: the width of the vertical one (<see cref="OverflowY"/> is
    /// <see cref="Overflow.Scroll"/>) and the height of the horizontal one (<see cref="OverflowX"/> is
    /// <see cref="Overflow.Scroll"/>), as Taffy's <c>scrollbar_gutter</c> takes them.
    /// </summary>
    public Size ScrollbarGutter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!HasScrollbarGutter)
            {
                return default;
            }

            return new(OverflowY == Overflow.Scroll ? ScrollbarWidth : 0, OverflowX == Overflow.Scroll ? ScrollbarWidth : 0);
        }
    }

    /// <summary>
    /// Gets a value indicating whether a scrollbar takes room (<see cref="ScrollbarGutter"/> is not zero): the layout's algorithms
    /// add nothing for the others, which are nearly all (row V2).
    /// </summary>
    public bool HasScrollbarGutter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (OverflowX == Overflow.Scroll || OverflowY == Overflow.Scroll) && ScrollbarWidth > 0;
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="AspectRatio"/> is set.
    /// </summary>
    public bool HasAspectRatio => AspectRatio > 0;

    /// <summary>
    /// Gets a value indicating whether the node takes part in its parent's flow: shown and not absolutely positioned.
    /// </summary>
    public bool IsInFlow => !Hidden && Position != Position.Absolute;

    /// <summary>
    /// Gets a value indicating whether the node's size cannot depend on its content: both sides fixed (design §2.4, the
    /// structural boundary). A parent that reads the node's content still takes the change (design §2.4.1).
    /// </summary>
    public bool IsStructuralBoundary => Width.IsPx && Height.IsPx && !HasSizingKeywordLimit;

    /// <summary>
    /// Gets a value indicating whether a minimum or a maximum is a sizing keyword (row L3): resolving it measures the node or
    /// reads the space it may stretch into.
    /// </summary>
    public bool HasSizingKeywordLimit =>
        MinWidth.IsSizingKeyword || MinHeight.IsSizingKeyword || MaxWidth.IsSizingKeyword || MaxHeight.IsSizingKeyword;

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
