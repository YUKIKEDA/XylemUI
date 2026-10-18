namespace XylemUI;

internal static partial class LayoutEngine
{
    // Row L2 (design §3.2): the children that are not in a container's flow. An absolutely positioned child is laid out after
    // the flow against its parent, which is always its containing block here (Flutter's Stack, Avalonia's Canvas, and Taffy
    // 0.9.0, which GPUI uses; the fixtures put `position: relative` on every box, so CSS's nearest positioned ancestor is the
    // parent there too). Its size and place follow CSS Position 3 as the oof.rs of the Taffy clone, which made the fixtures,
    // does: the inset-modified containing block, the stretch-fit size, and a static position where both insets are auto. A
    // hidden child and its subtree get empty rectangles.

    // Lays out the absolutely positioned children of a flex container and hides its hidden ones, after the final layout of its
    // items. The static position of each axis is the content box, aligned as the container would align the child along the
    // main axis (`justify-content`) and across it (`align-self`, with `stretch` as the start: CSS Flexbox §4.1).
    private static void LayOutFlexOutOfFlowChildren(
        UiTree tree,
        NodeHandle firstChild,
        in LayoutStyle style,
        Size containerSize,
        in FlexConstants constants,
        float parentWidth
    )
    {
        bool row = constants.Row;
        Edges border = EdgeInsets.ResolveOrZero(style.Border, parentWidth);
        Edges inset = constants.Inset;
        float mainSize = row ? containerSize.Width : containerSize.Height;
        float crossSize = row ? containerSize.Height : containerSize.Width;
        var mainArea = new StaticArea(inset.MainStart(row), mainSize - (row ? inset.Right : inset.Bottom));
        var crossArea = new StaticArea(inset.CrossStart(row), crossSize - (row ? inset.Bottom : inset.Right));
        bool mainReversed = constants.Reverse;
        bool crossReversed = constants.WrapReverse;

        // `start` and `end` follow the writing direction, not a reversed flex direction; `flex-start`, `flex-end`, and the
        // fallbacks of the distributions follow the flex direction. Chrome applies no `safe` fallback along the main axis.
        StaticEdge mainEdge = constants.JustifyContent.Keyword switch
        {
            AlignContentKeyword.Start => StaticEdge.Start,
            AlignContentKeyword.End => StaticEdge.End,
            AlignContentKeyword.SpaceEvenly or AlignContentKeyword.SpaceAround or AlignContentKeyword.Center => StaticEdge.Center,
            AlignContentKeyword.FlexEnd => mainReversed ? StaticEdge.Start : StaticEdge.End,
            _ => mainReversed ? StaticEdge.End : StaticEdge.Start,
        };
        var mainStatic = new StaticPosition(mainArea, mainEdge, Safe: false);
        for (NodeHandle child = firstChild; !child.IsNone; )
        {
            ref UiNode c = ref tree.Nodes.GetRef(child);
            NodeHandle next = c.NextSibling;
            ref readonly LayoutStyle s = ref c.Layout.Style;
            if (s.Hidden)
            {
                HideSubtree(tree, child);
            }
            else if (s.Position == Position.Absolute)
            {
                AlignItems alignSelf = s.AlignSelf ?? constants.AlignItems;
                AlignItemsKeyword keyword = alignSelf.Keyword == AlignItemsKeyword.Normal ? AlignItemsKeyword.Stretch : alignSelf.Keyword;
                var crossStatic = new StaticPosition(crossArea, FlexCrossEdge(keyword, crossReversed), alignSelf.Safe);
                LayOutAbsoluteChild(tree, child, in s, containerSize, border, row ? mainStatic : crossStatic, row ? crossStatic : mainStatic);
            }

            child = next;
        }
    }

    // Returns a value indicating whether the root is absolutely positioned in a viewport of definite size: then the viewport is
    // its containing block (Taffy's compute_root_layout); in a viewport of content size the root is laid out in the flow.
    private static bool IsAbsoluteRootIn(UiTree tree, NodeHandle root, in LayoutInput input) =>
        tree.Nodes.GetRef(root).Layout.Style.Position == Position.Absolute && input.ParentSize.IsComplete;

    // Lays out an absolutely positioned root against the viewport, with its static position at the viewport's origin.
    private static void LayOutAbsoluteRoot(UiTree tree, NodeHandle root, Size viewport)
    {
        LayoutStyle style = tree.Nodes.GetRef(root).Layout.Style;
        var origin = new StaticPosition(new StaticArea(0, 0), StaticEdge.Start, Safe: false);
        LayOutAbsoluteChild(tree, root, in style, viewport, default, origin, origin);
    }

