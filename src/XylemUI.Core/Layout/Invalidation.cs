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
    /// Marks the work a change on <paramref name="node"/> needs.
    /// </summary>
    public static void Invalidate(UiTree tree, NodeHandle node, ChangeKind kind) => MarkDirty(tree, node, FlagsFor(kind));

    /// <summary>
    /// Sets <see cref="DirtyState.SelfDirty"/> on <paramref name="origin"/>. A layout change also sets
    /// <see cref="DirtyState.SubtreeDirty"/> from the origin up to the nearest layout boundary or the root, drops the
    /// measurements of every node on the way (they depended on what changed), and makes that node a layout root. Other changes
    /// go to the paint updates.
    /// </summary>
    public static void MarkDirty(UiTree tree, NodeHandle origin, DirtyFlags flags)
    {
        tree.Nodes.GetRef(origin).Dirty.SelfDirty |= flags;
        if ((flags & DirtyFlags.Layout) == 0)
        {
            tree.PaintUpdates.Add(origin);
            return;
        }

        NodeHandle current = origin;
        while (true)
        {
            ref UiNode n = ref tree.Nodes.GetRef(current);
            n.Dirty.SubtreeDirty |= flags;
            n.Dirty.Epoch++;
            DropCaches(ref n);
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
    public static void NotifyWindowResized(UiTree tree, NodeHandle root, Size size)
    {
        tree.SetRoot(root);
        MarkDirty(tree, root, DirtyFlags.Layout);
        OverwriteLayoutRequest(tree, root, LayoutInput.Exact(size), LayoutReason.WindowResize);
    }

    /// <summary>
    /// Lays out the root in a viewport whose axes may be content constraints, as CSS lays out the root element in the initial
    /// containing block (Taffy's in-flow root). The layout tests that take Chrome's results use it; a window gives an exact
    /// size (<see cref="NotifyWindowResized"/>).
    /// </summary>
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
    public static bool IsLayoutBoundaryForInvalidation(UiTree tree, in UiNode node) =>
        (node.Layout.Style.IsStructuralBoundary || node.Layout.CachedComputedBoundary) && !ContentReadByParent(tree, in node);

    /// <summary>
    /// Returns a value indicating whether the parent of <paramref name="node"/> read its content (a measuring run or a
    /// baseline) for a result it still caches.
    /// </summary>
    public static bool ContentReadByParent(UiTree tree, in UiNode node) =>
        !node.Parent.IsNone
        && node.Layout.ContentReadGeneration != 0
        && node.Layout.ContentReadGeneration == tree.Nodes.GetRef(node.Parent).Layout.CacheGeneration;

    // A dirty node's measurements depended on what changed, and its children's marks of a read by it lapse with the
    // generation. Its layout entry needs no clearing: a dirty node never reads it, and its next layout run replaces it.
    private static void DropCaches(ref UiNode node)
    {
        node.Layout.Measures.Clear();
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
    }

    /// <summary>
    /// Puts a request back after its layout threw. When the node has a request already (made during the pass), the stronger
    /// reason wins with its constraint, the rule of a window resize over a content change; otherwise the request is added.
    /// </summary>
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
    }
}
