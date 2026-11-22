namespace XylemUI;

/// <summary>
/// How a container distributes its free space (CSS's <c>justify-content</c>, design §2.13, "The alignment of a container"): in
/// a row or a column, the space of a line along the main axis; in a grid, the space around and between the columns.
/// </summary>
public enum JustifyContent : byte
{
    /// <summary>
    /// The container's default: as <see cref="FlexStart"/> in a row or a column; in a grid, as <see cref="Stretch"/>.
    /// </summary>
    Normal,

    /// <summary>
    /// The items, or the grid's columns, are packed at the start.
    /// </summary>
    Start,

    /// <summary>
    /// The items, or the grid's columns, are packed at the end.
    /// </summary>
    End,

    /// <summary>
    /// The items are packed at the start of the row's or the column's main axis; in a grid, as <see cref="Start"/>.
    /// </summary>
    FlexStart,

    /// <summary>
    /// The items are packed at the end of the row's or the column's main axis; in a grid, as <see cref="End"/>.
    /// </summary>
    FlexEnd,

    /// <summary>
    /// The items, or the grid's columns, are packed in the middle.
    /// </summary>
    Center,

    /// <summary>
    /// In a row or a column, as <see cref="FlexStart"/>; in a grid, the <c>auto</c> columns grow to fill the free space.
    /// </summary>
    Stretch,

    /// <summary>
    /// The free space goes between the items, or the grid's columns, in equal parts, and none at the ends.
    /// </summary>
    SpaceBetween,

    /// <summary>
    /// Each item, or each grid column, has an equal part of the free space around it, so the ends get half a part.
    /// </summary>
    SpaceAround,

    /// <summary>
    /// The free space goes between the items, or the grid's columns, and at the ends, in equal parts.
    /// </summary>
    SpaceEvenly,
}
