using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XylemUI;

/// <summary>
/// The UI tree of one window: the nodes in an arena, and the work the next frame owes them (design §2.4.1, §2.5).
/// </summary>
/// <remarks>
/// It belongs to the UI thread. Structure changes go through these methods, so that versions and dirty flags stay right.
/// </remarks>
internal sealed class UiTree
{
    private readonly Comparison<LayoutRequest> _shallowestFirst;
    private readonly List<LayoutRequest> _deferred = [];
    private List<LayoutRequest> _layoutRoots = [];
    private List<LayoutRequest> _layoutRootsSpare = [];

    // The measurement caches, by the node's slot index: apart from the nodes, so that reading a node does not load them.
    private MeasureCache[] _measures = new MeasureCache[256];

    /// <summary>
    /// Initializes a new instance of the <see cref="UiTree"/> class.
    /// </summary>
    public UiTree() => _shallowestFirst = CompareDepth;

    /// <summary>
    /// Gets the root: the node the window's size is given to (<see cref="Invalidation.NotifyWindowResized"/>); none before.
    /// </summary>
    public NodeHandle Root { get; private set; }

    /// <summary>
    /// Gets the nodes.
    /// </summary>
    internal Arena<UiNode> Nodes { get; } = new();

    /// <summary>
    /// Gets the dirty boundaries (or the root) to lay out from, with the source of their constraints.
    /// </summary>
    internal List<LayoutRequest> LayoutRoots => _layoutRoots;

    /// <summary>
    /// Gets the nodes whose change needs paint, hit testing, or semantics but no layout. Their stages come with later rows; S6b
    /// only records them.
    /// </summary>
    internal List<NodeHandle> PaintUpdates { get; } = [];

    /// <summary>
    /// Gets what the parents read from the leaves whose content changed since their last layout (row L5), one per leaf.
    /// </summary>
    internal List<ContentSnapshot> ContentSnapshots { get; } = [];

    /// <summary>
    /// Gets the cached order of the layout roots: shallowest first. Created once, so sorting allocates nothing.
    /// </summary>
    internal Comparison<LayoutRequest> ShallowestFirst => _shallowestFirst;

    /// <summary>
    /// Gets or sets a hook that runs each time a node's layout algorithm or measure function runs. Tests count layouts with it.
    /// </summary>
    internal Action<NodeHandle>? LayoutRunHook { get; set; }

    /// <summary>
    /// Creates a node that is not in the tree yet. It needs a layout. A leaf with content has <paramref name="measure"/>, and
    /// may have <paramref name="intrinsicWidths"/>, which must agree with it.
    /// </summary>
    public NodeHandle CreateNode(LayoutStyle style, MeasureFunc? measure = null, IntrinsicWidthsFunc? intrinsicWidths = null)
    {
        if (intrinsicWidths is not null && measure is null)
        {
            throw new ArgumentException("A node with intrinsic widths needs a measure function.", nameof(intrinsicWidths));
        }

        var node = new UiNode
        {
            Layout = new LayoutState
            {
                Style = style,
                UsesPercentOfParentWidth = style.UsesPercentOfParentWidth,
                UsesPercentOfParentHeight = style.UsesPercentOfParentHeight,
                HasBox = LayoutEngine.TryResolveBox(in style, out ResolvedBox box),
                HasInset = style.Inset != default,
                HasKeywordLimits = style.HasSizingKeywordLimit,
                Measure = measure,
                IntrinsicWidths = intrinsicWidths,
                FirstBaseline = float.NaN,
                CacheGeneration = 1,
            },
            Dirty = new DirtyState { SelfDirty = DirtyFlags.Layout, SubtreeDirty = DirtyFlags.Layout },
        };
        NodeHandle handle = Nodes.Allocate(in node);
        if (handle.Index >= _measures.Length)
        {
            Array.Resize(ref _measures, Math.Max(_measures.Length * 2, handle.Index + 1));
        }

        _measures[handle.Index] = default;
        _measures[handle.Index].Box = box;
        return handle;
    }

    /// <summary>
    /// Returns the measurement cache of a live node.
    /// </summary>
    internal ref MeasureCache MeasuresOf(NodeHandle node) => ref _measures[node.Index];

    /// <summary>
    /// Gets the bytes the measurement caches take: one per slot of the arena, grown by doubling (row L1a's layout scenario
    /// reports it).
    /// </summary>
    internal long MeasureCacheBytes => (long)_measures.Length * Unsafe.SizeOf<MeasureCache>();

    /// <summary>
    /// Appends <paramref name="child"/>, a node with no parent, as the last child of <paramref name="parent"/>, and marks the
    /// parent's layout dirty.
    /// </summary>
    public void AppendChild(NodeHandle parent, NodeHandle child)
    {
        ref UiNode c = ref Nodes.GetRef(child);
        if (!c.Parent.IsNone)
        {
            Invariant.Fail("AppendChild was given a node that already has a parent.");
        }

        c.Parent = parent;
        ref UiNode p = ref Nodes.GetRef(parent);
        if (p.LastChild.IsNone)
        {
            p.FirstChild = child;
        }
        else
        {
            Nodes.GetRef(p.LastChild).NextSibling = child;
        }

        p.LastChild = child;
        p.Versions.Children++;
        if (!c.Layout.Style.IsInFlow)
        {
            p.Layout.OutOfFlowChildren++;
        }

        SetDepth(child, p.Depth + 1, p.IsAttached);
        Invalidation.MarkDirty(this, parent, DirtyFlags.Layout);
    }

