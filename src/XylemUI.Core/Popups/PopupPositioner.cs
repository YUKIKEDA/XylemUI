namespace XylemUI;

/// <summary>
/// Places a popup against its anchor rectangle (design §7.3, "Placement"): the xdg positioner's model, as Avalonia's
/// <c>ManagedPopupPositioner</c> (<c>PopupPositioning/ManagedPopupPositioner.cs</c> lines 104 to 235). The popup's gravity side is
/// put at the anchor point, moved by the offset; one that does not fit in the bounds is adjusted on X and then on Y, each by
/// flipping, then sliding, then resizing.
/// </summary>
internal static class PopupPositioner
{
    /// <summary>
    /// Returns the popup's rectangle.
    /// </summary>
    /// <param name="placement">The anchor point, the gravity, the offset, and the adjustments.</param>
    /// <param name="anchor">The anchor rectangle, in the bounds' coordinates.</param>
    /// <param name="size">The popup's size.</param>
    /// <param name="bounds">The rectangle the popup is kept in: the window's client area.</param>
    /// <returns>The popup's rectangle: its position, and its size cut to the bounds when it is resized.</returns>
    public static Rect Place(in PopupPlacement placement, Rect anchor, Size size, Rect bounds)
    {
        PopupAdjustment adjust = placement.Adjustment;
        (float x, float width) = Axis(
            horizontal: true,
            placement,
            anchor.X,
            anchor.Width,
            size.Width,
            bounds.X,
            bounds.Width,
            (adjust & PopupAdjustment.FlipX) != 0,
            (adjust & PopupAdjustment.SlideX) != 0,
            (adjust & PopupAdjustment.ResizeX) != 0
        );
        (float y, float height) = Axis(
            horizontal: false,
            placement,
            anchor.Y,
            anchor.Height,
            size.Height,
            bounds.Y,
            bounds.Height,
            (adjust & PopupAdjustment.FlipY) != 0,
            (adjust & PopupAdjustment.SlideY) != 0,
            (adjust & PopupAdjustment.ResizeY) != 0
        );
        return new Rect(x, y, width, height);
    }

    // One axis: the unconstrained position, then the flip when it fits, the slide, and the resize (Avalonia's order).
    private static (float Start, float Length) Axis(
        bool horizontal,
        in PopupPlacement placement,
        float anchorStart,
        float anchorLength,
        float length,
        float boundsStart,
        float boundsLength,
        bool flip,
        bool slide,
        bool resize
    )
    {
        int point = Side(placement.Anchor, horizontal);
        int grow = Side(placement.Gravity, horizontal);
        float offset = horizontal ? placement.Offset.X : placement.Offset.Y;
        float boundsEnd = boundsStart + boundsLength;
        float start = Unconstrained(point, grow, offset, anchorStart, anchorLength, length);
        if (flip && !Fits(start, length, boundsStart, boundsEnd))
        {
            // The xdg positioner inverts the offset with the flip.
            float flipped = Unconstrained(-point, -grow, -offset, anchorStart, anchorLength, length);
            if (Fits(flipped, length, boundsStart, boundsEnd))
            {
                start = flipped;
            }
        }

        if (slide && !Fits(start, length, boundsStart, boundsEnd))
        {
            start = Math.Max(start, boundsStart);
            if (start + length > boundsEnd)
            {
                start = boundsEnd - length;
            }
        }

        if (resize && !Fits(start, length, boundsStart, boundsEnd))
        {
            float end = Math.Min(start + length, boundsEnd);
            float cut = Math.Max(start, boundsStart);
            if (end > cut)
            {
                (start, length) = (cut, end - cut);
            }
        }

        return (start, length);
    }

    // The start on an axis of a popup whose gravity side is at the anchor's point, moved by the offset.
    private static float Unconstrained(int point, int grow, float offset, float anchorStart, float anchorLength, float length)
    {
        float at = anchorStart + (anchorLength * (point + 1) * 0.5f);
        float start = at - (length * (1 - grow) * 0.5f);
        return start + offset;
    }

    private static bool Fits(float start, float length, float boundsStart, float boundsEnd) => start >= boundsStart && start + length <= boundsEnd;

    // The side of a point on an axis: -1 for the start (left or top), 0 for the middle, 1 for the end.
    private static int Side(PopupAnchor anchor, bool horizontal) =>
        horizontal
            ? anchor switch
            {
                PopupAnchor.Left or PopupAnchor.TopLeft or PopupAnchor.BottomLeft => -1,
                PopupAnchor.Right or PopupAnchor.TopRight or PopupAnchor.BottomRight => 1,
                _ => 0,
            }
            : anchor switch
            {
                PopupAnchor.Top or PopupAnchor.TopLeft or PopupAnchor.TopRight => -1,
                PopupAnchor.Bottom or PopupAnchor.BottomLeft or PopupAnchor.BottomRight => 1,
                _ => 0,
            };
}
