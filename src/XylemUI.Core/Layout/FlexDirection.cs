namespace XylemUI;

/// <summary>
/// The main axis of a flex container (CSS's <c>flex-direction</c>).
/// </summary>
internal enum FlexDirection : byte
{
    /// <summary>Left to right.</summary>
    Row,

    /// <summary>Top to bottom.</summary>
    Column,

    /// <summary>Right to left.</summary>
    RowReverse,

    /// <summary>Bottom to top.</summary>
    ColumnReverse,
}

/// <summary>
/// Whether a flex container breaks its items into lines (CSS's <c>flex-wrap</c>).
/// </summary>
internal enum FlexWrap : byte
{
    /// <summary>One line; the items shrink or overflow.</summary>
    NoWrap,

    /// <summary>Lines from the cross start to the cross end.</summary>
    Wrap,

    /// <summary>Lines from the cross end to the cross start.</summary>
    WrapReverse,
}

/// <summary>
/// Where an item goes in its line or its block (CSS's <c>align-items</c>, <c>align-self</c>, <c>justify-items</c>, and
/// <c>justify-self</c>, design §3.2).
/// </summary>
internal enum AlignItemsKeyword : byte
{
    /// <summary><c>normal</c>: <see cref="Stretch"/> for a flex item and a block's child.</summary>
    Normal,

    /// <summary>The start of the axis.</summary>
    Start,

    /// <summary>The end of the axis.</summary>
    End,

    /// <summary>The start of the flex line's cross axis, flipped by <see cref="FlexWrap.WrapReverse"/>.</summary>
    FlexStart,

    /// <summary>The end of the flex line's cross axis, flipped by <see cref="FlexWrap.WrapReverse"/>.</summary>
    FlexEnd,

    /// <summary>The middle.</summary>
    Center,

    /// <summary>The items' first baselines on one line.</summary>
    Baseline,

    /// <summary>The whole cross size, when the item's cross size is automatic.</summary>
    Stretch,
}

/// <summary>
/// How a container distributes the space between its items or lines (CSS's <c>justify-content</c> and
/// <c>align-content</c>, design §3.2).
/// </summary>
internal enum AlignContentKeyword : byte
{
    /// <summary><c>normal</c>: <see cref="FlexStart"/> for <c>justify-content</c>, <see cref="Stretch"/> for a flex container's
    /// <c>align-content</c>, the content where it is for a block's.</summary>
    Normal,

    /// <summary>The start of the axis.</summary>
    Start,

    /// <summary>The end of the axis.</summary>
    End,

    /// <summary>The flex-relative start of the axis.</summary>
    FlexStart,

    /// <summary>The flex-relative end of the axis.</summary>
    FlexEnd,

    /// <summary>The middle.</summary>
    Center,

    /// <summary>The lines grow to fill the container (as <see cref="FlexStart"/> along the main axis).</summary>
    Stretch,

    /// <summary>Equal space between, none at the ends.</summary>
    SpaceBetween,

    /// <summary>Equal space around each, half of it at the ends.</summary>
    SpaceAround,

    /// <summary>Equal space between and at the ends.</summary>
    SpaceEvenly,
}

/// <summary>
/// An item's alignment, with its overflow alignment: a safe alignment falls back to the start when the item would
/// overflow (CSS Box Alignment §4.4).
/// </summary>
/// <param name="Keyword">Where the item goes.</param>
/// <param name="Safe">Whether the item goes to the start when it overflows (<c>safe</c>); otherwise <c>unsafe</c>.</param>
internal readonly record struct AlignItems(AlignItemsKeyword Keyword, bool Safe = false);

/// <summary>
/// A distribution of space, with its overflow alignment (CSS Box Alignment §4.4).
/// </summary>
/// <param name="Keyword">How the space is distributed.</param>
/// <param name="Safe">Whether the content goes to the start when it overflows (<c>safe</c>); otherwise <c>unsafe</c>.</param>
internal readonly record struct AlignContent(AlignContentKeyword Keyword, bool Safe = false);