    // The edge of the content box that `align-self` takes an absolutely positioned flex child to.
    private static StaticEdge FlexCrossEdge(AlignItemsKeyword keyword, bool reversed) =>
        keyword switch
        {
            AlignItemsKeyword.Start or AlignItemsKeyword.Baseline => StaticEdge.Start,
            AlignItemsKeyword.End => StaticEdge.End,
            AlignItemsKeyword.Center => StaticEdge.Center,
            AlignItemsKeyword.FlexEnd => reversed ? StaticEdge.Start : StaticEdge.End,
            _ => reversed ? StaticEdge.End : StaticEdge.Start,
        };

    // Lays out the absolutely positioned children of a block and hides its hidden ones, after the flow. The static position is
    // the content box across (aligned by `justify-self`, else the block's `justify-items`) and a line where the child would have
    // started in the flow down (aligned by `align-self`, `normal` by default), moved with the flow by `align-content`.
    private static void LayOutBlockOutOfFlowChildren(
        UiTree tree,
        NodeHandle firstChild,
        in LayoutStyle style,
        BlockProps props,
        Size outerSize,
        Edges inset,
        float parentWidth,
        float alignOffset
    )
    {
        Edges border = EdgeInsets.ResolveOrZero(style.Border, parentWidth);
        float innerWidth = Math.Max(0, outerSize.Width - inset.Horizontal);
        var inlineArea = new StaticArea(inset.Left, outerSize.Width - inset.Right);
        float staticY = inset.Top;
        for (NodeHandle child = firstChild; !child.IsNone; )
        {
            ref UiNode c = ref tree.Nodes.GetRef(child);
            NodeHandle next = c.NextSibling;
            ref readonly LayoutStyle s = ref c.Layout.Style;
            if (s.Hidden)
            {
                HideSubtree(tree, child);
            }
            else if (s.Position != Position.Absolute)
            {
                // The flow ahead of the next static position, as LayOutBlockChildren stacked it (no margin collapsing).
                Edges flowMargin = EdgeInsets.ResolveOrZero(s.Margin, innerWidth);
                staticY += flowMargin.Top + c.Layout.Rect.Height + flowMargin.Bottom;
            }
            else
            {
                AlignItems justify = s.JustifySelf ?? props.JustifyItems;
                AlignItems align = s.AlignSelf ?? new AlignItems(AlignItemsKeyword.Normal);
                float y = staticY + alignOffset;
                LayOutAbsoluteChild(
                    tree,
                    child,
                    in s,
                    outerSize,
                    border,
                    StaticPosition.Of(justify, inlineArea),
                    StaticPosition.Of(align, new StaticArea(y, y))
                );
            }

            child = next;
        }
    }

