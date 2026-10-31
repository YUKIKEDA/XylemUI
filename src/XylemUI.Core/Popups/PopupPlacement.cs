using System.Numerics;

namespace XylemUI;

/// <summary>
/// A point of a rectangle: the anchor point of a popup's anchor rectangle, or the direction a popup grows from that point (its
/// gravity), as the xdg positioner names them (design §7.3, "Placement").
/// </summary>
public enum PopupAnchor : byte
{
    /// <summary>
    /// The center; as a gravity, the popup is centered on the point.
    /// </summary>
    Center,

    /// <summary>
    /// The middle of the top edge; as a gravity, the popup grows up.
    /// </summary>
    Top,

    /// <summary>
    /// The middle of the bottom edge; as a gravity, the popup grows down.
    /// </summary>
    Bottom,

    /// <summary>
    /// The middle of the left edge; as a gravity, the popup grows to the left.
    /// </summary>
    Left,

    /// <summary>
    /// The middle of the right edge; as a gravity, the popup grows to the right.
    /// </summary>
    Right,

    /// <summary>
    /// The top left corner; as a gravity, the popup grows up and to the left.
    /// </summary>
    TopLeft,

    /// <summary>
    /// The top right corner; as a gravity, the popup grows up and to the right.
    /// </summary>
    TopRight,

    /// <summary>
    /// The bottom left corner; as a gravity, the popup grows down and to the left.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// The bottom right corner; as a gravity, the popup grows down and to the right.
    /// </summary>
    BottomRight,
}

/// <summary>
/// How a popup that does not fit in its bounds is moved, on each axis (design §7.3, "Placement"): flipped to the other side of the
/// anchor when it fits there, slid into the bounds, and cut to them.
/// </summary>
[Flags]
public enum PopupAdjustment : byte
{
    /// <summary>
    /// Kept where it is placed, in the bounds or not.
    /// </summary>
    None = 0,

    /// <summary>
    /// Flipped to the other side horizontally when it fits there.
    /// </summary>
    FlipX = 1,

    /// <summary>
    /// Flipped to the other side vertically when it fits there.
    /// </summary>
    FlipY = 2,

    /// <summary>
    /// Slid horizontally into the bounds.
    /// </summary>
    SlideX = 4,

    /// <summary>
    /// Slid vertically into the bounds.
    /// </summary>
    SlideY = 8,

    /// <summary>
    /// Made no wider than the bounds.
    /// </summary>
    ResizeX = 16,

    /// <summary>
    /// Made no taller than the bounds.
    /// </summary>
    ResizeY = 32,

    /// <summary>
    /// Every adjustment, the default.
    /// </summary>
    All = FlipX | FlipY | SlideX | SlideY | ResizeX | ResizeY,
}

/// <summary>
/// Where a popup is placed against its anchor (design §7.3, "Placement"), in logical pixels: the popup's gravity side is put at the
/// anchor point of the anchor rectangle, moved by the offset, and adjusted into the window's client area.
/// </summary>
/// <param name="Anchor">The point of the anchor rectangle the popup is placed at.</param>
/// <param name="Gravity">The direction the popup grows from the point.</param>
/// <param name="Offset">How far the popup is moved from the point, before the adjustments.</param>
/// <param name="Adjustment">How a popup that does not fit is moved.</param>
/// <param name="AnchorRect">The anchor rectangle in the window's coordinates; <see langword="null"/> for the anchor node's box.</param>
public readonly record struct PopupPlacement(
    PopupAnchor Anchor,
    PopupAnchor Gravity,
    Vector2 Offset = default,
    PopupAdjustment Adjustment = PopupAdjustment.All,
    Rect? AnchorRect = null
)
{
    /// <summary>
    /// Gets the placement below the anchor, from its left edge: the default of a popup and of a menu.
    /// </summary>
    public static PopupPlacement Below { get; } = new(PopupAnchor.BottomLeft, PopupAnchor.BottomRight);

    /// <summary>
    /// Gets the placement to the right of the anchor, from its top edge: a submenu's.
    /// </summary>
    public static PopupPlacement Right { get; } = new(PopupAnchor.TopRight, PopupAnchor.BottomRight);

    /// <summary>
    /// Gets the placement that puts the content over the whole window, with no adjustment: a modal dialog's backdrop.
    /// </summary>
    public static PopupPlacement Fill { get; } = new(PopupAnchor.TopLeft, PopupAnchor.BottomRight, Adjustment: PopupAdjustment.None);

    /// <summary>
    /// Gets a value indicating whether the placement fills the window (<see cref="Fill"/>).
    /// </summary>
    internal bool Fills => this == Fill;

    /// <summary>
    /// Returns the placement at a point of the window, the popup growing down and to the right from it: a context menu's at the
    /// pointer (Avalonia's <c>PlacementMode.Pointer</c>).
    /// </summary>
    /// <param name="point">The point, in the window's logical pixels.</param>
    /// <returns>The placement, with a 1 × 1 anchor rectangle at the point.</returns>
    public static PopupPlacement AtPoint(Vector2 point) =>
        new(PopupAnchor.TopLeft, PopupAnchor.BottomRight, AnchorRect: new Rect(point.X, point.Y, 1, 1));
}
