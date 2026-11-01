using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XylemUI;

/// <summary>
/// Turns a change on a node into the work it needs: dirty flags, and a layout root at the nearest boundary (design §2.4,
/// §2.4.1).
/// </summary>
internal static class Invalidation
{
    /// <summary>
    /// Returns the stages a change needs (design §2.4, the dependency matrix).
    /// </summary>
    /// <param name="kind">The kind of change.</param>
    /// <returns>The stages that <paramref name="kind"/> makes dirty.</returns>
    public static DirtyFlags FlagsFor(ChangeKind kind) =>
        kind switch
        {
            ChangeKind.TextContent => DirtyFlags.Text | DirtyFlags.Layout | DirtyFlags.Paint | DirtyFlags.HitTest,
            ChangeKind.Font => DirtyFlags.Text | DirtyFlags.Layout | DirtyFlags.Paint,
            ChangeKind.Size => DirtyFlags.Layout | DirtyFlags.Paint | DirtyFlags.HitTest,
            ChangeKind.Color => DirtyFlags.Paint,
            ChangeKind.TransformProp => DirtyFlags.Transform | DirtyFlags.Paint | DirtyFlags.HitTest,
            ChangeKind.A11yLabel => DirtyFlags.Semantics,
            _ => throw new UnreachableException(),
        };

    /// <summary>
    /// Marks the work a change on <paramref name="node"/> needs. A change of a laid-out leaf's content marks only the leaf
    /// (<see cref="TryMarkContentChange"/>); other changes mark the path to the boundary (<see cref="MarkDirty"/>).
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="node"/>.</param>
    /// <param name="node">The live node that changed.</param>
    /// <param name="kind">The kind of change.</param>
    public static void Invalidate(UiTree tree, NodeHandle node, ChangeKind kind)
    {
        DirtyFlags flags = FlagsFor(kind);

        // A change the layout reads may make the node measure differently: its next failure is reported again, even while it is
        // still dirty from one. A change of color or the like does not (design §2.4.1).
        if ((flags & DirtyFlags.Layout) != 0)
        {
            tree.Nodes.GetRef(node).Layout.ReportedFailure = null;
        }

        if (kind is ChangeKind.TextContent or ChangeKind.Font && TryMarkContentChange(tree, node, flags))
        {
            return;
        }

        MarkDirty(tree, node, flags);
    }

    /// <summary>
    /// Marks a change of content on a leaf that its parent may not need to see (design §2.4.1, row L5): the leaf alone is made
    /// dirty and a layout root, and what the parent read from it is kept in a <see cref="ContentSnapshot"/>, for the layout
    /// pass to compare with the leaf's new values. Its repaint goes to the paint updates, as a change of color does: the
    /// ancestors get no mark, so their flags do not tell that the leaf needs painting. Returns <see langword="false"/>,
    /// marking nothing, when the change must take <see cref="MarkDirty"/>: the leaf was never laid out, is not attached, is
    /// the root or a boundary (the change stops at it anyway), is dirty from another change, or overwrote a value its parent
    /// read (<see cref="MeasureCache.EvictedGeneration"/>).
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="handle"/>.</param>
    /// <param name="handle">The live leaf whose content changed.</param>
    /// <param name="flags">The stages the change needs (<see cref="FlagsFor"/>).</param>
    /// <returns><see langword="true"/> if the leaf alone was marked; otherwise, <see langword="false"/>.</returns>
    public static bool TryMarkContentChange(UiTree tree, NodeHandle handle, DirtyFlags flags)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        if (((node.Dirty.SelfDirty | node.Dirty.SubtreeDirty) & DirtyFlags.Layout) != 0)
        {
            // A second change before the pass compares with the values of the last layout, kept at the first.
            int index = tree.FindContentSnapshot(handle);
            if (index < 0)
            {
                return false;
            }

            CollectionsMarshal.AsSpan(tree.ContentSnapshots)[index].Flags |= flags;
            MarkSelfForPaint(tree, handle, ref node, flags);
            node.Dirty.SubtreeDirty |= flags;
            return true;
        }

        if (
            !node.IsAttached
            || !node.Layout.WasLaidOut
            || node.Parent.IsNone
            || !node.FirstChild.IsNone
            || IsLayoutBoundaryForInvalidation(tree, in node)
        )
        {
            return false;
        }

        ref MeasureCache measures = ref tree.MeasuresOf(handle);
        if (measures.EvictedGeneration != 0 && measures.EvictedGeneration == tree.Nodes.GetRef(node.Parent).Layout.CacheGeneration)
        {
            return false;
        }