    // Sizes and places one absolutely positioned child against its parent's padding box (Taffy's layout_oof_box), and lays it
    // out. The static positions are relative to the parent's border box.
    private static void LayOutAbsoluteChild(
        UiTree tree,
        NodeHandle child,
        in LayoutStyle s,
        Size containerSize,
        Edges border,
        StaticPosition staticX,
        StaticPosition staticY
    )
    {
        var area = new OptionalSize(containerSize.Width - border.Horizontal, containerSize.Height - border.Vertical);
        var margin = new Edges(
            s.Margin.Left.Resolve(area.Width),
            s.Margin.Top.Resolve(area.Width),
            s.Margin.Right.Resolve(area.Width),
            s.Margin.Bottom.Resolve(area.Width)
        );
        Size paddingBorder = PaddingBorder(in s, area.Width).Sum();
        OptionalSize styleSize = StyleSize(in s, area);
        OptionalSize min = MinSize(in s, area).Or(new OptionalSize(paddingBorder.Width, paddingBorder.Height)).AtLeast(paddingBorder);
        OptionalSize max = MaxSize(in s, area);
        OptionalSize known = styleSize.Clamp(min, max);

        // `auto` is `normal` for an absolutely positioned box: it does not follow the container's `*-items` here.
        var horizontal = new AbsoluteAxis(
            area.Width,
            s.Inset.Left.Resolve(area.Width),
            s.Inset.Right.Resolve(area.Width),
            margin.Left,
            margin.Right,
            staticX.Minus(border.Left),
            AlignmentOf(s.JustifySelf),
            s.JustifySelf?.Safe == true,
            isInline: true
        );
        var vertical = new AbsoluteAxis(
            area.Height,
            s.Inset.Top.Resolve(area.Height),
            s.Inset.Bottom.Resolve(area.Height),
            margin.Top,
            margin.Bottom,
            staticY.Minus(border.Top),
            AlignmentOf(s.AlignSelf),
            s.AlignSelf?.Safe == true,
            isInline: false
        );
        var stretchFit = new Size(horizontal.StretchFitSize(), vertical.StretchFitSize());
        var available = new AvailableSize(
            AvailableSpace.Definite(Maybe.Clamp(stretchFit.Width, min.Width, max.Width)),
            AvailableSpace.Definite(Maybe.Clamp(stretchFit.Height, min.Height, max.Height))
        );

        // An automatic size is the stretch-fit size where both insets are set and the alignment stretches; the width first, so
        // that the aspect ratio gives the height (CSS Position 3 §5.1). An explicit `stretch` on both axes ignores the ratio.
        bool stretchWidth = float.IsNaN(known.Width) && horizontal.IsStretchSized;
        bool stretchHeight = float.IsNaN(known.Height) && vertical.IsStretchSized;
        bool bothExplicit = horizontal.Alignment == AbsoluteAlignment.Stretch && vertical.Alignment == AbsoluteAlignment.Stretch;
        float ratio = stretchWidth && stretchHeight && bothExplicit ? float.NaN : s.AspectRatio;
        if (stretchWidth)
        {
            known = WithAspectRatio(known with { Width = stretchFit.Width }, ratio).Clamp(min, max);
        }

        if (float.IsNaN(known.Height) && stretchHeight)
        {
            known = WithAspectRatio(known with { Height = stretchFit.Height }, ratio).Clamp(min, max);
        }

        OptionalSize sized = known;
        if (!known.IsComplete)
        {
            var measuring = new LayoutInput(known, true, true, area, available, SizingMode.ContentSize, RequestedAxis.Both);
            Size measured = LayoutChild(tree, child, in measuring, commit: false);
            sized = known.Or(new OptionalSize(measured.Width, measured.Height));
        }

        Size final = ToSize(sized.Clamp(min, max));
        var input = new LayoutInput(
            new OptionalSize(final.Width, final.Height),
            true,
            true,
            area,
            available,
            SizingMode.ContentSize,
            RequestedAxis.Both
        );
        _ = LayoutChild(tree, child, in input, commit: true);
        (float marginLeft, float marginRight) = horizontal.ResolveMargins(final.Width);
        (float marginTop, float marginBottom) = vertical.ResolveMargins(final.Height);
        float x = horizontal.Position(final.Width, marginLeft, marginRight) + border.Left;
        float y = vertical.Position(final.Height, marginTop, marginBottom) + border.Top;
        Place(tree, child, x, y);
    }

    private static AbsoluteAlignment AlignmentOf(AlignItems? alignment) =>
        (alignment?.Keyword ?? AlignItemsKeyword.Normal) switch
        {
            AlignItemsKeyword.Normal => AbsoluteAlignment.Normal,
            AlignItemsKeyword.Stretch => AbsoluteAlignment.Stretch,
            AlignItemsKeyword.Center => AbsoluteAlignment.Center,
            AlignItemsKeyword.End or AlignItemsKeyword.FlexEnd => AbsoluteAlignment.End,
            _ => AbsoluteAlignment.Start,
        };

    // Places a box of a size in a line by an edge.
    private static float AlignIn(StaticArea area, float size, StaticEdge edge) =>
        edge switch
        {
            StaticEdge.Start => area.Start,
            StaticEdge.End => area.End - size,
            _ => (area.Start + area.End - size) / 2,
        };

    // The shift of a relatively positioned child after its layout: its left inset, else minus its right; its top, else minus
    // its bottom. It moves no other child.
    private static (float X, float Y) RelativeOffset(in LayoutStyle style, float basisWidth, float basisHeight)
    {
        if (style.Position != Position.Relative)
        {
            return (0, 0);
        }

        EdgeInsets<LengthPercentageAuto> inset = style.Inset;
        float left = inset.Left.Resolve(basisWidth);
        float right = inset.Right.Resolve(basisWidth);
        float top = inset.Top.Resolve(basisHeight);
        float bottom = inset.Bottom.Resolve(basisHeight);
        return (Maybe.Or(left, Maybe.Or(-right, 0)), Maybe.Or(top, Maybe.Or(-bottom, 0)));
    }

