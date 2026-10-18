namespace XylemUI;

internal static partial class LayoutEngine
{
    // CSS block layout (Taffy's compute_block_layout) without margin collapsing, floats, or out-of-flow children (design §3.2):
    // the children stack from the top, each as wide as the content box less its margins unless its style says otherwise, and
    // their margins always add up. The block's first baseline is its first child's that has one, read when a parent asks for
    // it (ReadBaseline).
    private static Size ComputeBlock(UiTree tree, in LayoutStyle style, BlockProps props, NodeHandle firstChild, in LayoutInput input, bool commit)
    {
        OptionalSize parent = input.ParentSize;
        Edges paddingBorder = PaddingBorder(in style, parent.Width);
        Size inset = paddingBorder.Sum();
        OptionalSize min = MinSize(in style, parent);
        OptionalSize max = MaxSize(in style, parent);
        OptionalSize styleSize = StyleSize(in style, parent);
        OptionalSize clampedStyleSize = input.SizingMode == SizingMode.InherentSize ? styleSize.Clamp(min, max) : OptionalSize.None;

        // A size the parent decided on one axis gives the other through the aspect ratio (CSS Sizing 4, as Taffy's
        // compute_block_layout): only a newly filled axis is clamped, and a measuring run that decides neither gets none.
        OptionalSize given = input.KnownDimensions;
        if (style.HasAspectRatio)
        {
            given = given.Or(WithAspectRatio(given, style.AspectRatio).Clamp(min, max));
        }

        OptionalSize known = given.Or(MinMaxDefinite(min, max)).Or(clampedStyleSize).AtLeast(inset);

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
        float contentHeight = LayOutBlockChildren(tree, props, firstChild, contentBox, outerWidth, percentHeight, resolvedInset, commit);

        float outerHeight = Math.Max(Maybe.Or(known.Height, Maybe.Clamp(contentHeight, min.Height, max.Height)), inset.Height);

        // `align-content` moves the children, taken as one subject, in the free height (CSS Box Alignment §5.1).
        float offset = 0;
        if (commit && props.AlignContent.Keyword != AlignContentKeyword.Normal)
        {
            float freeSpace = outerHeight - contentHeight;
            offset = AlignmentOffset(freeSpace, 1, 0, AlignmentFallback(freeSpace, 1, props.AlignContent), reversed: false, isFirst: true);
            if (offset != 0)
            {
                for (NodeHandle child = firstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
                {
                    ref UiNode c = ref tree.Nodes.GetRef(child);
                    if (c.Layout.Style.IsInFlow)
                    {
                        Rect rect = c.Layout.Rect;
                        Place(tree, child, rect.X, rect.Y + offset);
                    }
                }
            }
        }

        if (commit)
        {
            LayOutBlockOutOfFlowChildren(tree, firstChild, in style, props, new Size(outerWidth, outerHeight), resolvedInset, parent.Width, offset);
        }

        return new Size(outerWidth, outerHeight);
    }

    // The width of a block whose width is not known: its widest child, margins included (Taffy's
    // determine_content_based_container_width).
    private static float ContentBasedBlockWidth(UiTree tree, NodeHandle firstChild, OptionalSize contentBox, AvailableSpace availableWidth)
    {
        float widest = 0;
        for (NodeHandle child = firstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            ref readonly LayoutStyle childStyle = ref tree.Nodes.GetRef(child).Layout.Style;
            if (!childStyle.IsInFlow)
            {
                continue;
            }

            OptionalSize childKnown = StyleSize(in childStyle, contentBox)
                .Clamp(MinSize(in childStyle, contentBox), MaxSize(in childStyle, contentBox));
            float marginSum = EdgeInsets.ResolveOrZero(childStyle.Margin, availableWidth.ToOption()).Horizontal;
            float paddingBorderWidth = PaddingBorder(in childStyle, contentBox.Width).Horizontal;
            AvailableSpace? keywordSpace = MeasureSpaceOf(childStyle.Width, float.NaN);

            // The style is read above: no ref into the arena is held across the measuring run (design §2.5).
            float width = childKnown.Width;
            if (float.IsNaN(width))
            {
                AvailableSpace childWidth = keywordSpace ?? availableWidth.Minus(marginSum);
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

            widest = Math.Max(widest, Math.Max(width, paddingBorderWidth) + marginSum);
        }

        return widest;
    }

    // Lays out or measures each child at the top of the space left by the ones before it, and returns the outer height of the
    // content (Taffy's perform_final_layout_on_in_flow_children, without margin collapsing). The children's sizes resolve
    // against the content box the node knew before its children; the input they get carries the width found since. A child's
    // automatic side margins share the width it leaves; otherwise a `justify-self` other than `stretch` places it.
    private static float LayOutBlockChildren(
        UiTree tree,
        BlockProps props,
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
            if (!childStyle.IsInFlow)
            {
                child = next;
                continue;
            }

            OptionalSize size = StyleSize(in childStyle, contentBox);
            OptionalSize min = MinSize(in childStyle, contentBox);
            OptionalSize max = MaxSize(in childStyle, contentBox);
            Edges margin = EdgeInsets.ResolveOrZero(childStyle.Margin, innerWidth);
            bool autoLeft = childStyle.Margin.Left.IsAuto;
            bool autoRight = childStyle.Margin.Right.IsAuto;
            float stretchWidth = Math.Max(0, innerWidth - margin.Horizontal);

            // `normal` and `stretch` keep the block's default; any other alignment makes an automatic width fit the content.
            AlignItems justify = childStyle.JustifySelf ?? props.JustifyItems;
            bool aligned = justify.Keyword is not (AlignItemsKeyword.Normal or AlignItemsKeyword.Stretch);
            Length widthStyle = aligned && childStyle.Width.IsAuto ? Length.FitContent : childStyle.Width;

            float keywordWidth = float.NaN;
            if (MeasureSpaceOf(widthStyle, stretchWidth) is { } keywordSpace)
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
                int autoMargins = (autoLeft ? 1 : 0) + (autoRight ? 1 : 0);
                float autoMargin = autoMargins == 0 ? 0 : Math.Max(0, stretchWidth - laidOut.Width) / autoMargins;
                float left = autoLeft ? autoMargin : margin.Left;
                float right = autoRight ? autoMargin : margin.Right;
                float x = inset.Left + left;
                if (aligned && autoMargins == 0)
                {
                    float freeSpace = innerWidth - (laidOut.Width + left + right);
                    AlignItemsKeyword keyword = justify.Safe && freeSpace < 0 ? AlignItemsKeyword.Start : justify.Keyword;
                    x += keyword switch
                    {
                        AlignItemsKeyword.Center => freeSpace / 2,
                        AlignItemsKeyword.End or AlignItemsKeyword.FlexEnd => freeSpace,
                        _ => 0,
                    };
                }

                (float dx, float dy) = RelativeOffset(in childStyle, innerWidth, parentSize.Height);
                Place(tree, child, x + dx, y + margin.Top + dy);
            }

            y += margin.Top + laidOut.Height + margin.Bottom;
            child = next;
        }

        return y + inset.Bottom;
    }
}
