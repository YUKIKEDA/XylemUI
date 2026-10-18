using System.Runtime.CompilerServices;

namespace XylemUI;

internal static partial class LayoutEngine
{
    // The items and lines of the flex runs on this thread, each kind as one stack: a container's run takes its spans at the
    // top, and the runs of its items, nested inside, take theirs above. With ArrayPool<T>.Shared, a Return in the steady state
    // allocated now and then on the runners (4,048 bytes in one call, Issue #92); a nested container's array goes past the
    // one array a size that the pool keeps on a thread.
    [ThreadStatic]
    private static ScratchStack<FlexItem>? _itemScratch;

    [ThreadStatic]
    private static ScratchStack<FlexLine>? _lineScratch;

    // CSS Flexbox §9 (Taffy's compute_flexbox_layout, design §3.2). The steps keep the numbers of §9. A layout run also returns
    // the item whose first baseline gives the container's (§8.5), none when it has no item; the baseline itself is read only
    // when a parent asks for it (ReadBaseline).
    private static Size ComputeFlex(
        UiTree tree,
        in LayoutStyle style,
        FlexProps props,
        NodeHandle firstChild,
        in LayoutInput input,
        bool commit,
        out NodeHandle baselineItem
    )
    {
        baselineItem = default;
        OptionalSize parent = input.ParentSize;
        Size inset = PaddingBorder(in style, parent.Width).Sum();
        OptionalSize min = MinSize(in style, parent);
        OptionalSize max = MaxSize(in style, parent);
        OptionalSize clampedStyleSize = input.SizingMode == SizingMode.InherentSize ? StyleSize(in style, parent).Clamp(min, max) : OptionalSize.None;
        OptionalSize known = input.KnownDimensions.Or(MinMaxDefinite(min, max).Or(clampedStyleSize).AtLeast(inset));

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

        // Only the sizes the parent passed can be indefinite; those from the node's own style are definite.
        bool widthIsDefinite = input.KnownWidthIsDefinite || float.IsNaN(input.KnownDimensions.Width);
        bool heightIsDefinite = input.KnownHeightIsDefinite || float.IsNaN(input.KnownDimensions.Height);

        FlexConstants constants = FlexConstantsOf(in style, props, known, widthIsDefinite, heightIsDefinite, input, min, max);
        constants.ContainerStyleVersion = tree.Nodes.GetRef(tree.Nodes.GetRef(firstChild).Parent).Versions.LayoutStyle;
        int count = 0;
        for (NodeHandle c = firstChild; !c.IsNone; c = tree.Nodes.GetRef(c).NextSibling)
        {
            count++;
        }

        ScratchStack<FlexItem> itemScratch = _itemScratch ??= new ScratchStack<FlexItem>();
        ScratchStack<FlexLine> lineScratch = _lineScratch ??= new ScratchStack<FlexLine>();
        int itemTop = itemScratch.Top;
        int lineTop = lineScratch.Top;
        try
        {
            Span<FlexItem> items = itemScratch.Push(count);
            Span<FlexLine> lineSpace = lineScratch.Push(Math.Max(count, 1));
            bool row = constants.Row;

            // 9.1.1 Generate the flex items.
            GenerateFlexItems(tree, firstChild, ref constants, items);

            // 9.2.2 The available main and cross space for the items.
            AvailableSize available = FlexAvailableSpace(known, input.Available, in constants);

            // 9.2.3 The flex base size and hypothetical main size of each item.
            DetermineFlexBaseSizes(tree, in constants, available, items, commit);

            // 9.3.5 Collect the items into lines.
            Span<FlexLine> lines = lineSpace[..CollectFlexLines(in constants, available, items, lineSpace)];

            // 9.2.4 The container's main size, unless it is known; then the main gap against it.
            float knownInnerMain = constants.NodeInnerSize.Main(row);
            if (!float.IsNaN(knownInnerMain))
            {
                constants.InnerContainerMain = knownInnerMain;
                constants.ContainerMain = knownInnerMain + constants.Inset.MainSum(row);
            }
            else
            {
                DetermineContainerMainSize(tree, available, items, lines, ref constants);
                constants.NodeInnerSize = constants.NodeInnerSize.WithMain(row, constants.InnerContainerMain);
                constants.GapMain = (row ? props.ColumnGap : props.RowGap).ResolveOrZero(constants.InnerContainerMain);
            }

            if (!commit && input.Axis == (row ? RequestedAxis.Horizontal : RequestedAxis.Vertical))
            {
                return row ? new Size(constants.ContainerMain, 0) : new Size(0, constants.ContainerMain);
            }

            // 9.3.6 Resolve the flexible lengths of each line.
            foreach (ref FlexLine line in lines)
            {
                ResolveFlexibleLengths(items.Slice(line.Start, line.Count), in constants);
            }

            // 9.4.7 The hypothetical cross size of each item, then the baselines that baseline alignment needs.
            foreach (ref FlexLine line in lines)
            {
                DetermineHypotheticalCrossSizes(tree, items.Slice(line.Start, line.Count), in constants, known, available);
            }

            if (constants.AnyBaseline)
            {
                CalculateChildrenBaselines(tree, items, lines, in constants, known, available);
            }

            // 9.4.8 The cross size of each line, 9.4.9 `align-content: stretch`, and 9.4.11 the used cross size of each item.
            CalculateLineCrossSizes(items, lines, known, in constants);
            StretchLines(lines, known, in constants);
            foreach (ref FlexLine line in lines)
            {
                DetermineUsedCrossSizes(items.Slice(line.Start, line.Count), line.CrossSize, in constants);
            }

            // 9.5.12 Distribute the remaining free space, and 9.6.13-14 align the items across their line.
            // With the initial values (`flex-start` along a line that is not reversed, no gap, every item stretched, no
            // automatic margin) every offset stays 0, so the steps are skipped.
            bool mainAtStart =
                constants.JustifyContent.Keyword is AlignContentKeyword.FlexStart or AlignContentKeyword.Start
                && !constants.Reverse
                && constants.GapMain == 0;
            bool crossAtStart = constants.AllStretched && !constants.WrapReverse;
            foreach (ref FlexLine line in lines)
            {
                if (!mainAtStart || constants.AnyAutoMargin)
                {
                    DistributeRemainingFreeSpace(items.Slice(line.Start, line.Count), in constants);
                }

                if (!crossAtStart || constants.AnyAutoMargin)
                {
                    AlignItemsAcrossTheLine(items.Slice(line.Start, line.Count), line.CrossSize, in constants);
                }
            }

            // 9.6.15 The container's cross size.
            float totalLineCross = 0;
            foreach (ref FlexLine line in lines)
            {
                totalLineCross += line.CrossSize;
            }

            float insetCross = constants.Inset.CrossSum(row);
            float outerCross = Math.Max(
                Maybe.Clamp(
                    Maybe.Or(known.Cross(row), totalLineCross + GapSum(constants.GapCross, lines.Length) + insetCross),
                    min.Cross(row),
                    max.Cross(row)
                ),
                insetCross
            );
            float innerCross = Math.Max(outerCross - insetCross, 0);
            Size containerSize = row ? new Size(constants.ContainerMain, outerCross) : new Size(outerCross, constants.ContainerMain);
            if (!commit)
            {
                return containerSize;
            }

            // 9.6.16 Align the lines, then the final layout of each item.
            AlignLines(lines, innerCross, totalLineCross, in constants);
            FinalLayoutPass(tree, items, lines, containerSize, in constants);
            baselineItem = BaselineItem(items, lines, in constants);
            return containerSize;
        }
        finally
        {
            itemScratch.PopTo(itemTop);
            lineScratch.PopTo(lineTop);
        }
    }

