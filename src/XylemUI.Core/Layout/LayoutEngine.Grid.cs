using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace XylemUI;

internal static partial class LayoutEngine
{
    [ThreadStatic]
    private static ScratchStack<GridItemState>? _gridItemScratch;

    [ThreadStatic]
    private static ScratchStack<GridTrackState>? _gridTrackScratch;

    [ThreadStatic]
    private static ScratchStack<int>? _gridOrderScratch;

    [ThreadStatic]
    private static GridOccupancy? _gridOccupancy;

    // CSS Grid's layout of a grid container (design §3.3, "The shape of P02f"), ported from Taffy 0.15.1's compute_grid_layout
    // (src/compute/grid/mod.rs): the explicit grid, the placement of the items with the implicit grid, the sizes of the columns
    // and then of the rows, a second run of either when the first changed what the other read, the container's size, the
    // alignment of the tracks, and the final layout of each item in its grid area. The tracks of an axis are a list of the lines
    // (gutters, which take the gap) and the tracks in turn, as Taffy keeps them.
    private static Size ComputeGrid(
        UiTree tree,
        in LayoutStyle style,
        GridProps props,
        NodeHandle firstChild,
        in LayoutInput input,
        bool commit,
        out NodeHandle baselineItem
    )
    {
        baselineItem = default;
        GridConstants c = GridConstantsOf(in style, props, in input);
        if (!commit)
        {
            if (c.OuterNodeSize.IsComplete)
            {
                return ToSize(c.OuterNodeSize);
            }

            if (input.Axis == RequestedAxis.Horizontal && !float.IsNaN(c.OuterNodeSize.Width))
            {
                return new Size(c.OuterNodeSize.Width, 0);
            }
        }

        // 2. The explicit grid. Clamped to the largest number of tracks (CSS Grid §8.4, "overlarge grids").
        int explicitColumns = Math.Min(TemplateTrackCount(props.Columns), Track.MaxTracks);
        int explicitRows = Math.Min(TemplateTrackCount(props.Rows), Track.MaxTracks);

        int count = 0;
        for (NodeHandle child = firstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            count += tree.Nodes.GetRef(child).Layout.Style.IsInFlow ? 1 : 0;
        }

        ScratchStack<GridItemState> itemScratch = _gridItemScratch ??= new ScratchStack<GridItemState>();
        ScratchStack<GridTrackState> trackScratch = _gridTrackScratch ??= new ScratchStack<GridTrackState>();
        int itemTop = itemScratch.Top;
        int trackTop = trackScratch.Top;
        try
        {
            // 3. The grid items, in document order, with their placements in lines from the explicit grid's start.
            Span<GridItemState> items = itemScratch.Push(count);
            GenerateGridItems(tree, firstChild, props, explicitColumns, explicitRows, items);

            // 4 and 5. The placement of the items, which grows the implicit grid.
            GridOccupancy occupancy = _gridOccupancy ??= new GridOccupancy();
            (TrackCounts estimatedColumns, TrackCounts estimatedRows) = EstimateGridSize(explicitColumns, explicitRows, items);
            occupancy.Reset(estimatedColumns, estimatedRows);
            PlaceGridItems(occupancy, items, props.AutoFlow == GridAutoFlow.Column);
            TrackCounts columnCounts = occupancy.Columns;
            TrackCounts rowCounts = occupancy.Rows;

            // The tracks of both axes, with their sizing functions.
            Span<GridTrackState> columns = trackScratch.Push((2 * columnCounts.Length) + 1);
            Span<GridTrackState> rows = trackScratch.Push((2 * rowCounts.Length) + 1);
            InitializeGridTracks(columns, columnCounts, props.Columns, props.AutoColumns, props.ColumnGap);
            InitializeGridTracks(rows, rowCounts, props.Rows, props.AutoRows, props.RowGap);

            // 6. The sizes of the tracks, the columns first.
            foreach (ref GridItemState item in items)
            {
                item.ColumnIndexStart = columnCounts.LineToTrackListIndex(item.ColumnStart);
                item.ColumnIndexEnd = columnCounts.LineToTrackListIndex(item.ColumnEnd);
                item.RowIndexStart = rowCounts.LineToTrackListIndex(item.RowStart);
                item.RowIndexEnd = rowCounts.LineToTrackListIndex(item.RowEnd);
                item.CrossesFlexibleColumn = AnySpanned(columns, item.ColumnIndexStart, item.ColumnIndexEnd, flexible: true);
                item.CrossesIntrinsicColumn = AnySpanned(columns, item.ColumnIndexStart, item.ColumnIndexEnd, flexible: false);
                item.CrossesFlexibleRow = AnySpanned(rows, item.RowIndexStart, item.RowIndexEnd, flexible: true);
                item.CrossesIntrinsicRow = AnySpanned(rows, item.RowIndexStart, item.RowIndexEnd, flexible: false);
            }

            bool anyBaseline = false;
            foreach (ref GridItemState item in items)
            {
                anyBaseline |= item.ParticipatesInBaselineAlignment;
            }

            OptionalSize innerNodeSize = c.InnerNodeSize;
            TrackSizing(tree, inline: true, in c, innerNodeSize, columns, rows, items, estimateFromMax: true, anyBaseline);
            float initialColumnSum = SumBaseSizes(columns);
            innerNodeSize = innerNodeSize with { Width = Maybe.Or(innerNodeSize.Width, initialColumnSum) };

            // Only the width was asked for: the rows need no sizing, which would measure the height of every item.
            if (!commit && input.Axis == RequestedAxis.Horizontal)
            {
                float width = Math.Max(Maybe.Clamp(initialColumnSum + c.ContentInset.Horizontal, c.Min.Width, c.Max.Width), c.PaddingBorder.Width);
                return new Size(width, 0);
            }

            foreach (ref GridItemState item in items)
            {
                item.HasGridAreaSize = false;
            }

            TrackSizing(tree, inline: false, in c, innerNodeSize, rows, columns, items, estimateFromMax: false, anyBaseline: false);
            float initialRowSum = SumBaseSizes(rows);

            // 6. The container's size.
            OptionalSize styleSize = input.KnownDimensions.Or(c.PreferredSize);
            Size borderBox = new(
                Math.Max(
                    Maybe.Clamp(Maybe.Or(styleSize.Width, initialColumnSum + c.ContentInset.Horizontal), c.Min.Width, c.Max.Width),
                    c.PaddingBorder.Width
                ),
                Math.Max(
                    Maybe.Clamp(Maybe.Or(styleSize.Height, initialRowSum + c.ContentInset.Vertical), c.Min.Height, c.Max.Height),
                    c.PaddingBorder.Height
                )
            );
            Size contentBox = new(Math.Max(0, borderBox.Width - c.ContentInset.Horizontal), Math.Max(0, borderBox.Height - c.ContentInset.Vertical));
            if (!commit)
            {
                return borderBox;
            }

            // Percentages now resolve against the content box when the tracks are sized again.
            innerNodeSize = new OptionalSize(contentBox.Width, contentBox.Height);
            (borderBox, contentBox) = RerunTrackSizing(
                tree,
                in c,
                styleSize,
                innerNodeSize,
                columns,
                rows,
                items,
                anyBaseline,
                borderBox,
                contentBox
            );

            // 8. The alignment of the tracks.
            AlignTracks(contentBox.Width, c.Padding.Left + c.Border.Left, columns, c.JustifyContent);
            AlignTracks(contentBox.Height, c.Padding.Top + c.Border.Top, rows, c.AlignContent);

            // 9. The size, alignment, and place of each item in its grid area.
            foreach (ref GridItemState item in items)
            {
                PositionGridItem(tree, ref item, columns, rows, in c);
            }

            if (tree.Nodes.GetRef(tree.Nodes.GetRef(firstChild).Parent).Layout.OutOfFlowChildren > 0)
            {
                LayOutGridOutOfFlowChildren(tree, firstChild, in style, borderBox, in c, input.ParentSize.Width);
            }

            baselineItem = GridBaselineItem(items);
            return borderBox;
        }
        finally
        {
            itemScratch.PopTo(itemTop);
            trackScratch.PopTo(trackTop);
        }
    }

