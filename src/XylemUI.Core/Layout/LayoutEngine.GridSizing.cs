using System.Runtime.CompilerServices;

namespace XylemUI;

internal static partial class LayoutEngine
{
    // A small amount below which space is not distributed, to end the loops despite rounding (Taffy's THRESHOLD).
    private const float DistributionThreshold = 0.01f;
    private const float BeyondLimitsThreshold = 0.000001f;

    // Which tracks a distribution of space affects (the closures of Taffy's track_sizing.rs).
    private enum TrackFilter : byte
    {
        All,

        // A min track sizing function that is not fixed: intrinsic, or a percentage of an unknown size.
        IntrinsicMin,
        MinOrMaxContentMin,
        MaxContentMin,

        // An `auto` minimum, unless the maximum is `min-content` (CSS Grid §7.2.4: a maximum below the minimum is floored).
        AutoMin,

        // A max track sizing function without a fixed value.
        IntrinsicMax,

        // A maximum that grows to max-content contributions, or a percentage of an unknown size.
        MaxContentMax,
    }

    // What limits a track's growth in a distribution.
    private enum TrackLimit : byte
    {
        GrowthLimit,
        FitContentLimitedGrowthLimit,
        FitContentLimit,
        Infinity,
    }

    // CSS Grid §11.3, the track sizing algorithm in one axis (Taffy's track_sizing_algorithm). The gutters are sized as empty
    // fixed tracks. Items are measured with an estimate of their area in the other axis: the fixed maximum of each track in the
    // first run of the columns (estimateFromMax), the base sizes found so far afterwards.
    private static void TrackSizing(
        UiTree tree,
        bool inline,
        in GridConstants c,
        OptionalSize innerNodeSize,
        Span<GridTrackState> axisTracks,
        Span<GridTrackState> otherTracks,
        Span<GridItemState> items,
        bool estimateFromMax,
        bool anyBaseline
    )
    {
        float axisMin = Get(c.InnerMin, inline);
        float axisMax = Get(c.InnerMax, inline);
        AlignContent axisAlignment = inline ? c.JustifyContent : c.AlignContent;
        AlignContent otherAlignment = inline ? c.AlignContent : c.JustifyContent;
        AvailableSpace axisAvailable = inline ? c.AvailableGridSpace.Width : c.AvailableGridSpace.Height;

        // 11.4 Initialize each track's base size and growth limit: a fixed function's size, else 0 and infinity.
        float percentageBasis = Maybe.Or(Get(innerNodeSize, inline), axisMin);
        foreach (ref GridTrackState track in axisTracks)
        {
            track.BaseSize = Maybe.Or(track.Min.DefiniteValue(percentageBasis), 0);
            track.GrowthLimit = Maybe.Or(track.Max.DefiniteValue(percentageBasis), float.PositiveInfinity);
            if (track.GrowthLimit < track.BaseSize)
            {
                track.GrowthLimit = track.BaseSize;
            }
        }

        float axisInner = Get(innerNodeSize, inline);
        foreach (ref GridTrackState track in axisTracks)
        {
            track.FitContentLimit = Maybe.Or(track.Max.FitContentLimit(axisInner), float.PositiveInfinity);
        }

        // 11.5.1 Shim the baseline-aligned items.
        if (anyBaseline)
        {
            ResolveItemBaselines(tree, items, innerNodeSize);
        }

        // Fixed tracks whose base size is their growth limit are final. A track with an intrinsic minimum may still grow past a
        // fixed growth limit (`minmax(auto, 0px)`).
        bool allFinal = true;
        foreach (ref GridTrackState track in axisTracks)
        {
            if (track.BaseSize != track.GrowthLimit || float.IsNaN(track.Min.DefiniteValue(percentageBasis)))
            {
                allFinal = false;
                break;
            }
        }

        if (allFinal)
        {
            return;
        }

        // Extra room each gutter of the other axis adds to the estimate of an item's area there, from the content alignment of
        // the other axis when its size is known.
        float adjustment = AlignmentGutterAdjustment(otherAlignment, Get(innerNodeSize, !inline), otherTracks, estimateFromMax);
        if (otherTracks.Length > 3)
        {
            for (int i = 2; i < otherTracks.Length; i += 2)
            {
                otherTracks[i].ContentAlignmentAdjustment = adjustment;
            }
        }

        // 11.5 Resolve the intrinsic track sizes.
        var sizer = new IntrinsicSizer(tree, inline, innerNodeSize, estimateFromMax);
        ResolveIntrinsicTrackSizes(ref sizer, axisTracks, otherTracks, items, axisAvailable);

        // 11.6 Maximize the tracks: free space to the tracks with finite growth limits.
        MaximiseTracks(axisTracks, axisAvailable);

        // The last two steps expand only into space the container's own size makes, not into any available space: a definite
        // space without a known size is max-content; a min-content constraint of the height is its max-content one.
        AvailableSpace expansion =
            !float.IsNaN(axisInner) ? AvailableSpace.Definite(axisInner)
            : axisAvailable.Kind == AvailableSpaceKind.MinContent && inline ? AvailableSpace.MinContent
            : AvailableSpace.MaxContent;

        // 11.7 Expand the flexible tracks.
        ExpandFlexibleTracks(ref sizer, axisTracks, otherTracks, items, axisMin, axisMax, expansion);

        // 11.8 Stretch the `auto` tracks.
        if (axisAlignment == new AlignContent(AlignContentKeyword.Stretch))
        {
            StretchAutoTracks(axisTracks, axisMin, expansion);
        }
    }

    // The extra size of each inner gutter in the other axis that its content alignment gives, when that axis's size and every
    // track's estimate are definite (Taffy's compute_alignment_gutter_adjustment). Items never cross the outermost gutters, so
    // start and end weigh the same.
    private static float AlignmentGutterAdjustment(AlignContent alignment, float axisInnerSize, Span<GridTrackState> tracks, bool estimateFromMax)
    {
        if (tracks.Length <= 1)
        {
            return 0;
        }

        int outer = alignment.Keyword switch
        {
            AlignContentKeyword.Normal or AlignContentKeyword.Stretch or AlignContentKeyword.SpaceBetween => 0,
            _ => 1,
        };
        int inner = alignment.Keyword switch
        {
            AlignContentKeyword.SpaceBetween or AlignContentKeyword.SpaceEvenly => 1,
            AlignContentKeyword.SpaceAround => 2,
            _ => 0,
        };
        if (inner == 0 || float.IsNaN(axisInnerSize))
        {
            return 0;
        }

        float sum = 0;
        foreach (ref GridTrackState track in tracks)
        {
            sum += TrackSizeEstimate(in track, axisInnerSize, estimateFromMax);
        }

        float free = float.IsNaN(sum) ? 0 : Math.Max(0, axisInnerSize - sum);
        int weighted = ((tracks.Length - 3) / 2 * inner) + (2 * outer);
        return free / weighted * inner;
    }

    // The size of a track of the other axis assumed when an item is measured, or NaN.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TrackSizeEstimate(in GridTrackState track, float basis, bool estimateFromMax) =>
        estimateFromMax ? track.Max.DefiniteValue(basis) : track.BaseSize;

