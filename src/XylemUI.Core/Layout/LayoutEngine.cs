using System.Buffers;
using System.Runtime.InteropServices;

namespace XylemUI;

/// <summary>
/// Lays out the dirty parts of a <see cref="UiTree"/>: the worklist of layout roots, the cache, and the Block and Flex
/// algorithms (design §2.4.1, §3.2).
/// </summary>
/// <remarks>
/// <para>
/// Layout is a pure computation over what is already written on the nodes. It creates no node (the arena forbids it) and reads
/// no signal (the reactive runtime fails the invariant).
/// </para>
/// <para>
/// A size given as <see cref="Length.Auto"/> is: for the width, all of the available width when it is finite, otherwise the
/// content's; for the height, the content's. A leaf with a measure function takes its content's size on both axes. Every size
/// is clamped into the constraint.
/// </para>
/// <para>
/// A node is laid out in one of two modes. Measuring returns the size the node would take for a constraint and writes nothing
/// on the node (a flex container asks this of its children before it divides the space). Laying out writes the size, the
/// children's positions, and the cache, and clears the dirty flags. Each mode has its own cache entry, so a clean child that is
/// measured and then laid out every frame hits both (the same split as Taffy's compute-size and perform-layout runs).
/// </para>
/// </remarks>
internal static class LayoutEngine
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
                        _ = Compute(tree, request.Node, ResolveAvailableSpace(tree, request), commit: true);
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

    private static AvailableSpace ResolveAvailableSpace(UiTree tree, in LayoutRequest request)
    {
        if (request.AvailableSpace is { } space)
        {
            return space;
        }

        // IntrinsicSizeChanged: the parent is clean, so the constraint it gave last time still holds.
        ref UiNode node = ref tree.Nodes.GetRef(request.Node);
        if (node.Layout.CachedKey is null)
        {
            Invariant.Fail("A layout root that was never laid out has a request without a constraint.");
        }

        return node.Layout.LastAvailableSpace;
    }

    // A clean node keeps the result of its last run with the same key: a layout for either mode, a measurement for measuring.
    // Any other node is computed.
    private static Size LayoutChild(UiTree tree, NodeHandle handle, AvailableSpace available, bool commit)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        if (((node.Dirty.SelfDirty | node.Dirty.SubtreeDirty) & DirtyFlags.Layout) == 0)
        {
            LayoutCacheKey key = KeyOf(in node, available);
            if (node.Layout.CachedKey == key)
            {
                return new Size(node.Layout.Rect.Width, node.Layout.Rect.Height);
            }

            if (!commit && node.Layout.MeasureKey == key)
            {
                return node.Layout.MeasuredSize;
            }
        }

        return Compute(tree, handle, available, commit);
    }

    private static Size Compute(UiTree tree, NodeHandle handle, AvailableSpace available, bool commit)
    {
        // Copy what the algorithm needs; the ref is taken again after the recursion (design §2.5).
        ref UiNode before = ref tree.Nodes.GetRef(handle);
        ulong epochBefore = before.Dirty.Epoch;
        LayoutStyle style = before.Layout.Style;
        MeasureFunc? measure = before.Layout.Measure;
        NodeHandle firstChild = before.FirstChild;

        tree.LayoutRunHook?.Invoke(handle);
        Size size =
            measure is not null && firstChild.IsNone ? MeasureLeaf(in style, measure, available)
            : style.Algorithm is LayoutAlgorithm.Flex flex ? LayoutFlex(tree, in style, flex.Direction, firstChild, available, commit)
            : LayoutBlock(tree, in style, firstChild, available, commit);

        ref UiNode n = ref tree.Nodes.GetRef(handle);
        if (!commit)
        {
            n.Layout.MeasureKey = KeyOf(in n, available);
            n.Layout.MeasuredSize = size;
            return size;
        }

        bool sizeChanged = n.Layout.Rect.Width != size.Width || n.Layout.Rect.Height != size.Height;
        n.Layout.Rect = n.Layout.Rect with { Width = size.Width, Height = size.Height };
        n.Layout.LastAvailableSpace = available;
        n.Layout.CachedComputedBoundary = available.IsTight;

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
        after.Layout.CachedKey = KeyOf(in after, available);

        // This run moved the children, which advanced DescendantLayout, but a measurement does not depend on where the
        // children were placed: a change of content inside always marks the path dirty. Re-stamp it so it stays valid.
        if (after.Layout.MeasureKey is { } measureKey)
        {
            after.Layout.MeasureKey = measureKey with { DescendantLayoutVersion = after.Versions.DescendantLayout };
        }

        return size;
    }

    private static LayoutCacheKey KeyOf(in UiNode node, AvailableSpace available) =>
        new(available, node.Versions.LayoutStyle, node.Versions.Children, node.Versions.DescendantLayout);

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
            if (Invalidation.IsLayoutBoundaryForInvalidation(in a))
            {
                return;
            }

            ancestor = a.Parent;
        }
    }

    private static Size MeasureLeaf(in LayoutStyle style, MeasureFunc measure, AvailableSpace available)
    {
        EdgeInsets padding = style.Padding;
        var content = new AvailableSpace(
            Math.Max(0, available.MinWidth - padding.Horizontal),
            Math.Max(0, available.MaxWidth - padding.Horizontal),
            Math.Max(0, available.MinHeight - padding.Vertical),
            Math.Max(0, available.MaxHeight - padding.Vertical)
        );
        Size measured = measure(content);
        float width = style.Width.IsFixed ? style.Width.Pixels : measured.Width + padding.Horizontal;
        float height = style.Height.IsFixed ? style.Height.Pixels : measured.Height + padding.Vertical;
        return new Size(available.ClampWidth(width), available.ClampHeight(height));
    }

    // The width or the height the node has before its children are laid out, or NaN when its content decides it.
    private static float DeterminedWidth(in LayoutStyle style, AvailableSpace available) =>
        style.Width.IsFixed ? available.ClampWidth(style.Width.Pixels)
        : float.IsFinite(available.MaxWidth) ? available.MaxWidth
        : float.NaN;

    private static float DeterminedHeight(in LayoutStyle style, AvailableSpace available) =>
        style.Height.IsFixed ? available.ClampHeight(style.Height.Pixels)
        : available.MinHeight == available.MaxHeight ? available.MinHeight
        : float.NaN;

    private static Size LayoutBlock(UiTree tree, in LayoutStyle style, NodeHandle firstChild, AvailableSpace available, bool commit)
    {
        EdgeInsets padding = style.Padding;
        float width = DeterminedWidth(in style, available);
        float contentWidth = float.IsNaN(width) ? float.PositiveInfinity : Math.Max(0, width - padding.Horizontal);

        float y = padding.Top;
        float widestChild = 0;
        NodeHandle child = firstChild;
        while (!child.IsNone)
        {
            ref UiNode c = ref tree.Nodes.GetRef(child);
            NodeHandle next = c.NextSibling;
            EdgeInsets margin = c.Layout.Style.Margin;

            var childSpace = new AvailableSpace(0, Math.Max(0, contentWidth - margin.Horizontal), 0, float.PositiveInfinity);
            Size size = LayoutChild(tree, child, childSpace, commit);
            if (commit)
            {
                Place(tree, child, padding.Left + margin.Left, y + margin.Top);
            }

            y += margin.Top + size.Height + margin.Bottom;
            widestChild = Math.Max(widestChild, size.Width + margin.Horizontal);
            child = next;
        }

        if (float.IsNaN(width))
        {
            width = available.ClampWidth(widestChild + padding.Horizontal);
        }

        float height = DeterminedHeight(in style, available);
        if (float.IsNaN(height))
        {
            height = available.ClampHeight(y + padding.Bottom);
        }

        return new Size(width, height);
    }

    private static Size LayoutFlex(
        UiTree tree,
        in LayoutStyle style,
        FlexDirection direction,
        NodeHandle firstChild,
        AvailableSpace available,
        bool commit
    )
    {
        bool row = direction == FlexDirection.Row;
        EdgeInsets padding = style.Padding;
        float paddingMain = row ? padding.Horizontal : padding.Vertical;
        float paddingCross = row ? padding.Vertical : padding.Horizontal;
        float width = DeterminedWidth(in style, available);
        float height = DeterminedHeight(in style, available);
        float main = row ? width : height;
        float cross = row ? height : width;
        float innerMain = float.IsNaN(main) ? float.PositiveInfinity : Math.Max(0, main - paddingMain);
        float crossLimit = float.IsNaN(cross) ? (row ? available.MaxHeight : available.MaxWidth) : cross;
        float innerCross = Math.Max(0, crossLimit - paddingCross);

        int count = 0;
        for (NodeHandle c = firstChild; !c.IsNone; c = tree.Nodes.GetRef(c).NextSibling)
        {
            count++;
        }

        float[] bases = ArrayPool<float>.Shared.Rent(Math.Max(1, count));
        try
        {
            // 1. The hypothetical main size of each child: its basis, its fixed main size, or its content.
            float usedMain = 0;
            float totalGrow = 0;
            float totalScaledShrink = 0;
            int i = 0;
            for (NodeHandle child = firstChild; !child.IsNone; i++)
            {
                ref UiNode c = ref tree.Nodes.GetRef(child);
                NodeHandle next = c.NextSibling;
                LayoutStyle childStyle = c.Layout.Style;
                EdgeInsets margin = childStyle.Margin;
                float marginCross = row ? margin.Vertical : margin.Horizontal;
                Length mainLength = row ? childStyle.Width : childStyle.Height;

                float basis;
                if (childStyle.FlexBasis.IsFixed)
                {
                    basis = childStyle.FlexBasis.Pixels;
                }
                else if (mainLength.IsFixed)
                {
                    basis = mainLength.Pixels;
                }
                else
                {
                    float crossMax = Math.Max(0, innerCross - marginCross);
                    AvailableSpace measureSpace = row
                        ? new AvailableSpace(0, float.PositiveInfinity, 0, crossMax)
                        : new AvailableSpace(0, crossMax, 0, float.PositiveInfinity);
                    Size measured = LayoutChild(tree, child, measureSpace, commit: false);
                    basis = row ? measured.Width : measured.Height;
                }

                bases[i] = basis;
                usedMain += basis + (row ? margin.Horizontal : margin.Vertical);
                totalGrow += childStyle.FlexGrow;
                totalScaledShrink += childStyle.FlexShrink * basis;
                child = next;
            }

            // 2. Divide the free space by grow, or the overflow by shrink weighted by the basis.
            float free = float.IsFinite(innerMain) ? innerMain - usedMain : 0;

            // 3. Lay out each child at its final main size and place it.
            float cursor = row ? padding.Left : padding.Top;
            float widestCross = 0;
            i = 0;
            for (NodeHandle child = firstChild; !child.IsNone; i++)
            {
                ref UiNode c = ref tree.Nodes.GetRef(child);
                NodeHandle next = c.NextSibling;
                LayoutStyle childStyle = c.Layout.Style;
                EdgeInsets margin = childStyle.Margin;

                float target = bases[i];
                if (free > 0 && totalGrow > 0)
                {
                    target += free * childStyle.FlexGrow / totalGrow;
                }
                else if (free < 0 && totalScaledShrink > 0)
                {
                    target += free * childStyle.FlexShrink * bases[i] / totalScaledShrink;
                }

                target = Math.Max(0, target);
                float crossMax = Math.Max(0, innerCross - (row ? margin.Vertical : margin.Horizontal));
                AvailableSpace finalSpace = row ? new AvailableSpace(target, target, 0, crossMax) : new AvailableSpace(0, crossMax, target, target);
                Size size = LayoutChild(tree, child, finalSpace, commit);

                float mainStart = row ? margin.Left : margin.Top;
                float mainEnd = row ? margin.Right : margin.Bottom;
                if (row)
                {
                    if (commit)
                    {
                        Place(tree, child, cursor + mainStart, padding.Top + margin.Top);
                    }

                    widestCross = Math.Max(widestCross, size.Height + margin.Vertical);
                    cursor += mainStart + size.Width + mainEnd;
                }
                else
                {
                    if (commit)
                    {
                        Place(tree, child, padding.Left + margin.Left, cursor + mainStart);
                    }

                    widestCross = Math.Max(widestCross, size.Width + margin.Horizontal);
                    cursor += mainStart + size.Height + mainEnd;
                }

                child = next;
            }

            float contentMain = cursor + (row ? padding.Right : padding.Bottom);
            if (row)
            {
                width = float.IsNaN(width) ? available.ClampWidth(contentMain) : width;
                height = float.IsNaN(height) ? available.ClampHeight(widestCross + paddingCross) : height;
            }
            else
            {
                height = float.IsNaN(height) ? available.ClampHeight(contentMain) : height;
                width = float.IsNaN(width) ? available.ClampWidth(widestCross + paddingCross) : width;
            }

            return new Size(width, height);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(bases);
        }
    }
}
