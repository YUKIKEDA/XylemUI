using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// Minimums and maximums that are sizing keywords (design §3.2, row L3) where Chrome's tests do not reach: a root, whose
// keywords the layout pass resolves against the window, and a change of content under a keyword.
[Collection(UiThreadGroup.Name)]
public sealed class LayoutSizingKeywordTests(UiThreadFixture ui)
{
    /// <summary>
    /// A block root takes its keyword limits against the window: a maximum width of min-content and a minimum height of stretch.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Root: block, maximum width min-content, minimum height stretch, window 300 x 100. Its one child is a leaf whose content
    ///   widths are 30 and 60 and whose content is 10 high at any width.
    ///
    /// Steps:
    /// - Lay the tree out.
    ///
    /// Expected:
    /// - The root is 30 wide (its min-content width, not the window's 300) and 100 high (the window, not the leaf's 10).
    /// </remarks>
    [Fact]
    public void RootKeywordLimits_ResolveAgainstTheWindow() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle { MaxWidth = Length.MinContent, MinHeight = Length.Stretch });
            NodeHandle leaf = tree.CreateNode(
                new LayoutStyle(),
                (known, available) => MeasureResult.Of(Maybe.Or(known.Width, available.Width.Kind == AvailableSpaceKind.MinContent ? 30 : 60), 10),
                _ => new IntrinsicWidths(30, 60, DependsOnHeight: false)
            );
            tree.AppendChild(root, leaf);

            LayOut(tree, root, 300, 100);

            Assert.Equal(new Rect(0, 0, 30, 100), tree.GetRect(root));
        });

    /// <summary>
    /// A node of a fixed size with a minimum width of max-content is no structural boundary: a change of its content changes
    /// its width.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Root: block, window 300 x 100. Its child B is 10 x 10 with a minimum width of max-content, and holds leaf L, whose
    ///   content widths are both 40. Laid out: B is 40 wide.
    ///
    /// Steps:
    /// - Make L's content widths 60, invalidate it, and run the pass.
    ///
    /// Expected:
    /// - B is 60 wide.
    /// </remarks>
    [Fact]
    public void ContentChange_UnderAMinimumOfMaxContent_ChangesTheFixedSize() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle box = Box(tree, root, Fixed(10, 10) with { MinWidth = Length.MaxContent });
            float width = 40;
            NodeHandle leaf = tree.CreateNode(
                new LayoutStyle(),
                (_, _) => MeasureResult.Of(width, 10),
                _ => new IntrinsicWidths(width, width, DependsOnHeight: false)
            );
            tree.AppendChild(box, leaf);
            LayOut(tree, root, 300, 100);
            Assert.Equal(40, tree.GetRect(box).Width);

            width = 60;
            Invalidation.Invalidate(tree, leaf, ChangeKind.TextContent);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(60, tree.GetRect(box).Width);
        });
}
