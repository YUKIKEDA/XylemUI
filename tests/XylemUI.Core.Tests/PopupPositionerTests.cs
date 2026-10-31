using System.Numerics;

namespace XylemUI.Tests;

// The placement of a popup against its anchor (design §7.3, "Placement"): the xdg positioner's model, as Avalonia's
// ManagedPopupPositioner. The bounds are an 800 × 600 window.
public sealed class PopupPositionerTests
{
    private static readonly Rect Window = new(0, 0, 800, 600);
    private static readonly Size Popup = new(200, 100);

    /// <summary>
    /// Where the popup fits, it is placed with its gravity side at the anchor point, moved by the offset.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An anchor of 100 × 30 at (100, 100).
    ///
    /// Steps:
    /// - Place below from the left edge, with an offset of (5, 2); to the right from the top; at a point (300, 300).
    ///
    /// Expected:
    /// - (105, 132), (200, 100), and (300, 300), each 200 × 100.
    /// </remarks>
    [Fact]
    public void Place_WhereItFits_PutsTheGravitySideAtTheAnchorPoint()
    {
        var anchor = new Rect(100, 100, 100, 30);

        Rect below = PopupPositioner.Place(PopupPlacement.Below with { Offset = new Vector2(5, 2) }, anchor, Popup, Window);
        Rect right = PopupPositioner.Place(PopupPlacement.Right, anchor, Popup, Window);
        PopupPlacement point = PopupPlacement.AtPoint(new Vector2(300, 300));
        Rect atPoint = PopupPositioner.Place(point, point.AnchorRect!.Value, Popup, Window);

        Assert.Equal(new Rect(105, 132, 200, 100), below);
        Assert.Equal(new Rect(200, 100, 200, 100), right);
        Assert.Equal(new Rect(300, 300, 200, 100), atPoint);
    }

    /// <summary>
    /// At each edge, a popup that does not fit flips to the other side of the anchor when it fits there, and slides into the bounds
    /// when it does not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Anchors of 100 × 30 near each edge.
    ///
    /// Steps:
    /// - Place below from the left: at (0, 560) near the bottom; at (750, 100) near the right; with the gravity up and to the left at
    ///   (0, 10) near the top left; below a 100 × 590 anchor at (750, 5), which leaves room neither below nor above.
    ///
    /// Expected:
    /// - The bottom flips above: (0, 460). The right edge cannot flip, as to the left of the anchor's right edge (850) it would
    ///   span 650 to 850, past 800, so it slides: (600, 130). The top left flips down and to the right on both axes: (100, 40). The
    ///   last flips on neither axis and slides on both: (600, 500).
    /// </remarks>
    [Fact]
    public void Place_AtTheEdges_FlipsWhereItFits_AndSlidesWhereItDoesNot()
    {
        Rect bottom = PopupPositioner.Place(PopupPlacement.Below, new Rect(0, 560, 100, 30), Popup, Window);
        Rect right = PopupPositioner.Place(PopupPlacement.Below, new Rect(750, 100, 100, 30), Popup, Window);
        var upLeft = new PopupPlacement(PopupAnchor.TopLeft, PopupAnchor.TopLeft);
        Rect topLeft = PopupPositioner.Place(upLeft, new Rect(0, 10, 100, 30), Popup, Window);
        Rect nowhere = PopupPositioner.Place(PopupPlacement.Below, new Rect(750, 5, 100, 590), Popup, Window);

        Assert.Equal(new Rect(0, 460, 200, 100), bottom);
        Assert.Equal(new Rect(600, 130, 200, 100), right);
        Assert.Equal(new Rect(100, 40, 200, 100), topLeft);
        Assert.Equal(new Rect(600, 500, 200, 100), nowhere);
    }

    /// <summary>
    /// A popup larger than the bounds slides to their start and is cut to them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An anchor of 100 × 30 at (100, 100), and a popup of 1,000 × 700.
    ///
    /// Steps:
    /// - Place below; place below with no resize.
    ///
    /// Expected:
    /// - (0, 0, 800, 600); with no resize, slid to the end and past the start: (-200, -100, 1000, 700).
    /// </remarks>
    [Fact]
    public void Place_LargerThanTheBounds_SlidesAndIsCut()
    {
        var anchor = new Rect(100, 100, 100, 30);
        var large = new Size(1_000, 700);

        Rect cut = PopupPositioner.Place(PopupPlacement.Below, anchor, large, Window);
        PopupPlacement noResize = PopupPlacement.Below with
        {
            Adjustment = PopupAdjustment.All & ~(PopupAdjustment.ResizeX | PopupAdjustment.ResizeY),
        };
        Rect slid = PopupPositioner.Place(noResize, anchor, large, Window);

        Assert.Equal(new Rect(0, 0, 800, 600), cut);
        Assert.Equal(new Rect(-200, -100, 1_000, 700), slid);
    }
}