    // 11.5.1 The baselines of the items in each row that align by baseline, and the shim that moves each to the row's largest
    // (Taffy's resolve_item_baselines). A row with one such item or none needs nothing.
    private static void ResolveItemBaselines(UiTree tree, Span<GridItemState> items, OptionalSize innerNodeSize)
    {
        ScratchStack<int> orderScratch = _gridOrderScratch ??= new ScratchStack<int>();
        int top = orderScratch.Top;
        try
        {
            // The items in the order of their first rows, stably.
            Span<int> order = orderScratch.Push(items.Length);
            for (int i = 0; i < order.Length; i++)
            {
                int index = i;
                int j = i - 1;
                while (j >= 0 && items[order[j]].RowStart > items[index].RowStart)
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = index;
            }

            for (int start = 0; start < order.Length; )
            {
                int row = items[order[start]].RowStart;
                int end = start + 1;
                while (end < order.Length && items[order[end]].RowStart == row)
                {
                    end++;
                }

                int participating = 0;
                for (int i = start; i < end; i++)
                {
                    participating += items[order[i]].ParticipatesInBaselineAlignment ? 1 : 0;
                }

                if (participating > 1)
                {
                    float rowBaseline = float.NegativeInfinity;
                    for (int i = start; i < end; i++)
                    {
                        ref GridItemState item = ref items[order[i]];
                        if (!item.ParticipatesInBaselineAlignment)
                        {
                            continue;
                        }

                        var input = new LayoutInput(
                            OptionalSize.None,
                            true,
                            true,
                            innerNodeSize,
                            new AvailableSize(AvailableSpace.MinContent, AvailableSpace.MinContent),
                            SizingMode.InherentSize,
                            RequestedAxis.Both
                        );
                        Size size = LayoutChild(tree, item.Node, in input, commit: true);
                        item.Baseline = Maybe.Or(ReadBaseline(tree, item.Node), size.Height);
                        rowBaseline = Math.Max(rowBaseline, MarginBoxBaseline(tree, in item, innerNodeSize.Width));
                    }

                    for (int i = start; i < end; i++)
                    {
                        ref GridItemState item = ref items[order[i]];
                        if (item.ParticipatesInBaselineAlignment)
                        {
                            item.BaselineShim = rowBaseline - MarginBoxBaseline(tree, in item, innerNodeSize.Width);
                            item.HasKnownDimensions = false;
                        }
                    }
                }

                start = end;
            }
        }
        finally
        {
            orderScratch.PopTo(top);
        }

        static float MarginBoxBaseline(UiTree tree, in GridItemState item, float width) =>
            Maybe.Or(item.Baseline, 0) + StyleOf(tree, in item).Margin.Top.ResolveOrZero(width);
    }

    // 11.5 Resolve the intrinsic track sizes (Taffy's resolve_intrinsic_track_sizes): the items in batches, first those that cross
    // no flexible track by ascending span, then those that cross one, all together.
    private static void ResolveIntrinsicTrackSizes(
        ref IntrinsicSizer sizer,
        Span<GridTrackState> axisTracks,
        Span<GridTrackState> otherTracks,
        Span<GridItemState> items,
        AvailableSpace axisAvailable
    )
    {
        bool inline = sizer.Inline;
        ScratchStack<int> orderScratch = _gridOrderScratch ??= new ScratchStack<int>();
        int top = orderScratch.Top;
        try
        {
            Span<int> order = orderScratch.Push(items.Length);
            int nonFlex = OrderForIntrinsicSizing(items, inline, order, orderScratch);
            float axisInner = Get(sizer.InnerNodeSize, inline);
            for (int batchStart = 0; batchStart < order.Length; )
            {
                bool isFlex = batchStart >= nonFlex;
                int batchSpan = items[order[batchStart]].Span(inline);
                int batchEnd = batchStart + 1;
                if (isFlex)
                {
                    batchEnd = order.Length;
                }
                else
                {
                    while (batchEnd < nonFlex && items[order[batchEnd]].Span(inline) == batchSpan)
                    {
                        batchEnd++;
                    }
                }

                Span<int> batch = order[batchStart..batchEnd];
                if (!isFlex && batchSpan == 1)
                {
                    SizeTracksForSingleSpanItems(ref sizer, axisTracks, otherTracks, items, batch, axisAvailable, axisInner);
                }
                else
                {
                    SizeTracksForSpanningItems(ref sizer, axisTracks, otherTracks, items, batch, axisAvailable, axisInner, isFlex);
                }

                batchStart = batchEnd;
            }
        }
        finally
        {
            orderScratch.PopTo(top);
        }

        // Step 5. A track whose growth limit is still infinite (it has no items, or it is flexible) takes its base size, so that
        // the maximization below does not grow the flexible tracks.
        foreach (ref GridTrackState track in axisTracks)
        {
            if (float.IsPositiveInfinity(track.GrowthLimit))
            {
                track.GrowthLimit = track.BaseSize;
            }
        }
    }