    /// <summary>
    /// Replaces the layout style of a node and marks it and its parent for layout: the parent's algorithm reads the style (the
    /// size, the margins, the flex factors), so a boundary does not stop the change at the node itself.
    /// </summary>
    public void SetStyle(NodeHandle node, LayoutStyle style)
    {
        ref UiNode n = ref Nodes.GetRef(node);
        if (!n.Parent.IsNone && n.Layout.Style.IsInFlow != style.IsInFlow)
        {
            Nodes.GetRef(n.Parent).Layout.OutOfFlowChildren += style.IsInFlow ? -1 : 1;
        }

        ref UiNode changed = ref Nodes.GetRef(node);
        changed.Layout.Style = style;
        changed.Layout.UsesPercentOfParentWidth = style.UsesPercentOfParentWidth;
        changed.Layout.UsesPercentOfParentHeight = style.UsesPercentOfParentHeight;
        changed.Layout.HasBox = LayoutEngine.TryResolveBox(in style, out MeasuresOf(node).Box);
        changed.Layout.HasInset = style.Inset != default;
        changed.Layout.HasKeywordLimits = style.HasSizingKeywordLimit;
        changed.Versions.LayoutStyle++;
        NodeHandle parent = changed.Parent;
        Invalidation.Invalidate(this, node, ChangeKind.Size);
        if (!parent.IsNone)
        {
            Invalidation.MarkDirty(this, parent, DirtyFlags.Layout);
        }
    }

    /// <summary>
    /// Returns the rectangle of the node's last layout.
    /// </summary>
    public Rect GetRect(NodeHandle node) => Nodes.GetRef(node).Layout.Rect;

    /// <summary>
    /// Makes <paramref name="node"/> the root, the first time a window size is given. The root has no parent, and a tree has one.
    /// </summary>
    internal void SetRoot(NodeHandle node)
    {
        if (Root == node)
        {
            return;
        }

        if (!Root.IsNone)
        {
            Invariant.Fail("A UI tree has one root; a window size was given to another node.");
        }

        if (!Nodes.GetRef(node).Parent.IsNone)
        {
            Invariant.Fail("The root of a UI tree cannot have a parent.");
        }

        Root = node;
        SetDepth(node, 0, attached: true);
    }

    /// <summary>
    /// Returns the index of the snapshot of <paramref name="node"/> in <see cref="ContentSnapshots"/>, or -1. The node holds
    /// it (<see cref="LayoutState.ContentSnapshotSlot"/>): a search of the list would make N changes before one pass cost N²
    /// (PR #112's review).
    /// </summary>
    internal int FindContentSnapshot(NodeHandle node) => Nodes.GetRef(node).Layout.ContentSnapshotSlot - 1;

    /// <summary>
    /// Adds the snapshot of a node that has none.
    /// </summary>
    internal void AddContentSnapshot(in ContentSnapshot snapshot)
    {
        ContentSnapshots.Add(snapshot);
        Nodes.GetRef(snapshot.Node).Layout.ContentSnapshotSlot = ContentSnapshots.Count;
    }

    /// <summary>
    /// Removes the snapshot at <paramref name="index"/>; the last one takes its place.
    /// </summary>
    internal void RemoveContentSnapshotAt(int index)
    {
        int last = ContentSnapshots.Count - 1;
        Nodes.GetRef(ContentSnapshots[index].Node).Layout.ContentSnapshotSlot = 0;
        if (index != last)
        {
            ContentSnapshots[index] = ContentSnapshots[last];
            Nodes.GetRef(ContentSnapshots[index].Node).Layout.ContentSnapshotSlot = index + 1;
        }

        ContentSnapshots.RemoveAt(last);
    }

    /// <summary>
    /// Keeps a request whose layout threw, to put back after the pass (<see cref="RestoreDeferredRequests"/>).
    /// </summary>
    internal void DeferRequest(LayoutRequest request) => _deferred.Add(request);

    /// <summary>
    /// Puts the requests whose layout threw back into the layout roots, for the next pass.
    /// </summary>
    internal void RestoreDeferredRequests()
    {
        foreach (LayoutRequest request in CollectionsMarshal.AsSpan(_deferred))
        {
            Invalidation.RestoreRequest(this, request);
        }

        _deferred.Clear();
    }

    /// <summary>
    /// Takes the current layout roots for one cycle of the worklist and leaves an empty list in their place, so that requests
    /// made during the cycle wait for the next one. The returned list is cleared and handed back by the next call.
    /// </summary>
    internal List<LayoutRequest> TakeLayoutRoots()
    {
        (_layoutRoots, _layoutRootsSpare) = (_layoutRootsSpare, _layoutRoots);
        return _layoutRootsSpare;
    }

    private int CompareDepth(LayoutRequest a, LayoutRequest b) => Nodes.GetRef(a.Node).Depth.CompareTo(Nodes.GetRef(b.Node).Depth);

    // Values are copied before each step down, so no ref is held across the recursion (design §2.5).
    private void SetDepth(NodeHandle node, int depth, bool attached)
    {
        ref UiNode n = ref Nodes.GetRef(node);
        n.Depth = depth;
        n.IsAttached = attached;
        NodeHandle child = n.FirstChild;
        while (!child.IsNone)
        {
            NodeHandle next = Nodes.GetRef(child).NextSibling;
            SetDepth(child, depth + 1, attached);
            child = next;
        }
    }
}
