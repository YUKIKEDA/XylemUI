using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XylemUI;

/// <summary>
/// Lays out the dirty parts of a <see cref="UiTree"/>: the worklist of layout roots, the cache, and the leaf, Block, and Flex
/// algorithms (design §2.4.1, §3.2).
/// </summary>
/// <remarks>
/// <para>
/// Layout is a pure computation over what is already written on the nodes. It creates no node (the arena forbids it) and reads
/// no signal (the reactive runtime fails the invariant).
/// </para>
/// <para>
/// The algorithms follow CSS (Chrome's results, as Taffy implements them, design §3.2) without margin collapsing, floats, or
/// inline flow. A node is laid out in one of two modes. Measuring returns the size the node would take for an input and writes
/// nothing on the node but its measurement cache (a flex container asks this of its children before it divides the space).
/// Laying out writes the size, the children's positions, and the cache, and clears the dirty flags (Taffy's compute-size and
/// perform-layout runs).
/// </para>
/// </remarks>
internal static partial class LayoutEngine
{
    /// <summary>
    /// The number of worklist cycles one pass may take before it stops and reports a <see cref="LayoutStabilizationException"/>
    /// (the same contract as <see cref="ReactiveRuntime.MaxFlushIterations"/>).
    /// </summary>
    public const int MaxLayoutIterations = 100;

    /// <summary>
    /// Lays out every layout root, shallowest first, until no request is left. A root that an earlier root's layout already
    /// cleaned is skipped; a node marked during the pass is laid out in the next cycle.
    /// </summary>
    public static void RunLayoutPass(UiTree tree)
    {
        UiThread.VerifyAccess();
        ReactiveRuntime.EnterLayout();
        try
        {
            int iterations = 0;
            while (tree.LayoutRoots.Count > 0)
            {
                if (++iterations > MaxLayoutIterations)
                {
                    // The requests left stay for the next pass, like the effects of a flush that did not stabilize.
                    ReactiveRuntime.ReportException(new LayoutStabilizationException(MaxLayoutIterations));
                    return;
                }

                using Arena<UiNode>.AllocationForbidden noAllocation = tree.Nodes.ForbidAllocation();
                List<LayoutRequest> current = tree.TakeLayoutRoots();
                if (current.Count > 1)
                {
                    current.Sort(tree.ShallowestFirst);
                }

                foreach (LayoutRequest request in CollectionsMarshal.AsSpan(current))
                {
                    DirtyState dirty = tree.Nodes.GetRef(request.Node).Dirty;

                    // A node inside a hidden subtree takes no part in the layout (row L2).
                    if (((dirty.SelfDirty | dirty.SubtreeDirty) & DirtyFlags.Layout) != 0 && IsInHiddenSubtree(tree, request.Node))
                    {
                        HideSubtree(tree, request.Node);
                        int hidden = tree.FindContentSnapshot(request.Node);
                        if (hidden >= 0)
                        {
                            tree.RemoveContentSnapshotAt(hidden);
                        }

                        continue;
                    }

                    if (((dirty.SelfDirty | dirty.SubtreeDirty) & DirtyFlags.Layout) == 0)
                    {
                        // An ancestor laid the leaf out with its new content first. The parent's layout read the new values,
                        // but its measurements may still hold ones of the old content, which an ancestor read in this pass
                        // (the random test found it): the path from the parent is marked, as MarkDirty marks it.
                        int snapshot = tree.FindContentSnapshot(request.Node);
                        if (snapshot >= 0)
                        {
                            DirtyFlags flags = tree.ContentSnapshots[snapshot].Flags;
                            tree.RemoveContentSnapshotAt(snapshot);
                            Invalidation.MarkPath(tree, tree.Nodes.GetRef(request.Node).Parent, flags);
                        }

                        continue;
                    }

                    // A dirty root is never skipped by the cache. A measure function may throw: the exception is reported like an
                    // effect's, the other roots go on, and the request waits for the next pass.
                    try
                    {
                        LayoutInput input = ResolveInput(tree, request);
                        if (request.Node == tree.Root)
                        {
                            input = WithRootKeywordLimits(tree, request.Node, in input);
                        }

                        if (request.Node == tree.Root && IsAbsoluteRootIn(tree, request.Node, in input))
                        {
                            LayOutAbsoluteRoot(tree, request.Node, ToSize(input.ParentSize));
                        }
                        else
                        {
                            _ = Compute(tree, request.Node, in input, commit: true);
                            if (request.Node == tree.Root)
                            {
                                PlaceRoot(tree, request.Node, in input);
                            }
                        }

                        CompareContentChange(tree, request.Node);
                    }
                    catch (Exception exception) when (exception is not InvariantViolationException)
                    {
                        ReactiveRuntime.ReportException(exception);
                        tree.DeferRequest(request);
                    }
                }

                current.Clear();
            }
        }
        finally
        {
            ReactiveRuntime.ExitLayout();
            tree.RestoreDeferredRequests();
        }
    }

