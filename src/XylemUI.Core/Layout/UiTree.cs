using System.Numerics;
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
    private List<NodeHandle> _paintUpdates = [];
    private List<NodeHandle> _paintUpdatesSpare = [];
    private bool _frameRequested;

    // The measurement caches, by the node's slot index: apart from the nodes, so that reading a node does not load them.
    private MeasureCache[] _measures = new MeasureCache[256];

    /// <summary>
    /// Initializes a new instance of the <see cref="UiTree"/> class.
    /// </summary>
    /// <param name="text">
    /// The text tools of the UI thread, which its text leaves are measured with (design §3.2, "Text leaves"); <see langword="null"/>
    /// for a tree without text leaves.
    /// </param>
    public UiTree(TextContext? text = null)
    {
        _shallowestFirst = CompareDepth;
        Text = text;
    }

    /// <summary>
    /// Gets the text tools its text leaves are measured with; <see langword="null"/> for a tree without text leaves.
    /// </summary>
    public TextContext? Text { get; }

    /// <summary>
    /// Gets or sets the number of exceptions of a leaf's measurement that the layout passes counted and did not report: the same type
    /// again from the same leaf, before a change of the leaf (design §2.4.1).
    /// </summary>
    internal int SuppressedLayoutExceptions { get; set; }

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
    /// Gets the nodes whose change needs paint, hit testing, or semantics but no layout. A frame of <see cref="WindowFrames"/>
    /// takes them (<see cref="TakePaintUpdates"/>), clears their marks, and draws the tree again.
    /// </summary>
    internal List<NodeHandle> PaintUpdates => _paintUpdates;

    /// <summary>
    /// Gets or sets what the tree calls when work is first queued after <see cref="ClearFrameRequest"/>: a layout request or a
    /// paint update (design §5.0, "The draw stage and the frame drive"). <see cref="WindowFrames"/> asks for a frame with it.
    /// </summary>
    internal Action? FrameNeeded { get; set; }

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
    /// Creates a text leaf that is not in the tree yet, with no text: it measures its text with the tree's
    /// <see cref="TextContext"/> (design §3.2, "Text leaves"). It needs a layout.
    /// </summary>
    /// <param name="style">The layout style.</param>
    /// <param name="textStyle">The text style.</param>
    /// <returns>The leaf.</returns>
    /// <exception cref="InvalidOperationException">The tree has no <see cref="TextContext"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The text style's size or line height is out of its range.</exception>
    /// <exception cref="ArgumentException">The text style's language is the default value.</exception>
    public NodeHandle CreateTextNode(LayoutStyle style, TextStyle textStyle)
    {
        if (Text is null)
        {
            throw new InvalidOperationException("The UI tree has no text context, so it cannot measure a text leaf.");
        }

        TextStyle.Validate(in textStyle, nameof(textStyle));
        NodeHandle handle = CreateNode(style);
        Nodes.GetRef(handle).Text = new TextLeaf(textStyle);
        return handle;
    }

    /// <summary>
    /// Writes the text of a text leaf. A text equal to the one there changes nothing; another drops the leaf's caches and marks
    /// its layout, as a change of content (row L5).
    /// </summary>
    /// <param name="node">The text leaf.</param>
    /// <param name="text">The text.</param>
    /// <exception cref="InvalidOperationException">The node is not a text leaf.</exception>
    public void SetText(NodeHandle node, ReadOnlySpan<char> text)
    {
        if (TextLeafOf(node).SetContent(text))
        {
            Invalidation.Invalidate(this, node, ChangeKind.TextContent);
        }
    }

    /// <summary>
    /// Replaces the text style of a text leaf. A style equal to the one there changes nothing; another drops the leaf's caches and
    /// marks its layout, as a change of content (row L5).
    /// </summary>
    /// <param name="node">The text leaf.</param>
    /// <param name="style">The text style.</param>
    /// <exception cref="InvalidOperationException">The node is not a text leaf.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size or the line height is out of its range.</exception>
    /// <exception cref="ArgumentException">The language is the default value.</exception>
    public void SetTextStyle(NodeHandle node, TextStyle style)
    {
        TextStyle.Validate(in style, nameof(style));
        if (TextLeafOf(node).SetStyle(style))
        {
            Invalidation.Invalidate(this, node, ChangeKind.Font);
        }
    }

    /// <summary>
    /// Sets the shadow under a node's box; equal to the one there, nothing changes. Another marks the node to be drawn again,
    /// with no layout.
    /// </summary>
    public void SetShadow(NodeHandle node, BoxShadow? shadow)
    {
        NodeVisual visual = VisualOf(node);
        if (visual.Shadow != shadow)
        {
            visual.Shadow = shadow;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets a node's background; equal to the one there, nothing changes. Another marks the node to be drawn again, with no
    /// layout.
    /// </summary>
    public void SetBackground(NodeHandle node, Paint? background)
    {
        NodeVisual visual = VisualOf(node);
        if (visual.Background != background)
        {
            visual.Background = background;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets the corner radii of a node's box; equal to the ones there, nothing changes. Others mark the node to be drawn again,
    /// with no layout.
    /// </summary>
    public void SetCornerRadii(NodeHandle node, CornerRadii radii)
    {
        NodeVisual visual = VisualOf(node);
        if (visual.Radii != radii)
        {
            visual.Radii = radii;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets the paint of a node's border, which is drawn at the widths of its <see cref="LayoutStyle.Border"/>; equal to the one
    /// there, nothing changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    public void SetBorderPaint(NodeHandle node, Paint? paint)
    {
        NodeVisual visual = VisualOf(node);
        if (visual.BorderPaint != paint)
        {
            visual.BorderPaint = paint;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets a node's draw function. The same delegate instance as the one there changes nothing (delegates compare by reference,
    /// so a caller hands the same instance on); another marks the node to be drawn again, with no layout.
    /// </summary>
    public void SetDraw(NodeHandle node, DrawFunc? draw)
    {
        NodeVisual visual = VisualOf(node);
        if (!ReferenceEquals(visual.Draw, draw))
        {
            visual.Draw = draw;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets a text leaf's own color for its text, or <see langword="null"/> to take the color its ancestors resolve; equal to the
    /// one there, nothing changes. Another marks the leaf to be drawn again, with no layout.
    /// </summary>
    /// <exception cref="InvalidOperationException">The node is not a text leaf.</exception>
    public void SetTextColor(NodeHandle node, Color? color)
    {
        TextLeaf leaf = TextLeafOf(node);
        if (leaf.Color != color)
        {
            leaf.Color = color;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets the color that the text leaves of a node's subtree take when they have none of their own, or <see langword="null"/> to
    /// take the parent's; equal to the one there, nothing changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    public void SetForeground(NodeHandle node, Color? color)
    {
        NodeVisual visual = VisualOf(node);
        if (visual.Foreground != color)
        {
            visual.Foreground = color;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets the opacity of a node and its subtree; equal to the one there, nothing changes. Another marks the node to be drawn
    /// again, with no layout.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not a number from 0 to 1.</exception>
    public void SetOpacity(NodeHandle node, float opacity)
    {
        CheckOpacity(opacity, nameof(opacity));
        NodeVisual visual = VisualOf(node);
        if (visual.Opacity != opacity)
        {
            visual.Opacity = opacity;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets the transform of a node and its subtree, about the center of the node's border box, or <see langword="null"/> for
    /// none; equal to the one there, nothing changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">An element of <paramref name="transform"/> is not a finite number.</exception>
    public void SetTransform(NodeHandle node, Matrix3x2? transform)
    {
        CheckTransform(transform, nameof(transform));
        NodeVisual visual = VisualOf(node);
        if (visual.Transform != transform)
        {
            visual.Transform = transform;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets what the hovered state lays over a node's look, or <see langword="null"/> for nothing; equal to the one there, nothing
    /// changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public void SetHoveredStyle(NodeHandle node, PaintStyle? style)
    {
        CheckStyle(style, nameof(style));
        PseudoStyles states = StatesOf(node);
        if (states.Hovered != style)
        {
            states.Hovered = style;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets what the pressed state lays over a node's hovered look, or <see langword="null"/> for nothing; equal to the one there,
    /// nothing changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public void SetPressedStyle(NodeHandle node, PaintStyle? style)
    {
        CheckStyle(style, nameof(style));
        PseudoStyles states = StatesOf(node);
        if (states.Pressed != style)
        {
            states.Pressed = style;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Adds a pointer handler to a node (design §7, "Pointer input"). The node then writes a hit region, so it is drawn again.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of a routed event.</param>
    /// <param name="handler">The handler.</param>
    public void AddPointerHandler(NodeHandle node, PointerEventKind kind, EventPhase phase, PointerHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        InputOf(node).Add(new HandlerEntry(kind, phase, handler));
        Invalidation.Invalidate(this, node, ChangeKind.Color);
    }

    /// <summary>
    /// Removes the first pointer handler of a node added with the same kind, phase, and delegate; the node is drawn again when one
    /// was there.
    /// </summary>
    /// <returns>Whether a handler was removed.</returns>
    public bool RemovePointerHandler(NodeHandle node, PointerEventKind kind, EventPhase phase, PointerHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (Nodes.GetRef(node).Input is not { } input || !input.Remove(new HandlerEntry(kind, phase, handler)))
        {
            return false;
        }

        Invalidation.Invalidate(this, node, ChangeKind.Color);
        return true;
    }

    /// <summary>
    /// Sets the cursor over a node and its descendants that set none, or <see langword="null"/> for none; equal to the one there,
    /// nothing changes. Another marks the node to be drawn again, as whether it writes a hit region may change.
    /// </summary>
    public void SetCursor(NodeHandle node, CursorKind? cursor)
    {
        NodeInput input = InputOf(node);
        if (input.Cursor != cursor)
        {
            input.Cursor = cursor;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets whether a node writes a hit region with no handler, so that the pointer does not reach the nodes behind it; equal to the
    /// value there, nothing changes. Another marks the node to be drawn again.
    /// </summary>
    public void SetBlocksPointer(NodeHandle node, bool blocks)
    {
        NodeInput input = InputOf(node);
        if (input.BlocksPointer != blocks)
        {
            input.BlocksPointer = blocks;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets or clears a pseudo-state of a live node. A change marks the node to be drawn again only when it has a look for that
    /// state (design §2.13).
    /// </summary>
    internal void SetPseudoState(NodeHandle node, PseudoState state, bool on)
    {
        ref UiNode n = ref Nodes.GetRef(node);
        PseudoState next = on ? n.Pseudo | state : n.Pseudo & ~state;
        if (next == n.Pseudo)
        {
            return;
        }

        n.Pseudo = next;
        PseudoStyles? styles = n.Visual?.States;
        bool styled = state == PseudoState.Hovered ? styles?.Hovered is not null : styles?.Pressed is not null;
        if (styled)
        {
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Calls <see cref="FrameNeeded"/> once since the last <see cref="ClearFrameRequest"/>: work was queued.
    /// </summary>
    internal void NoteWork()
    {
        if (!_frameRequested)
        {
            _frameRequested = true;
            FrameNeeded?.Invoke();
        }
    }

    /// <summary>
    /// Forgets that a frame was asked for, at the start of one: work queued after it asks for the next.
    /// </summary>
    internal void ClearFrameRequest() => _frameRequested = false;

    /// <summary>
    /// Takes the paint updates for one frame and leaves an empty list in their place, so that a node marked while the frame is drawn
    /// goes into the next one. The returned list is cleared and handed back by the next call.
    /// </summary>
    internal List<NodeHandle> TakePaintUpdates()
    {
        (_paintUpdates, _paintUpdatesSpare) = (_paintUpdatesSpare, _paintUpdates);
        _paintUpdates.Clear();
        return _paintUpdatesSpare;
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

    // The node's visual, made the first time: the tree owns it, and a later change writes into it.
    private NodeVisual VisualOf(NodeHandle node) => Nodes.GetRef(node).Visual ??= new NodeVisual();

    // The node's pseudo-state looks, made the first time one is set (PR #319's review).
    private PseudoStyles StatesOf(NodeHandle node) => VisualOf(node).States ??= new PseudoStyles();

    // The node's input part, made the first time.
    private NodeInput InputOf(NodeHandle node) => Nodes.GetRef(node).Input ??= new NodeInput();

    // The checks of DisplayList.PushOpacity and PushTransform, made when the value is given so that the draw stage does not throw.
    private static void CheckOpacity(float opacity, string name)
    {
        if (!(opacity >= 0) || opacity > 1)
        {
            throw new ArgumentOutOfRangeException(name, opacity, "The opacity is not a number from 0 to 1.");
        }
    }

    private static void CheckTransform(Matrix3x2? transform, string name)
    {
        if (
            transform is { } t
            && !(
                float.IsFinite(t.M11)
                && float.IsFinite(t.M12)
                && float.IsFinite(t.M21)
                && float.IsFinite(t.M22)
                && float.IsFinite(t.M31)
                && float.IsFinite(t.M32)
            )
        )
        {
            throw new ArgumentOutOfRangeException(name, "The transform's elements are finite numbers.");
        }
    }

    private static void CheckStyle(PaintStyle? style, string name)
    {
        if (style is { } s)
        {
            if (s.Opacity is { } opacity)
            {
                CheckOpacity(opacity, name);
            }

            CheckTransform(s.Transform, name);
        }
    }

    private TextLeaf TextLeafOf(NodeHandle node) =>
        Nodes.GetRef(node).Text ?? throw new InvalidOperationException("The node is not a text leaf; create one with CreateTextNode.");

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
