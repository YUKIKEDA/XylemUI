using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// The tree's moves that Show and For use (row V3a, design §2.7, "The tree's moves"): a node put after a given sibling, taken out
// after a given sibling, put in another's place, and every child taken out at once. Each keeps what the parent knows of its
// children (the last child, the count of children out of the flow, the version of the children) and marks the parent's layout.
[Collection(UiThreadGroup.Name)]
public sealed class UiTreeMoveTests(UiThreadFixture ui)
{
    /// <summary>
    /// <see cref="UiTree.InsertAfter"/> puts a node first, between two children, or last, and gives it and its subtree the
    /// parent's depth, attachment, and disabled state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → box (disabled) → a, b, laid out at 400 x 300. Three nodes with no parent: a hidden one, a plain one, and one with a
    ///   child.
    ///
    /// Steps:
    /// - Put the hidden node first, the plain one after a, and the one with a child after b.
    ///
    /// Expected:
    /// - The children are hidden, a, plain, b, last; the last child is the last one; one child is out of the flow; the version of
    ///   the children rose by 3; the box's own layout is dirty.
    /// - The three nodes and the child have the depths below the box, are attached, and are disabled.
    /// </remarks>
    [Fact]
    public void InsertAfter_PutsTheNodeInPlace_WithTheParentsState() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle box = Box(tree, root, Column());
            NodeHandle a = Leaf(tree, box, 10, 10);
            NodeHandle b = Leaf(tree, box, 10, 10);
            tree.SetDisabled(box, true);
            LayOut(tree, root, 400, 300);
            ulong version = tree.Nodes.GetRef(box).Versions.Children;
            NodeHandle hidden = tree.CreateNode(new LayoutStyle { Hidden = true });
            NodeHandle plain = tree.CreateNode(new LayoutStyle());
            NodeHandle last = tree.CreateNode(new LayoutStyle());
            NodeHandle inner = Box(tree, last, new LayoutStyle());

            tree.InsertAfter(box, default, hidden);
            tree.InsertAfter(box, a, plain);
            tree.InsertAfter(box, b, last);

