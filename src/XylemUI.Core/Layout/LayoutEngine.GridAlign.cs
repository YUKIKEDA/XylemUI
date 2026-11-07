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
        if (item.Bare && !tree.Nodes.GetRef(item.Node).Layout.HasInset)
        {
            PositionBareGridItem(tree, in item, left, right, top, bottom, area);
            return;
        }

        // What the item's style gives, read before its layout: no ref into the arena is held across a measurement (design §2.5),
        // and the style is not copied, as it was 6 % of the grid scene in each item's state (PR #650).
        ref readonly LayoutStyle s = ref StyleOf(tree, in item);
        Size paddingBorder = PaddingBorder(in s, area.Width).Sum();
        float ratio = s.HasAspectRatio ? s.AspectRatio : float.NaN;
        Length widthStyle = s.Width;
        Length heightStyle = s.Height;
        OptionalSize inherent = WithAspectRatio(new OptionalSize(widthStyle.Resolve(area.Width), heightStyle.Resolve(area.Height)), ratio);
        OptionalSize min = WithAspectRatio(
            new OptionalSize(
                Math.Max(Maybe.Or(s.MinWidth.Resolve(area.Width), paddingBorder.Width), paddingBorder.Width),
                Math.Max(Maybe.Or(s.MinHeight.Resolve(area.Height), paddingBorder.Height), paddingBorder.Height)
            ),
            ratio
        );
        OptionalSize max = WithAspectRatio(new OptionalSize(s.MaxWidth.Resolve(area.Width), s.MaxHeight.Resolve(area.Height)), ratio);
        (float dx, float dy) = RelativeOffset(in s, area.Width, area.Height);
        EdgeInsets<LengthPercentageAuto> margin = s.Margin;

        AlignItems horizontal = item.JustifySelf;
        if (horizontal.Keyword == AlignItemsKeyword.Normal)
        {
            horizontal = new AlignItems(
                !float.IsNaN(inherent.Width) || widthStyle.IsSizingKeyword ? AlignItemsKeyword.Start : AlignItemsKeyword.Stretch
            );
        }

        AlignItems vertical = item.AlignSelf;
        if (vertical.Keyword == AlignItemsKeyword.Normal)
        {
            vertical = new AlignItems(
                !float.IsNaN(inherent.Height) || heightStyle.IsSizingKeyword ? AlignItemsKeyword.Start : AlignItemsKeyword.Stretch
            );
        }

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
        bool keywordWidth = float.IsNaN(inherent.Width) && widthStyle.IsSizingKeyword;
        bool keywordHeight = float.IsNaN(inherent.Height) && heightStyle.IsSizingKeyword;
        AvailableSpace? measureWidth = keywordWidth ? MeasureSpaceOf(widthStyle, stretch.Width) : null;
        AvailableSpace? measureHeight = keywordHeight ? MeasureSpaceOf(heightStyle, stretch.Height) : null;
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
                    : StretchOf(widthStyle, stretch.Width);
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
                    : StretchOf(heightStyle, stretch.Height);
            }
            else if (!float.IsNaN(marginTop) && !float.IsNaN(marginBottom) && vertical == new AlignItems(AlignItemsKeyword.Stretch))
            {
                height = stretch.Height;
            }
        }

        size = WithAspectRatio(size with { Height = height }, ratio).Clamp(min, max);

        // Not stretched, and no height that gives it through the aspect ratio: the fit-content width in the area.
        if (float.IsNaN(size.Width) && !keywordWidth)
        {
            size = size with { Width = Maybe.Clamp(FitContentWidth(tree, item.Node, stretch.Width, parentSize, size.Height), min.Width, max.Width) };
        }

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
        tree.Nodes.GetRef(item.Node).Layout.RelativeY = dy;
        Place(tree, item.Node, x + dx, y + dy);
    }

    // PositionGridItem for a bare item without an inset: no size, limit, margin, padding, border, or relative shift of its own,
    // so `normal` stretches it in both axes and only its area and its alignment place it.
    private static void PositionBareGridItem(UiTree tree, in GridItemState item, float left, float right, float top, float bottom, Size area)
    {
        AlignItems horizontal = item.JustifySelf.Keyword == AlignItemsKeyword.Normal ? new AlignItems(AlignItemsKeyword.Stretch) : item.JustifySelf;
        AlignItems vertical = item.AlignSelf.Keyword == AlignItemsKeyword.Normal ? new AlignItems(AlignItemsKeyword.Stretch) : item.AlignSelf;
        var stretch = new Size(Math.Max(area.Width, 0), Math.Max(area.Height - item.BaselineShim, 0));
        bool stretchWidth = horizontal == new AlignItems(AlignItemsKeyword.Stretch);
        bool stretchHeight = vertical == new AlignItems(AlignItemsKeyword.Stretch);
        float knownHeight = stretchHeight ? stretch.Height : float.NaN;
        float knownWidth = stretchWidth
            ? stretch.Width
            : FitContentWidth(tree, item.Node, stretch.Width, new OptionalSize(area.Width, area.Height), knownHeight);
        var input = new LayoutInput(
            new OptionalSize(knownWidth, knownHeight),
            true,
            true,
            new OptionalSize(area.Width, area.Height),
            new AvailableSize(AvailableSpace.Definite(stretch.Width), AvailableSpace.Definite(stretch.Height)),
            SizingMode.InherentSize,
            RequestedAxis.Both
        );
        Size laidOut = LayoutChild(tree, item.Node, in input, commit: true);
        float width = Math.Max(knownWidth, 0);
        float height = Math.Max(stretchHeight ? stretch.Height : laidOut.Height, 0);
        float x = AlignInArea(left, right, horizontal, width, 0, 0, 0);
        float y = AlignInArea(top, bottom, vertical, height, 0, 0, item.BaselineShim);
        tree.Nodes.GetRef(item.Node).Layout.RelativeY = 0;
        Place(tree, item.Node, x, y);
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
    // one is laid out with its grid area as its containing block (Taffy's resolve_absolute_grid_area): the area between the
    // lines its placement names, and the padding box's edge for an end without a line or with a line outside the grid. Its
    // static position is aligned by its justify-self and align-self in the area its lines give (Taffy's
    // resolve_static_position_grid_area). A placement's names resolve as an item's do.
    private static void LayOutGridOutOfFlowChildren(
        UiTree tree,
        NodeHandle firstChild,
        Size containerSize,
        in GridConstants c,
        in GridAxisLines columnLines,
        in GridAxisLines rowLines
    )
    {
        // The padding box, which an end without a line takes.
        var paddingX = new StaticArea(c.Border.Left, containerSize.Width - c.Border.Right - c.Gutter.Width);
        var paddingY = new StaticArea(c.Border.Top, containerSize.Height - c.Border.Bottom - c.Gutter.Height);
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
                (int columnStart, int columnEnd) = AbsoluteLines(
                    OriginZeroPlacement.Of(s.GridColumn, columnLines.Counts.Explicit, columnLines.Names)
                );
                (int rowStart, int rowEnd) = AbsoluteLines(OriginZeroPlacement.Of(s.GridRow, rowLines.Counts.Explicit, rowLines.Names));
                StaticArea areaX = columnLines.ContainingBlock(columnStart, columnEnd, paddingX);
                StaticArea areaY = rowLines.ContainingBlock(rowStart, rowEnd, paddingY);

                // The containing block, given as the border that leaves it inside the container.
                var area = new Edges(areaX.Start, areaY.Start, containerSize.Width - areaX.End, containerSize.Height - areaY.End);
                LayOutAbsoluteChild(
                    tree,
                    child,
                    in s,
                    containerSize,
                    area,
                    StaticPosition.Of(s.JustifySelf ?? c.JustifyItems, columnLines.StaticArea(columnStart, columnEnd, paddingX)),
                    StaticPosition.Of(s.AlignSelf ?? c.AlignItems, rowLines.StaticArea(rowStart, rowEnd, paddingY))
                );
            }

            child = next;
        }
    }

    // The lines of an absolutely positioned child's placement, or NoLine for an end without one (Taffy's
    // resolve_absolutely_positioned_grid_tracks): two lines in order, a line and a span from it, or a line alone.
    private static (int Start, int End) AbsoluteLines(OriginZeroPlacement placement) =>
        (placement.StartKind, placement.EndKind) switch
        {
            (GridLineKind.Line, GridLineKind.Line) => placement.Start == placement.End
                ? (placement.Start, placement.Start + 1)
                : (Math.Min(placement.Start, placement.End), Math.Max(placement.Start, placement.End)),
            (GridLineKind.Line, GridLineKind.Span) => (placement.Start, placement.Start + placement.End),
            (GridLineKind.Line, _) => (placement.Start, GridAxisLines.NoLine),
            (GridLineKind.Span, GridLineKind.Line) => (placement.End - placement.Start, placement.End),
            (_, GridLineKind.Line) => (GridAxisLines.NoLine, placement.End),
            _ => (GridAxisLines.NoLine, GridAxisLines.NoLine),
        };

    // The laid-out tracks of an axis and its named lines, for the absolutely positioned children.
    private readonly ref struct GridAxisLines(Span<GridTrackState> tracks, TrackCounts counts, ReadOnlySpan<long> names)
    {
        // An end without a line.
        public const int NoLine = int.MinValue;

        public TrackCounts Counts { get; } = counts;

        public ReadOnlySpan<long> Names { get; } = names;

        private Span<GridTrackState> Tracks { get; } = tracks;

        // The containing block in the axis (Taffy's resolve_absolute_grid_axis): a start line is the start of the track after
        // it, and an end line the end of the track before it. A line in a run of collapsed tracks takes the first track after
        // the run as a start and the last before it as an end, as the gutters of the run overlap. A line outside the grid is no
        // line. The area is never negative: it starts at its start.
        public StaticArea ContainingBlock(int startLine, int endLine, StaticArea padding)
        {
            int trackCount = Counts.Length;
            int start = TrackOf(startLine);
            int end = TrackOf(endLine);
            float startPosition =
                start == NoLine ? padding.Start
                : start < trackCount ? TrackEdge(start, isStart: true)
                : trackCount > 0 ? TrackEnd(trackCount - 1)
                : Tracks[0].Offset;
            float endPosition =
                end == NoLine ? padding.End
                : end >= 1 && end - 1 < trackCount ? TrackEdge(end - 1, isStart: false)
                : trackCount > 0 ? TrackStart(0)
                : Tracks[0].Offset;
            return new StaticArea(startPosition, Math.Max(startPosition, endPosition));
        }

        // The area of the static position (Taffy's resolve_static_position_grid_area): a start line is the offset of the track
        // after it, and an end line the offset of its gutter, the end of the track before it.
        public StaticArea StaticArea(int startLine, int endLine, StaticArea padding)
        {
            int start = TrackOf(startLine);
            int end = TrackOf(endLine);
            float startPosition = start == NoLine ? padding.Start : Tracks[Math.Min((2 * start) + 1, Tracks.Length - 1)].Offset;
            float endPosition =
                end == NoLine ? padding.End
                : end == 0 ? Tracks[Math.Min(1, Tracks.Length - 1)].Offset
                : Tracks[2 * end].Offset;
            return new StaticArea(startPosition, endPosition);
        }

        // A line's index among the tracks' lines, from 0 at the grid's start, or NoLine outside the grid.
        private int TrackOf(int line) =>
            line == NoLine || line < Counts.ImplicitStartLine || line > Counts.ImplicitEndLine ? NoLine : Counts.LineToNextTrack(line);

        private float TrackStart(int track) => Tracks[(2 * track) + 1].Offset;

        private float TrackEnd(int track) => Tracks[(2 * track) + 1].Offset + Tracks[(2 * track) + 1].BaseSize;

        private float TrackEdge(int track, bool isStart)
        {
            if (Tracks[(2 * track) + 1].IsCollapsed)
            {
                int first = track;
                while (first > 0 && Tracks[(2 * first) - 1].IsCollapsed)
                {
                    first--;
                }

                int last = track;
                while (last + 1 < Counts.Length && Tracks[(2 * last) + 3].IsCollapsed)
                {
                    last++;
                }

                if (isStart && last + 1 < Counts.Length)
                {
                    track = last + 1;
                }
                else if (!isStart && first > 0)
                {
                    track = first - 1;
                }
            }

            return isStart ? TrackStart(track) : TrackEnd(track);
        }
    }
}
