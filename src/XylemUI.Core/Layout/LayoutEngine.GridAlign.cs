namespace XylemUI;

internal static partial class LayoutEngine
{
    // The item's style, read from the tree where it is used: no ref into the arena is held across a measurement (design §2.5).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static ref readonly LayoutStyle StyleOf(UiTree tree, in GridItemState item) => ref tree.Nodes.GetRef(item.Node).Layout.Style;

    // 8. The tracks' offsets in an axis by its content alignment (Taffy's align_tracks, alignment.rs): the free space goes before
    // and between the tracks that are not collapsed; the gaps are tracks already. Left to right only (row P03 adds the other).
    private static void AlignTracks(float contentSize, float origin, Span<GridTrackState> tracks, AlignContent alignment)
    {
        float free = contentSize - SumBaseSizes(tracks);
        int count = 0;
        for (int i = 1; i < tracks.Length; i += 2)
        {
            count += tracks[i].IsCollapsed ? 0 : 1;
        }

        AlignContentKeyword mode = AlignmentFallback(free, count, alignment);

        // With every track collapsed, the lines still align in the container.
        float total = origin + (count == 0 ? AlignmentOffset(free, 0, 0, mode, reversed: false, isFirst: true) : 0);
        bool seen = false;
        for (int i = 0; i < tracks.Length; i++)
        {
            ref GridTrackState track = ref tracks[i];
            bool isTrack = i % 2 == 1 && !track.IsCollapsed;
            float offset = isTrack ? AlignmentOffset(free, count, 0, mode, reversed: false, isFirst: !seen) : 0;
            track.Offset = total + offset;
            total += offset + track.BaseSize;
            seen |= isTrack;
        }
    }