        tree.AddContentSnapshot(
            new ContentSnapshot
            {
                Node = handle,
                Flags = flags,
                Size = new Size(node.Layout.Rect.Width, node.Layout.Rect.Height),
                FirstBaseline = node.Layout.FirstBaseline,
                Measures = measures,
            }
        );
        MarkSelfForPaint(tree, handle, ref node, flags);
        node.Dirty.SubtreeDirty |= flags;
        node.Dirty.Epoch++;
        DropCaches(tree, handle, ref node);
        EnqueueLayoutRoot(tree, handle);
        return true;
    }

    /// <summary>
    /// Sets <see cref="DirtyState.SelfDirty"/> on <paramref name="origin"/>. The flags other than layout go to the paint updates, with
    /// a layout change too. A layout change also sets <see cref="DirtyState.SubtreeDirty"/> from the origin up to the nearest layout
    /// boundary or the root, drops the measurements of every node on the way (they depended on what changed), and makes that node a
    /// layout root.
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="origin"/>.</param>
    /// <param name="origin">The live node that changed.</param>
    /// <param name="flags">The stages the change needs.</param>
    public static void MarkDirty(UiTree tree, NodeHandle origin, DirtyFlags flags)
    {
        ref UiNode node = ref tree.Nodes.GetRef(origin);
        DirtyFlags paint = flags & ~DirtyFlags.Layout;
        if (paint != 0)
        {
            // Only a frame that takes the node from the paint updates clears these flags. Set without the list, they stayed after the
            // layout pass, and a later change of color found them set and asked for no frame (Issue #324).
            MarkSelfForPaint(tree, origin, ref node, paint);
        }

        if ((flags & DirtyFlags.Layout) == 0)
        {
            return;
        }

        node.Layout.ReportedFailure = null;
        node.Dirty.SelfDirty |= DirtyFlags.Layout;
        MarkPath(tree, origin, flags);
    }

    // Sets the node's own flags, and records it in the paint updates unless a stage other than layout was pending already: it
    // waits there once until the paint stage takes the list and the flags, so the list holds at most one entry a node, however
    // often the node changes (PR #112's review).
    private static void MarkSelfForPaint(UiTree tree, NodeHandle handle, ref UiNode node, DirtyFlags flags)
    {
        if ((node.Dirty.SelfDirty & ~DirtyFlags.Layout) == 0)
        {
            tree.PaintUpdates.Add(handle);
            tree.NoteWork();
        }

        node.Dirty.SelfDirty |= flags;
    }

    /// <summary>
    /// Sets <see cref="DirtyState.SubtreeDirty"/> from <paramref name="from"/> up to the nearest layout boundary or the root,
    /// drops the measurements of every node on the way, and makes that node a layout root: the part of
    /// <see cref="MarkDirty"/> for a layout change, which a leaf whose content changed also takes from its parent when a value
    /// the parent read differs (row L5).
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="from"/>.</param>
    /// <param name="from">The live node the path starts at.</param>
    /// <param name="flags">The flags to add to <see cref="DirtyState.SubtreeDirty"/> on the path.</param>
    public static void MarkPath(UiTree tree, NodeHandle from, DirtyFlags flags)
    {
        NodeHandle current = from;
        while (true)
        {
            ref UiNode n = ref tree.Nodes.GetRef(current);
            n.Dirty.SubtreeDirty |= flags;
            n.Dirty.Epoch++;
            DropCaches(tree, current, ref n);
            if (n.Parent.IsNone || IsLayoutBoundaryForInvalidation(tree, in n))
            {
                // A node not attached yet keeps its flags; appending it marks the parent, whose layout reaches it.
                if (n.IsAttached)
                {
                    EnqueueLayoutRoot(tree, current);
                }

                return;
            }

            current = n.Parent;
        }
    }

    /// <summary>
    /// Lays out the window's root at the size the operating system gave it (design §2.4.1).
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="root"/>.</param>
    /// <param name="root">The live node to make the tree's root.</param>
    /// <param name="size">The window's client size.</param>
    public static void NotifyWindowResized(UiTree tree, NodeHandle root, Size size)
    {
        tree.SetRoot(root);
        MarkDirty(tree, root, DirtyFlags.Layout);
        OverwriteLayoutRequest(tree, root, LayoutInput.Exact(size), LayoutReason.WindowResize);
    }

    /// <summary>
    /// Lays out a popup's root at the window's size (design §7.3, "The v0.2 form"): a render root of the tree, with the window's
    /// constraint.
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="root"/>.</param>
    /// <param name="root">The popup's root, attached with no parent.</param>
    /// <param name="size">The window's client size, in logical pixels.</param>
    public static void NotifyPopupResized(UiTree tree, NodeHandle root, Size size)
    {
        MarkDirty(tree, root, DirtyFlags.Layout);
        OverwriteLayoutRequest(tree, root, LayoutInput.Exact(size), LayoutReason.WindowResize);
    }

    /// <summary>
    /// Lays out the root in a viewport whose axes may be content constraints, as CSS lays out the root element in the initial
    /// containing block (Taffy's in-flow root). The layout tests that take Chrome's results use it; a window gives an exact
    /// size (<see cref="NotifyWindowResized"/>).
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="root"/>.</param>
    /// <param name="root">The live node to make the tree's root.</param>
    /// <param name="viewport">The space the root is laid out in.</param>
    internal static void NotifyViewport(UiTree tree, NodeHandle root, AvailableSize viewport)
    {
        tree.SetRoot(root);
        MarkDirty(tree, root, DirtyFlags.Layout);
        OverwriteLayoutRequest(tree, root, LayoutEngine.RootInput(tree.Nodes.GetRef(root).Layout.Style, viewport), LayoutReason.WindowResize);
    }

    /// <summary>
    /// Returns a value indicating whether a change inside <paramref name="node"/> stops there: its size cannot depend on its
    /// content, by its style or by the exact input of its last layout, and its parent did not read its content in a result
    /// the parent still keeps (design §2.4.1). This frame's input does not exist yet.
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="node"/>.</param>
    /// <param name="node">The node to check.</param>
    /// <returns><see langword="true"/> if <paramref name="node"/> is a layout boundary for a change inside it; otherwise, <see langword="false"/>.</returns>
    public static bool IsLayoutBoundaryForInvalidation(UiTree tree, in UiNode node) =>
        node.Layout.Style.Hidden
        || ((node.Layout.Style.IsStructuralBoundary || node.Layout.CachedComputedBoundary) && !ContentReadByParent(tree, in node));

    /// <summary>
    /// Returns a value indicating whether the parent of <paramref name="node"/> read its content (a measuring run or a
    /// baseline) for a result it still caches.
    /// </summary>
    /// <param name="tree">The tree that holds <paramref name="node"/>.</param>
    /// <param name="node">The node to check.</param>
    /// <returns><see langword="true"/> if the parent's cached result read the content; otherwise, <see langword="false"/>.</returns>
    public static bool ContentReadByParent(UiTree tree, in UiNode node) =>
        !node.Parent.IsNone
        && node.Layout.ContentReadGeneration != 0
        && node.Layout.ContentReadGeneration == tree.Nodes.GetRef(node.Parent).Layout.CacheGeneration;

    // A dirty node's measurements depended on what changed, and its children's marks of a read by it lapse with the
    // generation. Its layout entry needs no clearing: a dirty node never reads it, and its next layout run replaces it.
    private static void DropCaches(UiTree tree, NodeHandle handle, ref UiNode node)
    {
        tree.MeasuresOf(handle).Clear();
        node.Layout.CacheGeneration++;
    }

    // MarkDirty's request: IntrinsicSizeChanged, with the node's last constraint. A request already there for the node
    // covers it, whatever its reason (a window resize carries its own constraint and is stronger).
    private static void EnqueueLayoutRoot(UiTree tree, NodeHandle node)
    {
        foreach (LayoutRequest request in CollectionsMarshal.AsSpan(tree.LayoutRoots))
        {
            if (request.Node == node)
            {
                return;
            }
        }

        tree.LayoutRoots.Add(new LayoutRequest { Node = node, Reason = LayoutReason.IntrinsicSizeChanged });
        tree.NoteWork();
    }

    /// <summary>
    /// Puts a request back after its layout threw. When the node has a request already (made during the pass), the stronger
    /// reason wins with its constraint, the rule of a window resize over a content change; otherwise the request is added.
    /// </summary>
    /// <param name="tree">The tree whose layout roots take the request.</param>
    /// <param name="request">The request whose layout threw.</param>
    internal static void RestoreRequest(UiTree tree, LayoutRequest request)
    {
        Span<LayoutRequest> roots = CollectionsMarshal.AsSpan(tree.LayoutRoots);
        for (int i = 0; i < roots.Length; i++)
        {
            ref LayoutRequest existing = ref roots[i];
            if (existing.Node != request.Node)
            {
                continue;
            }

            if (request.Reason > existing.Reason)
            {
                existing = request;
            }

            return;
        }

        tree.LayoutRoots.Add(request);
        tree.NoteWork();
    }

    private static void OverwriteLayoutRequest(UiTree tree, NodeHandle node, LayoutInput input, LayoutReason reason)
    {
        Span<LayoutRequest> roots = CollectionsMarshal.AsSpan(tree.LayoutRoots);
        for (int i = 0; i < roots.Length; i++)
        {
            ref LayoutRequest r = ref roots[i];
            if (r.Node == node)
            {
                r.Input = input;
                r.Reason = reason;
                return;
            }
        }

        tree.LayoutRoots.Add(
            new LayoutRequest
            {
                Node = node,
                Input = input,
                Reason = reason,
            }
        );
        tree.NoteWork();
    }
}
