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
    /// <see cref="DirtyState.SubtreeDirty"/> from the origin up to the nearest layout boundary or the root, and makes that node
    /// a layout root. Other changes go to the paint updates.
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
            if (IsLayoutBoundaryForInvalidation(in n) || n.Parent.IsNone)
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
        OverwriteLayoutRequest(tree, root, AvailableSpace.Definite(size), LayoutReason.WindowResize);
    }

    /// <summary>
    /// Returns a value indicating whether a change inside <paramref name="node"/> stops there: its size cannot depend on its
    /// content, by its style or by the tight constraint of its last layout. This frame's constraint does not exist yet.
    /// </summary>
    public static bool IsLayoutBoundaryForInvalidation(in UiNode node) =>
        node.Layout.Style.IsStructuralBoundary || node.Layout.CachedComputedBoundary;

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
    /// Puts a request back after its layout threw: merged into a request for the same node, otherwise added.
    /// </summary>
    internal static void RestoreRequest(UiTree tree, LayoutRequest request)
    {
        foreach (LayoutRequest existing in CollectionsMarshal.AsSpan(tree.LayoutRoots))
        {
            if (existing.Node == request.Node)
            {
                return;
            }
        }

        tree.LayoutRoots.Add(request);
    }

    private static void OverwriteLayoutRequest(UiTree tree, NodeHandle node, AvailableSpace space, LayoutReason reason)
    {
        Span<LayoutRequest> roots = CollectionsMarshal.AsSpan(tree.LayoutRoots);
        for (int i = 0; i < roots.Length; i++)
        {
            ref LayoutRequest r = ref roots[i];
            if (r.Node == node)
            {
                r.AvailableSpace = space;
                r.Reason = reason;
                return;
            }
        }

        tree.LayoutRoots.Add(
            new LayoutRequest
            {
                Node = node,
                AvailableSpace = space,
                Reason = reason,
            }
        );
    }
}
