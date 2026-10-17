using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// Dirty flags and layout roots (S6b, design §2.4, §2.4.1). Layout state is process-wide in the reactive runtime, so these
// tests run on the fixture's UI thread like the reactive ones.
[Collection(UiThreadGroup.Name)]
public sealed class InvalidationTests(UiThreadFixture ui)
{
    /// <summary>
    /// A layout change sets SelfDirty on the node where it started, SubtreeDirty on the path up to the nearest boundary, and
    /// makes the boundary a layout root; nothing above the boundary is touched.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root (block) → boundary (fixed 200 x 100) → middle (block, auto) → leaf. Laid out once, so every flag is clear.
    ///
    /// Steps:
    /// - Invalidate the leaf with ChangeKind.Size.
    ///
    /// Expected:
    /// - leaf: SelfDirty and SubtreeDirty have Layout. middle and boundary: only SubtreeDirty. root: nothing.
    /// - The only layout root is the boundary, for IntrinsicSizeChanged, with no constraint of its own.
    /// </remarks>
    [Fact]
    public void LayoutChange_MarksThePathAndStopsAtTheBoundary() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle boundary = Box(tree, root, Fixed(200, 100));
            NodeHandle middle = Box(tree, boundary, new LayoutStyle());
            NodeHandle leaf = Leaf(tree, middle, 10, 10);
            LayOut(tree, root, 400, 300);

            Invalidation.Invalidate(tree, leaf, ChangeKind.Size);