    // 9. An item's size in its grid area, its alignment there, and its layout (Taffy's align_and_position_item for an item in the
    // flow): percentages and margins resolve against the area (both margins against its width, as CSS Grid asks). `normal`
    // stretches an item with no size of its own in the axis, and starts it otherwise. An aspect ratio does not start it
    // vertically, as Taffy 0.15.1 does: Chrome 157 stretches it (Fixtures/XylemUI, grid_aspect_ratio_item_normal_alignment_starts,
    // whose name says what Taffy does).
    private static void PositionGridItem(
        UiTree tree,
        ref GridItemState item,
        Span<GridTrackState> columns,
        Span<GridTrackState> rows,
        in GridConstants c
    )
    {
        float left = columns[item.ColumnIndexStart + 1].Offset;
        float right = columns[item.ColumnIndexEnd].Offset;
        float top = rows[item.RowIndexStart + 1].Offset;
        float bottom = rows[item.RowIndexEnd].Offset;
        var area = new Size(right - left, bottom - top);

        LayoutStyle s = StyleOf(tree, in item);
        Size paddingBorder = PaddingBorder(in s, area.Width).Sum();
        float ratio = s.HasAspectRatio ? s.AspectRatio : float.NaN;
        OptionalSize inherent = WithAspectRatio(new OptionalSize(s.Width.Resolve(area.Width), s.Height.Resolve(area.Height)), ratio);
        OptionalSize min = WithAspectRatio(
            new OptionalSize(
                Math.Max(Maybe.Or(s.MinWidth.Resolve(area.Width), paddingBorder.Width), paddingBorder.Width),
                Math.Max(Maybe.Or(s.MinHeight.Resolve(area.Height), paddingBorder.Height), paddingBorder.Height)
            ),
            ratio
        );
        OptionalSize max = WithAspectRatio(new OptionalSize(s.MaxWidth.Resolve(area.Width), s.MaxHeight.Resolve(area.Height)), ratio);

        AlignItems horizontal = item.JustifySelf;
        if (horizontal.Keyword == AlignItemsKeyword.Normal)
        {
            horizontal = new AlignItems(
                !float.IsNaN(inherent.Width) || s.Width.IsSizingKeyword ? AlignItemsKeyword.Start : AlignItemsKeyword.Stretch
            );
        }

        AlignItems vertical = item.AlignSelf;
        if (vertical.Keyword == AlignItemsKeyword.Normal)
        {
            vertical = new AlignItems(
                !float.IsNaN(inherent.Height) || s.Height.IsSizingKeyword ? AlignItemsKeyword.Start : AlignItemsKeyword.Stretch
            );
        }

        EdgeInsets<LengthPercentageAuto> margin = s.Margin;
        float marginLeft = margin.Left.Resolve(area.Width);
        float marginRight = margin.Right.Resolve(area.Width);
        float marginTop = margin.Top.Resolve(area.Width);
        float marginBottom = margin.Bottom.Resolve(area.Width);
        var stretch = new Size(
            Math.Max(area.Width - Maybe.Or(marginLeft, 0) - Maybe.Or(marginRight, 0), 0),
            Math.Max(area.Height - Maybe.Or(marginTop, 0) - Maybe.Or(marginBottom, 0) - item.BaselineShim, 0)
        );

        // A size that is a sizing keyword resolves to a size, or measures the item under the keyword's space; one measurement when
        // both axes measure.
        bool keywordWidth = float.IsNaN(inherent.Width) && s.Width.IsSizingKeyword;
        bool keywordHeight = float.IsNaN(inherent.Height) && s.Height.IsSizingKeyword;
        AvailableSpace? measureWidth = keywordWidth ? MeasureSpaceOf(s.Width, stretch.Width) : null;
        AvailableSpace? measureHeight = keywordHeight ? MeasureSpaceOf(s.Height, stretch.Height) : null;
        Size measuredBoth = new(float.NaN, float.NaN);
        var parentSize = new OptionalSize(area.Width, area.Height);
        if (measureWidth is { } bothWidth && measureHeight is { } bothHeight)
        {
            var bothInput = new LayoutInput(
                OptionalSize.None,
                true,
                true,
                parentSize,
                new AvailableSize(bothWidth, bothHeight),
                SizingMode.InherentSize,
                RequestedAxis.Both
            );
            measuredBoth = LayoutChild(tree, item.Node, in bothInput, commit: false);
        }

        float width = inherent.Width;
        if (float.IsNaN(width))
        {
            if (keywordWidth)
            {
                width = measureWidth is { } space
                    ? Maybe.Or(
                        measuredBoth.Width,
                        MeasureChild(
                            tree,
                            item.Node,
                            OptionalSize.None,
                            parentSize,
                            new AvailableSize(space, AvailableSpace.Definite(stretch.Height)),
                            SizingMode.InherentSize,
                            horizontal: true
                        )
                    )
                    : StretchOf(s.Width, stretch.Width);
            }
            else if (!float.IsNaN(marginLeft) && !float.IsNaN(marginRight) && horizontal == new AlignItems(AlignItemsKeyword.Stretch))
            {
                width = stretch.Width;
            }
        }

        OptionalSize size = WithAspectRatio(new OptionalSize(width, inherent.Height), ratio);
        float height = size.Height;
        if (float.IsNaN(height))
        {
            if (keywordHeight)
            {
                height = measureHeight is { } space
                    ? Maybe.Or(
                        measuredBoth.Height,
                        MeasureChild(
                            tree,
                            item.Node,
                            size with
                            {
                                Height = float.NaN,
                            },
                            parentSize,
                            new AvailableSize(AvailableSpace.Definite(Maybe.Or(size.Width, stretch.Width)), space),
                            SizingMode.InherentSize,
                            horizontal: false
                        )
                    )
                    : StretchOf(s.Height, stretch.Height);
            }
            else if (!float.IsNaN(marginTop) && !float.IsNaN(marginBottom) && vertical == new AlignItems(AlignItemsKeyword.Stretch))
            {
                height = stretch.Height;
            }
        }

        size = WithAspectRatio(size with { Height = height }, ratio).Clamp(min, max);
        var input = new LayoutInput(
            size,
            true,
            true,
            parentSize,
            new AvailableSize(AvailableSpace.Definite(stretch.Width), AvailableSpace.Definite(stretch.Height)),
            SizingMode.InherentSize,
            RequestedAxis.Both
        );
        Size laidOut = LayoutChild(tree, item.Node, in input, commit: true);
        var final = new OptionalSize(Maybe.Or(size.Width, laidOut.Width), Maybe.Or(size.Height, laidOut.Height)).Clamp(min, max);

        float x = AlignInArea(left, right, horizontal, final.Width, marginLeft, marginRight, 0);
        float y = AlignInArea(top, bottom, vertical, final.Height, marginTop, marginBottom, item.BaselineShim);

        // A relative shift moves the item, not the grid's baseline.
        (float dx, float dy) = RelativeOffset(in s, area.Width, area.Height);
        tree.Nodes.GetRef(item.Node).Layout.RelativeY = dy;
        Place(tree, item.Node, x + dx, y + dy);
    }