    /// <summary>
    /// Returns the input of a root laid out in a viewport, as CSS lays out the root element in the initial containing block
    /// (Taffy's in-flow root): the viewport less the root's margins, and for a block root the viewport's width.
    /// </summary>
    internal static LayoutInput RootInput(in LayoutStyle style, AvailableSize viewport)
    {
        var parent = new OptionalSize(viewport.Width.ToOption(), viewport.Height.ToOption());
        Edges margin = EdgeInsets.ResolveOrZero(style.Margin, parent.Width);
        var available = new AvailableSize(viewport.Width.Minus(margin.Horizontal).AtLeast(0), viewport.Height.Minus(margin.Vertical).AtLeast(0));
        OptionalSize known = OptionalSize.None;
        if (style.Algorithm is LayoutAlgorithm.Block)
        {
            Size paddingBorder = PaddingBorder(in style, parent.Width).Sum();
            OptionalSize min = MinSize(in style, parent);
            OptionalSize max = MaxSize(in style, parent);
            known = MinMaxDefinite(min, max)
                .Or(StyleSize(in style, parent).Clamp(min, max))
                .Or(new OptionalSize(available.Width.ToOption(), float.NaN))
                .AtLeast(paddingBorder);
        }

        return new LayoutInput(known, true, true, parent, available, SizingMode.InherentSize, RequestedAxis.Both);
    }

    private static LayoutInput ResolveInput(UiTree tree, in LayoutRequest request)
    {
        if (request.Input is { } input)
        {
            return input;
        }

        // IntrinsicSizeChanged: the parent is clean, so the input it gave last time still holds.
        ref UiNode node = ref tree.Nodes.GetRef(request.Node);
        if (!node.Layout.WasLaidOut)
        {
            Invariant.Fail("A layout root that was never laid out has a request without an input.");
        }

        return node.Layout.LastInput;
    }

    // A root's keyword limits clamp the size its input gives it (row L3). RootInput runs outside the layout pass and cannot
    // measure, so the pass resolves them against the viewport.
    private static LayoutInput WithRootKeywordLimits(UiTree tree, NodeHandle root, in LayoutInput input)
    {
        ref readonly LayoutStyle style = ref tree.Nodes.GetRef(root).Layout.Style;
        if (!style.HasSizingKeywordLimit || style.Position == Position.Absolute)
        {
            return input;
        }

        OptionalSize parent = input.ParentSize;
        OptionalSize min = MinSize(in style, parent);
        OptionalSize max = MaxSize(in style, parent);
        OptionalSize styleSize = StyleSize(in style, parent);
        var limits = KeywordLimits.Of(in style);
        var stretch = new Size(input.Available.Width.ToOption(), input.Available.Height.ToOption());
        ResolveKeywordLimits(tree, root, limits, styleSize, parent, stretch, contentHeights: false, ref min, ref max);
        Size size = LayoutChild(tree, root, in input, commit: false);
        var known = new OptionalSize(Maybe.Clamp(size.Width, min.Width, max.Width), Maybe.Clamp(size.Height, min.Height, max.Height));
        return input with { KnownDimensions = known };
    }

