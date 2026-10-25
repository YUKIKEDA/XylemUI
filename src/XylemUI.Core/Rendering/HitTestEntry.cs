namespace XylemUI;

/// <summary>
/// One hit region of a frame (design §7, "Hit testing"; row R4e): a node's rounded rectangle in the commands' coordinates, and the
/// display list's state it was written in, whose transform and clips the hit test reads instead of copies.
/// </summary>
/// <param name="Node">The node the region belongs to.</param>
/// <param name="Left">The left edge, in the commands' coordinates: physical pixels, moved by the scopes' move and on pixels as the
/// commands' edges are; before the state's transform when it has one.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Bottom">The bottom edge.</param>
/// <param name="Radii">The radius of each corner, in physical pixels, scaled down as CSS's when two on a side sum past it.</param>
/// <param name="State">The display list's state (<see cref="StateCommand"/>) the region was written in, or -1 outside every state.</param>
/// <param name="Group">The innermost clip's layer around the region (a group record), whose clips are applied when the layer is
/// composited, or -1.</param>
internal readonly record struct HitTestEntry(
    NodeHandle Node,
    float Left,
    float Top,
    float Right,
    float Bottom,
    CornerRadii Radii,
    int State,
    int Group
);