    // Step 1 of Taffy's compute_grid_layout: the container's box, its limits, and the space its tracks are sized in.
    private static GridConstants GridConstantsOf(in LayoutStyle style, GridProps props, in LayoutInput input)
    {
        OptionalSize parent = input.ParentSize;
        Edges padding = EdgeInsets.ResolveOrZero(style.Padding, parent.Width);
        Edges border = EdgeInsets.ResolveOrZero(style.Border, parent.Width);
        Size paddingBorder = (padding + border).Sum();
        OptionalSize min = MinSize(in style, parent);
        OptionalSize max = MaxSize(in style, parent);
        OptionalSize preferred = input.SizingMode == SizingMode.InherentSize ? StyleSize(in style, parent) : OptionalSize.None;

        // The scrollbars take room at the end of a row and at the bottom, inside the padding box.
        Size gutter = style.ScrollbarGutter;
        Edges contentInset = WithGutter(padding + border, in style);

        OptionalSize sized = input.KnownDimensions.Or(preferred);
        AvailableSize available = new(
            ConstrainedSpace(sized.Width, input.Available.Width, min.Width, max.Width, paddingBorder.Width),
            ConstrainedSpace(sized.Height, input.Available.Height, min.Height, max.Height, paddingBorder.Height)
        );
        OptionalSize outer = new(
            Maybe.Max(Maybe.Clamp(sized.Width, min.Width, max.Width), paddingBorder.Width),
            Maybe.Max(Maybe.Clamp(sized.Height, min.Height, max.Height), paddingBorder.Height)
        );
        Size insetSum = contentInset.Sum();
        return new GridConstants
        {
            Padding = padding,
            Border = border,
            PaddingBorder = paddingBorder,
            Min = min,
            Max = max,
            PreferredSize = preferred,
            Gutter = gutter,
            ContentInset = contentInset,

            // `normal` behaves as `stretch` for the tracks of a grid (Taffy's grid_align_content).
            AlignContent = StretchIfNormal(props.AlignContent),
            JustifyContent = StretchIfNormal(props.JustifyContent),
            AlignItems = props.AlignItems,
            JustifyItems = props.JustifyItems,
            AvailableGridSpace = new AvailableSize(available.Width.Minus(insetSum.Width), available.Height.Minus(insetSum.Height)),
            OuterNodeSize = outer,
            InnerMin = new OptionalSize(min.Width - insetSum.Width, min.Height - insetSum.Height),
            InnerMax = new OptionalSize(max.Width - insetSum.Width, max.Height - insetSum.Height),
            InnerNodeSize = new OptionalSize(outer.Width - insetSum.Width, outer.Height - insetSum.Height),
        };
    }

