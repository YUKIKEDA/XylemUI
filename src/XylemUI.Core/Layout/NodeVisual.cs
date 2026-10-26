namespace XylemUI;

/// <summary>
/// What the draw stage draws for a node besides its children (design §5.0, "The draw stage and the frame drive"): the box's shadow,
/// background, corner radii, and border paint, and a draw function of the user's. Only a node with any of them has one
/// (<see cref="UiNode.Visual"/>); the tree makes it at the first setter and keeps it, so that a change allocates nothing.
/// </summary>
/// <remarks>
/// Only the tree writes it (<see cref="UiTree.SetBackground"/> and the others), so a change always marks the node and no two nodes
/// share one. The border's widths are the layout's (<see cref="LayoutStyle.Border"/>); this holds only their paint.
/// </remarks>
internal sealed class NodeVisual
{
    /// <summary>
    /// Gets or sets the shadow under the box; <see langword="null"/> for none.
    /// </summary>
    public BoxShadow? Shadow { get; internal set; }

    /// <summary>
    /// Gets or sets the box's background; <see langword="null"/> for none.
    /// </summary>
    public Paint? Background { get; internal set; }

    /// <summary>
    /// Gets or sets the box's corner radii, which its shadow, background, and border take.
    /// </summary>
    public CornerRadii Radii { get; internal set; }

    /// <summary>
    /// Gets or sets the paint of the box's border, drawn at the widths the layout gave it; <see langword="null"/> for none.
    /// </summary>
    public Paint? BorderPaint { get; internal set; }

    /// <summary>
    /// Gets or sets the user's draw function, called after the box and before the children; <see langword="null"/> for none.
    /// </summary>
    public DrawFunc? Draw { get; internal set; }
}

/// <summary>
/// Draws a node's content of the user's (design §5.0, "The draw stage and the frame drive").
/// </summary>
/// <param name="context">The context to draw and write hit regions in.</param>
/// <param name="bounds">The node's border box, in absolute logical pixels.</param>
internal delegate void DrawFunc(DrawingContext context, Rect bounds);
