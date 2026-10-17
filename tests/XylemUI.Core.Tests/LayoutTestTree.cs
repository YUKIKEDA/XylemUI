namespace XylemUI.Tests;

// Builds small UI trees for the layout tests (S6b). Every size is chosen so the expected rectangles can be computed by hand.
internal static class LayoutTestTree
{
    // A leaf whose content always measures width x height.
    public static NodeHandle Leaf(UiTree tree, NodeHandle parent, float width, float height, LayoutStyle style = default)
    {
        NodeHandle leaf = tree.CreateNode(StyleOrDefault(style), _ => new Size(width, height));
        tree.AppendChild(parent, leaf);
        return leaf;
    }

    // A container child with the given style.
    public static NodeHandle Box(UiTree tree, NodeHandle parent, LayoutStyle style)
    {
        NodeHandle box = tree.CreateNode(style);
        tree.AppendChild(parent, box);
        return box;
    }

    public static LayoutStyle Fixed(float width, float height) => new() { Width = Length.Px(width), Height = Length.Px(height) };

    public static LayoutStyle Row() => new() { Algorithm = new LayoutAlgorithm.Flex(FlexDirection.Row) };

    public static LayoutStyle Column() => new() { Algorithm = new LayoutAlgorithm.Flex(FlexDirection.Column) };

    // Lays the tree out at the window size and returns nothing; the tree is clean afterwards.
    public static void LayOut(UiTree tree, NodeHandle root, float width, float height)
    {
        Invalidation.NotifyWindowResized(tree, root, new Size(width, height));
        LayoutEngine.RunLayoutPass(tree);
    }

    public static bool IsLayoutClean(UiTree tree, NodeHandle node)
    {
        DirtyState dirty = tree.Nodes.GetRef(node).Dirty;
        return ((dirty.SelfDirty | dirty.SubtreeDirty) & DirtyFlags.Layout) == 0;
    }

    // `default(LayoutStyle)` skips the constructor, so its FlexShrink would be 0 instead of 1.
    private static LayoutStyle StyleOrDefault(LayoutStyle style) => style == default ? new LayoutStyle() : style;
}
