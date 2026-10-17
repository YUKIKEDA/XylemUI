using System.Buffers;

namespace XylemUI;

internal static partial class LayoutEngine
{
    // CSS Flexbox §9 (Taffy's compute_flexbox_layout) with one line and CSS's initial values (design §3.2): no wrapping,
    // `justify-content: flex-start`, `align-items: stretch`, no gaps. The steps keep the numbers of §9.
    private static Size ComputeFlex(UiTree tree, in LayoutStyle style, FlexProps props, NodeHandle firstChild, in LayoutInput input, bool commit)
    {
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
        int count = 0;
        for (NodeHandle c = firstChild; !c.IsNone; c = tree.Nodes.GetRef(c).NextSibling)
        {
            count++;
        }

        FlexItem[] items = ArrayPool<FlexItem>.Shared.Rent(count);
        try
        {
            Span<FlexItem> line = items.AsSpan(0, count);

            // 9.1.1 Generate the flex items.
            GenerateFlexItems(tree, firstChild, in constants, line);

            // 9.2.2 The available main and cross space for the items.
            AvailableSize available = FlexAvailableSpace(known, input.Available, in constants);

            // 9.2.3 The flex base size and hypothetical main size of each item.
            DetermineFlexBaseSizes(tree, in constants, available, line);

            // 9.2.4 The container's main size, unless it is known.
            bool row = constants.Row;
            float knownInnerMain = constants.NodeInnerSize.Main(row);
            if (!float.IsNaN(knownInnerMain))
            {
                constants.InnerContainerMain = knownInnerMain;
                constants.ContainerMain = knownInnerMain + constants.Inset.MainSum(row);
            }
            else
            {
                DetermineContainerMainSize(tree, available, line, ref constants);
                constants.NodeInnerSize = constants.NodeInnerSize.WithMain(row, constants.InnerContainerMain);
            }

            if (!commit && input.Axis == (row ? RequestedAxis.Horizontal : RequestedAxis.Vertical))
            {
                return row ? new Size(constants.ContainerMain, 0) : new Size(0, constants.ContainerMain);
            }

            // 9.3.6 Resolve the flexible lengths.
            ResolveFlexibleLengths(line, in constants);

            // 9.4.7 The hypothetical cross size of each item.
            DetermineHypotheticalCrossSizes(tree, line, in constants, known, available);

            // 9.4.8 The cross size of the line, and 9.4.9 `align-content: stretch`.
            float lineCross = LineCrossSize(line, known, in constants);

            // 9.4.11 The used cross size of each item: a stretched item takes the line's.
            DetermineUsedCrossSizes(tree, line, lineCross, in constants);

            // 9.5.12 and 9.6.13-14: with `flex-start` and `stretch` every item sits at the start of both axes.

            // 9.6.15 The container's cross size.
            float outerCross = Math.Max(
                Maybe.Clamp(Maybe.Or(known.Cross(row), lineCross + constants.Inset.CrossSum(row)), min.Cross(row), max.Cross(row)),
                constants.Inset.CrossSum(row)
            );
            Size containerSize = row ? new Size(constants.ContainerMain, outerCross) : new Size(outerCross, constants.ContainerMain);
            if (!commit)
            {
                return containerSize;
            }

            // The final layout of each item.
            float mainOffset = constants.Inset.MainStart(row);
            float crossOffset = constants.Inset.CrossStart(row);
            foreach (ref FlexItem item in line)
            {
                var itemInput = new LayoutInput(
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
                Size size = LayoutChild(tree, item.Node, in itemInput, commit: true);
                float main = mainOffset + item.Margin.MainStart(row);
                float cross = crossOffset + item.Margin.CrossStart(row);
                if (row)
                {
                    Place(tree, item.Node, main, cross);
                }
                else
                {
                    Place(tree, item.Node, cross, main);
                }

                mainOffset += item.Margin.MainSum(row) + (row ? size.Width : size.Height);
            }

            return containerSize;
        }
        finally
        {
            ArrayPool<FlexItem>.Shared.Return(items);
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
        bool row = props.Direction == FlexDirection.Row;
        Edges inset = PaddingBorder(in style, input.ParentSize.Width);
        bool mainIsDefinite = row ? widthIsDefinite : heightIsDefinite;
        bool crossIsDefinite = row ? heightIsDefinite : widthIsDefinite;
        bool hasDefiniteCross = crossIsDefinite && !float.IsNaN(known.Cross(row));
        return new FlexConstants
        {
            Row = row,
            Min = min,
            Max = max,
            Inset = inset,
            NodeOuterSize = known,
            NodeInnerSize = known.Minus(inset.Sum()),
            KnownMainSizeIsDefinite = mainIsDefinite,
            HasDefiniteMainSize = mainIsDefinite && !float.IsNaN(known.Main(row)),
            HasDefiniteCrossSize = hasDefiniteCross,
            CrossAvailableSpaceIsDefinite = hasDefiniteCross || input.Available.Cross(row).IsDefinite,
        };
    }

    private static void GenerateFlexItems(UiTree tree, NodeHandle firstChild, in FlexConstants constants, Span<FlexItem> items)
    {
        bool row = constants.Row;

        // Percentages of the items resolve against the container's content box only where it is definite.
        OptionalSize percentBasis = constants.KnownMainSizeIsDefinite ? constants.NodeInnerSize : constants.NodeInnerSize.WithMain(row, float.NaN);
        float innerWidth = constants.NodeInnerSize.Width;
        int i = 0;
        for (NodeHandle child = firstChild; !child.IsNone; i++)
        {
            ref UiNode c = ref tree.Nodes.GetRef(child);
            LayoutStyle s = c.Layout.Style;
            items[i] = new FlexItem
            {
                Node = child,
                Size = StyleSize(in s, percentBasis),
                Min = MinSize(in s, percentBasis),
                Max = MaxSize(in s, percentBasis),
                MainStyle = row ? s.Width : s.Height,
                CrossStyle = row ? s.Height : s.Width,
                FlexBasisStyle = s.FlexBasis,
                Margin = EdgeInsets.ResolveOrZero(s.Margin, innerWidth),
                PaddingBorder = PaddingBorder(in s, innerWidth),
                FlexGrow = s.FlexGrow,
                FlexShrink = s.FlexShrink,
            };
            child = c.NextSibling;
        }
    }

    // 9.2.2: the container's content box where it is known, otherwise the space the parent gave less the padding and border.
    private static AvailableSize FlexAvailableSpace(OptionalSize known, AvailableSize outer, in FlexConstants constants)
    {
        Size inset = constants.Inset.Sum();
        return new AvailableSize(InnerSpace(known.Width, outer.Width, inset.Width), InnerSpace(known.Height, outer.Height, inset.Height));
    }

    private static AvailableSpace InnerSpace(float known, AvailableSpace outer, float inset) =>
        float.IsNaN(known) ? outer.Minus(inset).AtLeast(0) : AvailableSpace.Definite(Math.Max(0, known - inset));

    private static void DetermineFlexBaseSizes(UiTree tree, in FlexConstants constants, AvailableSize available, Span<FlexItem> items)
    {
        bool row = constants.Row;
        float crossParent = constants.NodeInnerSize.Cross(row);
        OptionalSize childParentSize = OptionalSize.None.WithCross(row, crossParent);
        float percentMain = constants.KnownMainSizeIsDefinite ? constants.NodeInnerSize.Main(row) : float.NaN;

        foreach (ref FlexItem item in items)
        {
            float minCross = item.Min.Cross(row);
            float maxCross = item.Max.Cross(row);
            AvailableSpace crossSpace = available.Cross(row);
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
            if (float.IsNaN(childKnown.Cross(row)))
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
        }
    }

    // 9.2.4 for a container whose main size is not known: under a definite space the items' bases; under a content constraint
    // the items' content contributions (CSS Flexbox §9.9.1, as Chrome computes it).
    private static void DetermineContainerMainSize(UiTree tree, AvailableSize available, Span<FlexItem> items, ref FlexConstants constants)
    {
        bool row = constants.Row;
        float insetMain = constants.Inset.MainSum(row);
        float outerMain = constants.NodeOuterSize.Main(row);
        if (float.IsNaN(outerMain))
        {
            AvailableSpace mainSpace = available.Main(row);
            float sum = 0;
            if (mainSpace.IsDefinite)
            {
                foreach (ref FlexItem item in items)
                {
                    sum += Math.Max(Maybe.Max(item.FlexBasis, item.Min.Main(row)) + item.Margin.MainSum(row), item.PaddingBorder.MainSum(row));
                }
            }
            else
            {
                foreach (ref FlexItem item in items)
                {
                    sum += ContentContribution(tree, ref item, available, in constants);
                }
            }

            outerMain = sum + insetMain;
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
            if (float.IsNaN(childKnown.Cross(row)))
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

    // 9.7 Resolving flexible lengths: grow or shrink the unfrozen items, freeze those clamped by their minimum or maximum,
    // and repeat.
    private static void ResolveFlexibleLengths(Span<FlexItem> items, in FlexConstants constants)
    {
        bool row = constants.Row;
        float innerMain = Maybe.Or(constants.NodeInnerSize.Main(row), 0);
        float hypotheticalSum = 0;
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
        float usedSpace = 0;
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
            float remaining = innerMain - itemsSpace;
            float freeSpace =
                growing && sumGrow < 1 ? Math.Min(initialFreeSpace * sumGrow, remaining)
                : shrinking && sumShrink < 1 ? Math.Max(initialFreeSpace * sumShrink, remaining)
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
        bool lineCrossIsKnown = !float.IsNaN(known.Cross(row));
        foreach (ref FlexItem item in items)
        {
            item.HypotheticalInnerCross = 0;
            item.HypotheticalOuterCross = 0;
            if (lineCrossIsKnown && item.IsStretched)
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

    // 9.4.8 and 9.4.9 for a single line: the container's inner cross size where it is known, otherwise the largest outer
    // hypothetical cross size clamped by the container's minimum and maximum; then stretched to the container's minimum.
    private static float LineCrossSize(Span<FlexItem> items, OptionalSize known, in FlexConstants constants)
    {
        bool row = constants.Row;
        float insetCross = constants.Inset.CrossSum(row);
        float minCross = constants.Min.Cross(row);
        float maxCross = constants.Max.Cross(row);
        float lineCross;
        if (!float.IsNaN(known.Cross(row)))
        {
            lineCross = Math.Max(Maybe.Clamp(known.Cross(row), minCross, maxCross) - insetCross, 0);
        }
        else
        {
            lineCross = 0;
            foreach (ref FlexItem item in items)
            {
                lineCross = Math.Max(lineCross, item.HypotheticalOuterCross);
            }

            lineCross = Maybe.Clamp(lineCross, minCross - insetCross, maxCross - insetCross);
        }

        // `align-content: stretch`: a line shorter than the container's minimum inner cross size grows to it.
        float containerMinInner = Maybe.Max(Maybe.Clamp(Maybe.Or(known.Cross(row), minCross), minCross, maxCross) - insetCross, 0);
        return float.IsNaN(containerMinInner) || lineCross >= containerMinInner ? lineCross : containerMinInner;
    }

    private static void DetermineUsedCrossSizes(UiTree tree, Span<FlexItem> items, float lineCross, in FlexConstants constants)
    {
        bool row = constants.Row;
        foreach (ref FlexItem item in items)
        {
            if (item.IsStretched)
            {
                // The maximum resolves against the container's content box here even where it is indefinite (Chrome and
                // Firefox, as Taffy).
                LayoutStyle s = tree.Nodes.GetRef(item.Node).Layout.Style;
                float maxCross = MaxSize(in s, constants.NodeInnerSize).Cross(row);
                item.TargetCross = Maybe.Clamp(Math.Max(lineCross - item.Margin.CrossSum(row), 0), item.Min.Cross(row), maxCross);
            }
            else
            {
                item.TargetCross = item.HypotheticalInnerCross;
            }
        }
    }

    // CSS Flexbox §9.8: an item's main size is definite when the container's is or its basis is.
    private static bool IsKnownMainDefinite(in FlexConstants constants, in FlexItem item) =>
        constants.HasDefiniteMainSize || item.FlexBasisIsDefinite;

    // A stretched item's cross size is definite, and so is a fixed one; a column item's fit-content width is definite when the
    // container's cross space is.
    private static bool IsKnownCrossDefinite(in FlexConstants constants, in FlexItem item) =>
        item.IsStretched || !float.IsNaN(item.Size.Cross(constants.Row)) || (!constants.Row && constants.CrossAvailableSpaceIsDefinite);

    private struct FlexConstants
    {
        public bool Row;
        public OptionalSize Min;
        public OptionalSize Max;
        public Edges Inset;
        public OptionalSize NodeOuterSize;
        public OptionalSize NodeInnerSize;
        public bool KnownMainSizeIsDefinite;
        public bool HasDefiniteMainSize;
        public bool HasDefiniteCrossSize;
        public bool CrossAvailableSpaceIsDefinite;
        public float ContainerMain;
        public float InnerContainerMain;
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
        public Edges PaddingBorder;
        public float FlexGrow;
        public float FlexShrink;
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
        public float Violation;
        public bool Frozen;

        // `align-self: stretch` with an automatic cross size (L1a has no other alignment and no automatic margins).
        public readonly bool IsStretched => CrossStyle.IsAuto;
    }
}