            ref UiNode p = ref tree.Nodes.GetRef(box);
            Assert.Equal([hidden, a, plain, b, last], Children(tree, box));
            Assert.Equal(last, p.LastChild);
            Assert.Equal(1, p.Layout.OutOfFlowChildren);
            Assert.Equal(version + 3, p.Versions.Children);
            Assert.True(p.Dirty.SelfDirty.HasFlag(DirtyFlags.Layout));
            foreach ((NodeHandle node, int below) in new[] { (hidden, 1), (plain, 1), (last, 1), (inner, 2) })
            {
                ref UiNode n = ref tree.Nodes.GetRef(node);
                Assert.Equal(p.Depth + below, n.Depth);
                Assert.True(n.IsAttached);
                Assert.True((n.Pseudo & PseudoState.Disabled) != 0);
            }
        });

    /// <summary>
    /// <see cref="UiTree.DetachAfter"/> takes a child out given the one before it, and the child stays alive with no parent.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → box → a, hidden, b, laid out at 400 x 300.
    ///
    /// Steps:
    /// - Take the hidden child out after a; lay out again; take b out after a.
    ///
    /// Expected:
    /// - After the first: the children are a, b; no child is out of the flow; the version rose by 1; the box's own layout is
    ///   dirty; the hidden node is alive with no parent and no next sibling.
    /// - After the second: the last child is a, and the box's own layout is dirty again.
    /// </remarks>
    [Fact]
    public void DetachAfter_TakesTheChildOut_AndMarksTheParent() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle box = Box(tree, root, Column());
            NodeHandle a = Leaf(tree, box, 10, 10);
            NodeHandle hidden = Box(tree, box, new LayoutStyle { Hidden = true });
            NodeHandle b = Leaf(tree, box, 10, 10);
            LayOut(tree, root, 400, 300);
            ulong version = tree.Nodes.GetRef(box).Versions.Children;

            tree.DetachAfter(box, a, hidden);

            Assert.Equal([a, b], Children(tree, box));
            Assert.Equal(0, tree.Nodes.GetRef(box).Layout.OutOfFlowChildren);
            Assert.Equal(version + 1, tree.Nodes.GetRef(box).Versions.Children);
            Assert.True(tree.Nodes.GetRef(box).Dirty.SelfDirty.HasFlag(DirtyFlags.Layout));
            Assert.True(tree.Nodes.IsAlive(hidden));
            Assert.True(tree.Nodes.GetRef(hidden).Parent.IsNone);
            Assert.True(tree.Nodes.GetRef(hidden).NextSibling.IsNone);

            LayoutEngine.RunLayoutPass(tree);
            tree.DetachAfter(box, a, b);

            Assert.Equal(a, tree.Nodes.GetRef(box).LastChild);
            Assert.True(tree.Nodes.GetRef(box).Dirty.SelfDirty.HasFlag(DirtyFlags.Layout));
        });

    /// <summary>
    /// <see cref="UiTree.ReplaceChild"/> puts a node in another's place, and the other stays alive with no parent.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → box → a, b, c, laid out at 400 x 300; a hidden node with no parent.
    ///
    /// Steps:
    /// - Replace b with the hidden node.
    ///
    /// Expected:
    /// - The children are a, hidden, c; one child is out of the flow; b is alive with no parent; one sibling was walked.
    /// </remarks>
    [Fact]
    public void ReplaceChild_PutsTheNodeInTheOthersPlace() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle box = Box(tree, root, Column());
            NodeHandle a = Leaf(tree, box, 10, 10);
            NodeHandle b = Leaf(tree, box, 10, 10);
            NodeHandle c = Leaf(tree, box, 10, 10);
            LayOut(tree, root, 400, 300);
            NodeHandle hidden = tree.CreateNode(new LayoutStyle { Hidden = true });
            long walks = tree.SiblingWalks;

            tree.ReplaceChild(b, hidden);

            Assert.Equal([a, hidden, c], Children(tree, box));
            Assert.Equal(1, tree.Nodes.GetRef(box).Layout.OutOfFlowChildren);
            Assert.True(tree.Nodes.IsAlive(b));
            Assert.True(tree.Nodes.GetRef(b).Parent.IsNone);
            Assert.Equal(walks + 1, tree.SiblingWalks);
        });

    /// <summary>
    /// <see cref="UiTree.DetachChildren"/> takes every child out in one pass; the children stay alive with no parent, and the
    /// parent takes new children as an empty one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → box → a, hidden, b, laid out at 400 x 300.
    ///
    /// Steps:
    /// - Take every child out; then append a again.
    ///
    /// Expected:
    /// - The box has no first or last child, no child out of the flow, a version 1 higher, and its own layout dirty; each child is
    ///   alive with no parent and no next sibling; no sibling was walked.
    /// - After the append, the children are a alone.
    /// </remarks>
    [Fact]
    public void DetachChildren_TakesEveryChildOut() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle box = Box(tree, root, Column());
            NodeHandle a = Leaf(tree, box, 10, 10);
            NodeHandle hidden = Box(tree, box, new LayoutStyle { Hidden = true });
            NodeHandle b = Leaf(tree, box, 10, 10);
            LayOut(tree, root, 400, 300);
            ulong version = tree.Nodes.GetRef(box).Versions.Children;
            long walks = tree.SiblingWalks;

            tree.DetachChildren(box);

            ref UiNode p = ref tree.Nodes.GetRef(box);
            Assert.True(p.FirstChild.IsNone);
            Assert.True(p.LastChild.IsNone);
            Assert.Equal(0, p.Layout.OutOfFlowChildren);
            Assert.Equal(version + 1, p.Versions.Children);
            Assert.True(p.Dirty.SelfDirty.HasFlag(DirtyFlags.Layout));
            foreach (NodeHandle child in new[] { a, hidden, b })
            {
                Assert.True(tree.Nodes.IsAlive(child));
                Assert.True(tree.Nodes.GetRef(child).Parent.IsNone);
                Assert.True(tree.Nodes.GetRef(child).NextSibling.IsNone);
            }

            Assert.Equal(walks, tree.SiblingWalks);
            tree.AppendChild(box, a);
            Assert.Equal([a], Children(tree, box));
        });

    private static List<NodeHandle> Children(UiTree tree, NodeHandle parent)
    {
        var children = new List<NodeHandle>();
        for (NodeHandle child = tree.Nodes.GetRef(parent).FirstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            children.Add(child);
        }

        return children;
    }
}