    // An item's start in one axis of its grid area (Taffy's align_item_within_area, left to right): auto margins share the free
    // space; a safe alignment that overflows starts; baseline and stretch start.
    private static float AlignInArea(float start, float end, AlignItems alignment, float size, float marginStart, float marginEnd, float shim)
    {
        float fixedStart = Maybe.Or(marginStart, 0) + shim;
        float fixedEnd = Maybe.Or(marginEnd, 0);
        float areaSize = Math.Max(end - start, 0);
        float free = Math.Max(areaSize - size - fixedStart - fixedEnd, 0);
        int autoCount = (float.IsNaN(marginStart) ? 1 : 0) + (float.IsNaN(marginEnd) ? 1 : 0);
        float autoSize = autoCount > 0 ? free / autoCount : 0;
        float resolvedStart = Maybe.Or(marginStart, autoSize) + shim;
        float resolvedEnd = Maybe.Or(marginEnd, autoSize);
        bool overflows = size + fixedStart + fixedEnd > areaSize;
        AlignItemsKeyword keyword = alignment.Safe && overflows ? AlignItemsKeyword.Start : alignment.Keyword;
        float offset = keyword switch
        {
            AlignItemsKeyword.End or AlignItemsKeyword.FlexEnd => areaSize - size - resolvedEnd,
            AlignItemsKeyword.Center => (areaSize - size + resolvedStart - resolvedEnd) / 2,
            _ => resolvedStart,
        };
        return start + offset;
    }

    // The children out of the grid's flow, after its items: a hidden child gets an empty rectangle, and an absolutely positioned
    // one is laid out against the container's padding box with the content box as its static position, aligned by its
    // justify-self and align-self (Taffy's static position when the grid is not the containing block). The grid's lines as the
    // static position come with P02f's next implementation PR.
    private static void LayOutGridOutOfFlowChildren(
        UiTree tree,
        NodeHandle firstChild,
        in LayoutStyle style,
        Size containerSize,
        in GridConstants c,
        float parentWidth
    )
    {
        Edges border = WithGutter(EdgeInsets.ResolveOrZero(style.Border, parentWidth), in style);
        var inlineArea = new StaticArea(c.Border.Left + c.Padding.Left, containerSize.Width - c.Border.Right - c.Padding.Right - c.Gutter.Width);
        var blockArea = new StaticArea(c.Border.Top + c.Padding.Top, containerSize.Height - c.Border.Bottom - c.Padding.Bottom - c.Gutter.Height);
        for (NodeHandle child = firstChild; !child.IsNone; )
        {
            ref UiNode node = ref tree.Nodes.GetRef(child);
            NodeHandle next = node.NextSibling;
            ref readonly LayoutStyle s = ref node.Layout.Style;
            if (s.Hidden)
            {
                HideSubtree(tree, child);
            }
            else if (s.Position == Position.Absolute)
            {
                LayOutAbsoluteChild(
                    tree,
                    child,
                    in s,
                    containerSize,
                    border,
                    StaticPosition.Of(s.JustifySelf ?? c.JustifyItems, inlineArea),
                    StaticPosition.Of(s.AlignSelf ?? c.AlignItems, blockArea)
                );
            }

            child = next;
        }
    }
}