            Assert.True(tree.Nodes.GetRef(leaf).Dirty.SelfDirty.HasFlag(DirtyFlags.Layout));
            Assert.True(tree.Nodes.GetRef(leaf).Dirty.SubtreeDirty.HasFlag(DirtyFlags.Layout));
            Assert.Equal(DirtyFlags.None, tree.Nodes.GetRef(middle).Dirty.SelfDirty);
            Assert.True(tree.Nodes.GetRef(middle).Dirty.SubtreeDirty.HasFlag(DirtyFlags.Layout));
            Assert.Equal(DirtyFlags.None, tree.Nodes.GetRef(boundary).Dirty.SelfDirty);
            Assert.True(tree.Nodes.GetRef(boundary).Dirty.SubtreeDirty.HasFlag(DirtyFlags.Layout));
            Assert.Equal(default, tree.Nodes.GetRef(root).Dirty with { Epoch = 0 });
            LayoutRequest request = Assert.Single(tree.LayoutRoots);
            Assert.Equal(boundary, request.Node);
            Assert.Equal(LayoutReason.IntrinsicSizeChanged, request.Reason);
            Assert.Null(request.AvailableSpace);
        });

    /// <summary>
    /// With no boundary on the way, a layout change runs up to the root, which becomes the layout root.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → middle → leaf, all auto. Laid out once.
    ///
    /// Steps:
    /// - Invalidate the leaf with ChangeKind.Size.
    ///
    /// Expected:
    /// - root has SubtreeDirty Layout and is the only layout root.
    /// </remarks>
    [Fact]
    public void LayoutChange_WithoutABoundary_StopsAtTheRoot() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle middle = Box(tree, root, new LayoutStyle());
            NodeHandle leaf = Leaf(tree, middle, 10, 10);
            LayOut(tree, root, 400, 300);

            Invalidation.Invalidate(tree, leaf, ChangeKind.Size);

            Assert.True(tree.Nodes.GetRef(root).Dirty.SubtreeDirty.HasFlag(DirtyFlags.Layout));
            Assert.Equal(root, Assert.Single(tree.LayoutRoots).Node);
        });

    /// <summary>
    /// A node last laid out under a tight constraint is a boundary for the next change inside it (the computed boundary).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → host (auto) → leaf, laid out. Then host alone is laid out at a tight 120 x 80 from a request put on the
    ///   worklist directly: no S6b algorithm gives a child a constraint tight on both axes (the stretch of row L1 will).
    ///
    /// Steps:
    /// - Invalidate the leaf with ChangeKind.Size.
    ///
    /// Expected:
    /// - The layout root is host, not root; root has no flags.
    /// </remarks>
    [Fact]
    public void LayoutChange_InsideANodeWithATightConstraint_StopsThere() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle host = Box(tree, root, new LayoutStyle());
            NodeHandle leaf = Leaf(tree, host, 10, 10);
            LayOut(tree, root, 400, 300);
            tree.Nodes.GetRef(host).Dirty.SubtreeDirty = DirtyFlags.Layout;
            tree.LayoutRoots.Add(
                new LayoutRequest
                {
                    Node = host,
                    AvailableSpace = AvailableSpace.Definite(new Size(120, 80)),
                    Reason = LayoutReason.WindowResize,
                }
            );
            LayoutEngine.RunLayoutPass(tree);
            Assert.True(tree.Nodes.GetRef(host).Layout.CachedComputedBoundary);

            Invalidation.Invalidate(tree, leaf, ChangeKind.Size);

            Assert.Equal(host, Assert.Single(tree.LayoutRoots).Node);
            Assert.False(tree.Nodes.GetRef(root).Dirty.SubtreeDirty.HasFlag(DirtyFlags.Layout));
        });

    /// <summary>
    /// A node that is not attached yet makes no layout root when it is styled; it is laid out once it is appended.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A laid-out root. A new leaf and a new fixed-size box (a structural boundary), neither appended.
    ///
    /// Steps:
    /// - Set a style on both and run a pass. Then append both to the root and run a pass.
    ///
    /// Expected:
    /// - The first pass finds no layout root and does not fail; both nodes stay dirty.
    /// - After appending, both are laid out: the leaf is 30 high, the box 40 x 20, and nothing is left dirty.
    /// - A later style change on the box makes the box itself the layout root (it is attached now, and a boundary).
    /// </remarks>
    [Fact]
    public void DetachedNode_StyledBeforeAppend_IsLaidOutOnlyWhenAttached() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            LayOut(tree, root, 300, 200);
            NodeHandle leaf = tree.CreateNode(new LayoutStyle(), _ => new Size(10, 10));
            NodeHandle box = tree.CreateNode(Fixed(40, 20));

            tree.SetStyle(leaf, new LayoutStyle { Height = Length.Px(30) });
            tree.SetStyle(box, Fixed(40, 20));
            Assert.Empty(tree.LayoutRoots);
            LayoutEngine.RunLayoutPass(tree);
            Assert.False(IsLayoutClean(tree, leaf));
            Assert.False(IsLayoutClean(tree, box));

            tree.AppendChild(root, leaf);
            tree.AppendChild(root, box);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(30, tree.GetRect(leaf).Height);
            Assert.Equal((40f, 20f), (tree.GetRect(box).Width, tree.GetRect(box).Height));
            Assert.True(IsLayoutClean(tree, leaf));
            Assert.True(IsLayoutClean(tree, box));
            Assert.Empty(tree.LayoutRoots);

            // Appended after the root was set, the box is attached: a change to it now stops at it, a boundary.
            tree.SetStyle(box, Fixed(50, 20));
            Assert.Equal(box, Assert.Single(tree.LayoutRoots).Node);
        });

    /// <summary>
    /// A tree has one root: giving a window size to another node fails the invariant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release ends the process). A tree whose root was given a window size.
    ///
    /// Steps:
    /// - Give a window size to a second node without a parent.
    ///
    /// Expected:
    /// - <see cref="InvariantViolationException"/>.
    /// </remarks>
    [Fact]
    public void SecondRoot_FailsTheInvariant()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            LayOut(tree, root, 300, 200);
            NodeHandle other = tree.CreateNode(new LayoutStyle());

            Assert.Throws<InvariantViolationException>(() => Invalidation.NotifyWindowResized(tree, other, new Size(10, 10)));
        });
    }

    /// <summary>
    /// A paint-only change marks the node for paint and queues no layout root.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → leaf, laid out once.
    ///
    /// Steps:
    /// - Invalidate the leaf with ChangeKind.Color.
    ///
    /// Expected:
    /// - leaf: SelfDirty is Paint, SubtreeDirty is clear; no layout root; the leaf is in the paint updates.
    /// </remarks>
    [Fact]
    public void ColorChange_QueuesNoLayoutRoot() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle leaf = Leaf(tree, root, 10, 10);
            LayOut(tree, root, 400, 300);

            Invalidation.Invalidate(tree, leaf, ChangeKind.Color);

            Assert.Equal(DirtyFlags.Paint, tree.Nodes.GetRef(leaf).Dirty.SelfDirty);
            Assert.Equal(DirtyFlags.None, tree.Nodes.GetRef(leaf).Dirty.SubtreeDirty);
            Assert.Empty(tree.LayoutRoots);
            Assert.Equal(leaf, Assert.Single(tree.PaintUpdates));
        });

    /// <summary>
    /// Each kind of change maps to the stages of the design's matrix.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Map every ChangeKind.
    ///
    /// Expected:
    /// - The flags of design §2.4 for each.
    /// </remarks>
    [Theory]
    [InlineData(ChangeKind.TextContent, DirtyFlags.Text | DirtyFlags.Layout | DirtyFlags.Paint | DirtyFlags.HitTest)]
    [InlineData(ChangeKind.Font, DirtyFlags.Text | DirtyFlags.Layout | DirtyFlags.Paint)]
    [InlineData(ChangeKind.Size, DirtyFlags.Layout | DirtyFlags.Paint | DirtyFlags.HitTest)]
    [InlineData(ChangeKind.Color, DirtyFlags.Paint)]
    [InlineData(ChangeKind.TransformProp, DirtyFlags.Transform | DirtyFlags.Paint | DirtyFlags.HitTest)]
    [InlineData(ChangeKind.A11yLabel, DirtyFlags.Semantics)]
    internal void FlagsFor_EachKind_FollowsTheMatrix(ChangeKind kind, DirtyFlags expected) => Assert.Equal(expected, Invalidation.FlagsFor(kind));

    /// <summary>
    /// Two requests for one node merge into one: a window resize keeps its constraint and its reason over a later content
    /// change.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → leaf, laid out once.
    ///
    /// Steps:
    /// - Notify a window resize to 500 x 400, then invalidate the leaf with ChangeKind.Size.
    ///
    /// Expected:
    /// - One request: root, WindowResize, the tight constraint 500 x 400.
    /// </remarks>
    [Fact]
    public void Requests_ForOneNode_MergeAndKeepTheResize() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle leaf = Leaf(tree, root, 10, 10);
            LayOut(tree, root, 400, 300);

            Invalidation.NotifyWindowResized(tree, root, new Size(500, 400));
            Invalidation.Invalidate(tree, leaf, ChangeKind.Size);

            LayoutRequest request = Assert.Single(tree.LayoutRoots);
            Assert.Equal(root, request.Node);
            Assert.Equal(LayoutReason.WindowResize, request.Reason);
            Assert.Equal(AvailableSpace.Definite(new Size(500, 400)), request.AvailableSpace);
        });
}
