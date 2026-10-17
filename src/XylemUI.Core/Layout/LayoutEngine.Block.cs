namespace XylemUI;

internal static partial class LayoutEngine
{
    // CSS block layout (Taffy's compute_block_layout) without margin collapsing, floats, or out-of-flow children (design §3.2):
    // the children stack from the top, each as wide as the content box less its margins unless its style says otherwise, and
    // their margins always add up.
    private static Size ComputeBlock(UiTree tree, in LayoutStyle style, NodeHandle firstChild, in LayoutInput input, bool commit)
    {
        OptionalSize parent = input.ParentSize;
        Edges paddingBorder = PaddingBorder(in style, parent.Width);
        Size inset = paddingBorder.Sum();
        OptionalSize min = MinSize(in style, parent);
        OptionalSize max = MaxSize(in style, parent);
        OptionalSize styleSize = StyleSize(in style, parent);
        OptionalSize clampedStyleSize = input.SizingMode == SizingMode.InherentSize ? styleSize.Clamp(min, max) : OptionalSize.None;
        OptionalSize known = input.KnownDimensions.Or(MinMaxDefinite(min, max)).Or(clampedStyleSize).AtLeast(inset);

        if (!commit)
        {
            if (known.IsComplete)
            {
                return ToSize(known);
            }

            if (input.Axis == RequestedAxis.Horizontal && !float.IsNaN(known.Width))
            {
                return new Size(known.Width, 0);
            }
        }

        // Percentages of the children resolve against the content box, the height only when it is definite.
        var percentBasis = new OptionalSize(known.Width, input.KnownHeightIsDefinite ? known.Height : float.NaN);
        OptionalSize contentBox = percentBasis.Minus(inset);

        float outerWidth = known.Width;
        if (float.IsNaN(outerWidth))
        {
            AvailableSpace availableWidth = input.Available.Width.Minus(inset.Width);
            float intrinsic = ContentBasedBlockWidth(tree, firstChild, contentBox, availableWidth) + inset.Width;
            outerWidth = Math.Max(Maybe.Clamp(intrinsic, min.Width, max.Width), inset.Width);
        }

        if (!commit)
        {
            if (!float.IsNaN(known.Height))
            {
                return new Size(outerWidth, known.Height);
            }

            if (input.Axis == RequestedAxis.Horizontal)
            {
                return new Size(outerWidth, 0);
            }
        }

        // A content-size run ignores the node's own height, so it is no basis for the children's percentages either.
        float percentHeight = Maybe.Or(
            percentBasis.Height,
            input.SizingMode == SizingMode.InherentSize ? Maybe.Max(styleSize.Height, min.Height) : float.NaN
        );

        // Percentage paddings and borders resolve against the containing block's width, or the node's own at the root.
        Edges resolvedInset = PaddingBorder(in style, Maybe.Or(parent.Width, outerWidth));
        float contentHeight = LayOutBlockChildren(tree, firstChild, contentBox, outerWidth, percentHeight, resolvedInset, commit);

        float outerHeight = Maybe.Or(known.Height, Maybe.Clamp(contentHeight, min.Height, max.Height));
        return new Size(outerWidth, Math.Max(outerHeight, inset.Height));
    }

    // The width of a block whose width is not known: its widest child, margins included (Taffy's
    // determine_content_based_container_width).
    private static float ContentBasedBlockWidth(UiTree tree, NodeHandle firstChild, OptionalSize contentBox, AvailableSpace availableWidth)
    {
        float widest = 0;
        for (NodeHandle child = firstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            LayoutStyle childStyle = tree.Nodes.GetRef(child).Layout.Style;
            OptionalSize childKnown = StyleSize(in childStyle, contentBox)
                .Clamp(MinSize(in childStyle, contentBox), MaxSize(in childStyle, contentBox));
            float marginSum = EdgeInsets.ResolveOrZero(childStyle.Margin, availableWidth.ToOption()).Horizontal;

            float width = childKnown.Width;
            if (float.IsNaN(width))
            {
                AvailableSpace childWidth = MeasureSpaceOf(childStyle.Width, float.NaN) ?? availableWidth.Minus(marginSum);
                width = MeasureChild(
                    tree,
                    child,
                    childKnown,
                    OptionalSize.None,
                    new AvailableSize(childWidth, AvailableSpace.MinContent),
                    SizingMode.InherentSize,
                    horizontal: true
                );
            }

            float paddingBorderWidth = PaddingBorder(in childStyle, contentBox.Width).Horizontal;
            widest = Math.Max(widest, Math.Max(width, paddingBorderWidth) + marginSum);
        }

        return widest;
    }

    // Lays out or measures each child at the top of the space left by the ones before it, and returns the outer height of the
    // content (Taffy's perform_final_layout_on_in_flow_children, without margin collapsing). The children's sizes resolve
    // against the content box the node knew before its children; the input they get carries the width found since.
    private static float LayOutBlockChildren(
        UiTree tree,
        NodeHandle firstChild,
        OptionalSize contentBox,
        float outerWidth,
        float percentHeight,
        Edges inset,
        bool commit
    )
    {
        float innerWidth = Math.Max(0, outerWidth - inset.Horizontal);
        var parentSize = new OptionalSize(innerWidth, percentHeight - inset.Vertical);
        float y = inset.Top;
        for (NodeHandle child = firstChild; !child.IsNone; )
        {
            ref UiNode c = ref tree.Nodes.GetRef(child);
            NodeHandle next = c.NextSibling;
            LayoutStyle childStyle = c.Layout.Style;

            OptionalSize size = StyleSize(in childStyle, contentBox);
            OptionalSize min = MinSize(in childStyle, contentBox);
            OptionalSize max = MaxSize(in childStyle, contentBox);
            Edges margin = EdgeInsets.ResolveOrZero(childStyle.Margin, innerWidth);
            float stretchWidth = Math.Max(0, innerWidth - margin.Horizontal);

            float keywordWidth = float.NaN;
            if (MeasureSpaceOf(childStyle.Width, stretchWidth) is { } keywordSpace)
            {
                keywordWidth = MeasureChild(
                    tree,
                    child,
                    OptionalSize.None,
                    parentSize,
                    new AvailableSize(keywordSpace, AvailableSpace.MaxContent),
                    SizingMode.InherentSize,
                    horizontal: true
                );
            }

            float width = Maybe.Clamp(Maybe.Or(Maybe.Or(size.Width, keywordWidth), stretchWidth), min.Width, max.Width);
            OptionalSize known = new OptionalSize(width, size.Height).Clamp(min, max);
            var input = new LayoutInput(
                known,
                true,
                true,
                parentSize,
                new AvailableSize(AvailableSpace.Definite(stretchWidth), AvailableSpace.MaxContent),
                SizingMode.InherentSize,
                RequestedAxis.Both
            );
            Size laidOut = LayoutChild(tree, child, in input, commit);
            if (commit)
            {
                Place(tree, child, inset.Left + margin.Left, y + margin.Top);
            }

            y += margin.Top + laidOut.Height + margin.Bottom;
            child = next;
        }

        return y + inset.Bottom;
    }
}
