using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The visibility (row L13, design §3.2, "Nodes not shown") in a headless app: a hidden node keeps its place, and it and its whole
// subtree are neither drawn nor hit, take no focus, and are left out of the accessibility tree; a change runs no layout. The window
// is 800 × 600 at a scale of 1.
public sealed class VisibilityTests
{
    private static readonly Color Red = Color.FromRgb(0xFF0000);
    private static readonly Color Green = Color.FromRgb(0x00FF00);
    private static readonly Color Blue = Color.FromRgb(0x0000FF);

    /// <summary>
    /// A hidden node keeps its size, and the siblings after it keep their places.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a 30 × 20 box, a 40 × 50 box whose visibility is a signal at Visible, and a 30 × 20 box.
    ///
    /// Steps:
    /// - Settle; read the middle box's rectangle and the last box's. Set the signal to Hidden; settle; read them again.
    ///
    /// Expected:
    /// - Both times the middle box is 40 × 50 at y 20, and the last box is at y 70.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Hidden_KeepsItsSize_AndItsSiblingsDoNotMove()
    {
        var visibility = Signal.Create(Visibility.Visible);
        Element middle = default;
        Element last = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Box().Width(30).Height(20), middle = Box().Width(40).Height(50).Visibility(visibility), last = Box().Width(30).Height(20))
        );
        _ = app.PumpAndSettle();
        (Rect Middle, float LastY) shown = (app.Tree.GetRect(middle.Node), app.Tree.GetRect(last.Node).Y);

        visibility.Set(Visibility.Hidden);
        _ = app.PumpAndSettle();
        (Rect Middle, float LastY) hidden = (app.Tree.GetRect(middle.Node), app.Tree.GetRect(last.Node).Y);

        Assert.Equal((new Rect(0, 20, 40, 50), 70f), shown);
        Assert.Equal(shown, hidden);
    }

    /// <summary>
    /// Nothing of a hidden subtree is drawn: neither the boxes nor the draw functions of the node and its descendants, while a sibling
    /// is still drawn.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a red 100 × 100 box with a draw function that counts, whose visibility is a signal at Visible, holding a blue
    ///   50 × 50 box with a draw function that counts; then a green 100 × 100 box.
    ///
    /// Steps:
    /// - Settle; read the colors the frame filled and the counts. Zero the counts; set the signal to Hidden; settle; read the colors,
    ///   the counts, and whether a frame was presented.
    ///
    /// Expected:
    /// - Shown: red, blue, and green filled; each draw function called. Hidden: a frame presented, green filled, neither red nor
    ///   blue, and neither draw function called.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Hidden_DrawsNothingOfTheSubtree()
    {
        var visibility = Signal.Create(Visibility.Visible);
        int outer = 0;
        int inner = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Box(Box().Width(50).Height(50).Background(new Paint(Blue)).Draw((_, _) => inner++))
                    .Width(100)
                    .Height(100)
                    .Background(new Paint(Red))
                    .Draw((_, _) => outer++)
                    .Visibility(visibility),
                Box().Width(100).Height(100).Background(new Paint(Green))
            )
        );
        _ = app.PumpAndSettle();
        List<Color> shownFills = Fills(app);
        (int Outer, int Inner) shownDraws = (outer, inner);

        (outer, inner) = (0, 0);
        int presents = app.PresentCount;
        visibility.Set(Visibility.Hidden);
        _ = app.PumpAndSettle();

        Assert.Contains(Red, shownFills);
        Assert.Contains(Blue, shownFills);
        Assert.Contains(Green, shownFills);
        Assert.True(shownDraws.Outer > 0 && shownDraws.Inner > 0);
        Assert.True(app.PresentCount > presents);
        List<Color> hiddenFills = Fills(app);
        Assert.Contains(Green, hiddenFills);
        Assert.DoesNotContain(Red, hiddenFills);
        Assert.DoesNotContain(Blue, hiddenFills);
        Assert.Equal((0, 0), (outer, inner));
    }

    /// <summary>
    /// A press on a hidden node's area reaches the node behind it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 300 × 300 box that counts clicks, holding a 100 × 100 box that counts clicks, whose visibility is a signal at Visible.
    ///
    /// Steps:
    /// - Click (50, 50) and read the counts. Set the signal to Hidden; settle; click (50, 50); read the counts.
    ///
    /// Expected:
    /// - Shown: the inner box counts 1 and the outer 1 (the click bubbles). Hidden: the outer box counts 2 and the inner still 1.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Hidden_PassesThePointerToTheNodeBehind()
    {
        var visibility = Signal.Create(Visibility.Visible);
        int outer = 0;
        int inner = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box(Box().Width(100).Height(100).OnClick(() => inner++).Visibility(visibility)).Width(300).Height(300).OnClick(() => outer++)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(50, 50);
        (int Outer, int Inner) shown = (outer, inner);
        visibility.Set(Visibility.Hidden);
        _ = app.PumpAndSettle();
        app.Pointer.Click(50, 50);
        _ = app.PumpAndSettle();

        Assert.Equal((1, 1), shown);
        Assert.Equal((2, 1), (outer, inner));
    }

    /// <summary>
    /// Tab passes over a hidden node's focusable descendants, both ways, and none of them can be given the focus; shown, Tab stops at
    /// them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a focusable box "a", a column whose visibility is a signal at Hidden holding focusable boxes "b" and "c", and a
    ///   focusable box "d"; each 20 × 20.
    ///
    /// Steps:
    /// - Focus "a"; Tab; read the focus. Shift+Tab; read. Try to focus "b". Set the signal to Visible; settle; focus "a"; Tab; read.
    ///
    /// Expected:
    /// - Hidden: Tab goes from "a" to "d", Shift+Tab back to "a", and "b" cannot take the focus. Shown: Tab goes from "a" to "b".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Hidden_TabPassesOverTheSubtree()
    {
        var visibility = Signal.Create(Visibility.Hidden);
        Element a = default;
        Element b = default;
        Element d = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(a = FocusableBox(), Column(b = FocusableBox(), FocusableBox()).Visibility(visibility), d = FocusableBox())
        );
        _ = app.PumpAndSettle();

        _ = app.Focus(a);
        _ = app.Keyboard.Press(Key.Tab);
        NodeHandle forward = app.Tree.Focus.Focused;
        _ = app.Keyboard.Press(Key.Tab, KeyModifiers.Shift);
        NodeHandle backward = app.Tree.Focus.Focused;
        bool focusedHidden = app.Focus(b);
        visibility.Set(Visibility.Visible);
        _ = app.PumpAndSettle();
        _ = app.Focus(a);
        _ = app.Keyboard.Press(Key.Tab);
        NodeHandle shown = app.Tree.Focus.Focused;
        _ = app.PumpAndSettle();

        Assert.Equal(d.Node, forward);
        Assert.Equal(a.Node, backward);
        Assert.False(focusedHidden);
        Assert.Equal(b.Node, shown);
    }

    /// <summary>
    /// A focused node loses the focus when it or an ancestor becomes hidden, with a focus-out, and goes nowhere: the next Tab starts
    /// from the first node. A focused node outside the subtree keeps the focus.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a focusable box "a", and a box whose visibility is a signal at Visible holding a focusable box "c" that counts
    ///   its own focus-outs; each 20 × 20.
    ///
    /// Steps:
    /// - Focus "a"; set the signal to Hidden; settle; read the focus. Set it to Visible; settle; focus "c"; set it to Hidden; read
    ///   the focus and the focus-outs; settle; Tab; read the focus.
    ///
    /// Expected:
    /// - "a" keeps the focus while the other subtree hides. Hiding "c"'s parent leaves no node focused, with one focus-out from "c";
    ///   the next Tab focuses "a".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task HiddenAroundTheFocus_TakesTheFocusAway_AndTabStartsFromTheFirst()
    {
        var visibility = Signal.Create(Visibility.Visible);
        Element a = default;
        Element c = default;
        int outs = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Column(a = FocusableBox(), Box(c = FocusableBox()).Visibility(visibility)));
        _ = app.PumpAndSettle();
        app.Tree.AddFocusHandler(
            c.Node,
            FocusEventKind.FocusOut,
            EventPhase.Bubble,
            (in FocusEvent e) =>
            {
                if (e.TargetNode == c.Node)
                {
                    outs++;
                }
            }
        );

        _ = app.Focus(a);
        visibility.Set(Visibility.Hidden);
        _ = app.PumpAndSettle();
        NodeHandle other = app.Tree.Focus.Focused;
        visibility.Set(Visibility.Visible);
        _ = app.PumpAndSettle();
        _ = app.Focus(c);
        visibility.Set(Visibility.Hidden);
        (bool None, int Outs) lost = (app.Tree.Focus.Focused.IsNone, outs);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.Tab);
        NodeHandle next = app.Tree.Focus.Focused;
        _ = app.PumpAndSettle();

        Assert.Equal(a.Node, other);
        Assert.Equal((true, 1), lost);
        Assert.Equal(a.Node, next);
    }

    /// <summary>
    /// A hidden subtree is left out of the accessibility tree, and comes back when it shows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a focusable box "a", and a column whose visibility is a signal at Visible holding focusable boxes "b" and "c"; a
    ///   focusable node with no role of its own is a Group.
    ///
    /// Steps:
    /// - Make the accessibility tree; read its groups. Set the signal to Hidden; settle; read. Set it to Visible; settle; read.
    ///
    /// Expected:
    /// - "a", "b", "c"; then "a" alone; then "a", "b", "c".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Hidden_LeavesTheSubtreeOutOfTheAccessibilityTree()
    {
        var visibility = Signal.Create(Visibility.Visible);
        Element a = default;
        Element b = default;
        Element c = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(a = FocusableBox(), Column(b = FocusableBox(), c = FocusableBox()).Visibility(visibility))
        );
        _ = app.PumpAndSettle();
        string Groups() =>
            string.Join(
                ',',
                app.FindByRole(SemanticsRole.Group)
                    .Select(g =>
                        g.Node == a.Node ? "a"
                        : g.Node == b.Node ? "b"
                        : g.Node == c.Node ? "c"
                        : "other"
                    )
            );

        var seen = new List<string> { Groups() };
        visibility.Set(Visibility.Hidden);
        _ = app.PumpAndSettle();
        seen.Add(Groups());
        visibility.Set(Visibility.Visible);
        _ = app.PumpAndSettle();
        seen.Add(Groups());

        Assert.Equal(["a,b,c", "a", "a,b,c"], seen);
    }

    /// <summary>
    /// A hidden window's root leaves the window's node of the accessibility tree with no children, and they come back when it shows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The root: a column whose visibility is a signal at Visible, holding a focusable box.
    ///
    /// Steps:
    /// - Make the accessibility tree; count its groups. Set the signal to Hidden; settle; count. Set it to Visible; settle; count.
    ///
    /// Expected:
    /// - 1, 0, 1.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task HiddenRoot_LeavesTheWindowWithNoChildren()
    {
        var visibility = Signal.Create(Visibility.Visible);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Column(FocusableBox()).Visibility(visibility));
        _ = app.PumpAndSettle();

        var counts = new List<int> { app.FindByRole(SemanticsRole.Group).Count };
        visibility.Set(Visibility.Hidden);
        _ = app.PumpAndSettle();
        counts.Add(app.FindByRole(SemanticsRole.Group).Count);
        visibility.Set(Visibility.Visible);
        _ = app.PumpAndSettle();
        counts.Add(app.FindByRole(SemanticsRole.Group).Count);

        Assert.Equal([1, 0, 1], counts);
    }

    /// <summary>
    /// A change of the visibility runs no layout: it marks the node to be drawn again alone. A change of the layout style's
    /// <c>Hidden</c> on the same node marks the layout, so the check can see one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a 40 × 50 box whose visibility is a signal at Visible, and a 30 × 20 box; settled.
    ///
    /// Steps:
    /// - Set the signal to Hidden; read the layout roots, the paint updates, and the root's layout marks before any frame; settle.
    ///   Set it to Visible; read them again; settle. Set the box's layout style hidden; read the layout roots; settle.
    ///
    /// Expected:
    /// - Both visibility changes: no layout root, a paint update, and no layout mark on the root. The layout style's change: a
    ///   layout root.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Change_RunsNoLayout()
    {
        var visibility = Signal.Create(Visibility.Visible);
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(box = Box().Width(40).Height(50).Visibility(visibility), Box().Width(30).Height(20))
        );
        _ = app.PumpAndSettle();
        UiTree tree = app.Tree;
        (int LayoutRoots, bool Painted, DirtyFlags RootLayout) Marks()
        {
            ref UiNode root = ref tree.Nodes.GetRef(app.RootNode);
            return (tree.LayoutRoots.Count, tree.PaintUpdates.Count > 0, (root.Dirty.SelfDirty | root.Dirty.SubtreeDirty) & DirtyFlags.Layout);
        }

        visibility.Set(Visibility.Hidden);
        (int, bool, DirtyFlags) hiding = Marks();
        _ = app.PumpAndSettle();
        visibility.Set(Visibility.Visible);
        (int, bool, DirtyFlags) showing = Marks();
        _ = app.PumpAndSettle();
        tree.SetStyle(box.Node, tree.Nodes.GetRef(box.Node).Layout.Style with { Hidden = true });
        int styled = tree.LayoutRoots.Count;
        _ = app.PumpAndSettle();

        Assert.Equal((0, true, DirtyFlags.None), hiding);
        Assert.Equal((0, true, DirtyFlags.None), showing);
        Assert.True(styled > 0);
    }

    /// <summary>
    /// The whole subtree of a hidden node is hidden: a descendant set to Visible is neither drawn nor hit, takes no focus, and is
    /// not in the accessibility tree.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column holding a 200 × 200 box hidden, holding a focusable 100 × 100 box set to Visible that counts its clicks and its
    ///   draws.
    ///
    /// Steps:
    /// - Settle; click (50, 50); try to focus the inner box; read the accessibility tree's groups.
    ///
    /// Expected:
    /// - No draw and no click; the inner box cannot take the focus; no group.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task VisibleChildOfAHiddenParent_StaysHidden()
    {
        int clicks = 0;
        int draws = 0;
        Element inner = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Box(inner = Box().Width(100).Height(100).Focusable().OnClick(() => clicks++).Draw((_, _) => draws++).Visibility(Visibility.Visible))
                    .Width(200)
                    .Height(200)
                    .Visibility(Visibility.Hidden)
            )
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(50, 50);
        bool focused = app.Focus(inner);
        IReadOnlyList<Element> groups = app.FindByRole(SemanticsRole.Group);
        _ = app.PumpAndSettle();

        Assert.Equal((0, 0), (draws, clicks));
        Assert.False(focused);
        Assert.Empty(groups);
    }

    /// <summary>
    /// A value that is not a visibility is refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box in a headless app.
    ///
    /// Steps:
    /// - Set its visibility to <c>(Visibility)2</c>.
    ///
    /// Expected:
    /// - <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task UndefinedValue_Throws()
    {
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => box = Box().Width(10).Height(10));
        _ = app.PumpAndSettle();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => box.Visibility((Visibility)2));
    }

    /// <summary>
    /// A <c>Show</c>'s exit under a hidden ancestor runs on the clock to its end and then takes the part out, as when it is shown.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden box holding a <c>Show</c> of a 10 × 10 box with a fade of 100 ms, whose condition is true; settled.
    ///
    /// Steps:
    /// - Set the condition false; read whether the part is alive; settle; read it again and the time the settle took.
    ///
    /// Expected:
    /// - Alive just after the change; freed after the settle, which took 100 ms or more.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ShowExit_UnderAHiddenAncestor_RunsToItsEnd()
    {
        var when = Signal.Create(true);
        Element part = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Box(Show(when, () => part = Box().Width(10).Height(10), motion: ShowMotion.Fade(TimeSpan.FromMilliseconds(100))))
                    .Width(100)
                    .Height(100)
                    .Visibility(Visibility.Hidden)
            )
        );
        _ = app.PumpAndSettle();

        TimeSpan start = app.Time.Elapsed;
        when.Set(false);
        bool aliveAtOnce = app.Tree.Nodes.IsAlive(part.Node);
        _ = app.PumpAndSettle();

        Assert.True(aliveAtOnce);
        Assert.False(app.Tree.Nodes.IsAlive(part.Node));
        Assert.True(app.Time.Elapsed - start >= TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Hiding and showing a subtree, with the focus taken away and given back and the pointer over it, allocates nothing in the
    /// steady state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a 100 × 100 box whose visibility is a signal, holding a focusable 50 × 50 box with a hovered look; the pointer
    ///   over the inner box; warmed up with 20 iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: on an even index set the signal to Hidden; on
    ///   an odd one set it to Visible and focus the inner box; settle.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_HideAndShow_AllocatesNothing()
    {
        var visibility = Signal.Create(Visibility.Visible);
        var look = new PaintStyle { Background = new Paint(Blue) };
        Element inner = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Box(inner = Box().Width(50).Height(50).Focusable().Hovered(look)).Width(100).Height(100).Visibility(visibility))
        );
        _ = app.PumpAndSettle();
        app.Pointer.Move(25, 25);
        void Step(int index)
        {
            if (index % 2 == 0)
            {
                visibility.Set(Visibility.Hidden);
            }
            else
            {
                visibility.Set(Visibility.Visible);
                _ = app.Focus(inner);
            }

            _ = app.PumpAndSettle();
        }

        for (int i = 0; i < 20; i++)
        {
            Step(i);
        }

        PerformanceAssert.DoesNotAllocate(1_000, Step);
    }

    private static Element FocusableBox() => Box().Width(20).Height(20).Focusable();

    // The colors of the rectangles the last frame filled.
    private static List<Color> Fills(HeadlessApp app)
    {
        var fills = new List<Color>();
        DisplayListReader reader = app.Frames.List.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            switch (tag)
            {
                case CommandTag.FillRect:
                    fills.Add(reader.ReadFillRect().Color);
                    break;
                case CommandTag.GlyphRun:
                    _ = reader.ReadGlyphRun();
                    break;
                case CommandTag.Shape:
                    _ = reader.ReadShape();
                    break;
                case CommandTag.Pop:
                    reader.ReadPop();
                    break;
                case CommandTag.Shadow:
                    _ = reader.ReadShadow();
                    break;
                case CommandTag.PushOpacity:
                    _ = reader.ReadGroup();
                    break;
                case CommandTag.PushState:
                    _ = reader.ReadState(out _);
                    break;
                default:
                    _ = reader.ReadCurve();
                    break;
            }
        }

        return fills;
    }
}