    // The root's margin box sits at the origin of its viewport.
    private static void PlaceRoot(UiTree tree, NodeHandle root, in LayoutInput input)
    {
        ref readonly LayoutStyle style = ref tree.Nodes.GetRef(root).Layout.Style;
        Edges margin = EdgeInsets.ResolveOrZero(style.Margin, input.ParentSize.Width);

        // A relatively positioned root is shifted by its insets, against the viewport (Taffy's compute_root_layout).
        (float dx, float dy) = RelativeOffset(in style, input.ParentSize.Width, input.ParentSize.Height);
        Place(tree, root, margin.Left + dx, margin.Top + dy);
    }

    // Lays out or measures a child. A clean child keeps the result of its last run with the same key: a layout for either mode,
    // a measurement for measuring. A measuring run reads the child's content, which the child records (design §2.4.1).
    internal static Size LayoutChild(UiTree tree, NodeHandle handle, in LayoutInput input, bool commit)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        if (!commit && !node.Parent.IsNone)
        {
            node.Layout.ContentReadGeneration = tree.Nodes.GetRef(node.Parent).Layout.CacheGeneration;
        }

        // A min-content or max-content width of a leaf comes from its content widths, kept apart from the measurements (row L4).
        // A dirty leaf dropped them when it was marked, so they are computed again from its new content.
        if (
            !commit
            && input.Axis == RequestedAxis.Horizontal
            && node.Layout.IntrinsicWidths is not null
            && node.FirstChild.IsNone
            && float.IsNaN(input.KnownDimensions.Width)
            && input.Available.Width.Kind is AvailableSpaceKind.MinContent or AvailableSpaceKind.MaxContent
        )
        {
            return new Size(LeafIntrinsicWidth(tree, handle, in input), 0);
        }

        if (((node.Dirty.SelfDirty | node.Dirty.SubtreeDirty) & DirtyFlags.Layout) == 0)
        {
            if (node.Layout.WasLaidOut && node.Layout.CachedKey.MatchesInput(in node, in input))
            {
                return new Size(node.Layout.Rect.Width, node.Layout.Rect.Height);
            }

            if (!commit)
            {
                LayoutCacheKey key = LayoutCacheKey.Of(in node, in input);
                if (tree.MeasuresOf(handle).TryGet(in key, input.Axis, out Size measured))
                {
                    return measured;
                }
            }
        }

