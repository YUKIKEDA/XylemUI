namespace XylemUI;

/// <summary>
/// Where a container puts its children across (CSS's <c>align-items</c>, design §2.13, "The alignment of a container"): in a row
/// or a column, each child across its line; in a grid, each child along the height of its cell. A child's own alignment wins.
/// </summary>
public enum AlignItems : byte
{
    /// <summary>
    /// The container's default: as <see cref="Stretch"/> in a row or a column; in a grid, a child with no height fills its cell's
    /// height, and one with a height goes to the start.
    /// </summary>
    Normal,

    /// <summary>
    /// The child goes to the start of the cross axis, or of its cell's height.
    /// </summary>
    Start,

    /// <summary>
    /// The child goes to the end of the cross axis, or of its cell's height.
    /// </summary>
    End,

    /// <summary>
    /// The child goes to the start of its line's cross axis in a row or a column; in a grid, as <see cref="Start"/>.
    /// </summary>
    FlexStart,

    /// <summary>
    /// The child goes to the end of its line's cross axis in a row or a column; in a grid, as <see cref="End"/>.
    /// </summary>
    FlexEnd,

    /// <summary>
    /// The child goes to the middle of its line's cross axis, or of its cell's height.
    /// </summary>
    Center,

    /// <summary>
    /// The children's first baselines line up, in a line of a row or in a row of a grid.
    /// </summary>
    Baseline,

    /// <summary>
    /// A child whose cross size, or height in a grid, is automatic fills its line's cross size or its cell's height.
    /// </summary>
    Stretch,
}