    // The space of an axis: the node's size where it is known, else what the parent gives, clamped as a definite space is.
    private static AvailableSpace ConstrainedSpace(float sized, AvailableSpace available, float min, float max, float floor)
    {
        if (!float.IsNaN(sized))
        {
            return AvailableSpace.Definite(Maybe.Max(Maybe.Clamp(sized, min, max), floor));
        }

        return available.Kind == AvailableSpaceKind.Definite
            ? AvailableSpace.Definite(Maybe.Max(Maybe.Clamp(available.Value, min, max), floor))
            : available;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static AlignContent StretchIfNormal(AlignContent alignment) =>
        alignment.Keyword == AlignContentKeyword.Normal ? new AlignContent(AlignContentKeyword.Stretch) : alignment;

    // The number of tracks a template gives (Taffy's compute_explicit_grid_size_in_axis, without an automatic repetition).
    private static int TemplateTrackCount(ImmutableArray<GridTrack> template)
    {
        if (template.IsDefaultOrEmpty)
        {
            return 0;
        }

        long total = 0;
        foreach (GridTrack entry in template)
        {
            total += entry.IsRepeat ? (long)entry.Count * entry.Repeated.Length : 1;
        }

        return (int)Math.Min(total, Track.MaxTracks);
    }

    // The items: the children in the flow, in document order, with their styles and their placements in lines from the
    // explicit grid's start (Taffy's into_origin_zero).
    private static void GenerateGridItems(
        UiTree tree,
        NodeHandle firstChild,
        GridProps props,
        int explicitColumns,
        int explicitRows,
        Span<GridItemState> items
    )
    {
        int i = 0;
        int order = 0;
        for (NodeHandle child = firstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling, order++)
        {
            ref readonly LayoutStyle s = ref tree.Nodes.GetRef(child).Layout.Style;
            if (!s.IsInFlow)
            {
                continue;
            }

            ref GridItemState item = ref items[i++];
            item = default;
            item.Node = child;
            item.SourceOrder = order;
            item.AutoBlockMargin = s.Margin.Top.IsAuto || s.Margin.Bottom.IsAuto;
            item.PercentHeight = s.Height.Kind == LengthKind.Percent;
            item.OverflowX = s.OverflowX;
            item.OverflowY = s.OverflowY;
            item.ColumnPlacement = OriginZeroPlacement.Of(s.GridColumn, explicitColumns);
            item.RowPlacement = OriginZeroPlacement.Of(s.GridRow, explicitRows);
            item.AlignSelf = s.AlignSelf ?? props.AlignItems;
            item.JustifySelf = s.JustifySelf ?? props.JustifyItems;
            item.Baseline = float.NaN;
            item.MinContentCache = OptionalSize.None;
            item.MaxContentCache = OptionalSize.None;
            item.MinimumCache = OptionalSize.None;
        }
    }

    // An estimate of the numbers of tracks, which sizes the occupancy matrix (Taffy's compute_grid_size_estimate): exact for the
    // explicit tracks and the implicit ones before them, a lower bound after them, which the placement grows.
    private static (TrackCounts Columns, TrackCounts Rows) EstimateGridSize(int explicitColumns, int explicitRows, Span<GridItemState> items)
    {
        int columnMin = 0;
        int columnMax = 0;
        int columnSpan = 0;
        int rowMin = 0;
        int rowMax = 0;
        int rowSpan = 0;
        foreach (ref GridItemState item in items)
        {
            item.ColumnPlacement.Estimate(out int min, out int max, out int span);
            columnMin = Math.Min(columnMin, min);
            columnMax = Math.Max(columnMax, max);
            columnSpan = Math.Max(columnSpan, span);
            item.RowPlacement.Estimate(out min, out max, out span);
            rowMin = Math.Min(rowMin, min);
            rowMax = Math.Max(rowMax, max);
            rowSpan = Math.Max(rowSpan, span);
        }

        return (Estimate(explicitColumns, columnMin, columnMax, columnSpan), Estimate(explicitRows, rowMin, rowMax, rowSpan));

        static TrackCounts Estimate(int explicitCount, int minLine, int maxLine, int span)
        {
            int negative = minLine < 0 ? -minLine : 0;
            int positive = maxLine > explicitCount ? maxLine - explicitCount : 0;
            if (negative + explicitCount + positive < span)
            {
                positive = span - explicitCount - negative;
            }

            return new TrackCounts(negative, explicitCount, positive);
        }
    }

    // CSS Grid §8.5, the grid item placement algorithm (Taffy's place_grid_items, placement.rs): the items placed at definite lines
    // in both axes, then those placed at definite lines in the secondary axis, then the rest in turn from a cursor that moves on.
    // The primary axis is the one the auto-flow fills along: the columns for `row`.
    private static void PlaceGridItems(GridOccupancy occupancy, Span<GridItemState> items, bool columnFlow)
    {
        bool primaryIsColumns = !columnFlow;

        // 1. The items with definite positions in both axes.
        foreach (ref GridItemState item in items)
        {
            if (item.ColumnPlacement.IsDefinite && item.RowPlacement.IsDefinite)
            {
                Primary(ref item, primaryIsColumns).ResolveDefinite(out int primaryStart, out int primaryEnd);
                Secondary(ref item, primaryIsColumns).ResolveDefinite(out int secondaryStart, out int secondaryEnd);
                RecordPlacement(
                    occupancy,
                    ref item,
                    primaryIsColumns,
                    primaryStart,
                    primaryEnd,
                    secondaryStart,
                    secondaryEnd,
                    GridOccupancy.CellState.DefinitelyPlaced
                );
            }
        }

        // 2. The items with a definite position in the secondary axis only.
        foreach (ref GridItemState item in items)
        {
            if (Secondary(ref item, primaryIsColumns).IsDefinite && !Primary(ref item, primaryIsColumns).IsDefinite)
            {
                Secondary(ref item, primaryIsColumns).ResolveDefinite(out int secondaryStart, out int secondaryEnd);
                int position = occupancy.AutoPlacementCursor(primaryIsColumns, secondaryStart, out int cursor)
                    ? cursor
                    : TrackCountsOf(occupancy, primaryIsColumns).ImplicitStartLine;
                int span = Primary(ref item, primaryIsColumns).IndefiniteSpan;
                while (true)
                {
                    (int start, int end) = IndefiniteSpan(position, span);
                    if (!occupancy.Collides(primaryIsColumns, start, end, secondaryStart, secondaryEnd, out int next))
                    {
                        RecordPlacement(
                            occupancy,
                            ref item,
                            primaryIsColumns,
                            start,
                            end,
                            secondaryStart,
                            secondaryEnd,
                            GridOccupancy.CellState.AutoPlaced
                        );
                        break;
                    }

                    position = next;
                }
            }
        }

        // 3. The implicit grid's columns are already counted: the estimate sized the matrix and each placement grew it.
        // 4. The remaining items, in turn from a cursor.
        int primaryCursor = TrackCountsOf(occupancy, primaryIsColumns).ImplicitStartLine;
        int secondaryCursor = TrackCountsOf(occupancy, !primaryIsColumns).ImplicitStartLine;
        foreach (ref GridItemState item in items)
        {
            if (Secondary(ref item, primaryIsColumns).IsDefinite)
            {
                continue;
            }

            (int primaryStart, int primaryEnd, int secondaryStart, int secondaryEnd) = PlaceIndefiniteItem(
                occupancy,
                Primary(ref item, primaryIsColumns),
                Secondary(ref item, primaryIsColumns),
                primaryIsColumns,
                primaryCursor,
                secondaryCursor
            );
            RecordPlacement(
                occupancy,
                ref item,
                primaryIsColumns,
                primaryStart,
                primaryEnd,
                secondaryStart,
                secondaryEnd,
                GridOccupancy.CellState.AutoPlaced
            );
            primaryCursor = primaryEnd;
            secondaryCursor = secondaryStart;
        }
    }

    // Step 4 for one item (Taffy's place_indefinitely_positioned_item).
    private static (int PrimaryStart, int PrimaryEnd, int SecondaryStart, int SecondaryEnd) PlaceIndefiniteItem(
        GridOccupancy occupancy,
        OriginZeroPlacement primary,
        OriginZeroPlacement secondary,
        bool primaryIsColumns,
        int primaryIndex,
        int secondaryIndex
    )
    {
        TrackCounts primaryCounts = TrackCountsOf(occupancy, primaryIsColumns);
        int secondarySpan = secondary.IndefiniteSpan;
        if (primary.IsDefinite)
        {
            // A fixed position in the primary axis: move along the secondary one until the item fits.
            primary.ResolveDefinite(out int primaryStart, out int primaryEnd);
            if (primaryStart < primaryIndex)
            {
                secondaryIndex = SaturatingIncrement(secondaryIndex);
            }

            while (true)
            {
                (int secondaryStart, int secondaryEnd) = IndefiniteSpan(secondaryIndex, secondarySpan);
                if (occupancy.Collides(!primaryIsColumns, secondaryStart, secondaryEnd, primaryStart, primaryEnd, out int next))
                {
                    secondaryIndex = next;
                    continue;
                }

                return (primaryStart, primaryEnd, secondaryStart, secondaryEnd);
            }
        }

        // No fixed position: move along the primary axis to the end of the tracks there, then to the start of the next secondary
        // track. An item that spans every primary track fits only in a stripe of empty secondary tracks.
        int primarySpan = primary.IndefiniteSpan;
        bool spansAllPrimaryTracks = primarySpan >= primaryCounts.Length;
        while (true)
        {
            (int primaryStart, int primaryEnd) = IndefiniteSpan(primaryIndex, primarySpan);
            (int secondaryStart, int secondaryEnd) = IndefiniteSpan(secondaryIndex, secondarySpan);
            if (primaryEnd > primaryCounts.ImplicitEndLine)
            {
                // Out of bounds even at the start: it can never fit, and RecordPlacement clamps it into the grid.
                if (primaryIndex == primaryCounts.ImplicitStartLine)
                {
                    return (primaryStart, primaryEnd, secondaryStart, secondaryEnd);
                }

                secondaryIndex = SaturatingIncrement(secondaryIndex);
                primaryIndex = primaryCounts.ImplicitStartLine;
                continue;
            }

            if (spansAllPrimaryTracks)
            {
                if (occupancy.OccupiedTrack(columns: !primaryIsColumns, secondaryStart, secondaryEnd, out int nextSecondary))
                {
                    secondaryIndex = nextSecondary;
                    primaryIndex = primaryCounts.ImplicitStartLine;
                    continue;
                }

                return (primaryStart, primaryEnd, secondaryStart, secondaryEnd);
            }

            if (occupancy.Collides(primaryIsColumns, primaryStart, primaryEnd, secondaryStart, secondaryEnd, out int next))
            {
                primaryIndex = next;
                continue;
            }

            return (primaryStart, primaryEnd, secondaryStart, secondaryEnd);
        }
    }

    // Records an item's lines in the matrix and on the item, clamped into the limited grid with a span of at least one track
    // (Taffy's record_grid_placement and clamp_span_to_limited_grid).
    private static void RecordPlacement(
        GridOccupancy occupancy,
        ref GridItemState item,
        bool primaryIsColumns,
        int primaryStart,
        int primaryEnd,
        int secondaryStart,
        int secondaryEnd,
        GridOccupancy.CellState state
    )
    {
        (primaryStart, primaryEnd) = ClampToLimitedGrid(primaryStart, primaryEnd);
        (secondaryStart, secondaryEnd) = ClampToLimitedGrid(secondaryStart, secondaryEnd);
        occupancy.Mark(primaryIsColumns, primaryStart, primaryEnd, secondaryStart, secondaryEnd, state);
        if (primaryIsColumns)
        {
            (item.ColumnStart, item.ColumnEnd, item.RowStart, item.RowEnd) = (primaryStart, primaryEnd, secondaryStart, secondaryEnd);
        }
        else
        {
            (item.RowStart, item.RowEnd, item.ColumnStart, item.ColumnEnd) = (primaryStart, primaryEnd, secondaryStart, secondaryEnd);
        }

        static (int Start, int End) ClampToLimitedGrid(int start, int end)
        {
            int clampedStart = Math.Clamp(start, -Track.MaxTracks, Track.MaxTracks - 1);
            return (clampedStart, Math.Clamp(end, clampedStart + 1, Track.MaxTracks));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref OriginZeroPlacement Primary(ref GridItemState item, bool primaryIsColumns) =>
        ref primaryIsColumns ? ref item.ColumnPlacement : ref item.RowPlacement;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref OriginZeroPlacement Secondary(ref GridItemState item, bool primaryIsColumns) =>
        ref primaryIsColumns ? ref item.RowPlacement : ref item.ColumnPlacement;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TrackCounts TrackCountsOf(GridOccupancy occupancy, bool columns) => columns ? occupancy.Columns : occupancy.Rows;

    // An indefinite span from a position, clamped to the range of a line (Taffy's resolve_indefinite_grid_span).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int Start, int End) IndefiniteSpan(int position, int span) =>
        (Math.Clamp(position, short.MinValue, short.MaxValue), Math.Clamp(position + span, short.MinValue, short.MaxValue));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SaturatingIncrement(int position) => Math.Min(position + 1, short.MaxValue);

    // The tracks of an axis with their sizing functions, and the gutters between them (Taffy's initialize_grid_tracks): the
    // implicit tracks before the explicit grid, the explicit ones, and the implicit ones after. The implicit tracks take the
    // auto sizes in turn, those before the grid counting back from its start. The first and last lines are collapsed.
    private static void InitializeGridTracks(
        Span<GridTrackState> tracks,
        TrackCounts counts,
        ImmutableArray<GridTrack> template,
        ImmutableArray<TrackSize> autoTracks,
        LengthPercentage gap
    )
    {
        TrackFunction gutter = gap.IsPercent ? new TrackFunction(TrackKind.Percent, gap.Value) : new TrackFunction(TrackKind.Px, gap.Value);
        int next = 0;
        tracks[next++] = GridTrackState.Gutter(gutter);
        int autoCount = autoTracks.IsDefault ? 0 : autoTracks.Length;
        if (counts.NegativeImplicit > 0)
        {
            int offset = autoCount == 0 ? 0 : autoCount - (counts.NegativeImplicit % autoCount);
            for (int i = 0; i < counts.NegativeImplicit; i++)
            {
                AddTrack(tracks, ref next, autoCount == 0 ? Track.Auto : autoTracks[(offset + i) % autoCount], gutter);
            }
        }

        int explicitLimit = counts.NegativeImplicit + counts.Explicit;
        int current = counts.NegativeImplicit;
        if (counts.Explicit > 0)
        {
            foreach (GridTrack entry in template)
            {
                if (!entry.IsRepeat)
                {
                    if (current < explicitLimit)
                    {
                        AddTrack(tracks, ref next, entry.Single, gutter);
                        current++;
                    }

                    continue;
                }

                int repeated = Math.Min(entry.Repeated.Length * entry.Count, explicitLimit - current);
                for (int i = 0; i < repeated; i++)
                {
                    AddTrack(tracks, ref next, entry.Repeated[i % entry.Repeated.Length], gutter);
                    current++;
                }
            }
        }

        int after = counts.PositiveImplicit + (explicitLimit - current);
        for (int i = 0; i < after; i++)
        {
            AddTrack(tracks, ref next, autoCount == 0 ? Track.Auto : autoTracks[i % autoCount], gutter);
        }

        tracks[0].Collapse();
        tracks[next - 1].Collapse();

        static void AddTrack(Span<GridTrackState> tracks, ref int next, TrackSize size, TrackFunction gutter)
        {
            tracks[next++] = GridTrackState.Track(size.Min, size.Max);
            tracks[next++] = GridTrackState.Gutter(gutter);
        }
    }

    // Whether a track an item spans is flexible, or has an intrinsic sizing function.
    private static bool AnySpanned(Span<GridTrackState> tracks, int startLine, int endLine, bool flexible)
    {
        for (int i = startLine + 1; i < endLine; i++)
        {
            if (flexible ? tracks[i].IsFlexible : tracks[i].HasIntrinsicSizingFunction)
            {
                return true;
            }
        }

        return false;
    }

    private static float SumBaseSizes(Span<GridTrackState> tracks)
    {
        float sum = 0;
        foreach (ref GridTrackState track in tracks)
        {
            sum += track.BaseSize;
        }

        return sum;
    }

    // The container and its tracks, once the container's size is known (Taffy's compute_grid_layout from "Column sizing must be
    // re-run"): the columns are sized again when their sizes depend on percentages of a width that was not known, or when an
    // item's min-content width contribution in an intrinsic column changed; then the rows, likewise. A size that a second run
    // changed and no percentage read is the container's again.
    private static (Size BorderBox, Size ContentBox) RerunTrackSizing(
        UiTree tree,
        in GridConstants c,
        OptionalSize styleSize,
        OptionalSize innerNodeSize,
        Span<GridTrackState> columns,
        Span<GridTrackState> rows,
        Span<GridItemState> items,
        bool anyBaseline,
        Size borderBox,
        Size contentBox
    )
    {
        bool hasPercentageColumn = AnyUsesPercentage(columns);
        bool hasPercentageRow = AnyUsesPercentage(rows);
        bool rerunColumns = float.IsNaN(c.OuterNodeSize.Width) && hasPercentageColumn;
        bool columnContributionChanged = false;
        if (!rerunColumns)
        {
            // Every item is visited: each updates its caches.
            foreach (ref GridItemState item in items)
            {
                if (item.CrossesIntrinsicColumn)
                {
                    columnContributionChanged |= RefreshMinContentContribution(tree, ref item, inline: true, columns, rows, innerNodeSize);
                }
            }

            rerunColumns = columnContributionChanged;
        }
        else
        {
            foreach (ref GridItemState item in items)
            {
                item.HasGridAreaSize = false;
                item.MinContentCache = item.MinContentCache with { Width = float.NaN };
                item.MaxContentCache = item.MaxContentCache with { Width = float.NaN };
                item.MinimumCache = item.MinimumCache with { Width = float.NaN };
            }
        }

        bool rerunRows = float.IsNaN(c.OuterNodeSize.Height) && hasPercentageRow;
        bool rowContributionChanged = false;
        if (rerunRows)
        {
            foreach (ref GridItemState item in items)
            {
                item.HasGridAreaSize = false;
                item.MinContentCache = item.MinContentCache with { Height = float.NaN };
                item.MaxContentCache = item.MaxContentCache with { Height = float.NaN };
                item.MinimumCache = item.MinimumCache with { Height = float.NaN };
            }
        }

        if (rerunColumns)
        {
            TrackSizing(tree, inline: true, in c, innerNodeSize, columns, rows, items, estimateFromMax: false, anyBaseline);
            if (!rerunRows)
            {
                foreach (ref GridItemState item in items)
                {
                    if (item.CrossesIntrinsicRow)
                    {
                        rowContributionChanged |= RefreshMinContentContribution(tree, ref item, inline: false, rows, columns, innerNodeSize);
                    }
                }

                rerunRows = rowContributionChanged;
            }
        }

        if (rerunRows)
        {
            TrackSizing(tree, inline: false, in c, innerNodeSize, rows, columns, items, estimateFromMax: false, anyBaseline: false);
        }

        if (columnContributionChanged && !hasPercentageColumn)
        {
            float width = Math.Max(
                Maybe.Clamp(Maybe.Or(styleSize.Width, SumBaseSizes(columns) + c.ContentInset.Horizontal), c.Min.Width, c.Max.Width),
                c.PaddingBorder.Width
            );
            borderBox = borderBox with { Width = width };
            contentBox = contentBox with { Width = Math.Max(0, width - c.ContentInset.Horizontal) };
        }

        if (rowContributionChanged && !hasPercentageRow)
        {
            float height = Math.Max(
                Maybe.Clamp(Maybe.Or(styleSize.Height, SumBaseSizes(rows) + c.ContentInset.Vertical), c.Min.Height, c.Max.Height),
                c.PaddingBorder.Height
            );
            borderBox = borderBox with { Height = height };
            contentBox = contentBox with { Height = Math.Max(0, height - c.ContentInset.Vertical) };
        }

        return (borderBox, contentBox);
    }

    private static bool AnyUsesPercentage(Span<GridTrackState> tracks)
    {
        foreach (ref GridTrackState track in tracks)
        {
            if (track.UsesPercentage)
            {
                return true;
            }
        }

        return false;
    }

    // The item's min-content contribution in an axis with the tracks as sized, kept as the cache; returns whether it changed
    // from the one the cache held.
    private static bool RefreshMinContentContribution(
        UiTree tree,
        ref GridItemState item,
        bool inline,
        Span<GridTrackState> axisTracks,
        Span<GridTrackState> otherTracks,
        OptionalSize innerNodeSize
    )
    {
        OptionalSize area = GridAreaSize(ref item, inline, axisTracks, otherTracks, innerNodeSize, estimateFromMax: false);
        OptionalSize available = With(area, inline, float.NaN);
        float contribution = MinContentContribution(tree, ref item, inline, area, available);
        bool changed = contribution != Get(item.MinContentCache, inline);
        item.HasGridAreaSize = true;
        item.GridAreaSize = area;
        item.MinContentCache = With(item.MinContentCache, inline, contribution);
        item.MaxContentCache = With(item.MaxContentCache, inline, float.NaN);
        item.MinimumCache = With(item.MinimumCache, inline, float.NaN);
        return changed;
    }

    // The item whose baseline is the grid's (Taffy's grid_container_baseline): in the first row, the first item that aligns by
    // baseline, else the first item.
    private static NodeHandle GridBaselineItem(Span<GridItemState> items)
    {
        if (items.IsEmpty)
        {
            return default;
        }

        int firstRow = int.MaxValue;
        foreach (ref GridItemState item in items)
        {
            firstRow = Math.Min(firstRow, item.RowIndexStart);
        }

        int first = -1;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].RowIndexStart != firstRow)
            {
                continue;
            }

            if (first < 0)
            {
                first = i;
            }

            if (items[i].ParticipatesInBaselineAlignment)
            {
                return items[i].Node;
            }
        }

        return items[first].Node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Get(OptionalSize size, bool inline) => inline ? size.Width : size.Height;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Get(Size size, bool inline) => inline ? size.Width : size.Height;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize With(OptionalSize size, bool inline, float value) =>
        inline ? size with { Width = value } : size with { Height = value };

    // What the layout of a grid reads of its container, once (Taffy's GridContainerConstants).
    private struct GridConstants
    {
        public Edges Padding;
        public Edges Border;
        public Size PaddingBorder;
        public OptionalSize Min;
        public OptionalSize Max;
        public OptionalSize PreferredSize;

        // The room the scrollbars take: the width of the vertical one, the height of the horizontal one.
        public Size Gutter;

        // The padding, the border, and the scrollbars' room.
        public Edges ContentInset;
        public AlignContent AlignContent;
        public AlignContent JustifyContent;
        public AlignItems AlignItems;
        public AlignItems JustifyItems;
        public AvailableSize AvailableGridSpace;
        public OptionalSize OuterNodeSize;
        public OptionalSize InnerMin;
        public OptionalSize InnerMax;
        public OptionalSize InnerNodeSize;
    }

    // A placement in lines from the explicit grid's start (Taffy's Line<OriginZeroGridPlacement>).
    private struct OriginZeroPlacement
    {
        public GridLineKind StartKind;
        public int Start;
        public GridLineKind EndKind;
        public int End;

        // Whether a line of either end is given: then the placement does not need the auto-placement in this axis.
        public readonly bool IsDefinite => StartKind == GridLineKind.Line || EndKind == GridLineKind.Line;

        // The span of a placement without a line (Taffy's indefinite_span).
        public readonly int IndefiniteSpan =>
            (StartKind, EndKind) switch
            {
                (GridLineKind.Line, GridLineKind.Span) => End,
                (GridLineKind.Span, _) => Start,
                (_, GridLineKind.Span) => End,
                _ => 1,
            };

        public static OriginZeroPlacement Of(GridPlacement placement, int explicitCount)
        {
            var result = default(OriginZeroPlacement);
            (result.StartKind, result.Start) = Resolve(placement.Start, explicitCount);
            (result.EndKind, result.End) = Resolve(placement.End, explicitCount);
            return result;
        }

        // Both ends, when at least one is a line (Taffy's resolve_definite_grid_lines): two equal lines take one track, and two
        // lines in the wrong order are swapped (CSS Grid §8.3.1).
        public readonly void ResolveDefinite(out int start, out int end)
        {
            switch (StartKind, EndKind)
            {
                case (GridLineKind.Line, GridLineKind.Line):
                    (start, end) = Start == End ? (Start, Start + 1) : (Math.Min(Start, End), Math.Max(Start, End));
                    break;
                case (GridLineKind.Line, GridLineKind.Span):
                    (start, end) = (Start, Start + End);
                    break;
                case (GridLineKind.Line, _):
                    (start, end) = (Start, Start + 1);
                    break;
                case (GridLineKind.Span, GridLineKind.Line):
                    (start, end) = (End - Start, End);
                    break;
                default:
                    (start, end) = (End - 1, End);
                    break;
            }
        }

        // The smallest and largest lines the placement uses, for the estimate of the track counts, and its span when it has no
        // line (Taffy's child_min_line_max_line_span); 0 where it gives no line, which never changes the estimate.
        public readonly void Estimate(out int min, out int max, out int span)
        {
            (min, max) = (StartKind, EndKind) switch
            {
                (GridLineKind.Line, GridLineKind.Line) => Start == End ? (Start, Start + 1) : (Math.Min(Start, End), Math.Max(Start, End)),
                (GridLineKind.Line, GridLineKind.Span) => (Start, Start + End),
                (GridLineKind.Line, _) => (Start, Start + 1),
                (GridLineKind.Span, GridLineKind.Line) => (End - Start, End),
                (_, GridLineKind.Line) => (End - 1, End),
                _ => (0, 0),
            };
            span = IsDefinite ? 1 : IndefiniteSpan;
            min = Math.Max(min, -Track.MaxTracks);
            max = Math.Min(max, Track.MaxTracks);
        }

        // A line from the explicit grid's start: line 1 is 0, line -1 the explicit grid's end; line 0 is auto; a span is from 1
        // to the largest number of tracks.
        private static (GridLineKind Kind, int Value) Resolve(GridLine line, int explicitCount) =>
            line.Kind switch
            {
                GridLineKind.Span => (GridLineKind.Span, Math.Clamp((int)line.Value, 1, Track.MaxTracks)),
                GridLineKind.Line when line.Value > 0 => (GridLineKind.Line, Math.Clamp(line.Value - 1, -Track.MaxTracks, Track.MaxTracks)),
                GridLineKind.Line when line.Value < 0 => (
                    GridLineKind.Line,
                    Math.Clamp(line.Value + explicitCount + 1, -Track.MaxTracks, Track.MaxTracks)
                ),
                _ => (GridLineKind.Auto, 0),
            };
    }

    // One track or gutter of an axis while its sizes are found (Taffy's GridTrack).
    private struct GridTrackState
    {
        public TrackFunction Min;
        public TrackFunction Max;
        public bool IsGutter;
        public bool IsCollapsed;
        public bool InfinitelyGrowable;

        // An item of one track gave the growth limit a contribution in this batch, which may be 0.
        public bool HasPlannedGrowthLimit;
        public float Offset;
        public float BaseSize;

        // Can be infinite.
        public float GrowthLimit;

        // Added to the estimate of the track in the other axis when the items' contents are measured.
        public float ContentAlignmentAdjustment;
        public float ItemIncurredIncrease;
        public float BaseSizePlannedIncrease;
        public float GrowthLimitPlannedIncrease;

        // The `fit-content()` limit, resolved once per run of the track sizing; infinite when there is none.
        public float FitContentLimit;

        public readonly bool IsFlexible => Max.Kind == TrackKind.Fr;

        public readonly bool UsesPercentage => Min.UsesPercentage || Max.UsesPercentage;

        public readonly bool HasIntrinsicSizingFunction => Min.IsIntrinsic || Max.IsIntrinsic;

        public readonly float FlexFactor => IsFlexible ? Max.Value : 0;

        public readonly float FitContentLimitedGrowthLimit => Math.Min(GrowthLimit, FitContentLimit);

        public static GridTrackState Track(TrackFunction min, TrackFunction max) =>
            new()
            {
                Min = min,
                Max = max,
                FitContentLimit = float.PositiveInfinity,
            };

        public static GridTrackState Gutter(TrackFunction size) =>
            new()
            {
                Min = size,
                Max = size,
                IsGutter = true,
                FitContentLimit = float.PositiveInfinity,
            };

        // Collapsed tracks and gutters take no room: their sizing functions are a fixed zero (Taffy's GridTrack::collapse).
        public void Collapse()
        {
            IsCollapsed = true;
            Min = TrackFunction.Zero;
            Max = TrackFunction.Zero;
            FitContentLimit = float.PositiveInfinity;
        }
    }

    // A grid item while the grid is laid out (Taffy's GridItem): its style, its lines, and the caches of its contributions,
    // which hold for one run of the track sizing. A cache holds NaN where it has no value.
    private struct GridItemState
    {
        public NodeHandle Node;
        public int SourceOrder;

        // What the layout reads of the item's style most often; the rest is read from the tree where it is used (no ref into the
        // arena is held across a measurement, design §2.5). Copying the whole style into each item was 6 % of the grid scene.
        public bool AutoBlockMargin;
        public bool PercentHeight;
        public Overflow OverflowX;
        public Overflow OverflowY;
        public OriginZeroPlacement ColumnPlacement;
        public OriginZeroPlacement RowPlacement;

        // The lines the placement gave, from the explicit grid's start.
        public int ColumnStart;
        public int ColumnEnd;
        public int RowStart;
        public int RowEnd;

        // The same lines as indexes into the axes' track lists.
        public int ColumnIndexStart;
        public int ColumnIndexEnd;
        public int RowIndexStart;
        public int RowIndexEnd;
        public AlignItems AlignSelf;
        public AlignItems JustifySelf;

        // The first baseline from the top of the border box, or NaN; and the room above it that baseline alignment adds as a
        // margin.
        public float Baseline;
        public float BaselineShim;
        public bool CrossesFlexibleColumn;
        public bool CrossesIntrinsicColumn;
        public bool CrossesFlexibleRow;
        public bool CrossesIntrinsicRow;
        public bool HasGridAreaSize;
        public OptionalSize GridAreaSize;
        public bool HasKnownDimensions;
        public OptionalSize KnownDimensionsArea;
        public OptionalSize KnownDimensions;
        public OptionalSize MinContentCache;
        public OptionalSize MaxContentCache;
        public OptionalSize MinimumCache;

        public readonly bool HasAutoBlockMargin => AutoBlockMargin;

        // An item whose height is a percentage of rows that are sized by their content depends on itself through the rows
        // (CSS Grid §11.5): it aligns by its fallback alignment.
        public readonly bool HasCyclicBlockSizeDependency => PercentHeight && (CrossesIntrinsicRow || CrossesFlexibleRow);

        public readonly bool ParticipatesInBaselineAlignment =>
            AlignSelf.Keyword == AlignItemsKeyword.Baseline && !HasAutoBlockMargin && !HasCyclicBlockSizeDependency;

        public readonly int Span(bool inline) => inline ? ColumnEnd - ColumnStart : RowEnd - RowStart;

        public readonly bool CrossesFlexibleTrack(bool inline) => inline ? CrossesFlexibleColumn : CrossesFlexibleRow;

        public readonly bool CrossesIntrinsicTrack(bool inline) => inline ? CrossesIntrinsicColumn : CrossesIntrinsicRow;

        // The tracks and lines inside the item's area in an axis, as a range of the track list.
        public readonly (int Start, int End) TrackRange(bool inline) =>
            inline ? (ColumnIndexStart + 1, ColumnIndexEnd) : (RowIndexStart + 1, RowIndexEnd);

        public readonly bool IsScrollContainer(bool inline) => (inline ? OverflowX : OverflowY) >= Overflow.Hidden;
    }
}