        return Compute(tree, handle, in input, commit);
    }

    // Reads a laid-out child's first baseline, from the top of its border box, or NaN when it has none. The parent's result
    // depends on it as on a measurement, so the child records the read in the same way (design §2.4.1): a change inside a
    // child of fixed size, which no measuring run reads, still reaches the parent. A container's baseline comes from a child of
    // its own and is read here when a parent asks for it (as Flutter's getDistanceToBaseline), so the marks follow the
    // baselines that are read: a container whose baseline no parent reads does not mark its first child.
    private static float ReadBaseline(UiTree tree, NodeHandle handle)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        node.Layout.ContentReadGeneration = tree.Nodes.GetRef(node.Parent).Layout.CacheGeneration;
        NodeHandle child = node.BaselineChild;
        if (child.IsNone)
        {
            return node.Layout.FirstBaseline;
        }

        // A flex item without a baseline gives one synthesized from its border box; a block goes on to its next child. The refs
        // are taken again after each read (design §2.5).
        if (!node.Layout.BaselineFromFirstChildWithOne)
        {
            float inner = ReadBaseline(tree, child);
            Rect rect = tree.Nodes.GetRef(child).Layout.Rect;
            return rect.Y + Maybe.Or(inner, rect.Height);
        }

        for (; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            // A child out of the flow gives the block no baseline (CSS 2.1 section 10.8.1).
            if (!tree.Nodes.GetRef(child).Layout.Style.IsInFlow)
            {
                continue;
            }

            float inner = ReadBaseline(tree, child);
            if (!float.IsNaN(inner))
            {
                return tree.Nodes.GetRef(child).Layout.Rect.Y + inner;
            }
        }

        return float.NaN;
    }

    // Measures one axis of a child.
    private static float MeasureChild(
        UiTree tree,
        NodeHandle child,
        OptionalSize known,
        OptionalSize parentSize,
        AvailableSize available,
        SizingMode sizingMode,
        bool horizontal
    )
    {
        var input = new LayoutInput(
            known,
            true,
            true,
            parentSize,
            available,
            sizingMode,
            horizontal ? RequestedAxis.Horizontal : RequestedAxis.Vertical
        );
        Size size = LayoutChild(tree, child, in input, commit: false);
        return horizontal ? size.Width : size.Height;
    }

    private static Size Compute(UiTree tree, NodeHandle handle, in LayoutInput input, bool commit)
    {
        // Copy what the algorithm needs; the ref is taken again after the recursion (design §2.5).
        ref UiNode before = ref tree.Nodes.GetRef(handle);
        ulong epochBefore = before.Dirty.Epoch;
        LayoutStyle style = before.Layout.Style;
        MeasureFunc? measure = before.Layout.Measure;
        NodeHandle firstChild = before.FirstChild;

        // A hidden node takes no room and lays out nothing (row L2).
        if (style.Hidden)
        {
            if (commit)
            {
                HideSubtree(tree, handle);
            }

            return default;
        }

        tree.LayoutRunHook?.Invoke(handle);
        float baseline = float.NaN;
        NodeHandle baselineChild = default;
        Size size;
        if (firstChild.IsNone)
        {
            size = ComputeLeaf(in style, measure, in input, commit, out baseline);
        }
        else if (style.Algorithm is LayoutAlgorithm.Flex flex)
        {
            size = ComputeFlex(tree, in style, flex.Props, firstChild, in input, commit, out baselineChild);
        }
        else
        {
            size = ComputeBlock(tree, in style, ((LayoutAlgorithm.Block)style.Algorithm).Props, firstChild, in input, commit);
            baselineChild = firstChild;
        }

        ref UiNode n = ref tree.Nodes.GetRef(handle);
        if (!commit)
        {
            if (tree.MeasuresOf(handle).Store(LayoutCacheKey.Of(in n, in input), input.Axis, size))
            {
                NoteOverwrite(tree, handle);
            }

            return size;
        }

        bool sizeChanged = n.Layout.Rect.Width != size.Width || n.Layout.Rect.Height != size.Height;
        n.Layout.Rect = n.Layout.Rect with { Width = size.Width, Height = size.Height };
        n.Layout.LastInput = input;
        n.Layout.WasLaidOut = true;
        n.Layout.FirstBaseline = baseline;
        n.BaselineChild = baselineChild;
        n.Layout.BaselineFromFirstChildWithOne = !firstChild.IsNone && style.Algorithm is LayoutAlgorithm.Block;
        n.Layout.CachedComputedBoundary = input.IsExact;

        // Marked again while it was laid out: the flags stay, and its new request runs in the next cycle.
        if (n.Dirty.Epoch == epochBefore)
        {
            n.Dirty.SelfDirty &= ~DirtyFlags.Layout;
            n.Dirty.SubtreeDirty &= ~DirtyFlags.Layout;
        }

        if (sizeChanged)
        {
            OnRectChanged(tree, handle);
        }

        // Written last: OnRectChanged of the children advanced DescendantLayout during the algorithm.
        ref UiNode after = ref tree.Nodes.GetRef(handle);
        after.Layout.CachedKey = LayoutCacheKey.Of(in after, in input);

        // This run moved the children, which advanced DescendantLayout, but a measurement does not depend on where the
        // children were placed: a change of content inside drops the cache. Re-stamp the measurements so they stay valid.
        tree.MeasuresOf(handle).Restamp(after.Versions.DescendantLayout);
        return size;
    }

    // A value the parent read may be among the ones overwritten: a change of the node's content then goes on to the parent
    // without a comparison while the parent keeps its results (design §2.4.1, row L5).
    private static void NoteOverwrite(UiTree tree, NodeHandle handle)
    {
        NodeHandle parent = tree.Nodes.GetRef(handle).Parent;
        if (!parent.IsNone)
        {
            tree.MeasuresOf(handle).EvictedGeneration = tree.Nodes.GetRef(parent).Layout.CacheGeneration;
        }
    }

    private static void Place(UiTree tree, NodeHandle handle, float x, float y)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        if (node.Layout.Rect.X != x || node.Layout.Rect.Y != y)
        {
            node.Layout.Rect = node.Layout.Rect with { X = x, Y = y };
            OnRectChanged(tree, handle);
        }
    }

    // The node's rectangle changed: advance its version and the descendant version of its ancestors up to the nearest
    // boundary, which stops the change (design §2.4, DescendantLayoutVersion).
    private static void OnRectChanged(UiTree tree, NodeHandle handle)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        node.Versions.Layout++;
        NodeHandle ancestor = node.Parent;
        while (!ancestor.IsNone)
        {
            ref UiNode a = ref tree.Nodes.GetRef(ancestor);
            a.Versions.DescendantLayout++;
            if (Invalidation.IsLayoutBoundaryForInvalidation(tree, in a))
            {
                return;
            }

            ancestor = a.Parent;
        }
    }

    // A style's lengths in pixels where it uses no percentage (LayoutState.Box); none otherwise.
    internal static bool TryResolveBox(in LayoutStyle style, out ResolvedBox box)
    {
        // With an aspect ratio, the maximum depends on who resolves it: a leaf does not apply the ratio to it (Taffy). A keyword
        // limit is resolved by measuring the node.
        if (style.UsesPercentOfParentWidth || style.UsesPercentOfParentHeight || style.HasAspectRatio || style.HasSizingKeywordLimit)
        {
            box = default;
            return false;
        }

        box = new ResolvedBox(
            StyleSize(in style, OptionalSize.None),
            MinSize(in style, OptionalSize.None),
            MaxSize(in style, OptionalSize.None),
            EdgeInsets.ResolveOrZero(style.Margin, float.NaN),
            PaddingBorder(in style, float.NaN)
        );
        return true;
    }

    // The width and the height of the style against the parent's content box.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize StyleSize(in LayoutStyle style, OptionalSize parent) =>
        WithAspectRatio(new(style.Width.Resolve(parent.Width), style.Height.Resolve(parent.Height)), style.AspectRatio);

    // The minimum and the maximum follow the aspect ratio too (Taffy's maybe_apply_aspect_ratio on every resolved size), except
    // the maximum that clamps a stretched item (MaxSizeIgnoringAspectRatio).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize MinSize(in LayoutStyle style, OptionalSize parent) =>
        WithAspectRatio(new(style.MinWidth.Resolve(parent.Width), style.MinHeight.Resolve(parent.Height)), style.AspectRatio);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize MaxSize(in LayoutStyle style, OptionalSize parent) =>
        WithAspectRatio(MaxSizeIgnoringAspectRatio(in style, parent), style.AspectRatio);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize MaxSizeIgnoringAspectRatio(in LayoutStyle style, OptionalSize parent) =>
        new(style.MaxWidth.Resolve(parent.Width), style.MaxHeight.Resolve(parent.Height));

    // An axis that is not decided follows the other through the aspect ratio, when there is one (CSS's aspect-ratio).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize WithAspectRatio(OptionalSize size, float ratio)
    {
        if (!(ratio > 0))
        {
            return size;
        }

        bool width = !float.IsNaN(size.Width);
        bool height = !float.IsNaN(size.Height);
        return width && !height ? new OptionalSize(size.Width, size.Width / ratio)
            : height && !width ? new OptionalSize(size.Height * ratio, size.Height)
            : size;
    }

    // The padding and the border, which CSS resolves against the parent's width on every side.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Edges PaddingBorder(in LayoutStyle style, float parentWidth) =>
        EdgeInsets.ResolveOrZero(style.Padding, parentWidth) + EdgeInsets.ResolveOrZero(style.Border, parentWidth);

    // The size an axis has when its minimum is at least its maximum: the minimum wins (CSS).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize MinMaxDefinite(OptionalSize min, OptionalSize max) =>
        new(
            !float.IsNaN(min.Width) && !float.IsNaN(max.Width) && max.Width <= min.Width ? min.Width : float.NaN,
            !float.IsNaN(min.Height) && !float.IsNaN(max.Height) && max.Height <= min.Height ? min.Height : float.NaN
        );

    // A size keyword of a style resolved against the space it may stretch into (Taffy's resolve_sizing_keyword): the available
    // space to measure the node under, or none for a length that is not a content keyword, or whose space is unknown (NaN).
    // Stretch takes its size from StretchOf without measuring.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static AvailableSpace? MeasureSpaceOf(Length length, float stretch) =>
        length.Kind switch
        {
            LengthKind.MinContent => AvailableSpace.MinContent,
            LengthKind.MaxContent => AvailableSpace.MaxContent,
            LengthKind.FitContent when !float.IsNaN(stretch) => AvailableSpace.Definite(stretch),
            _ => null,
        };

    // The size a stretch length takes: the space it may stretch into, or NaN for another length or an unknown space (Taffy's
    // resolve_sizing_keyword, the exact resolution).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float StretchOf(Length length, float stretch) => length.Kind == LengthKind.Stretch ? stretch : float.NaN;

    // The minimum and the maximum that the sizing keywords of a node give it (row L3), in place of the ones its other lengths
    // gave: stretch takes the space the node may stretch into (NaN where unknown), and a content keyword measures the node's
    // content on that axis. Chrome counts a content keyword in a height only on a column's main axis (contentHeights); elsewhere
    // it is the initial value, as is a keyword whose space is unknown. A resolved limit goes on through the aspect ratio, as
    // MinSize and MaxSize take a fixed one (Chrome: a minimum width of max-content gives the height its ratio). The limits are
    // copied out of the style first: no ref into the arena is held across the measuring runs (design §2.5).
    private static void ResolveKeywordLimits(
        UiTree tree,
        NodeHandle node,
        KeywordLimits limits,
        OptionalSize known,
        OptionalSize parentSize,
        Size stretch,
        bool contentHeights,
        ref OptionalSize min,
        ref OptionalSize max
    )
    {
        min = new OptionalSize(
            KeywordLimit(tree, node, limits.MinWidth, true, known, parentSize, stretch, contentHeights, min.Width),
            KeywordLimit(tree, node, limits.MinHeight, false, known, parentSize, stretch, contentHeights, min.Height)
        );
        max = new OptionalSize(
            KeywordLimit(tree, node, limits.MaxWidth, true, known, parentSize, stretch, contentHeights, max.Width),
            KeywordLimit(tree, node, limits.MaxHeight, false, known, parentSize, stretch, contentHeights, max.Height)
        );
        min = WithAspectRatio(min, limits.AspectRatio);
        max = WithAspectRatio(max, limits.AspectRatio);
    }

    private static float KeywordLimit(
        UiTree tree,
        NodeHandle node,
        Length limit,
        bool horizontal,
        OptionalSize known,
        OptionalSize parentSize,
        Size stretch,
        bool contentHeights,
        float other
    )
    {
        if (!limit.IsSizingKeyword)
        {
            return other;
        }

        float axisStretch = horizontal ? stretch.Width : stretch.Height;
        if (limit.Kind == LengthKind.Stretch)
        {
            return axisStretch;
        }

        if ((!horizontal && !contentHeights) || MeasureSpaceOf(limit, axisStretch) is not { } space)
        {
            return float.NaN;
        }

        if (horizontal)
        {
            var widthSpace = new AvailableSize(space, AvailableSpace.MaxContent);
            return MeasureChild(tree, node, known with { Width = float.NaN }, parentSize, widthSpace, SizingMode.ContentSize, horizontal: true);
        }

        float width = Maybe.Or(known.Width, stretch.Width);
        var heightSpace = new AvailableSize(float.IsNaN(width) ? AvailableSpace.MaxContent : AvailableSpace.Definite(width), space);
        return MeasureChild(tree, node, known with { Height = float.NaN }, parentSize, heightSpace, SizingMode.ContentSize, horizontal: false);
    }

    // The minimum and the maximum of a style, with its aspect ratio, copied out of it to be resolved as sizing keywords
    // (ResolveKeywordLimits).
    private readonly record struct KeywordLimits(Length MinWidth, Length MinHeight, Length MaxWidth, Length MaxHeight, float AspectRatio)
    {
        public static KeywordLimits Of(in LayoutStyle style) =>
            new(style.MinWidth, style.MinHeight, style.MaxWidth, style.MaxHeight, style.AspectRatio);
    }
}