    private static FlexConstants FlexConstantsOf(
        in LayoutStyle style,
        FlexProps props,
        OptionalSize known,
        bool widthIsDefinite,
        bool heightIsDefinite,
        in LayoutInput input,
        OptionalSize min,
        OptionalSize max
    )
    {
        bool row = props.Direction is FlexDirection.Row or FlexDirection.RowReverse;
        Edges inset = PaddingBorder(in style, input.ParentSize.Width);
        bool mainIsDefinite = row ? widthIsDefinite : heightIsDefinite;
        bool crossIsDefinite = row ? heightIsDefinite : widthIsDefinite;
        bool hasDefiniteCross = crossIsDefinite && !float.IsNaN(known.Cross(row));
        OptionalSize inner = known.Minus(inset.Sum());

        // A gap resolves against the content box, 0 where it is not known; the main gap again once the main size is known.
        float columnGap = props.ColumnGap.ResolveOrZero(Maybe.Or(inner.Width, 0));
        float rowGap = props.RowGap.ResolveOrZero(Maybe.Or(inner.Height, 0));

        // `normal` behaves as `stretch` across the line and for the lines, and as `flex-start` along the main axis.
        AlignItems alignItems =
            props.AlignItems.Keyword == AlignItemsKeyword.Normal ? props.AlignItems with { Keyword = AlignItemsKeyword.Stretch } : props.AlignItems;
        AlignContent alignContent =
            props.AlignContent.Keyword == AlignContentKeyword.Normal
                ? props.AlignContent with
                {
                    Keyword = AlignContentKeyword.Stretch,
                }
                : props.AlignContent;
        AlignContent justifyContent =
            props.JustifyContent.Keyword == AlignContentKeyword.Normal
                ? props.JustifyContent with
                {
                    Keyword = AlignContentKeyword.FlexStart,
                }
                : props.JustifyContent;
        return new FlexConstants
        {
            Row = row,
            Reverse = props.Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse,
            Wrap = props.Wrap != FlexWrap.NoWrap,
            WrapReverse = props.Wrap == FlexWrap.WrapReverse,
            Min = min,
            Max = max,
            Inset = inset,
            NodeOuterSize = known,
            NodeInnerSize = inner,
            KnownMainSizeIsDefinite = mainIsDefinite,
            HasDefiniteMainSize = mainIsDefinite && !float.IsNaN(known.Main(row)),
            HasDefiniteCrossSize = hasDefiniteCross,
            CrossAvailableSpaceIsDefinite = hasDefiniteCross || input.Available.Cross(row).IsDefinite,
            GapMain = row ? columnGap : rowGap,
            GapCross = row ? rowGap : columnGap,
            AlignItems = alignItems,
            AlignContent = alignContent,
            JustifyContent = justifyContent,
            AllStretched = true,
        };
    }

    // Also notes whether any item aligns by baseline or has an automatic margin, so that the steps for them run only then.
    private static void GenerateFlexItems(UiTree tree, NodeHandle firstChild, ref FlexConstants constants, Span<FlexItem> items)
    {
        bool row = constants.Row;

        // Percentages of the items resolve against the container's content box only where it is definite.
        OptionalSize percentBasis = constants.KnownMainSizeIsDefinite ? constants.NodeInnerSize : constants.NodeInnerSize.WithMain(row, float.NaN);
        float innerWidth = constants.NodeInnerSize.Width;
        int i = 0;
        for (NodeHandle child = firstChild; !child.IsNone; i++)
        {
            ref UiNode c = ref tree.Nodes.GetRef(child);
            ref readonly LayoutStyle s = ref c.Layout.Style;

            // Field by field into the pooled item: the later steps write every other field before they read it.
            ref FlexItem item = ref items[i];
            item.Node = child;
            if (c.Layout.HasBox)
            {
                ref readonly ResolvedBox box = ref c.Layout.Box;
                item.Size = box.Size;
                item.Min = box.Min;
                item.Max = box.Max;
                item.Margin = box.Margin;
                item.PaddingBorder = box.PaddingBorder;
            }
            else
            {
                item.Size = StyleSize(in s, percentBasis);
                item.Min = MinSize(in s, percentBasis);
                item.Max = MaxSize(in s, percentBasis);
                item.Margin = EdgeInsets.ResolveOrZero(s.Margin, innerWidth);
                item.PaddingBorder = PaddingBorder(in s, innerWidth);
            }

            item.MainStyle = row ? s.Width : s.Height;
            item.CrossStyle = row ? s.Height : s.Width;
            item.FlexBasisStyle = s.FlexBasis;
            item.AutoLeft = s.Margin.Left.IsAuto;
            item.AutoTop = s.Margin.Top.IsAuto;
            item.AutoRight = s.Margin.Right.IsAuto;
            item.AutoBottom = s.Margin.Bottom.IsAuto;
            item.FlexGrow = s.FlexGrow;
            item.FlexShrink = s.FlexShrink;
            AlignItems alignSelf = s.AlignSelf ?? constants.AlignItems;
            item.AlignSelf = alignSelf.Keyword == AlignItemsKeyword.Normal ? alignSelf with { Keyword = AlignItemsKeyword.Stretch } : alignSelf;
            constants.AnyBaseline |= item.AlignSelf.Keyword == AlignItemsKeyword.Baseline;
            constants.AnyAutoMargin |= item.AutoLeft | item.AutoTop | item.AutoRight | item.AutoBottom;
            item.Stretched = !item.HasAutoCrossMargin(row) && item.CrossStyle.IsAuto && item.AlignSelf.Keyword == AlignItemsKeyword.Stretch;
            constants.AllStretched &= item.Stretched;

            // A stretched item's maximum resolves against the content box even where it is indefinite (Chrome and Firefox,
            // as Taffy); only a percentage differs from the maximum above.
            LengthPercentageAuto maxCross = row ? s.MaxHeight : s.MaxWidth;
            item.StretchMaxCross = maxCross.IsPercent ? maxCross.Resolve(constants.NodeInnerSize.Cross(row)) : item.Max.Cross(row);
            item.FlexBasisIsDefinite = false;
            item.Baseline = 0;
            item.OffsetMain = 0;
            item.OffsetCross = 0;
            child = c.NextSibling;
        }
    }