    // Writes the items' indexes in the order of the batches: those that cross no flexible track, stably by span, then those that
    // do. Returns how many cross none.
    private static int OrderForIntrinsicSizing(Span<GridItemState> items, bool inline, Span<int> order, ScratchStack<int> scratch)
    {
        int front = 0;
        int maxSpan = 1;
        for (int i = 0; i < items.Length; i++)
        {
            if (!items[i].CrossesFlexibleTrack(inline))
            {
                order[front++] = i;
                maxSpan = Math.Max(maxSpan, items[i].Span(inline));
            }
        }

        int back = front;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].CrossesFlexibleTrack(inline))
            {
                order[back++] = i;
            }
        }

        if (maxSpan == 1)
        {
            return front;
        }

        // A counting sort by span keeps the order of equal spans.
        Span<int> counts = scratch.Push(maxSpan + 2);
        Span<int> sorted = scratch.Push(front);
        counts.Clear();
        for (int i = 0; i < front; i++)
        {
            counts[items[order[i]].Span(inline) + 1]++;
        }

        for (int s = 1; s < counts.Length; s++)
        {
            counts[s] += counts[s - 1];
        }

        for (int i = 0; i < front; i++)
        {
            sorted[counts[items[order[i]].Span(inline)]++] = order[i];
        }

        sorted.CopyTo(order);
        return front;
    }

    // Step 2 for the items that span one track and cross no flexible one (Taffy's fast path): each track's base size takes its
    // items' contributions by its minimum, and its growth limit by its maximum.
    private static void SizeTracksForSingleSpanItems(
        ref IntrinsicSizer sizer,
        Span<GridTrackState> axisTracks,
        Span<GridTrackState> otherTracks,
        Span<GridItemState> items,
        Span<int> batch,
        AvailableSpace axisAvailable,
        float axisInner
    )
    {
        bool inline = sizer.Inline;
        foreach (int index in batch)
        {
            ref GridItemState item = ref items[index];
            int trackIndex = (inline ? item.ColumnIndexStart : item.RowIndexStart) + 1;
            GridTrackState track = axisTracks[trackIndex];
            float baseSize;
            if (track.Min.Kind == TrackKind.MinContent)
            {
                baseSize = Math.Max(track.BaseSize, sizer.MinContent(ref item, axisTracks, otherTracks));
            }
            else if (track.Min.Kind == TrackKind.MaxContent)
            {
                baseSize = Math.Max(track.BaseSize, sizer.MaxContent(ref item, axisTracks, otherTracks));
            }
            else if (track.Min.BehavesAsAuto(axisInner))
            {
                // The item's minimum contribution, also in a content-sized container: Taffy 0.15.1 takes the limited min-content
                // contribution there, as CSS Grid §11.5 says, but Chrome 157 takes the minimum, so that an item with
                // `min-width: 0` gives an auto column nothing under a min-content constraint (Fixtures/XylemUI,
                // grid_min_content_auto_track_item_min_width_zero). With an automatic minimum the two are the same.
                baseSize = Math.Max(track.BaseSize, sizer.Minimum(ref item, axisTracks, otherTracks));
            }
            else
            {
                baseSize = track.BaseSize;
            }

            axisTracks[trackIndex].BaseSize = baseSize;

            // The min-content contribution is found for every item in an intrinsic track, as the check of whether the sizes run
            // again compares it with the cache.
            track = axisTracks[trackIndex];
            float minContentContribution = track.HasIntrinsicSizingFunction ? sizer.MinContent(ref item, axisTracks, otherTracks) : float.NaN;
            float planned = float.NaN;
            if (track.Max.IsFitContent)
            {
                // At least the max-content contribution limited by the fit-content() argument, and the min-content one for an
                // item that is not a scroll container.
                float maxContent = Math.Min(sizer.MaxContent(ref item, axisTracks, otherTracks), track.FitContentLimit);
                planned =
                    !float.IsNaN(minContentContribution) && !item.IsScrollContainer(inline)
                        ? Math.Max(minContentContribution, maxContent)
                        : maxContent;
            }
            else if (track.Max.IsMaxContentAlike || (track.Max.UsesPercentage && float.IsNaN(axisInner)))
            {
                // A percentage of a size not known yet behaves as auto (as Chrome).
                planned = sizer.MaxContent(ref item, axisTracks, otherTracks);
            }
            else if (track.Max.IsIntrinsic)
            {
                planned = minContentContribution;
            }

            if (!float.IsNaN(planned))
            {
                ref GridTrackState t = ref axisTracks[trackIndex];
                t.GrowthLimitPlannedIncrease = Math.Max(t.GrowthLimitPlannedIncrease, planned);
                t.HasPlannedGrowthLimit = true;
            }
        }

        // A track whose items contribute takes the largest contribution as its growth limit, also when it is 0 (CSS Grid §11.5
        // step 2, and Chrome 157: Fixtures/XylemUI, grid_spanning_item_grows_infinitely_growable_track, where an empty item keeps
        // the second column from taking the spanning item's growth). Taffy 0.15.1 writes it only when it is above 0, which keeps an
        // infinite growth limit for the spanning items' steps.
        foreach (ref GridTrackState track in axisTracks)
        {
            if (track.HasPlannedGrowthLimit)
            {
                track.GrowthLimit = float.IsPositiveInfinity(track.GrowthLimit)
                    ? track.GrowthLimitPlannedIncrease
                    : Math.Max(track.GrowthLimit, track.GrowthLimitPlannedIncrease);
            }

            track.InfinitelyGrowable = false;
            track.GrowthLimitPlannedIncrease = 0;
            track.HasPlannedGrowthLimit = false;
            if (track.GrowthLimit < track.BaseSize)
            {
                track.GrowthLimit = track.BaseSize;
            }
        }
    }

    // Steps 2 to 4 for a batch of items that span the same number of tracks, or that cross a flexible track (Taffy's general
    // path): the base sizes for the intrinsic minimums, the content-based ones, and the max-content ones; then, for a batch that
    // crosses no flexible track, the growth limits for the intrinsic maximums and the max-content ones.
    private static void SizeTracksForSpanningItems(
        ref IntrinsicSizer sizer,
        Span<GridTrackState> axisTracks,
        Span<GridTrackState> otherTracks,
        Span<GridItemState> items,
        Span<int> batch,
        AvailableSpace axisAvailable,
        float axisInner,
        bool isFlex
    )
    {
        bool inline = sizer.Inline;

        // 1. For intrinsic minimums: the items' minimum contributions, or their limited min-content ones under a content constraint
        // for an item that is not a scroll container (Taffy's QUIRK).
        foreach (int index in batch)
        {
            ref GridItemState item = ref items[index];
            if (!item.CrossesIntrinsicTrack(inline))
            {
                continue;
            }

            float space;
            if (axisAvailable.Kind != AvailableSpaceKind.Definite && !item.IsScrollContainer(inline))
            {
                float minimum = sizer.Minimum(ref item, axisTracks, otherTracks);
                float minContent = sizer.MinContent(ref item, axisTracks, otherTracks);
                (int s, int e) = item.TrackRange(inline);
                float limit = SpannedTrackLimit(axisTracks[s..e], axisInner, fitContent: true);
                float limitedMinContent = Math.Max(Maybe.Min(minContent, limit), minimum);
                if (isFlex)
                {
                    // An item that crosses flexible tracks gives them only the crossed flex factors' share of its content past the
                    // spanned inflexible tracks, at most all of it; its minimum contribution is the floor (CSS Grid 2 §12.5, as
                    // browsers: a `0fr` track holding a `min-height: 0` item collapses).
                    (float inflexible, float scale) = FlexShare(axisTracks[s..e]);
                    space = Math.Max(minimum, inflexible + (Math.Max(limitedMinContent - inflexible, 0) * scale));
                }
                else
                {
                    space = limitedMinContent;
                }
            }
            else
            {
                space = sizer.Minimum(ref item, axisTracks, otherTracks);
            }

            if (space > 0)
            {
                (int start, int end) = item.TrackRange(inline);
                TrackLimit limit = item.IsScrollContainer(inline) ? TrackLimit.FitContentLimitedGrowthLimit : TrackLimit.GrowthLimit;
                DistributeToBaseSizes(isFlex, space, axisTracks[start..end], TrackFilter.IntrinsicMin, axisInner, limit, maximum: false);
            }
        }

        FlushBaseSizeIncreases(axisTracks);

        // 2. For content-based minimums: the min-content contributions, to the tracks with a min-content or max-content minimum.
        foreach (int index in batch)
        {
            ref GridItemState item = ref items[index];
            (int start, int end) = item.TrackRange(inline);
            if (!AnyAffected(axisTracks[start..end], TrackFilter.MinOrMaxContentMin, axisInner))
            {
                continue;
            }

            float space = sizer.MinContent(ref item, axisTracks, otherTracks);
            if (space > 0)
            {
                TrackLimit limit = item.IsScrollContainer(inline) ? TrackLimit.FitContentLimitedGrowthLimit : TrackLimit.GrowthLimit;
                DistributeToBaseSizes(isFlex, space, axisTracks[start..end], TrackFilter.MinOrMaxContentMin, axisInner, limit, maximum: false);
            }
        }

        FlushBaseSizeIncreases(axisTracks);

        // 3. For max-content minimums under a max-content constraint: the limited max-content contributions, to the tracks with a
        // max-content minimum if the item spans one, else to those with an `auto` one (as Chrome and Firefox).
        if (axisAvailable.Kind == AvailableSpaceKind.MaxContent)
        {
            foreach (int index in batch)
            {
                ref GridItemState item = ref items[index];
                (int start, int end) = item.TrackRange(inline);
                Span<GridTrackState> spanned = axisTracks[start..end];
                if (!AnyAffected(spanned, TrackFilter.AutoMin, axisInner) && !AnyAffected(spanned, TrackFilter.MaxContentMin, axisInner))
                {
                    continue;
                }

                float maxContent = sizer.MaxContent(ref item, axisTracks, otherTracks);
                float space = Maybe.Min(maxContent, SpannedTrackLimit(spanned, axisInner, fitContent: true));
                if (isFlex)
                {
                    // As for the intrinsic minimums, from the spanned inflexible tracks' sizes, so the scaling does not compound.
                    (float inflexible, float scale) = FlexShare(spanned);
                    space = inflexible + (Math.Max(space - inflexible, 0) * scale);
                }

                if (space > 0)
                {
                    if (AnyAffected(spanned, TrackFilter.MaxContentMin, axisInner))
                    {
                        DistributeToBaseSizes(isFlex, space, spanned, TrackFilter.MaxContentMin, axisInner, TrackLimit.Infinity, maximum: true);
                    }
                    else
                    {
                        DistributeToBaseSizes(
                            isFlex,
                            space,
                            spanned,
                            TrackFilter.AutoMin,
                            axisInner,
                            TrackLimit.FitContentLimitedGrowthLimit,
                            maximum: true
                        );
                    }
                }
            }

            FlushBaseSizeIncreases(axisTracks);
        }

        // In all cases: the max-content contributions, to the tracks with a max-content minimum.
        foreach (int index in batch)
        {
            ref GridItemState item = ref items[index];
            (int start, int end) = item.TrackRange(inline);
            if (!AnyAffected(axisTracks[start..end], TrackFilter.MaxContentMin, axisInner))
            {
                continue;
            }

            float space = sizer.MaxContent(ref item, axisTracks, otherTracks);
            if (space > 0)
            {
                DistributeToBaseSizes(
                    isFlex,
                    space,
                    axisTracks[start..end],
                    TrackFilter.MaxContentMin,
                    axisInner,
                    TrackLimit.GrowthLimit,
                    maximum: true
                );
            }
        }

        FlushBaseSizeIncreases(axisTracks);

        // 4. A growth limit below the base size takes the base size.
        foreach (ref GridTrackState track in axisTracks)
        {
            if (track.GrowthLimit < track.BaseSize)
            {
                track.GrowthLimit = track.BaseSize;
            }
        }

        // A flexible track has a flexible maximum, not an intrinsic one: the last steps do not apply.
        if (isFlex)
        {
            return;
        }

        // 5. For intrinsic maximums: the min-content contributions, to the growth limits of the tracks without a fixed maximum.
        foreach (int index in batch)
        {
            ref GridItemState item = ref items[index];
            (int start, int end) = item.TrackRange(inline);
            if (!AnyAffected(axisTracks[start..end], TrackFilter.IntrinsicMax, axisInner))
            {
                continue;
            }

            float space = sizer.MinContent(ref item, axisTracks, otherTracks);
            if (space > 0)
            {
                DistributeToGrowthLimits(space, axisTracks[start..end], TrackFilter.IntrinsicMax, axisInner);
            }
        }

        // A growth limit that went from infinite to finite here is infinitely growable for the next step.
        FlushGrowthLimitIncreases(axisTracks, infinitelyGrowable: true);

        // 6. For max-content maximums: the max-content contributions, to the growth limits of the tracks whose maximum grows to
        // them, each limited by its fit-content() argument.
        foreach (int index in batch)
        {
            ref GridItemState item = ref items[index];
            (int start, int end) = item.TrackRange(inline);
            if (!AnyAffected(axisTracks[start..end], TrackFilter.MaxContentMax, axisInner))
            {
                continue;
            }

            float space = sizer.MaxContent(ref item, axisTracks, otherTracks);
            if (space > 0)
            {
                DistributeToGrowthLimits(space, axisTracks[start..end], TrackFilter.MaxContentMax, axisInner);
            }
        }

        FlushGrowthLimitIncreases(axisTracks, infinitelyGrowable: false);
    }

    // The base sizes of an item's spanned inflexible tracks, and its crossed flex factors' sum at most 1 (the share of the space
    // past them that the flexible tracks get).
    private static (float Inflexible, float Scale) FlexShare(Span<GridTrackState> spanned)
    {
        float inflexible = 0;
        float flexSum = 0;
        foreach (ref GridTrackState track in spanned)
        {
            if (track.IsFlexible)
            {
                flexSum += track.FlexFactor;
            }
            else
            {
                inflexible += track.BaseSize;
            }
        }

        return (inflexible, Math.Min(flexSum, 1));
    }

    // The sum of the fixed maximums of the tracks an item spans, when all are fixed (Taffy's spanned_track_limit, with
    // fit-content() arguments; spanned_fixed_track_limit without), else NaN.
    private static float SpannedTrackLimit(Span<GridTrackState> spanned, float axisInner, bool fitContent)
    {
        float sum = 0;
        foreach (ref GridTrackState track in spanned)
        {
            float value = fitContent ? track.Max.DefiniteLimit(axisInner) : track.Max.DefiniteValue(axisInner);
            if (float.IsNaN(value))
            {
                return float.NaN;
            }

            sum += value;
        }

        return sum;
    }

    private static void FlushBaseSizeIncreases(Span<GridTrackState> tracks)
    {
        foreach (ref GridTrackState track in tracks)
        {
            track.BaseSize += track.BaseSizePlannedIncrease;
            track.BaseSizePlannedIncrease = 0;
        }
    }

    private static void FlushGrowthLimitIncreases(Span<GridTrackState> tracks, bool infinitelyGrowable)
    {
        foreach (ref GridTrackState track in tracks)
        {
            if (track.GrowthLimitPlannedIncrease > 0)
            {
                track.GrowthLimit = float.IsPositiveInfinity(track.GrowthLimit)
                    ? track.BaseSize + track.GrowthLimitPlannedIncrease
                    : track.GrowthLimit + track.GrowthLimitPlannedIncrease;
                track.InfinitelyGrowable = infinitelyGrowable;
            }
            else
            {
                track.InfinitelyGrowable = false;
            }

            track.GrowthLimitPlannedIncrease = 0;
        }
    }

    private static bool AnyAffected(Span<GridTrackState> tracks, TrackFilter filter, float axisInner)
    {
        foreach (ref GridTrackState track in tracks)
        {
            if (IsAffected(in track, filter, axisInner))
            {
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAffected(in GridTrackState track, TrackFilter filter, float axisInner) =>
        filter switch
        {
            TrackFilter.All => true,
            TrackFilter.IntrinsicMin => float.IsNaN(track.Min.DefiniteValue(axisInner)),
            TrackFilter.MinOrMaxContentMin => track.Min.Kind is TrackKind.MinContent or TrackKind.MaxContent,
            TrackFilter.MaxContentMin => track.Min.Kind == TrackKind.MaxContent,
            TrackFilter.AutoMin => track.Min.Kind == TrackKind.Auto && track.Max.Kind != TrackKind.MinContent,
            TrackFilter.IntrinsicMax => float.IsNaN(track.Max.DefiniteValue(axisInner)),
            _ => track.Max.IsMaxContentAlike || (track.Max.UsesPercentage && float.IsNaN(axisInner)),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float LimitOf(in GridTrackState track, TrackLimit limit) =>
        limit switch
        {
            TrackLimit.GrowthLimit => track.GrowthLimit,
            TrackLimit.FitContentLimitedGrowthLimit => track.FitContentLimitedGrowthLimit,
            TrackLimit.FitContentLimit => track.FitContentLimit,
            _ => float.PositiveInfinity,
        };

    // 11.5.1 Distributing extra space across spanned tracks, to the base sizes (Taffy's distribute_item_space_to_base_size). For
    // an item that crosses a flexible track, only the flexible affected tracks take space, by their flex factors where their sum
    // is above zero (as Chrome), equally otherwise.
    private static void DistributeToBaseSizes(
        bool isFlex,
        float space,
        Span<GridTrackState> tracks,
        TrackFilter filter,
        float axisInner,
        TrackLimit limit,
        bool maximum
    )
    {
        bool byFlexFactor = false;
        if (isFlex)
        {
            float flexSum = 0;
            foreach (ref GridTrackState track in tracks)
            {
                if (track.IsFlexible && IsAffected(in track, filter, axisInner))
                {
                    flexSum += track.FlexFactor;
                }
            }

            byFlexFactor = flexSum > 0;
        }

        var affected = new Affected(filter, axisInner, isFlex, BeyondLimits: 0);
        if (space == 0 || !AnyAffected(tracks, in affected))
        {
            return;
        }

        // 1. The space to distribute: the contribution beyond the tracks' sizes.
        float trackSizes = 0;
        foreach (ref GridTrackState track in tracks)
        {
            trackSizes += track.BaseSize;
        }

        float extra = Math.Max(0, space - trackSizes);

        // 2. Up to the limits.
        extra = DistributeUpToLimits(extra, tracks, in affected, byFlexFactor, growthLimits: false, limit);

        // 3. Beyond the limits: to the affected tracks with an intrinsic maximum (for minimum contributions) or a max-content or
        // fit-content() one (for maximum contributions), or to all affected tracks when there are none. The fit-content()
        // argument caps the growth for maximum contributions only: Taffy 0.15.1 caps both, but with the growth limits that items
        // contributing 0 now set (SizeTracksForSingleSpanItems), Chrome 157 spreads a spanning item's minimum over a min-content
        // and a fit-content(10px) column evenly (Taffy's grid_span_2_min_content_fit_content_10px_indefinite: 20 and 20).
        if (extra > BeyondLimitsThreshold)
        {
            byte beyond = maximum ? (byte)2 : (byte)1;
            var narrowed = affected with { BeyondLimits = beyond };
            if (!AnyAffected(tracks, in narrowed))
            {
                narrowed = affected;
            }

            DistributeUpToLimits(
                extra,
                tracks,
                in narrowed,
                byFlexFactor,
                growthLimits: false,
                maximum ? TrackLimit.FitContentLimit : TrackLimit.Infinity
            );
        }

        // 4. Each track's planned increase is the largest item-incurred increase.
        foreach (ref GridTrackState track in tracks)
        {
            if (track.ItemIncurredIncrease > track.BaseSizePlannedIncrease)
            {
                track.BaseSizePlannedIncrease = track.ItemIncurredIncrease;
            }

            track.ItemIncurredIncrease = 0;
        }
    }

    // 11.5.1 for the growth limits (Taffy's distribute_item_space_to_growth_limit): up to the limits, a track takes space only when
    // it is infinitely growable or its growth limit is infinite; then all affected tracks, beyond.
    private static void DistributeToGrowthLimits(float space, Span<GridTrackState> tracks, TrackFilter filter, float axisInner)
    {
        var affected = new Affected(filter, axisInner, FlexOnly: false, BeyondLimits: 0);
        if (space == 0 || !AnyAffected(tracks, in affected))
        {
            return;
        }

        float trackSizes = 0;
        foreach (ref GridTrackState track in tracks)
        {
            trackSizes += float.IsPositiveInfinity(track.GrowthLimit) ? track.BaseSize : track.GrowthLimit;
        }

        float extra = Math.Max(0, space - trackSizes);
        extra = DistributeUpToLimits(
            extra,
            tracks,
            affected with
            {
                Growable = true,
            },
            byFlexFactor: false,
            growthLimits: true,
            TrackLimit.FitContentLimit
        );
        if (extra > 0)
        {
            DistributeUpToLimits(extra, tracks, in affected, byFlexFactor: false, growthLimits: true, TrackLimit.FitContentLimit);
        }

        foreach (ref GridTrackState track in tracks)
        {
            if (track.ItemIncurredIncrease > track.GrowthLimitPlannedIncrease)
            {
                track.GrowthLimitPlannedIncrease = track.ItemIncurredIncrease;
            }

            track.ItemIncurredIncrease = 0;
        }
    }

    // 11.6 Maximize the tracks (Taffy's maximise_tracks): positive free space goes to the tracks up to their growth limits; an
    // infinite free space sets every base size to its growth limit.
    private static void MaximiseTracks(Span<GridTrackState> tracks, AvailableSpace available)
    {
        float used = SumBaseSizes(tracks);
        float free = FreeSpace(available, used);
        if (float.IsPositiveInfinity(free))
        {
            foreach (ref GridTrackState track in tracks)
            {
                track.BaseSize = track.GrowthLimit;
            }
        }
        else if (free > 0)
        {
            DistributeUpToLimits(
                free,
                tracks,
                new Affected(TrackFilter.All, float.NaN, FlexOnly: false, BeyondLimits: 0),
                byFlexFactor: false,
                growthLimits: false,
                TrackLimit.FitContentLimitedGrowthLimit
            );
            foreach (ref GridTrackState track in tracks)
            {
                track.BaseSize += track.ItemIncurredIncrease;
                track.ItemIncurredIncrease = 0;
            }
        }
    }

    // The free space of an available space after the used space: infinite for max-content, 0 for min-content (Taffy's
    // compute_free_space).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FreeSpace(AvailableSpace available, float used) =>
        available.Kind switch
        {
            AvailableSpaceKind.Definite => available.Value - used,
            AvailableSpaceKind.MaxContent => float.PositiveInfinity,
            _ => 0,
        };

    // 11.7 Expand the flexible tracks (Taffy's expand_flexible_tracks): the largest fr size that fills the space without
    // exceeding it; under max-content, the largest of each flexible track's base size per factor and each crossing item's
    // max-content contribution per fr, then clamped by the container's limits.
    private static void ExpandFlexibleTracks(
        ref IntrinsicSizer sizer,
        Span<GridTrackState> axisTracks,
        Span<GridTrackState> otherTracks,
        Span<GridItemState> items,
        float axisMin,
        float axisMax,
        AvailableSpace expansion
    )
    {
        float flexFraction;
        if (expansion.Kind == AvailableSpaceKind.Definite)
        {
            float free = expansion.Value - SumBaseSizes(axisTracks);
            flexFraction = free <= 0 ? 0 : FindSizeOfFr(axisTracks, expansion.Value);
        }
        else if (expansion.Kind == AvailableSpaceKind.MinContent)
        {
            flexFraction = 0;
        }
        else
        {
            float fromTracks = 0;
            foreach (ref GridTrackState track in axisTracks)
            {
                if (track.IsFlexible)
                {
                    float factor = track.FlexFactor;
                    fromTracks = Math.Max(fromTracks, factor > 1 ? track.BaseSize / factor : track.BaseSize);
                }
            }

            float fromItems = 0;
            foreach (ref GridItemState item in items)
            {
                if (item.CrossesFlexibleTrack(sizer.Inline))
                {
                    float maxContent = sizer.MaxContent(ref item, axisTracks, otherTracks);
                    (int start, int end) = item.TrackRange(sizer.Inline);
                    fromItems = Math.Max(fromItems, FindSizeOfFr(axisTracks[start..end], maxContent));
                }
            }

            flexFraction = Math.Max(fromTracks, fromItems);

            // A grid smaller than the container's minimum or larger than its maximum redoes the fraction for that size; the
            // minimum wins.
            float hypothetical = 0;
            foreach (ref GridTrackState track in axisTracks)
            {
                hypothetical += track.IsFlexible ? Math.Max(track.BaseSize, track.FlexFactor * flexFraction) : track.BaseSize;
            }

            float min = Maybe.Or(axisMin, 0);
            float max = Maybe.Or(axisMax, float.PositiveInfinity);
            if (hypothetical < min)
            {
                flexFraction = FindSizeOfFr(axisTracks, min);
            }
            else if (hypothetical > max)
            {
                flexFraction = FindSizeOfFr(axisTracks, max);
            }
        }

        foreach (ref GridTrackState track in axisTracks)
        {
            if (track.IsFlexible)
            {
                track.BaseSize = Math.Max(track.BaseSize, track.FlexFactor * flexFraction);
            }
        }
    }

    // 11.7.1 Find the size of an fr (Taffy's find_size_of_fr): the leftover space after the inflexible tracks over the flex
    // factors' sum (at least 1), restarted while a flexible track would come out below its base size, treating it as
    // inflexible. Bounded by the number of tracks, as Taffy is, for non-finite inputs.
    private static float FindSizeOfFr(Span<GridTrackState> tracks, float spaceToFill)
    {
        if (spaceToFill == 0)
        {
            return 0;
        }

        float hypothetical = float.PositiveInfinity;
        for (int iteration = 0; iteration <= tracks.Length; iteration++)
        {
            float used = 0;
            float flexSum = 0;
            foreach (ref GridTrackState track in tracks)
            {
                if (track.IsFlexible && track.FlexFactor * hypothetical >= track.BaseSize)
                {
                    flexSum += track.FlexFactor;
                }
                else
                {
                    used += track.BaseSize;
                }
            }

            float previous = hypothetical;
            hypothetical = (spaceToFill - used) / Math.Max(flexSum, 1);
            bool valid = true;
            foreach (ref GridTrackState track in tracks)
            {
                if (track.IsFlexible && track.FlexFactor * hypothetical < track.BaseSize && track.FlexFactor * previous >= track.BaseSize)
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                break;
            }
        }

        return hypothetical;
    }

    // 11.8 Stretch the `auto` tracks (Taffy's stretch_auto_tracks): the remaining positive definite free space, equally; an
    // indefinite space with a definite minimum uses the minimum.
    private static void StretchAutoTracks(Span<GridTrackState> tracks, float axisMin, AvailableSpace expansion)
    {
        int autoCount = 0;
        foreach (ref GridTrackState track in tracks)
        {
            autoCount += track.Max.Kind == TrackKind.Auto ? 1 : 0;
        }

        if (autoCount == 0)
        {
            return;
        }

        float used = SumBaseSizes(tracks);
        float free =
            expansion.Kind == AvailableSpaceKind.Definite ? expansion.Value - used
            : float.IsNaN(axisMin) ? 0
            : axisMin - used;
        if (free > 0)
        {
            float each = free / autoCount;
            foreach (ref GridTrackState track in tracks)
            {
                if (track.Max.Kind == TrackKind.Auto)
                {
                    track.BaseSize += each;
                }
            }
        }
    }

    // Distributes space evenly, or by flex factor, to the affected tracks up to their limits, into their item-incurred increases
    // (Taffy's distribute_space_up_to_limits); returns what is left. The affected size is the base size, or for growth limits the
    // growth limit (the base size while it is infinite).
    private static float DistributeUpToLimits(
        float space,
        Span<GridTrackState> tracks,
        in Affected affected,
        bool byFlexFactor,
        bool growthLimits,
        TrackLimit limit
    )
    {
        for (int iteration = 0; iteration <= tracks.Length; iteration++)
        {
            if (space <= DistributionThreshold)
            {
                break;
            }

            float proportionSum = 0;
            float minIncreaseLimit = float.PositiveInfinity;
            foreach (ref GridTrackState track in tracks)
            {
                float size = AffectedSize(in track, growthLimits);
                float trackLimit = LimitOf(in track, limit);
                if (size + track.ItemIncurredIncrease < trackLimit && affected.Of(in track))
                {
                    float proportion = byFlexFactor ? track.FlexFactor : 1;
                    proportionSum += proportion;
                    minIncreaseLimit = Math.Min(minIncreaseLimit, (trackLimit - size - track.ItemIncurredIncrease) / proportion);
                }
            }

            if (proportionSum == 0)
            {
                break;
            }

            float increaseEach = Math.Min(minIncreaseLimit, space / proportionSum);
            foreach (ref GridTrackState track in tracks)
            {
                if (!affected.Of(in track))
                {
                    continue;
                }

                float increase = increaseEach * (byFlexFactor ? track.FlexFactor : 1);
                if (
                    increase > 0
                    && AffectedSize(in track, growthLimits) + track.ItemIncurredIncrease + increase
                        <= LimitOf(in track, limit) + DistributionThreshold
                )
                {
                    track.ItemIncurredIncrease += increase;
                    space -= increase;
                }
            }
        }

        return space;

        static float AffectedSize(in GridTrackState track, bool growthLimits) =>
            growthLimits && !float.IsPositiveInfinity(track.GrowthLimit) ? track.GrowthLimit : track.BaseSize;
    }

    private static bool AnyAffected(Span<GridTrackState> tracks, in Affected affected)
    {
        foreach (ref GridTrackState track in tracks)
        {
            if (affected.Of(in track))
            {
                return true;
            }
        }

        return false;
    }

    // The tracks a distribution affects: a filter; only the flexible ones for an item that crosses one; only the infinitely
    // growable ones (or those with an infinite growth limit) for growth limits up to their limits; and beyond the limits, only
    // those with an intrinsic maximum (1) or a max-content or fit-content() one (2).
    private readonly record struct Affected(TrackFilter Filter, float AxisInner, bool FlexOnly, byte BeyondLimits)
    {
        public bool Growable { get; init; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Of(in GridTrackState track) =>
            IsAffected(in track, Filter, AxisInner)
            && (!FlexOnly || track.IsFlexible)
            && (!Growable || track.InfinitelyGrowable || float.IsPositiveInfinity(track.GrowthLimit))
            && BeyondLimits switch
            {
                1 => track.Max.IsIntrinsic,
                2 => track.Max.IsMaxOrFitContent,
                _ => true,
            };
    }

    // Measures the items' contributions in the axis being sized, through the caches each item keeps for the run (Taffy's
    // IntrinsicSizeMeasurer). A contribution includes the item's margins.
    private readonly struct IntrinsicSizer(UiTree tree, bool inline, OptionalSize innerNodeSize, bool estimateFromMax)
    {
        public bool Inline => inline;

        public OptionalSize InnerNodeSize => innerNodeSize;

        public float MinContent(ref GridItemState item, Span<GridTrackState> axisTracks, Span<GridTrackState> otherTracks)
        {
            OptionalSize area = Area(ref item, axisTracks, otherTracks);
            OptionalSize available = With(area, inline, float.NaN);
            float contribution = Get(item.MinContentCache, inline);
            if (float.IsNaN(contribution))
            {
                contribution = MinContentContribution(tree, ref item, inline, area, available);
                item.MinContentCache = With(item.MinContentCache, inline, contribution);
            }

            return contribution + Get(MarginSums(tree, in item, available.Width), inline);
        }

        public float MaxContent(ref GridItemState item, Span<GridTrackState> axisTracks, Span<GridTrackState> otherTracks)
        {
            OptionalSize area = Area(ref item, axisTracks, otherTracks);
            OptionalSize available = With(area, inline, float.NaN);
            float contribution = Get(item.MaxContentCache, inline);
            if (float.IsNaN(contribution))
            {
                contribution = MaxContentContribution(tree, ref item, inline, area, available);
                item.MaxContentCache = With(item.MaxContentCache, inline, contribution);
            }

            return contribution + Get(MarginSums(tree, in item, available.Width), inline);
        }

        public float Minimum(ref GridItemState item, Span<GridTrackState> axisTracks, Span<GridTrackState> otherTracks)
        {
            OptionalSize area = Area(ref item, axisTracks, otherTracks);
            OptionalSize available = With(area, inline, float.NaN);
            float contribution = Get(item.MinimumCache, inline);
            if (float.IsNaN(contribution))
            {
                contribution = MinimumContribution(tree, ref item, inline, axisTracks, area, innerNodeSize);
                item.MinimumCache = With(item.MinimumCache, inline, contribution);
            }

            return contribution + Get(MarginSums(tree, in item, available.Width), inline);
        }

        private OptionalSize Area(ref GridItemState item, Span<GridTrackState> axisTracks, Span<GridTrackState> otherTracks)
        {
            if (!item.HasGridAreaSize)
            {
                item.GridAreaSize = GridAreaSize(ref item, inline, axisTracks, otherTracks, innerNodeSize, estimateFromMax);
                item.HasGridAreaSize = true;
            }

            return item.GridAreaSize;
        }
    }

    // The size of an item's grid area when it is known (Taffy's grid_area_size): in the axis being sized, the sum of the spanned
    // tracks when each is fixed (its minimum equals its maximum); in the other, the estimate of each spanned track and gutter.
    private static OptionalSize GridAreaSize(
        ref GridItemState item,
        bool inline,
        Span<GridTrackState> axisTracks,
        Span<GridTrackState> otherTracks,
        OptionalSize available,
        bool estimateFromMax
    )
    {
        (int start, int end) = item.TrackRange(inline);
        float axisBasis = Get(available, inline);
        float axisSize = 0;
        for (int i = start; i < end; i++)
        {
            float min = axisTracks[i].Min.DefiniteValue(axisBasis);
            float max = axisTracks[i].Max.DefiniteValue(axisBasis);
            if (float.IsNaN(min) || float.IsNaN(max) || min != max)
            {
                axisSize = float.NaN;
                break;
            }

            axisSize += axisTracks[i].BaseSize;
        }

        (start, end) = item.TrackRange(!inline);
        float otherBasis = Get(available, !inline);
        float otherSize = 0;
        for (int i = start; i < end; i++)
        {
            otherSize += TrackSizeEstimate(in otherTracks[i], otherBasis, estimateFromMax) + otherTracks[i].ContentAlignmentAdjustment;
        }

        return inline ? new OptionalSize(axisSize, otherSize) : new OptionalSize(otherSize, axisSize);
    }

    // The item's margins in each axis for its contributions, with the baseline shim (Taffy's
    // margins_axis_sums_with_baseline_shims): a horizontal percentage margin is 0, as the container's width may not be known.
    private static Size MarginSums(UiTree tree, in GridItemState item, float width)
    {
        EdgeInsets<LengthPercentageAuto> margin = StyleOf(tree, in item).Margin;
        return new Size(
            margin.Left.ResolveOrZero(0) + margin.Right.ResolveOrZero(0),
            margin.Top.ResolveOrZero(width) + item.BaselineShim + margin.Bottom.ResolveOrZero(width)
        );
    }

    // The item's min-content contribution without its margins (Taffy's min_content_contribution): its known size in the axis, else
    // its content measured under a min-content constraint where no space is known.
    private static float MinContentContribution(UiTree tree, ref GridItemState item, bool inline, OptionalSize area, OptionalSize available) =>
        ContentContribution(tree, ref item, inline, area, available, AvailableSpace.MinContent);

    private static float MaxContentContribution(UiTree tree, ref GridItemState item, bool inline, OptionalSize area, OptionalSize available) =>
        ContentContribution(tree, ref item, inline, area, available, AvailableSpace.MaxContent);

    private static float ContentContribution(
        UiTree tree,
        ref GridItemState item,
        bool inline,
        OptionalSize area,
        OptionalSize available,
        AvailableSpace unknown
    )
    {
        OptionalSize known = KnownDimensionsCached(tree, ref item, area);
        float knownSize = Get(known, inline);
        if (!float.IsNaN(knownSize))
        {
            return knownSize;
        }

        // The child sees its grid area as its containing block.
        OptionalSize space = AvailableMinusMargins(tree, in item, area, available);
        var measureSpace = new AvailableSize(
            float.IsNaN(space.Width) ? unknown : AvailableSpace.Definite(space.Width),
            float.IsNaN(space.Height) ? unknown : AvailableSpace.Definite(space.Height)
        );
        measureSpace = KeywordAdjustedSpace(tree, in item, area, measureSpace);
        return MeasureChild(tree, item.Node, known, area, measureSpace, SizingMode.InherentSize, inline);
    }

    private static OptionalSize KnownDimensionsCached(UiTree tree, ref GridItemState item, OptionalSize area)
    {
        if (!item.HasKnownDimensions || item.KnownDimensionsArea != area)
        {
            item.KnownDimensions = KnownDimensions(tree, in item, area);
            item.KnownDimensionsArea = area;
            item.HasKnownDimensions = true;
        }

        return item.KnownDimensions;
    }

    // The sizes passed to the child when its contributions are measured (Taffy's known_dimensions): its style's, else the area
    // less its margins where its alignment stretches it (`normal` stretches here, unlike at the final alignment), with the
    // aspect ratio and clamped by its limits.
    private static OptionalSize KnownDimensions(UiTree tree, in GridItemState item, OptionalSize area)
    {
        ref readonly LayoutStyle s = ref StyleOf(tree, in item);
        Size margins = MarginSums(tree, in item, area.Width);
        Size paddingBorder = PaddingBorder(in s, area.Width).Sum();
        float ratio = s.HasAspectRatio ? s.AspectRatio : float.NaN;
        OptionalSize inherent = WithAspectRatio(new OptionalSize(s.Width.Resolve(area.Width), s.Height.Resolve(area.Height)), ratio);
        OptionalSize min = WithAspectRatio(new OptionalSize(s.MinWidth.Resolve(area.Width), s.MinHeight.Resolve(area.Height)), ratio);
        min = new OptionalSize(
            Math.Max(Maybe.Or(min.Width, paddingBorder.Width), paddingBorder.Width),
            Math.Max(Maybe.Or(min.Height, paddingBorder.Height), paddingBorder.Height)
        );
        OptionalSize max = WithAspectRatio(new OptionalSize(s.MaxWidth.Resolve(area.Width), s.MaxHeight.Resolve(area.Height)), ratio);
        OptionalSize stretch = new(Maybe.Max(area.Width - margins.Width, 0), Maybe.Max(area.Height - margins.Height, 0));

        float width = inherent.Width;
        if (float.IsNaN(width))
        {
            if (s.Width.IsSizingKeyword)
            {
                width = StretchOf(s.Width, stretch.Width);
            }
            else if (!s.Margin.Left.IsAuto && !s.Margin.Right.IsAuto && IsStretchAligned(item.JustifySelf))
            {
                width = stretch.Width;
            }
        }

        OptionalSize size = WithAspectRatio(new OptionalSize(width, inherent.Height), ratio);
        float height = size.Height;
        if (float.IsNaN(height))
        {
            if (s.Height.IsSizingKeyword)
            {
                height = StretchOf(s.Height, stretch.Height);
            }
            else if (!s.Margin.Top.IsAuto && !s.Margin.Bottom.IsAuto && IsStretchAligned(item.AlignSelf))
            {
                height = stretch.Height;
            }
        }

        size = WithAspectRatio(size with { Height = height }, ratio);
        return size.Clamp(min, max);

        static bool IsStretchAligned(AlignItems alignment) => alignment.Keyword is AlignItemsKeyword.Stretch or AlignItemsKeyword.Normal;
    }

    // The space given to the item, less its margins and its baseline shim, as when it is laid out in its place (Taffy's
    // available_space_minus_margins): margins against the area's width, auto margins as 0.
    private static OptionalSize AvailableMinusMargins(UiTree tree, in GridItemState item, OptionalSize area, OptionalSize available)
    {
        if (float.IsNaN(available.Width) && float.IsNaN(available.Height))
        {
            return available;
        }

        Edges margin = EdgeInsets.ResolveOrZero(StyleOf(tree, in item).Margin, area.Width);
        return new OptionalSize(
            Maybe.Max(available.Width - margin.Horizontal, 0),
            Maybe.Max(available.Height - margin.Vertical - item.BaselineShim, 0)
        );
    }

    // The space to measure an axis whose size is a content keyword under (Taffy's keyword_adjusted_available_space).
    private static AvailableSize KeywordAdjustedSpace(UiTree tree, in GridItemState item, OptionalSize area, AvailableSize space)
    {
        ref readonly LayoutStyle s = ref StyleOf(tree, in item);
        if (!s.Width.IsSizingKeyword && !s.Height.IsSizingKeyword)
        {
            return space;
        }

        Size margins = MarginSums(tree, in item, area.Width);
        if (MeasureSpaceOf(s.Width, Maybe.Max(area.Width - margins.Width, 0)) is { } width)
        {
            space = space with { Width = width };
        }

        if (MeasureSpaceOf(s.Height, Maybe.Max(area.Height - margins.Height, 0)) is { } height)
        {
            space = space with { Height = height };
        }

        return space;
    }

    // The item's minimum contribution without its margins (Taffy's minimum_contribution, CSS Grid §6.6): its preferred size
    // clamped by its limits when it has one; its min-content contribution for a content keyword; its minimum size; zero for a
    // scroll container; else its automatic minimum, the content-based one when it spans an `auto` minimum track and no flexible
    // track among several, limited by the fixed maximums of the tracks it spans (no smaller than its padding and border).
    private static float MinimumContribution(
        UiTree tree,
        ref GridItemState item,
        bool inline,
        Span<GridTrackState> axisTracks,
        OptionalSize area,
        OptionalSize innerNodeSize
    )
    {
        ref readonly LayoutStyle s = ref StyleOf(tree, in item);
        Size paddingBorder = PaddingBorder(in s, area.Width).Sum();
        float ratio = s.HasAspectRatio ? s.AspectRatio : float.NaN;
        float preferred = Get(WithAspectRatio(new OptionalSize(s.Width.Resolve(area.Width), s.Height.Resolve(area.Height)), ratio), inline);
        Length minStyle = inline ? s.MinWidth : s.MinHeight;
        Length maxStyle = inline ? s.MaxWidth : s.MaxHeight;
        if (!float.IsNaN(preferred))
        {
            if (minStyle.IsAuto && maxStyle.IsAuto)
            {
                return preferred;
            }

            float basis = Get(area, inline);
            return Maybe.Clamp(preferred, minStyle.Resolve(basis), maxStyle.Resolve(basis));
        }

        Length sizeStyle = inline ? s.Width : s.Height;
        if (sizeStyle.IsSizingKeyword && sizeStyle.Kind != LengthKind.Stretch)
        {
            float cached = Get(item.MinContentCache, inline);
            if (float.IsNaN(cached))
            {
                cached = MinContentContribution(tree, ref item, inline, area, area);
                item.MinContentCache = With(item.MinContentCache, inline, cached);
            }

            return cached;
        }

        float minSize = Get(
            WithAspectRatio(new OptionalSize(s.MinWidth.Resolve(Maybe.Or(area.Width, 0)), s.MinHeight.Resolve(Maybe.Or(area.Height, 0))), ratio),
            inline
        );
        if (!float.IsNaN(minSize))
        {
            return minSize;
        }

        if (item.IsScrollContainer(inline))
        {
            return 0;
        }

        // The automatic minimum size (CSS Grid §6.6).
        (int start, int end) = item.TrackRange(inline);
        Span<GridTrackState> spanned = axisTracks[start..end];
        float axisInner = Get(innerNodeSize, inline);
        bool spansAutoMin = false;
        bool spansFlexible = false;
        foreach (ref GridTrackState track in spanned)
        {
            spansAutoMin |= track.Min.BehavesAsAuto(axisInner);
            spansFlexible |= track.IsFlexible;
        }

        if (!spansAutoMin || (spanned.Length > 1 && spansFlexible))
        {
            return 0;
        }

        float contribution = Get(item.MinContentCache, inline);
        if (float.IsNaN(contribution))
        {
            contribution = MinContentContribution(tree, ref item, inline, area, area);
            item.MinContentCache = With(item.MinContentCache, inline, contribution);
        }

        // Clamped by the fixed maximums of the spanned tracks (not by fit-content() arguments), as a stretch fit into them that
        // does not go below the padding and border.
        float limit = SpannedTrackLimit(spanned, axisInner, fitContent: false);
        return Maybe.Min(contribution, Maybe.Max(limit, Get(new OptionalSize(paddingBorder.Width, paddingBorder.Height), inline)));
    }
}
