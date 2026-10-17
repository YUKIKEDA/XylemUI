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
                    if (((dirty.SelfDirty | dirty.SubtreeDirty) & DirtyFlags.Layout) == 0)
                    {
                        continue;
                    }

                    // A dirty root is never skipped by the cache. A measure function may throw: the exception is reported like an
                    // effect's, the other roots go on, and the request waits for the next pass.
                    try
                    {
                        LayoutInput input = ResolveInput(tree, request);
                        _ = Compute(tree, request.Node, in input, commit: true);
                        if (request.Node == tree.Root)
                        {
                            PlaceRoot(tree, request.Node, in input);
                        }
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

    // The root's margin box sits at the origin of its viewport.
    private static void PlaceRoot(UiTree tree, NodeHandle root, in LayoutInput input)
    {
        Edges margin = EdgeInsets.ResolveOrZero(tree.Nodes.GetRef(root).Layout.Style.Margin, input.ParentSize.Width);
        Place(tree, root, margin.Left, margin.Top);
    }

    // Lays out or measures a child. A clean child keeps the result of its last run with the same key: a layout for either mode,
    // a measurement for measuring. A measuring run reads the child's content, which the child records (design §2.4.1).
    private static Size LayoutChild(UiTree tree, NodeHandle handle, in LayoutInput input, bool commit)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        if (!commit)
        {
            node.Layout.ContentReadGeneration = tree.Nodes.GetRef(node.Parent).Layout.CacheGeneration;
        }

        if (((node.Dirty.SelfDirty | node.Dirty.SubtreeDirty) & DirtyFlags.Layout) == 0)
        {
            LayoutCacheKey key = LayoutCacheKey.Of(in node, in input);
            if (node.Layout.WasLaidOut && node.Layout.CachedKey.Matches(in key))
            {
                return new Size(node.Layout.Rect.Width, node.Layout.Rect.Height);
            }

            if (!commit && tree.MeasuresOf(handle).TryGet(in key, input.Axis, out Size measured))
            {
                return measured;
            }
        }

        return Compute(tree, handle, in input, commit);
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

        tree.LayoutRunHook?.Invoke(handle);
        float baseline;
        Size size =
            firstChild.IsNone ? ComputeLeaf(in style, measure, in input, commit, out baseline)
            : style.Algorithm is LayoutAlgorithm.Flex flex ? ComputeFlex(tree, in style, flex.Props, firstChild, in input, commit, out baseline)
            : ComputeBlock(tree, in style, ((LayoutAlgorithm.Block)style.Algorithm).Props, firstChild, in input, commit, out baseline);

        ref UiNode n = ref tree.Nodes.GetRef(handle);
        if (!commit)
        {
            tree.MeasuresOf(handle).Store(LayoutCacheKey.Of(in n, in input), input.Axis, size);
            return size;
        }

        bool sizeChanged = n.Layout.Rect.Width != size.Width || n.Layout.Rect.Height != size.Height;
        n.Layout.Rect = n.Layout.Rect with { Width = size.Width, Height = size.Height };
        n.Layout.LastInput = input;
        n.Layout.WasLaidOut = true;
        n.Layout.FirstBaseline = baseline;
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

    // The width and the height of the style against the parent's content box.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize StyleSize(in LayoutStyle style, OptionalSize parent) =>
        new(style.Width.Resolve(parent.Width), style.Height.Resolve(parent.Height));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize MinSize(in LayoutStyle style, OptionalSize parent) =>
        new(style.MinWidth.Resolve(parent.Width), style.MinHeight.Resolve(parent.Height));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static OptionalSize MaxSize(in LayoutStyle style, OptionalSize parent) =>
        new(style.MaxWidth.Resolve(parent.Width), style.MaxHeight.Resolve(parent.Height));

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

    // A size keyword of a style resolved against the space it may stretch into (Taffy's resolve_sizing_keyword): the
    // available space to measure the node under, or none for a length that is not a content keyword.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static AvailableSpace? MeasureSpaceOf(Length length, float stretch) =>
        length.Kind switch
        {
            LengthKind.MinContent => AvailableSpace.MinContent,
            LengthKind.MaxContent => AvailableSpace.MaxContent,
            LengthKind.FitContent when !float.IsNaN(stretch) => AvailableSpace.Definite(stretch),
            _ => null,
        };
}