    // 9.2.2: the container's content box where it is known, otherwise the space the parent gave less the padding and border.
    private static AvailableSize FlexAvailableSpace(OptionalSize known, AvailableSize outer, in FlexConstants constants)
    {
        Size inset = constants.Inset.Sum();
        return new AvailableSize(InnerSpace(known.Width, outer.Width, inset.Width), InnerSpace(known.Height, outer.Height, inset.Height));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static AvailableSpace InnerSpace(float known, AvailableSpace outer, float inset) =>
        float.IsNaN(known) ? outer.Minus(inset).AtLeast(0) : AvailableSpace.Definite(Math.Max(0, known - inset));

    // A clean item whose step read the same container values as last time takes its results from FlexBaseMemo (row L4).
    private static void DetermineFlexBaseSizes(UiTree tree, in FlexConstants constants, AvailableSize available, Span<FlexItem> items, bool commit)
    {
        bool row = constants.Row;
        float crossParent = constants.NodeInnerSize.Cross(row);
        OptionalSize childParentSize = OptionalSize.None.WithCross(row, crossParent);
        float percentMain = constants.KnownMainSizeIsDefinite ? constants.NodeInnerSize.Main(row) : float.NaN;
        float innerWidth = constants.NodeInnerSize.Width;
        AvailableSpace crossSpace = available.Cross(row);
        bool minContentMain = available.Main(row).Kind == AvailableSpaceKind.MinContent;

        foreach (ref FlexItem item in items)
        {
            if (TryReuseBaseSize(tree, ref item, in constants, commit, innerWidth, crossParent, percentMain, crossSpace, minContentMain))
            {
                continue;
            }

            bool readContent = false;
            float minCross = item.Min.Cross(row);
            float maxCross = item.Max.Cross(row);
            AvailableSpace childCross = crossSpace.Kind switch
            {
                AvailableSpaceKind.Definite => AvailableSpace.Definite(
                    Maybe.Clamp(Math.Max(0, Maybe.Or(crossParent, crossSpace.Value) - item.Margin.CrossSum(row)), minCross, maxCross)
                ),
                AvailableSpaceKind.MinContent => float.IsNaN(minCross) ? AvailableSpace.MinContent : AvailableSpace.Definite(minCross),
                _ => float.IsNaN(maxCross) ? AvailableSpace.MaxContent : AvailableSpace.Definite(maxCross),
            };

            // A stretched item with an automatic cross size is measured at the cross space (CSS Flexbox §9.8: definite when the
            // container's cross size is).
            OptionalSize childKnown = item.Size.WithMain(row, float.NaN);
            childKnown = childKnown.WithCross(row, Maybe.Clamp(childKnown.Cross(row), minCross, maxCross));
            if (item.AlignSelf.Keyword == AlignItemsKeyword.Stretch && !item.HasAutoCrossMargin(row) && float.IsNaN(childKnown.Cross(row)))
            {
                childKnown = childKnown.WithCross(row, childCross.ToOption());
            }

            float flexBasis = item.FlexBasisStyle.Resolve(percentMain);
            float mainSize = item.Size.Main(row);
            float mainStretch = Maybe.Max(percentMain - item.Margin.MainSum(row), 0);
            item.FlexBasis = float.NaN;
            AvailableSpace? keywordSpace = null;
            if (item.FlexBasisStyle.Kind is LengthKind.MinContent or LengthKind.MaxContent or LengthKind.FitContent)
            {
                keywordSpace = MeasureSpaceOf(item.FlexBasisStyle, mainStretch);
            }
            else
            {
                float definite = Maybe.Or(flexBasis, item.FlexBasisStyle.IsAuto ? mainSize : float.NaN);
                if (!float.IsNaN(definite))
                {
                    item.FlexBasisIsDefinite = true;
                    item.FlexBasis = definite;
                }
                else if (item.FlexBasisStyle.IsAuto)
                {
                    keywordSpace = MeasureSpaceOf(item.MainStyle, mainStretch);
                }
            }

            if (float.IsNaN(item.FlexBasis))
            {
                readContent = true;

                // 9.2.3 E: the content's size, treating `content` as max-content (min-content under a min-content constraint).
                AvailableSpace mainSpace =
                    keywordSpace
                    ?? (available.Main(row).Kind == AvailableSpaceKind.MinContent ? AvailableSpace.MinContent : AvailableSpace.MaxContent);
                item.FlexBasis = MeasureChild(
                    tree,
                    item.Node,
                    childKnown,
                    childParentSize,
                    AvailableSize.FromAxes(row, mainSpace, childCross),
                    SizingMode.ContentSize,
                    horizontal: row
                );
            }

            // The basis is never smaller than the padding and border (Chrome and Firefox; Taffy's note on the spec).
            float paddingBorderMain = item.PaddingBorder.MainSum(row);
            item.FlexBasis = Math.Max(item.FlexBasis, paddingBorderMain);
            item.InnerFlexBasis = item.FlexBasis - paddingBorderMain;

            // CSS Flexbox §4.5: an automatic minimum is the content's min-content size, no larger than the specified size.
            float styleMinMain = item.Min.Main(row);
            if (float.IsNaN(styleMinMain))
            {
                readContent = true;
                float minContent = MeasureChild(
                    tree,
                    item.Node,
                    childKnown,
                    childParentSize,
                    AvailableSize.FromAxes(row, AvailableSpace.MinContent, childCross),
                    SizingMode.ContentSize,
                    horizontal: row
                );
                styleMinMain = Math.Max(Maybe.Min(Maybe.Min(minContent, mainSize), item.Max.Main(row)), paddingBorderMain);
            }

            item.ResolvedMinMain = styleMinMain;
            float hypotheticalInner = Maybe.Clamp(item.FlexBasis, Math.Max(styleMinMain, paddingBorderMain), item.Max.Main(row));
            item.HypotheticalInnerMain = hypotheticalInner;
            item.HypotheticalOuterMain = hypotheticalInner + item.Margin.MainSum(row);

            ref UiNode child = ref tree.Nodes.GetRef(item.Node);
            ref MeasureCache cache = ref tree.MeasuresOf(item.Node);
            (commit ? ref cache.LayoutBase : ref cache.MeasureBase) = new FlexBaseMemo
            {
                Valid = true,
                Row = row,
                ReadContent = readContent,
                MinContentMain = minContentMain,
                CrossKind = crossSpace.Kind,
                CrossValue = crossSpace.Value,
                InnerWidth = child.Layout.HasBox ? float.NaN : innerWidth,
                CrossParent = crossParent,
                PercentMain = ReadsContainerMain(in item, child.Layout.HasBox) ? percentMain : float.NaN,
                ContainerStyle = constants.ContainerStyleVersion,
                FlexBasis = item.FlexBasis,
                FlexBasisIsDefinite = item.FlexBasisIsDefinite,
                ResolvedMinMain = item.ResolvedMinMain,
            };
        }
    }

    // Whether an item's base-size step reads the container's main size: through a percentage in its style, or through a basis
    // or a main size that is a content keyword, measured against the space the item may stretch into. Otherwise a new main
    // size of the container (a width change of a row) leaves the step's results as they were. The width an item's percentage
    // margins and padding resolve against is compared apart: a row's width can be known but not definite.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ReadsContainerMain(in FlexItem item, bool hasBox) =>
        !hasBox
        || item.FlexBasisStyle.Kind is LengthKind.Percent or LengthKind.MinContent or LengthKind.MaxContent or LengthKind.FitContent
        || item.MainStyle.Kind is LengthKind.MinContent or LengthKind.MaxContent or LengthKind.FitContent;

    // The base-size step's results of an item from its container's last run (dropped when the item is marked dirty), where the
    // step would read the same values of the container: its style, the content-box sizes the item reads, and the spaces.
    // Content the step measured last time is marked as read again, as measuring marks it.
    private static bool TryReuseBaseSize(
        UiTree tree,
        ref FlexItem item,
        in FlexConstants constants,
        bool commit,
        float innerWidth,
        float crossParent,
        float percentMain,
        AvailableSpace crossSpace,
        bool minContentMain
    )
    {
        ref UiNode child = ref tree.Nodes.GetRef(item.Node);
        ref MeasureCache cache = ref tree.MeasuresOf(item.Node);
        ref readonly FlexBaseMemo memo = ref commit ? ref cache.LayoutBase : ref cache.MeasureBase;
        if (
            !memo.Valid
            || memo.ContainerStyle != constants.ContainerStyleVersion
            || memo.Row != constants.Row
            || memo.MinContentMain != minContentMain
            || memo.CrossKind != crossSpace.Kind
            || !Maybe.Same(memo.CrossValue, crossSpace.Value)
            || !Maybe.Same(memo.InnerWidth, child.Layout.HasBox ? float.NaN : innerWidth)
            || !Maybe.Same(memo.CrossParent, crossParent)
            || !Maybe.Same(memo.PercentMain, ReadsContainerMain(in item, child.Layout.HasBox) ? percentMain : float.NaN)
        )
        {
            return false;
        }

        if (memo.ReadContent)
        {
            child.Layout.ContentReadGeneration = tree.Nodes.GetRef(child.Parent).Layout.CacheGeneration;
        }

        bool row = constants.Row;
        item.FlexBasis = memo.FlexBasis;
        item.FlexBasisIsDefinite = memo.FlexBasisIsDefinite;
        item.InnerFlexBasis = memo.FlexBasis - item.PaddingBorder.MainSum(row);
        item.ResolvedMinMain = memo.ResolvedMinMain;
        float hypotheticalInner = Maybe.Clamp(memo.FlexBasis, Math.Max(memo.ResolvedMinMain, item.PaddingBorder.MainSum(row)), item.Max.Main(row));
        item.HypotheticalInnerMain = hypotheticalInner;
        item.HypotheticalOuterMain = hypotheticalInner + item.Margin.MainSum(row);
        return true;
    }

    // 9.3.5: one line unless the container wraps against a definite main size; then as many items on each line as fit, at
    // least one. Under a min-content constraint every item takes its own line; under a max-content constraint, one line.
    private static int CollectFlexLines(in FlexConstants constants, AvailableSize available, Span<FlexItem> items, Span<FlexLine> lines)
    {
        bool row = constants.Row;
        if (!constants.Wrap || !constants.KnownMainSizeIsDefinite)
        {
            lines[0] = new FlexLine { Start = 0, Count = items.Length };
            return 1;
        }

        AvailableSpace mainSpace;
        float maxMain = constants.Max.Main(row);
        AvailableSpace availableMain = available.Main(row);
        if (!float.IsNaN(maxMain))
        {
            // Without a definite main size the container is at most its maximum, so the maximum is the limit items wrap at.
            float space = Maybe.Or(availableMain.ToOption(), maxMain);
            space = constants.HasDefiniteMainSize ? space : Math.Min(space, maxMain);
            mainSpace = AvailableSpace.Definite(Maybe.Max(space, constants.Min.Main(row)));
        }
        else if (!row && !constants.HasDefiniteMainSize && availableMain.IsDefinite)
        {
            // A column's automatic main size is its content's, so a definite space from an ancestor does not make it wrap.
            mainSpace = AvailableSpace.MaxContent;
        }
        else
        {
            mainSpace = availableMain;
        }

        if (mainSpace.Kind == AvailableSpaceKind.MaxContent)
        {
            lines[0] = new FlexLine { Start = 0, Count = items.Length };
            return 1;
        }

        if (mainSpace.Kind == AvailableSpaceKind.MinContent)
        {
            if (items.Length == 0)
            {
                return EmptyLine(lines);
            }

            for (int i = 0; i < items.Length; i++)
            {
                lines[i] = new FlexLine { Start = i, Count = 1 };
            }

            return items.Length;
        }

        int count = 0;
        int start = 0;
        while (start < items.Length)
        {
            float length = 0;
            int end = start;
            for (; end < items.Length; end++)
            {
                length += items[end].HypotheticalOuterMain + (end == start ? 0 : constants.GapMain);
                if (length > mainSpace.Value && end != start)
                {
                    break;
                }
            }

            lines[count++] = new FlexLine { Start = start, Count = end - start };
            start = end;
        }

        return count == 0 ? EmptyLine(lines) : count;
    }

    private static int EmptyLine(Span<FlexLine> lines)
    {
        lines[0] = new FlexLine { Start = 0, Count = 0 };
        return 1;
    }

    // 9.2.4 for a container whose main size is not known: under a definite space the longest line of the items' bases; under
    // a content constraint the items' content contributions (CSS Flexbox §9.9.1, as Chrome computes it).
    private static void DetermineContainerMainSize(
        UiTree tree,
        AvailableSize available,
        Span<FlexItem> items,
        Span<FlexLine> lines,
        ref FlexConstants constants
    )
    {
        bool row = constants.Row;
        float insetMain = constants.Inset.MainSum(row);
        float outerMain = constants.NodeOuterSize.Main(row);
        if (float.IsNaN(outerMain))
        {
            AvailableSpace mainSpace = available.Main(row);
            float longest = 0;
            if (mainSpace.IsDefinite || (mainSpace.Kind == AvailableSpaceKind.MinContent && constants.Wrap))
            {
                foreach (ref FlexLine line in lines)
                {
                    float sum = GapSum(constants.GapMain, line.Count);
                    foreach (ref FlexItem item in items.Slice(line.Start, line.Count))
                    {
                        sum += Math.Max(Maybe.Max(item.FlexBasis, item.Min.Main(row)) + item.Margin.MainSum(row), item.PaddingBorder.MainSum(row));
                    }

                    longest = Math.Max(longest, sum);
                }

                outerMain = longest + insetMain;

                // Several lines fill the space they wrapped at.
                if (mainSpace.IsDefinite && lines.Length > 1)
                {
                    outerMain = Math.Max(outerMain, mainSpace.Value);
                }
            }
            else
            {
                // A wrapping row is at least as large as its largest item (Chrome; matters with negative margins).
                bool floorByLargest = constants.Wrap && row;
                foreach (ref FlexLine line in lines)
                {
                    float sum = 0;
                    float largest = 0;
                    foreach (ref FlexItem item in items.Slice(line.Start, line.Count))
                    {
                        float size = ContentContribution(tree, ref item, available, in constants);
                        sum += size;
                        largest = Math.Max(largest, size);
                    }

                    sum += GapSum(constants.GapMain, line.Count);
                    longest = Math.Max(longest, floorByLargest ? Math.Max(largest, sum) : sum);
                }

                outerMain = longest + insetMain;
            }
        }

        outerMain = Math.Max(Maybe.Clamp(outerMain, constants.Min.Main(row), constants.Max.Main(row)), insetMain);
        constants.ContainerMain = outerMain;
        constants.InnerContainerMain = Math.Max(outerMain - insetMain, 0);
    }

    // The size an item adds to its container's min- or max-content size: its content contribution clamped by its sizes and its
    // flex factors, plus its margins (Taffy's determine_container_main_size).
    private static float ContentContribution(UiTree tree, ref FlexItem item, AvailableSize available, in FlexConstants constants)
    {
        bool row = constants.Row;
        float styleMin = item.Min.Main(row);
        float stylePreferred = item.Size.Main(row);
        float styleMax = item.Max.Main(row);
        float marginSum = item.Margin.MainSum(row);

        // For rows the larger of the basis and the preferred size clamps; for columns the basis alone (Chrome and Firefox).
        float clampingBasis = row ? Maybe.Max(item.FlexBasis, stylePreferred) : item.FlexBasis;
        float basisMin = item.FlexShrink == 0 ? clampingBasis : float.NaN;
        float basisMax = item.FlexGrow == 0 ? clampingBasis : float.NaN;
        float minMain = Math.Max(Maybe.Or(Maybe.Or(Maybe.Max(styleMin, basisMin), basisMin), item.ResolvedMinMain), item.ResolvedMinMain);
        float maxMain = Maybe.Or(Maybe.Or(Maybe.Min(styleMax, basisMax), basisMax), float.PositiveInfinity);

        float contribution;
        if (!float.IsNaN(stylePreferred) && (maxMain <= minMain || maxMain <= stylePreferred))
        {
            contribution = Math.Max(Math.Min(stylePreferred, maxMain), minMain) + marginSum;
        }
        else if (maxMain <= minMain)
        {
            contribution = minMain + marginSum;
        }
        else if (!float.IsNaN(stylePreferred))
        {
            float inner = Math.Max(stylePreferred, item.PaddingBorder.MainSum(row));
            contribution = row
                ? Maybe.Clamp(inner + marginSum, styleMin, styleMax)
                : Maybe.Clamp(Math.Max(inner, item.FlexBasis) + marginSum, styleMin, styleMax);
        }
        else
        {
            float crossParent = constants.NodeInnerSize.Cross(row);
            AvailableSpace crossSpace = available.Cross(row);
            AvailableSpace childCross = (
                crossSpace.IsDefinite
                    ? AvailableSpace.Definite(Math.Max(0, Maybe.Or(crossParent, crossSpace.Value) - item.Margin.CrossSum(row)))
                    : crossSpace
            ).Clamp(item.Min.Cross(row), item.Max.Cross(row));
            OptionalSize childKnown = item.Size.WithMain(row, float.NaN);
            childKnown = childKnown.WithCross(row, Maybe.Clamp(childKnown.Cross(row), item.Min.Cross(row), item.Max.Cross(row)));
            if (item.AlignSelf.Keyword == AlignItemsKeyword.Stretch && float.IsNaN(childKnown.Cross(row)))
            {
                childKnown = childKnown.WithCross(row, childCross.ToOption());
            }

            float measured = MeasureChild(
                tree,
                item.Node,
                childKnown,
                constants.NodeInnerSize,
                AvailableSize.FromAxes(row, available.Main(row), childCross),
                SizingMode.ContentSize,
                horizontal: row
            );
            contribution = row
                ? Maybe.Clamp(measured + marginSum, styleMin, styleMax)
                : Maybe.Clamp(Math.Max(measured, item.FlexBasis) + marginSum, styleMin, styleMax);
        }

        float difference = contribution - marginSum - item.FlexBasis;
        float fraction =
            difference > 0 ? difference / Math.Max(1, item.FlexGrow)
            : difference < 0 ? difference / (Math.Max(1, item.FlexShrink) * item.InnerFlexBasis)
            : 0;
        float flexContribution;
        if (fraction > 0)
        {
            flexContribution = Math.Max(1, item.FlexGrow) * fraction;
        }
        else if (fraction < 0)
        {
            float scaledShrink = Math.Max(1, item.FlexShrink) * item.InnerFlexBasis;
            flexContribution = scaledShrink == 0 ? 0 : scaledShrink * fraction;
        }
        else
        {
            flexContribution = 0;
        }

        return item.FlexBasis + flexContribution + marginSum;
    }

    // 9.7 Resolving flexible lengths: grow or shrink the unfrozen items of a line, freeze those clamped by their minimum or
    // maximum, and repeat.
    private static void ResolveFlexibleLengths(Span<FlexItem> items, in FlexConstants constants)
    {
        bool row = constants.Row;
        float innerMain = Maybe.Or(constants.NodeInnerSize.Main(row), 0);
        float gaps = GapSum(constants.GapMain, items.Length);
        float hypotheticalSum = gaps;
        foreach (ref FlexItem item in items)
        {
            hypotheticalSum += item.HypotheticalOuterMain;
        }

        bool growing = hypotheticalSum < innerMain;
        bool shrinking = hypotheticalSum > innerMain;
        bool exactlySized = !growing && !shrinking;

        // 2. Freeze the inflexible items at their hypothetical main size.
        foreach (ref FlexItem item in items)
        {
            item.TargetMain = item.HypotheticalInnerMain;
            item.Frozen = false;
            if (
                exactlySized
                || (item.FlexGrow == 0 && item.FlexShrink == 0)
                || (growing && item.FlexBasis > item.HypotheticalInnerMain)
                || (shrinking && item.FlexBasis < item.HypotheticalInnerMain)
            )
            {
                item.Frozen = true;
                item.OuterTargetMain = item.TargetMain + item.Margin.MainSum(row);
            }
        }

        if (exactlySized)
        {
            return;
        }

        // 3. The initial free space.
        float usedSpace = gaps;
        foreach (ref FlexItem item in items)
        {
            usedSpace += item.Frozen ? item.OuterTargetMain : item.FlexBasis + item.Margin.MainSum(row);
        }

        float initialFreeSpace = innerMain - usedSpace;

        // 4. Loop.
        while (true)
        {
            bool allFrozen = true;
            float itemsSpace = 0;
            float sumGrow = 0;
            float sumShrink = 0;
            foreach (ref FlexItem item in items)
            {
                if (item.Frozen)
                {
                    itemsSpace += item.OuterTargetMain;
                }
                else
                {
                    allFrozen = false;
                    itemsSpace += item.FlexBasis + item.Margin.MainSum(row);
                    sumGrow += item.FlexGrow;
                    sumShrink += item.FlexShrink;
                }
            }

            if (allFrozen)
            {
                break;
            }

            // b. The remaining free space; flex factors that sum below one take only that share of the initial free space.
            float remaining = innerMain - (gaps + itemsSpace);
            float freeSpace =
                growing && sumGrow < 1 ? Math.Min((initialFreeSpace * sumGrow) - gaps, remaining)
                : shrinking && sumShrink < 1 ? Math.Max((initialFreeSpace * sumShrink) - gaps, remaining)
                : remaining;

            // c. Distribute it by the flex factors.
            if (float.IsNormal(freeSpace))
            {
                if (growing && sumGrow > 0)
                {
                    foreach (ref FlexItem item in items)
                    {
                        if (!item.Frozen)
                        {
                            item.TargetMain = item.FlexBasis + (freeSpace * (item.FlexGrow / sumGrow));
                        }
                    }
                }
                else if (shrinking && sumShrink > 0)
                {
                    float sumScaledShrink = 0;
                    foreach (ref FlexItem item in items)
                    {
                        if (!item.Frozen)
                        {
                            sumScaledShrink += item.InnerFlexBasis * item.FlexShrink;
                        }
                    }

                    if (sumScaledShrink > 0)
                    {
                        foreach (ref FlexItem item in items)
                        {
                            if (!item.Frozen)
                            {
                                item.TargetMain = item.FlexBasis + (freeSpace * (item.InnerFlexBasis * item.FlexShrink / sumScaledShrink));
                            }
                        }
                    }
                }
            }

            // d. Fix the minimum and maximum violations.
            float totalViolation = 0;
            foreach (ref FlexItem item in items)
            {
                if (item.Frozen)
                {
                    continue;
                }

                float clamped = Math.Max(Maybe.Clamp(item.TargetMain, item.ResolvedMinMain, item.Max.Main(row)), 0);
                item.Violation = clamped - item.TargetMain;
                item.TargetMain = clamped;
                item.OuterTargetMain = clamped + item.Margin.MainSum(row);
                totalViolation += item.Violation;
            }

            // e. Freeze the over-flexed items.
            foreach (ref FlexItem item in items)
            {
                if (!item.Frozen)
                {
                    item.Frozen =
                        totalViolation > 0 ? item.Violation > 0
                        : totalViolation < 0 ? item.Violation < 0
                        : true;
                }
            }
        }
    }

    // 9.4.7: lay each item out at its used main size, an automatic cross size fitting the content (`fit-content`).
    private static void DetermineHypotheticalCrossSizes(
        UiTree tree,
        Span<FlexItem> items,
        in FlexConstants constants,
        OptionalSize known,
        AvailableSize available
    )
    {
        bool row = constants.Row;

        // A single line of a container with a known cross size takes that size, so a stretched item needs no measuring.
        bool lineCrossIsKnown = !constants.Wrap && !float.IsNaN(known.Cross(row));
        foreach (ref FlexItem item in items)
        {
            item.HypotheticalInnerCross = 0;
            item.HypotheticalOuterCross = 0;
            if (lineCrossIsKnown && item.IsStretched(row) && !item.ParticipatesInBaseline(row))
            {
                continue;
            }

            float paddingBorderCross = item.PaddingBorder.CrossSum(row);
            float minCross = item.Min.Cross(row);
            float maxCross = item.Max.Cross(row);
            float childCross = Maybe.Max(Maybe.Clamp(item.Size.Cross(row), minCross, maxCross), paddingBorderCross);
            AvailableSpace availableCross = available
                .Cross(row)
                .Minus(item.Margin.CrossSum(row))
                .AtLeast(0)
                .Clamp(minCross, maxCross)
                .AtLeast(paddingBorderCross);
            float crossStretch = Maybe.Max(constants.NodeInnerSize.Cross(row) - item.Margin.CrossSum(row), 0);
            if (MeasureSpaceOf(item.CrossStyle, crossStretch) is { } keywordSpace)
            {
                availableCross = keywordSpace;
            }

            float innerCross = childCross;
            if (float.IsNaN(innerCross))
            {
                AvailableSpace availableMain = AvailableSpace.Definite(Math.Max(0, constants.ContainerMain - item.Margin.MainSum(row)));
                var input = new LayoutInput(
                    row ? new OptionalSize(item.TargetMain, float.NaN) : new OptionalSize(float.NaN, item.TargetMain),
                    row ? IsKnownMainDefinite(in constants, in item) : IsKnownCrossDefinite(in constants, in item),
                    row ? IsKnownCrossDefinite(in constants, in item) : IsKnownMainDefinite(in constants, in item),
                    constants.NodeInnerSize,
                    AvailableSize.FromAxes(row, availableMain, availableCross),
                    SizingMode.ContentSize,
                    row ? RequestedAxis.Vertical : RequestedAxis.Horizontal
                );
                Size measured = LayoutChild(tree, item.Node, in input, commit: false);
                innerCross = Math.Max(Maybe.Clamp(row ? measured.Height : measured.Width, minCross, maxCross), paddingBorderCross);
            }

            item.HypotheticalInnerCross = innerCross;
            item.HypotheticalOuterCross = innerCross + item.Margin.CrossSum(row);
        }
    }

    // The first baselines of the items that align by them, in a row's line that has two or more of them: each such item is
    // laid out at its hypothetical size and its baseline read. The read marks the item (ReadBaseline): an item of fixed cross
    // size is not measured before, so no measuring run marks it.
    private static void CalculateChildrenBaselines(
        UiTree tree,
        Span<FlexItem> items,
        Span<FlexLine> lines,
        in FlexConstants constants,
        OptionalSize known,
        AvailableSize available
    )
    {
        if (!constants.Row)
        {
            return;
        }

        foreach (ref FlexLine line in lines)
        {
            Span<FlexItem> lineItems = items.Slice(line.Start, line.Count);
            int participating = 0;
            foreach (ref FlexItem item in lineItems)
            {
                participating += item.ParticipatesInBaseline(row: true) ? 1 : 0;
            }

            if (participating <= 1)
            {
                continue;
            }

            foreach (ref FlexItem item in lineItems)
            {
                if (!item.ParticipatesInBaseline(row: true))
                {
                    continue;
                }

                var input = new LayoutInput(
                    new OptionalSize(item.TargetMain, item.HypotheticalInnerCross),
                    IsKnownMainDefinite(in constants, in item),
                    IsKnownCrossDefinite(in constants, in item),
                    constants.NodeInnerSize,
                    new AvailableSize(
                        AvailableSpace.Definite(Math.Max(0, constants.ContainerMain - item.Margin.Horizontal)),
                        available.Height.OrDefinite(known.Height).Minus(item.Margin.Vertical).AtLeast(0)
                    ),
                    SizingMode.ContentSize,
                    RequestedAxis.Both
                );
                Size size = LayoutChild(tree, item.Node, in input, commit: true);
                item.Baseline = Maybe.Or(ReadBaseline(tree, item.Node), size.Height) + item.Margin.Top;
            }
        }
    }

    // 9.4.8: the container's inner cross size for a single line with a known cross size; otherwise each line's largest outer
    // hypothetical cross size, baseline-aligned items measured from the line's largest baseline, and for a single line clamped
    // by the container's minimum and maximum.
    private static void CalculateLineCrossSizes(Span<FlexItem> items, Span<FlexLine> lines, OptionalSize known, in FlexConstants constants)
    {
        bool row = constants.Row;
        float insetCross = constants.Inset.CrossSum(row);
        float minCross = constants.Min.Cross(row);
        float maxCross = constants.Max.Cross(row);
        if (!constants.Wrap && !float.IsNaN(known.Cross(row)))
        {
            lines[0].CrossSize = Math.Max(Maybe.Clamp(known.Cross(row), minCross, maxCross) - insetCross, 0);
            return;
        }

        foreach (ref FlexLine line in lines)
        {
            Span<FlexItem> lineItems = items.Slice(line.Start, line.Count);
            float maxBaseline = 0;
            foreach (ref FlexItem item in lineItems)
            {
                maxBaseline = Math.Max(maxBaseline, item.Baseline);
            }

            float cross = 0;
            foreach (ref FlexItem item in lineItems)
            {
                cross = Math.Max(
                    cross,
                    item.ParticipatesInBaseline(row) ? maxBaseline - item.Baseline + item.HypotheticalOuterCross : item.HypotheticalOuterCross
                );
            }

            line.CrossSize = cross;
        }

        if (!constants.Wrap)
        {
            lines[0].CrossSize = Maybe.Clamp(lines[0].CrossSize, minCross - insetCross, maxCross - insetCross);
        }
    }

    // 9.4.9 `align-content: stretch`: lines shorter together than the container's inner cross size share the rest equally.
    private static void StretchLines(Span<FlexLine> lines, OptionalSize known, in FlexConstants constants)
    {
        if (constants.AlignContent.Keyword != AlignContentKeyword.Stretch)
        {
            return;
        }

        bool row = constants.Row;
        float minCross = constants.Min.Cross(row);
        float maxCross = constants.Max.Cross(row);
        float containerMinInner = Maybe.Max(Maybe.Clamp(Maybe.Or(known.Cross(row), minCross), minCross, maxCross) - constants.Inset.CrossSum(row), 0);
        if (float.IsNaN(containerMinInner))
        {
            return;
        }

        float total = GapSum(constants.GapCross, lines.Length);
        foreach (ref FlexLine line in lines)
        {
            total += line.CrossSize;
        }

        if (total < containerMinInner)
        {
            float addition = (containerMinInner - total) / lines.Length;
            foreach (ref FlexLine line in lines)
            {
                line.CrossSize += addition;
            }
        }
    }

    private static void DetermineUsedCrossSizes(Span<FlexItem> items, float lineCross, in FlexConstants constants)
    {
        bool row = constants.Row;
        foreach (ref FlexItem item in items)
        {
            if (item.Stretched)
            {
                item.TargetCross = Maybe.Clamp(Math.Max(lineCross - item.Margin.CrossSum(row), 0), item.Min.Cross(row), item.StretchMaxCross);
            }
            else
            {
                item.TargetCross = item.HypotheticalInnerCross;
            }

            item.OuterTargetCross = item.TargetCross + item.Margin.CrossSum(row);
        }
    }

    // 9.5.12: a line's free space goes to its automatic main margins, or else is distributed by `justify-content`.
    private static void DistributeRemainingFreeSpace(Span<FlexItem> items, in FlexConstants constants)
    {
        bool row = constants.Row;
        float used = GapSum(constants.GapMain, items.Length);
        int autoMargins = 0;
        foreach (ref FlexItem item in items)
        {
            used += item.OuterTargetMain;
            if (constants.AnyAutoMargin)
            {
                autoMargins += (item.AutoMainStart(row) ? 1 : 0) + (item.AutoMainEnd(row) ? 1 : 0);
            }
        }

        float freeSpace = constants.InnerContainerMain - used;
        if (freeSpace > 0 && autoMargins > 0)
        {
            float margin = freeSpace / autoMargins;
            foreach (ref FlexItem item in items)
            {
                if (item.AutoMainStart(row))
                {
                    item.Margin = row ? item.Margin with { Left = margin } : item.Margin with { Top = margin };
                }

                if (item.AutoMainEnd(row))
                {
                    item.Margin = row ? item.Margin with { Right = margin } : item.Margin with { Bottom = margin };
                }
            }

            freeSpace = 0;
        }

        AlignContentKeyword mode = AlignmentFallback(freeSpace, items.Length, constants.JustifyContent);
        for (int i = 0; i < items.Length; i++)
        {
            // A reversed line distributes from its last item, which comes first along the physical axis.
            int index = constants.Reverse ? items.Length - 1 - i : i;
            items[index].OffsetMain = AlignmentOffset(freeSpace, items.Length, constants.GapMain, mode, constants.Reverse, i == 0);
        }
    }

    // 9.6.13-14: an item's automatic cross margins take the free space of its line; otherwise `align-self` places it.
    private static void AlignItemsAcrossTheLine(Span<FlexItem> items, float lineCross, in FlexConstants constants)
    {
        bool row = constants.Row;
        float maxBaseline = 0;
        float maxBaselineToBottom = 0;
        if (constants.AnyBaseline)
        {
            foreach (ref FlexItem item in items)
            {
                maxBaseline = Math.Max(maxBaseline, item.Baseline);
                if (item.ParticipatesInBaseline(row))
                {
                    maxBaselineToBottom = Math.Max(maxBaselineToBottom, item.OuterTargetCross - item.Baseline);
                }
            }
        }

        foreach (ref FlexItem item in items)
        {
            float freeSpace = lineCross - item.OuterTargetCross;
            bool autoStart = constants.AnyAutoMargin && (row ? item.AutoTop : item.AutoLeft);
            bool autoEnd = constants.AnyAutoMargin && (row ? item.AutoBottom : item.AutoRight);
            if (autoStart && autoEnd)
            {
                item.Margin = row
                    ? item.Margin with
                    {
                        Top = freeSpace / 2,
                        Bottom = freeSpace / 2,
                    }
                    : item.Margin with
                    {
                        Left = freeSpace / 2,
                        Right = freeSpace / 2,
                    };
            }
            else if (autoStart)
            {
                item.Margin = row ? item.Margin with { Top = freeSpace } : item.Margin with { Left = freeSpace };
            }
            else if (autoEnd)
            {
                item.Margin = row ? item.Margin with { Bottom = freeSpace } : item.Margin with { Right = freeSpace };
            }
            else
            {
                item.OffsetCross = CrossOffset(in item, freeSpace, maxBaseline, maxBaselineToBottom, in constants);
            }
        }
    }

    // Where `align-self` puts an item in the free space of its line; `wrap-reverse` flips the flex-relative keywords. A safe
    // alignment of an item that overflows goes to the start.
    private static float CrossOffset(in FlexItem item, float freeSpace, float maxBaseline, float maxBaselineToBottom, in FlexConstants constants)
    {
        AlignItemsKeyword keyword = item.AlignSelf.Safe && freeSpace < 0 ? AlignItemsKeyword.Start : item.AlignSelf.Keyword;
        bool flip = constants.WrapReverse;
        return keyword switch
        {
            AlignItemsKeyword.Start => 0,
            AlignItemsKeyword.End => freeSpace,
            AlignItemsKeyword.FlexEnd => flip ? 0 : freeSpace,
            AlignItemsKeyword.Center => freeSpace / 2,
            AlignItemsKeyword.Baseline when constants.Row => flip
                ? freeSpace + item.OuterTargetCross - maxBaselineToBottom - item.Baseline
                : maxBaseline - item.Baseline,
            _ => flip ? freeSpace : 0,
        };
    }

    // 9.6.16: the free cross space distributed among the lines by `align-content`.
    private static void AlignLines(Span<FlexLine> lines, float innerCross, float totalLineCross, in FlexConstants constants)
    {
        float freeSpace = innerCross - totalLineCross - GapSum(constants.GapCross, lines.Length);
        AlignContentKeyword mode = AlignmentFallback(freeSpace, lines.Length, constants.AlignContent);
        for (int i = 0; i < lines.Length; i++)
        {
            int index = constants.WrapReverse ? lines.Length - 1 - i : i;
            lines[index].OffsetCross = AlignmentOffset(freeSpace, lines.Length, constants.GapCross, mode, constants.WrapReverse, i == 0);
        }
    }

    // The final layout of each item at its target size, placed along the main axis after the items before it (in reverse
    // order for a reversed direction) and across after the lines before it (in reverse order for `wrap-reverse`).
    private static void FinalLayoutPass(UiTree tree, Span<FlexItem> items, Span<FlexLine> lines, Size containerSize, in FlexConstants constants)
    {
        bool row = constants.Row;
        float totalCross = constants.Inset.CrossStart(row);
        for (int l = 0; l < lines.Length; l++)
        {
            int lineIndex = constants.WrapReverse ? lines.Length - 1 - l : l;
            ref FlexLine line = ref lines[lineIndex];
            float totalMain = constants.Inset.MainStart(row);
            Span<FlexItem> lineItems = items.Slice(line.Start, line.Count);
            for (int i = 0; i < lineItems.Length; i++)
            {
                int index = constants.Reverse ? lineItems.Length - 1 - i : i;
                ref FlexItem item = ref lineItems[index];
                var input = new LayoutInput(
                    row ? new OptionalSize(item.TargetMain, item.TargetCross) : new OptionalSize(item.TargetCross, item.TargetMain),
                    row ? IsKnownMainDefinite(in constants, in item) : IsKnownCrossDefinite(in constants, in item),
                    row ? IsKnownCrossDefinite(in constants, in item) : IsKnownMainDefinite(in constants, in item),
                    constants.NodeInnerSize,
                    new AvailableSize(
                        AvailableSpace.Definite(Math.Max(0, containerSize.Width - item.Margin.Horizontal)),
                        AvailableSpace.Definite(Math.Max(0, containerSize.Height - item.Margin.Vertical))
                    ),
                    SizingMode.ContentSize,
                    RequestedAxis.Both
                );
                Size size = LayoutChild(tree, item.Node, in input, commit: true);
                float main = totalMain + item.OffsetMain + item.Margin.MainStart(row);
                float crossOffset = totalCross + item.OffsetCross + line.OffsetCross;
                float cross = crossOffset + item.Margin.CrossStart(row);
                if (row)
                {
                    Place(tree, item.Node, main, cross);
                }
                else
                {
                    Place(tree, item.Node, cross, main);
                }

                totalMain += item.OffsetMain + item.Margin.MainSum(row) + (row ? size.Width : size.Height);
            }

            totalCross += line.OffsetCross + line.CrossSize;
        }
    }

    // CSS Flexbox §8.5: the item whose first baseline gives the container's, on the startmost line (the last for
    // `wrap-reverse`): in a row, its first item that aligns by baseline, else its first item; in a column, its startmost item.
    private static NodeHandle BaselineItem(Span<FlexItem> items, Span<FlexLine> lines, in FlexConstants constants)
    {
        FlexLine line = lines[constants.WrapReverse ? lines.Length - 1 : 0];
        if (line.Count == 0)
        {
            return default;
        }

        Span<FlexItem> lineItems = items.Slice(line.Start, line.Count);
        if (!constants.Row)
        {
            return lineItems[constants.Reverse ? lineItems.Length - 1 : 0].Node;
        }

        foreach (ref FlexItem item in lineItems)
        {
            if (item.ParticipatesInBaseline(row: true))
            {
                return item.Node;
            }
        }

        return lineItems[0].Node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GapSum(float gap, int count) => count <= 1 ? 0 : gap * (count - 1);

    // CSS Box Alignment's fallbacks (Taffy's apply_alignment_fallback): a distribution with one subject or no free space falls
    // back to a start or a center, and a safe alignment with no free space to the start.
    private static AlignContentKeyword AlignmentFallback(float freeSpace, int count, AlignContent alignment)
    {
        AlignContentKeyword keyword = alignment.Keyword == AlignContentKeyword.Normal ? AlignContentKeyword.Stretch : alignment.Keyword;
        bool safe = alignment.Safe;
        if (count <= 1 || freeSpace <= 0)
        {
            switch (keyword)
            {
                case AlignContentKeyword.Stretch or AlignContentKeyword.SpaceBetween:
                    keyword = AlignContentKeyword.FlexStart;
                    safe = true;
                    break;
                case AlignContentKeyword.SpaceAround or AlignContentKeyword.SpaceEvenly:
                    keyword = AlignContentKeyword.Center;
                    safe = true;
                    break;
            }
        }

        return freeSpace <= 0 && safe ? AlignContentKeyword.Start : keyword;
    }

    // The offset of a subject from the one before it (Taffy's compute_alignment_offset): the first takes its place in the free
    // space, every other the gap and its share of the space between.
    private static float AlignmentOffset(float freeSpace, int count, float gap, AlignContentKeyword mode, bool reversed, bool isFirst)
    {
        if (isFirst)
        {
            return mode switch
            {
                AlignContentKeyword.FlexStart => reversed ? freeSpace : 0,
                AlignContentKeyword.End => freeSpace,
                AlignContentKeyword.FlexEnd => reversed ? 0 : freeSpace,
                AlignContentKeyword.Center => freeSpace / 2,
                AlignContentKeyword.SpaceAround => freeSpace >= 0 ? freeSpace / count / 2 : freeSpace / 2,
                AlignContentKeyword.SpaceEvenly => freeSpace >= 0 ? freeSpace / (count + 1) : freeSpace / 2,
                _ => 0,
            };
        }

        float positive = Math.Max(freeSpace, 0);
        return gap
            + mode switch
            {
                AlignContentKeyword.SpaceBetween => positive / (count - 1),
                AlignContentKeyword.SpaceAround => positive / count,
                AlignContentKeyword.SpaceEvenly => positive / (count + 1),
                _ => 0,
            };
    }

    // CSS Flexbox §9.8: an item's main size is definite when the container's is or its basis is.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsKnownMainDefinite(in FlexConstants constants, in FlexItem item) =>
        constants.HasDefiniteMainSize || item.FlexBasisIsDefinite;

    // A stretched item's cross size is definite, and so is a fixed one; a column item's fit-content width is definite when the
    // container's cross space is.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsKnownCrossDefinite(in FlexConstants constants, in FlexItem item) =>
        item.IsStretched(constants.Row)
        || !float.IsNaN(item.Size.Cross(constants.Row))
        || (!constants.Row && constants.CrossAvailableSpaceIsDefinite);

    private struct FlexConstants
    {
        public bool Row;
        public bool Reverse;
        public bool Wrap;
        public bool WrapReverse;
        public OptionalSize Min;
        public OptionalSize Max;
        public Edges Inset;
        public OptionalSize NodeOuterSize;
        public OptionalSize NodeInnerSize;
        public bool KnownMainSizeIsDefinite;
        public bool HasDefiniteMainSize;
        public bool HasDefiniteCrossSize;
        public bool CrossAvailableSpaceIsDefinite;
        public float GapMain;
        public float GapCross;
        public AlignItems AlignItems;
        public AlignContent AlignContent;
        public AlignContent JustifyContent;
        public bool AnyBaseline;
        public bool AnyAutoMargin;
        public bool AllStretched;
        public float ContainerMain;
        public float InnerContainerMain;
        public ulong ContainerStyleVersion;
    }

    private struct FlexLine
    {
        public int Start;
        public int Count;
        public float CrossSize;
        public float OffsetCross;
    }

    private struct FlexItem
    {
        public NodeHandle Node;
        public OptionalSize Size;
        public OptionalSize Min;
        public OptionalSize Max;
        public Length MainStyle;
        public Length CrossStyle;
        public Length FlexBasisStyle;
        public Edges Margin;
        public bool AutoLeft;
        public bool AutoTop;
        public bool AutoRight;
        public bool AutoBottom;
        public Edges PaddingBorder;
        public float FlexGrow;
        public float FlexShrink;
        public AlignItems AlignSelf;
        public bool FlexBasisIsDefinite;
        public float FlexBasis;
        public float InnerFlexBasis;
        public float ResolvedMinMain;
        public float HypotheticalInnerMain;
        public float HypotheticalOuterMain;
        public float HypotheticalInnerCross;
        public float HypotheticalOuterCross;
        public float TargetMain;
        public float OuterTargetMain;
        public float TargetCross;
        public float OuterTargetCross;
        public float Violation;
        public bool Frozen;
        public bool Stretched;
        public float StretchMaxCross;
        public float Baseline;
        public float OffsetMain;
        public float OffsetCross;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool AutoMainStart(bool row) => row ? AutoLeft : AutoTop;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool AutoMainEnd(bool row) => row ? AutoRight : AutoBottom;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasAutoCrossMargin(bool row) => row ? AutoTop || AutoBottom : AutoLeft || AutoRight;

        // `align-self: stretch` with an automatic cross size and no automatic cross margin.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsStretched(bool row) => Stretched;

        // `align-self: baseline` with no automatic cross margin (CSS Flexbox §9.4).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ParticipatesInBaseline(bool row) => AlignSelf.Keyword == AlignItemsKeyword.Baseline && !HasAutoCrossMargin(row);
    }
}