    // A node out of the layout and its subtree: empty rectangles at the origin, no baseline, and nothing left to lay out. Their
    // last layouts no longer hold (WasLaidOut), so that a node shown again is laid out, not taken from its cache.
    private static void HideSubtree(UiTree tree, NodeHandle handle)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        if (node.Layout.Rect != default)
        {
            node.Layout.Rect = default;
            OnRectChanged(tree, handle);
        }

        ref UiNode n = ref tree.Nodes.GetRef(handle);
        n.Layout.FirstBaseline = float.NaN;
        n.Layout.WasLaidOut = false;
        n.Layout.CachedComputedBoundary = false;
        n.BaselineChild = default;
        n.Dirty.SelfDirty &= ~DirtyFlags.Layout;
        n.Dirty.SubtreeDirty &= ~DirtyFlags.Layout;
        for (NodeHandle child = n.FirstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            HideSubtree(tree, child);
        }
    }

    // Returns a value indicating whether the node or an ancestor is hidden: its layout belongs to no flow.
    private static bool IsInHiddenSubtree(UiTree tree, NodeHandle handle)
    {
        for (NodeHandle current = handle; !current.IsNone; current = tree.Nodes.GetRef(current).Parent)
        {
            if (tree.Nodes.GetRef(current).Layout.Style.Hidden)
            {
                return true;
            }
        }

        return false;
    }

    private enum StaticEdge : byte
    {
        Start,
        Center,
        End,
    }

    private enum AbsoluteAlignment : byte
    {
        Normal,
        Stretch,
        Start,
        Center,
        End,
    }

    // A line of one axis: its start and end.
    private readonly record struct StaticArea(float Start, float End)
    {
        public StaticArea Minus(float offset) => new(Start - offset, End - offset);
    }

    // Where a box whose insets are both auto goes in one axis: aligned by an edge in an area, or at the start of the
    // inset-modified containing block when the alignment is `safe` and the box overflows it.
    private readonly record struct StaticPosition(StaticArea Area, StaticEdge Edge, bool Safe)
    {
        // `normal`, `stretch`, and the start alignments take the start.
        public static StaticPosition Of(AlignItems alignment, StaticArea area)
        {
            StaticEdge edge = alignment.Keyword switch
            {
                AlignItemsKeyword.Center => StaticEdge.Center,
                AlignItemsKeyword.End or AlignItemsKeyword.FlexEnd => StaticEdge.End,
                _ => StaticEdge.Start,
            };
            return new StaticPosition(area, edge, alignment.Safe);
        }

        public StaticPosition Minus(float offset) => this with { Area = Area.Minus(offset) };
    }

    // One axis of an absolutely positioned box (Taffy's OofAxis, left to right only): the containing block's size, the insets
    // and margins (NaN where auto), the static position, and the self-alignment. Coordinates are from the padding box's start.
    private readonly struct AbsoluteAxis(
        float containingSize,
        float insetStart,
        float insetEnd,
        float marginStart,
        float marginEnd,
        StaticPosition staticPosition,
        AbsoluteAlignment alignment,
        bool safe,
        bool isInline
    )
    {
        public AbsoluteAlignment Alignment => alignment;

        // An automatic size stretches where both insets are set and the alignment is `stretch` or `normal`.
        public bool IsStretchSized =>
            !float.IsNaN(insetStart) && !float.IsNaN(insetEnd) && alignment is AbsoluteAlignment.Stretch or AbsoluteAlignment.Normal;

        // The stretch-fit size of the border box: the inset-modified containing block less the margins that are not auto.
        public float StretchFitSize()
        {
            StaticArea imcb = _imcb;
            return Math.Max(imcb.End - imcb.Start - (Maybe.Or(marginStart, 0) + Maybe.Or(marginEnd, 0)), 0);
        }

        // The margins with the automatic ones resolved (CSS Position 3 §5.3): they share the free space only where both insets
        // are set; along the inline axis, negative free space goes to the end margin.
        public (float Start, float End) ResolveMargins(float size)
        {
            bool autoStart = float.IsNaN(marginStart);
            bool autoEnd = float.IsNaN(marginEnd);
            if (!autoStart && !autoEnd)
            {
                return (marginStart, marginEnd);
            }

            if (float.IsNaN(insetStart) || float.IsNaN(insetEnd))
            {
                return (Maybe.Or(marginStart, 0), Maybe.Or(marginEnd, 0));
            }

            StaticArea imcb = _imcb;
            float free = imcb.End - imcb.Start - size;
            return !autoStart ? (marginStart, free - marginStart)
                : !autoEnd ? (free - marginEnd, marginEnd)
                : free < 0 && isInline ? (0, free)
                : (free / 2, free / 2);
        }

        // The start of the border box (CSS Position 3 §5.4).
        public float Position(float size, float resolvedStart, float resolvedEnd)
        {
            StaticArea imcb = _imcb;
            float marginBox = size + resolvedStart + resolvedEnd;
            bool start = !float.IsNaN(insetStart);
            bool end = !float.IsNaN(insetEnd);
            if (!start && !end)
            {
                StaticPosition sp = staticPosition;
                bool overflows = sp.Safe && marginBox > imcb.End - imcb.Start;
                return (overflows ? AlignIn(imcb, marginBox, StaticEdge.Start) : AlignIn(sp.Area, marginBox, sp.Edge)) + resolvedStart;
            }

            if (!end)
            {
                return imcb.Start + resolvedStart;
            }

            if (!start)
            {
                return imcb.End - size - resolvedEnd;
            }

            // The automatic margins took the free space already.
            if (float.IsNaN(marginStart) || float.IsNaN(marginEnd))
            {
                return imcb.Start + resolvedStart;
            }

            float imcbSize = imcb.End - imcb.Start;
            StaticEdge edge = alignment switch
            {
                AbsoluteAlignment.Center => StaticEdge.Center,
                AbsoluteAlignment.End => StaticEdge.End,
                _ => StaticEdge.Start,
            };
            float boxStart;
            if (safe)
            {
                boxStart = AlignIn(imcb, marginBox, marginBox > imcbSize ? StaticEdge.Start : edge);
            }
            else if (marginBox <= imcbSize || alignment == AbsoluteAlignment.Normal)
            {
                boxStart = AlignIn(imcb, marginBox, edge);
            }
            else
            {
                // The default overflow alignment (CSS Box Alignment §4.4): inside the bounds of the inset-modified containing
                // block and the containing block, as far as the alignment goes. Our styles do not tell `unsafe` from the
                // default, so an `unsafe` alignment takes this too.
                var limit = new StaticArea(Math.Min(imcb.Start, 0), Math.Max(imcb.End, containingSize));
                boxStart =
                    marginBox <= limit.End - limit.Start
                        ? Math.Max(Math.Min(AlignIn(imcb, marginBox, edge), limit.End - marginBox), limit.Start)
                        : AlignIn(limit, marginBox, StaticEdge.Start);
            }

            return boxStart + resolvedStart;
        }

        // The inset-modified containing block, found once: every step reads it.
        private readonly StaticArea _imcb = InsetModifiedContainingBlock(containingSize, insetStart, insetEnd, staticPosition);

        // The inset-modified containing block (CSS Position 3 §4.1). Where both insets are auto, the static position's area and
        // alignment decide it. An overconstrained one gives up its weaker inset.
        private static StaticArea InsetModifiedContainingBlock(float containingSize, float insetStart, float insetEnd, StaticPosition staticPosition)
        {
            bool start = !float.IsNaN(insetStart);
            bool end = !float.IsNaN(insetEnd);
            StaticArea imcb;
            bool weakerIsStart = false;
            if (start)
            {
                imcb = new StaticArea(insetStart, end ? containingSize - insetEnd : containingSize);
            }
            else if (end)
            {
                imcb = new StaticArea(0, containingSize - insetEnd);
                weakerIsStart = true;
            }
            else
            {
                StaticPosition sp = staticPosition;
                float center = (sp.Area.Start + sp.Area.End) / 2;
                float endDistance = containingSize - center;
                imcb = sp.Edge switch
                {
                    StaticEdge.Start => new StaticArea(sp.Area.Start, containingSize),
                    StaticEdge.End => new StaticArea(0, sp.Area.End),
                    _ when Math.Abs(center) <= Math.Abs(endDistance) => new StaticArea(0, 2 * center),
                    _ => new StaticArea(containingSize - (2 * endDistance), containingSize),
                };
            }

            if (imcb.End < imcb.Start)
            {
                imcb = weakerIsStart ? imcb with { Start = imcb.End } : imcb with { End = imcb.Start };
            }

            return imcb;
        }
    }
}
