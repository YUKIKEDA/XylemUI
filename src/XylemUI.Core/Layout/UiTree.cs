using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XylemUI.Text;

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
    private OverflowMemo[] _overflows = new OverflowMemo[256];

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
        Focus = new FocusManager(this);
        Animations = new AnimationClock(this);
    }

    /// <summary>
    /// Gets the focus of the tree's window (design §7, "Keys and the focus").
    /// </summary>
    internal FocusManager Focus { get; }

    /// <summary>
    /// Gets the animations of the tree's window that are running (design §2.14, "The frame clock").
    /// </summary>
    internal AnimationClock Animations { get; }

    /// <summary>
    /// Gets or sets the clipboard a text field copies to and pastes from (design §4.9, "The clipboard"); <see langword="null"/> for a
    /// tree no host gave one, where copying and pasting do nothing.
    /// </summary>
    internal IClipboardBackend? Clipboard { get; set; }

    /// <summary>
    /// Gets the OS's settings text fields draw their caret and selection with (design §4.9).
    /// </summary>
    internal TextEditingSettings TextEditing { get; private set; } = TextEditingSettings.Default;

    // The signals of IsFocused, made the first time a node's is asked for (design §7, "Element.Focus() and IsFocused()"): few nodes
    // read theirs, so they are kept apart from UiNode, as the scroll states.
    private readonly Dictionary<NodeHandle, Signal<bool>> _focusSignals = [];

    /// <summary>
    /// Gets or sets the tree whose root function a window's host runs now, where <see cref="Elements"/> makes nodes (design §2.13);
    /// <see langword="null"/> outside it. It belongs to the UI thread.
    /// </summary>
    internal static UiTree? Current { get; set; }

    // The scroll states attached to nodes (design §3.4.1): few nodes scroll, so they are kept apart from UiNode.
    private readonly Dictionary<NodeHandle, ScrollState> _scrollStates = [];

    // The test IDs that XylemUI.Testing finds nodes by (design §12.5, "The keys"): only the nodes given one are in it, so a node with
    // none costs nothing, and removing a node takes its ID with it.
    private readonly Dictionary<NodeHandle, string> _testIds = [];

    /// <summary>
    /// Gets or sets the language of the text that <see cref="Elements"/> makes with no language of its own: the OS's UI language,
    /// which the window's host reads from the platform (design §2.13, "The defaults of Text"); <see langword="null"/> until it does,
    /// for "und" (undetermined). It is not made here: a language is HarfBuzz's, and a tree with no text runs where HarfBuzz is not
    /// built (the Core tests on Linux and macOS until rows P04 and P06).
    /// </summary>
    internal TextLanguage? UiLanguage { get; set; }

    /// <summary>
    /// Returns the scroll state attached to a node, or <see langword="null"/>.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The state.</returns>
    internal ScrollState? ScrollStateOf(NodeHandle node) =>
        _scrollStates.Count > 0 && _scrollStates.TryGetValue(node, out ScrollState? state) ? state : null;

    /// <summary>
    /// Gets the number of nodes that have a test ID.
    /// </summary>
    internal int TestIdCount => _testIds.Count;

    /// <summary>
    /// Gives a node a test ID, replacing the one it had (design §12.5, "The keys").
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="id">The ID.</param>
    internal void SetTestId(NodeHandle node, string id)
    {
        // A stale handle throws here, as the other setters' InputOf does.
        _ = Nodes.GetRef(node);
        _testIds[node] = id;
    }

    /// <summary>
    /// Returns the test ID of a node, or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The ID.</returns>
    internal string? TestIdOf(NodeHandle node) => _testIds.Count > 0 && _testIds.TryGetValue(node, out string? id) ? id : null;

    /// <summary>
    /// Returns the scroll state attached to a node, attaching a new one when it has none: the wheel scrolls a scroll container
    /// that the app gave no state (design §3.4.1).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The state.</returns>
    internal ScrollState ScrollStateFor(NodeHandle node)
    {
        if (ScrollStateOf(node) is { } state)
        {
            return state;
        }

        state = new ScrollState();
        SetScrollState(node, state);
        return state;
    }

    /// <summary>
    /// Attaches a scroll state to a node, in place of the one it had; a state attached to another node is moved here. The node is
    /// drawn again with the state's position. A node already laid out writes its range to the state here, since a position change
    /// runs no layout pass to write it (PR #348's review: the wheel gave a container without a state a new one whose range stayed
    /// 0, and the next wheel brought it back to 0).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="state">The state.</param>
    internal void SetScrollState(NodeHandle node, ScrollState state)
    {
        if (state.Tree is { } previousTree && previousTree.Nodes.IsAlive(state.Node))
        {
            _ = previousTree._scrollStates.Remove(state.Node);
        }

        if (_scrollStates.Remove(node, out ScrollState? replaced))
        {
            replaced.Detach();
        }

        _scrollStates[node] = state;
        state.Attach(this, node);
        if (Nodes.GetRef(node).Layout.WasLaidOut)
        {
            ReactiveRuntime.EnterBatch();
            try
            {
                PublishScrollRange(node, state);
            }
            finally
            {
                ReactiveRuntime.ExitBatch();
            }
        }

        Invalidation.Invalidate(this, node, ChangeKind.TransformProp);
    }

    /// <summary>
    /// Returns a node's scroll position, in logical pixels: its state's, or none.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The position.</returns>
    internal Vector2 ScrollOffsetOf(NodeHandle node) => ScrollStateOf(node)?.OffsetValue ?? Vector2.Zero;

    /// <summary>
    /// Writes the range the last layout gave each node with a scroll state, in one batch, after the layout pass and before the
    /// draw stage (design §3.4.1): its largest position and its viewport, and its position kept in the range.
    /// </summary>
    internal void PublishScrollRanges()
    {
        if (_scrollStates.Count == 0)
        {
            return;
        }

        ReactiveRuntime.EnterBatch();
        try
        {
            foreach (KeyValuePair<NodeHandle, ScrollState> pair in _scrollStates)
            {
                if (Nodes.IsAlive(pair.Key) && Nodes.GetRef(pair.Key).Layout.WasLaidOut)
                {
                    PublishScrollRange(pair.Key, pair.Value);
                }
            }
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }

    // Writes the range a laid-out node's last layout gave it to its state: the largest position, and the viewport (the padding box
    // less the scrollbars' room).
    private void PublishScrollRange(NodeHandle node, ScrollState state)
    {
        Size max = MaxScrollOffsetOf(node);
        state.Publish(new Vector2(max.Width, max.Height), ViewportOf(node));
    }

    /// <summary>
    /// Returns the largest scroll position of a container: the one the last layout wrote to its state, or, for a container without
    /// a state yet, the one its boxes give (<see cref="MaxScrollOffsetOf"/>).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The largest position on each axis.</returns>
    internal Vector2 ScrollRangeOf(NodeHandle node)
    {
        if (ScrollStateOf(node) is { } state)
        {
            return state.MaxOffsetValue;
        }

        Size range = MaxScrollOffsetOf(node);
        return new Vector2(range.Width, range.Height);
    }

    /// <summary>
    /// Returns the part of a scroll container that shows its content, from its last committed layout, in logical pixels: the padding
    /// box less the scrollbars' room (design §3.4.1).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The viewport.</returns>
    internal Size ViewportOf(NodeHandle node)
    {
        ref readonly LayoutState layout = ref Nodes.GetRef(node).Layout;
        Size gutter = layout.Style.ScrollbarGutter;
        return new Size(
            Math.Max(0, layout.Rect.Width - layout.Border.Left - layout.Border.Right - gutter.Width),
            Math.Max(0, layout.Rect.Height - layout.Border.Top - layout.Border.Bottom - gutter.Height)
        );
    }

    /// <summary>
    /// Returns the largest scroll position of a node from its last committed layout, in logical pixels: the reachable end of its
    /// scrollable overflow less its padding box, without the scrollbars' room, and at least 0 (Taffy main's
    /// <c>Layout.scroll_width</c> and <c>scroll_height</c>, design §3.4.1).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The largest position on each axis.</returns>
    internal Size MaxScrollOffsetOf(NodeHandle node)
    {
        (float right, float bottom) = LayoutEngine.ScrollableOverflowOf(this, node);
        ref readonly LayoutState layout = ref Nodes.GetRef(node).Layout;
        Size gutter = layout.Style.ScrollbarGutter;
        Rect rect = layout.Rect;
        Edges border = layout.Border;
        return new Size(
            Math.Max(0, right + Math.Min(gutter.Width, rect.Width) - rect.Width + border.Left + border.Right),
            Math.Max(0, bottom + Math.Min(gutter.Height, rect.Height) - rect.Height + border.Top + border.Bottom)
        );
    }

    /// <summary>
    /// Gets the text tools its text leaves are measured with; <see langword="null"/> for a tree without text leaves.
    /// </summary>
    public TextContext? Text { get; }

    /// <summary>
    /// Gets the number of text leaves whose ranges have a link (design §4.8). While it is 0 and no pointer is over a link, the input
    /// does not look for links: a tree without links takes the pointer path it took before row T12.
    /// </summary>
    internal int LinkLeaves { get; private set; }

    /// <summary>
    /// Counts a text leaf whose ranges were written again: one more when they have a link and had none, one less the other way.
    /// </summary>
    /// <param name="before">Whether the ranges had a link before.</param>
    /// <param name="after">Whether they have one now.</param>
    internal void CountLinkLeaf(bool before, bool after) => LinkLeaves += (after ? 1 : 0) - (before ? 1 : 0);

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
    /// Gets or sets a hook that runs each time a <c>For</c> moves a row (design §2.7). Tests count the moves with it.
    /// </summary>
    internal Action<NodeHandle>? RowMovedHook { get; set; }

    /// <summary>
    /// Gets the number of siblings walked to find a previous sibling (the removal of a node nobody gave the previous sibling of,
    /// and <c>Show</c>'s replacement): tests check that <c>For</c> walks none.
    /// </summary>
    internal long SiblingWalks { get; private set; }

    /// <summary>
    /// Gets the containers that a <c>For</c> fills: each takes one, and no other child.
    /// </summary>
    internal HashSet<NodeHandle> ForContainers { get; } = [];

    /// <summary>
    /// Creates a node that is not in the tree yet. It needs a layout. A leaf with content has <paramref name="measure"/>, and
    /// may have <paramref name="intrinsicWidths"/>, which must agree with it.
    /// </summary>
    /// <param name="style">The layout style.</param>
    /// <param name="measure">The measure function of a leaf with content, or <see langword="null"/>.</param>
    /// <param name="intrinsicWidths">The content widths of a leaf with <paramref name="measure"/>, or <see langword="null"/>.</param>
    /// <returns>The node.</returns>
    /// <exception cref="ArgumentException"><paramref name="intrinsicWidths"/> is given without <paramref name="measure"/>.</exception>
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
            Array.Resize(ref _overflows, _measures.Length);
        }

        _measures[handle.Index] = default;
        _measures[handle.Index].Box = box;

        // A slot used again would keep the freed node's edges as known (design §3.4.1).
        _overflows[handle.Index] = default;
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
    /// <param name="node">The node.</param>
    /// <param name="shadow">The shadow, or <see langword="null"/> for none.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="background">The background, or <see langword="null"/> for none.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="radii">The corner radii.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="paint">The border's paint, or <see langword="null"/> for no border.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="draw">The draw function, or <see langword="null"/> for none.</param>
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
    /// <param name="node">The text leaf.</param>
    /// <param name="color">The text color, or <see langword="null"/>.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="color">The text color for the subtree, or <see langword="null"/>.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="opacity">The opacity, from 0 to 1.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="transform">The transform, or <see langword="null"/> for none.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="style">The hovered look, or <see langword="null"/>.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="style">The pressed look, or <see langword="null"/>.</param>
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
        InputOf(node).Add(new HandlerEntry(kind, phase, handler, Scope.CurrentRef));
        Invalidation.Invalidate(this, node, ChangeKind.Color);
    }

    /// <summary>
    /// Removes the first pointer handler of a node added with the same kind, phase, and delegate; the node is drawn again when one
    /// was there.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of a routed event.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>Whether a handler was removed.</returns>
    public bool RemovePointerHandler(NodeHandle node, PointerEventKind kind, EventPhase phase, PointerHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (Nodes.GetRef(node).Input is not { } input || !input.Remove(kind, phase, handler))
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
    /// <param name="node">The node.</param>
    /// <param name="cursor">The cursor, or <see langword="null"/> for none.</param>
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
    /// <param name="node">The node.</param>
    /// <param name="blocks">Whether the node blocks the pointer.</param>
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
    /// Sets what the focus-within state lays over a node's pressed look, or <see langword="null"/> for nothing; equal to the one
    /// there, nothing changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="style">The focus-within look, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public void SetFocusWithinStyle(NodeHandle node, PaintStyle? style)
    {
        CheckStyle(style, nameof(style));
        FocusStyles states = FocusStatesOf(node);
        if (states.FocusWithin != style)
        {
            states.FocusWithin = style;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets what the focused state lays over a node's focus-within look, or <see langword="null"/> for nothing; equal to the one
    /// there, nothing changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="style">The focused look, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public void SetFocusedStyle(NodeHandle node, PaintStyle? style)
    {
        CheckStyle(style, nameof(style));
        FocusStyles states = FocusStatesOf(node);
        if (states.Focused != style)
        {
            states.Focused = style;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets what the focus-visible state lays over a node's focused look, or <see langword="null"/> for nothing; equal to the one
    /// there, nothing changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="style">The focus-visible look, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public void SetFocusVisibleStyle(NodeHandle node, PaintStyle? style)
    {
        CheckStyle(style, nameof(style));
        FocusStyles states = FocusStatesOf(node);
        if (states.FocusVisible != style)
        {
            states.FocusVisible = style;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Adds a key handler to a node (design §7, "Keys and the focus"). The node writes no hit region for it.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    public void AddKeyHandler(NodeHandle node, KeyEventKind kind, EventPhase phase, KeyHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        InputOf(node).Add(new KeyHandlerEntry(kind, phase, handler, Scope.CurrentRef));
    }

    /// <summary>
    /// Removes the first key handler of a node added with the same kind, phase, and delegate.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>Whether a handler was removed.</returns>
    public bool RemoveKeyHandler(NodeHandle node, KeyEventKind kind, EventPhase phase, KeyHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Nodes.GetRef(node).Input is { } input && input.Remove(kind, phase, handler);
    }

    /// <summary>
    /// Adds a text input handler to a node (design §4.9, "Text input on Windows"). The node writes no hit region for it.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    public void AddTextHandler(NodeHandle node, EventPhase phase, TextInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        InputOf(node).Add(new TextHandlerEntry(phase, handler, Scope.CurrentRef));
    }

    /// <summary>
    /// Removes the first text input handler of a node added with the same phase and delegate.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>Whether a handler was removed.</returns>
    public bool RemoveTextHandler(NodeHandle node, EventPhase phase, TextInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Nodes.GetRef(node).Input is { } input && input.Remove(phase, handler);
    }

    /// <summary>
    /// Sets the OS's settings text fields draw with, from the host; a change draws the tree again, so that every field takes it.
    /// </summary>
    /// <param name="settings">The settings.</param>
    internal void SetTextEditing(TextEditingSettings settings)
    {
        if (TextEditing != settings)
        {
            TextEditing = settings;
            if (!Root.IsNone && Nodes.IsAlive(Root))
            {
                Invalidation.Invalidate(this, Root, ChangeKind.Color);
            }
        }
    }

    /// <summary>
    /// Sets whether a press on a node, or on a descendant with no node that takes the focus between them, leaves the focus where it
    /// is (design §4.9, "A press that keeps the focus").
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="keeps">Whether the press keeps the focus.</param>
    public void SetKeepsFocusOnPress(NodeHandle node, bool keeps) => InputOf(node).KeepsFocusOnPress = keeps;

    /// <summary>
    /// Adds a focus handler to a node (design §7, "Keys and the focus"). The node writes no hit region for it.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    public void AddFocusHandler(NodeHandle node, FocusEventKind kind, EventPhase phase, FocusHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        InputOf(node).Add(new FocusHandlerEntry(kind, phase, handler, Scope.CurrentRef));
    }

    /// <summary>
    /// Removes the first focus handler of a node added with the same kind, phase, and delegate.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>Whether a handler was removed.</returns>
    public bool RemoveFocusHandler(NodeHandle node, FocusEventKind kind, EventPhase phase, FocusHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Nodes.GetRef(node).Input is { } input && input.Remove(kind, phase, handler);
    }

    /// <summary>
    /// Sets whether a node takes the focus (design §7, "Keys and the focus"); equal to the value there, nothing changes. Another marks
    /// the node to be drawn again, as a focusable node writes a hit region; a focused node that stops being focusable loses the focus.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="focusable">Whether the node takes the focus.</param>
    public void SetFocusable(NodeHandle node, bool focusable)
    {
        NodeInput input = InputOf(node);
        if (input.Focusable != focusable)
        {
            input.Focusable = focusable;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
            if (!focusable)
            {
                Focus.Lose(node);
            }
        }
    }

    /// <summary>
    /// Sets what the disabled state lays over a node's other looks, or <see langword="null"/> for nothing; equal to the one there,
    /// nothing changes. Another marks the node to be drawn again, with no layout.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="style">The disabled look, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public void SetDisabledStyle(NodeHandle node, PaintStyle? style)
    {
        CheckStyle(style, nameof(style));
        PseudoStyles states = StatesOf(node);
        if (states.Disabled != style)
        {
            states.Disabled = style;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Sets whether a node itself is disabled (design §7, "Disabled"); equal to the value there, nothing changes. Another sets the
    /// disabled state of the node and its descendants again, which costs the subtree's nodes; a descendant with its own mark stays
    /// disabled. A focused node in a subtree that becomes disabled loses the focus, and the node is drawn again, so that the pointer's
    /// hover is tested again with the new state.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="disabled">Whether the node itself is disabled.</param>
    public void SetDisabled(NodeHandle node, bool disabled)
    {
        NodeInput input = InputOf(node);
        if (input.Disabled == disabled)
        {
            return;
        }

        input.Disabled = disabled;
        ref UiNode n = ref Nodes.GetRef(node);
        bool inherited = !n.Parent.IsNone && (Nodes.GetRef(n.Parent).Pseudo & PseudoState.Disabled) != 0;
        SetDisabledStates(node, inherited);
        Invalidation.Invalidate(this, node, ChangeKind.Color);
        if (disabled)
        {
            Focus.Lose(node);
        }
    }

    /// <summary>
    /// Sets a node's place in the order of Tab (design §7.1): a smaller index comes first; the nodes of one index come in the order
    /// of the tree.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="index">The node's tab index.</param>
    public void SetTabIndex(NodeHandle node, int index) => InputOf(node).TabIndex = index;

    /// <summary>
    /// Returns the signal of whether a node is focused, made the first time (design §7, "Element.Focus() and IsFocused()"): it has
    /// the value of the focused state, and it is dropped when the node is freed.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The signal.</returns>
    internal IReadOnlySignal<bool> IsFocusedSignal(NodeHandle node)
    {
        if (!_focusSignals.TryGetValue(node, out Signal<bool>? signal))
        {
            signal = Signal.Create((Nodes.GetRef(node).Pseudo & PseudoState.Focused) != 0);
            _focusSignals[node] = signal;
        }

        return signal;
    }

    /// <summary>
    /// Returns the signal of whether a node is focused, or <see langword="null"/> when no one asked for it.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The signal, or <see langword="null"/>.</returns>
    internal Signal<bool>? FocusSignalOf(NodeHandle node) =>
        _focusSignals.Count > 0 && _focusSignals.TryGetValue(node, out Signal<bool>? signal) ? signal : null;

    /// <summary>
    /// Sets or clears a pseudo-state of a live node. A change marks the node to be drawn again only when it has a look for that
    /// state (design §2.13).
    /// </summary>
    /// <param name="node">The live node.</param>
    /// <param name="state">The pseudo-state.</param>
    /// <param name="on"><see langword="true"/> to set <paramref name="state"/>; <see langword="false"/> to clear it.</param>
    internal void SetPseudoState(NodeHandle node, PseudoState state, bool on)
    {
        ref UiNode n = ref Nodes.GetRef(node);
        PseudoState next = on ? n.Pseudo | state : n.Pseudo & ~state;
        if (next == n.Pseudo)
        {
            return;
        }

        n.Pseudo = next;
        if (n.Visual?.States?.Has(state) == true)
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
    /// <returns>The paint updates of this frame.</returns>
    internal List<NodeHandle> TakePaintUpdates()
    {
        (_paintUpdates, _paintUpdatesSpare) = (_paintUpdatesSpare, _paintUpdates);
        _paintUpdates.Clear();
        return _paintUpdatesSpare;
    }

    /// <summary>
    /// Returns the measurement cache of a live node.
    /// </summary>
    /// <param name="node">The live node.</param>
    /// <returns>A reference to its measurement cache.</returns>
    internal ref MeasureCache MeasuresOf(NodeHandle node) => ref _measures[node.Index];

    /// <summary>
    /// Returns the remembered scrollable overflow of a node (design §3.4.1).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>A reference to its memo.</returns>
    internal ref OverflowMemo OverflowOf(NodeHandle node) => ref _overflows[node.Index];

    /// <summary>
    /// Gets or sets the number of containers whose scrollable overflow was found again rather than taken from their memo (design
    /// §3.4.1). Tests read it to show that writing a range costs the path that changed.
    /// </summary>
    internal long OverflowFinds { get; set; }

    /// <summary>
    /// Gets the bytes the measurement caches take: one per slot of the arena, grown by doubling (row L1a's layout scenario
    /// reports it).
    /// </summary>
    internal long MeasureCacheBytes => (long)_measures.Length * Unsafe.SizeOf<MeasureCache>();

    /// <summary>
    /// Appends <paramref name="child"/>, a node with no parent, as the last child of <paramref name="parent"/>, and marks the
    /// parent's layout dirty.
    /// </summary>
    /// <param name="parent">The parent.</param>
    /// <param name="child">The node to append; it has no parent.</param>
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

        // A subtree appended to a disabled parent becomes disabled (design §7, "Disabled"); a new subtree is enabled unless marked.
        if ((Nodes.GetRef(parent).Pseudo & PseudoState.Disabled) != 0)
        {
            SetDisabledStates(child, inherited: true);
        }

        Invalidation.MarkDirty(this, parent, DirtyFlags.Layout);
    }

    /// <summary>
    /// Puts <paramref name="child"/>, a node with no parent, among the children of <paramref name="parent"/> right after
    /// <paramref name="previous"/>, or first when <paramref name="previous"/> is none, and marks the parent's layout dirty
    /// (design §2.7, "The tree's moves"). It costs no walk of the children: <c>For</c> knows each row's previous sibling. A node
    /// put back in the parent it came from keeps its depth, so a move walks no subtree. It does not move a node to another parent:
    /// it takes a node back to the parent it came from, or a node that never had one. A disabled parent disables the node, but a
    /// node that took Disabled from another parent keeps it here.
    /// </summary>
    /// <param name="parent">The parent.</param>
    /// <param name="previous">The child that goes before it, or none.</param>
    /// <param name="child">The node.</param>
    internal void InsertAfter(NodeHandle parent, NodeHandle previous, NodeHandle child)
    {
        ref UiNode c = ref Nodes.GetRef(child);
        if (!c.Parent.IsNone)
        {
            Invariant.Fail("InsertAfter was given a node that already has a parent.");
        }

        c.Parent = parent;
        ref UiNode p = ref Nodes.GetRef(parent);
        if (previous.IsNone)
        {
            c.NextSibling = p.FirstChild;
            p.FirstChild = child;
        }
        else
        {
            ref UiNode before = ref Nodes.GetRef(previous);
            if (before.Parent != parent)
            {
                Invariant.Fail("InsertAfter was given a previous sibling of another parent.");
            }

            c.NextSibling = before.NextSibling;
            before.NextSibling = child;
        }

        if (p.LastChild == previous)
        {
            p.LastChild = child;
        }

        p.Versions.Children++;
        if (!c.Layout.Style.IsInFlow)
        {
            p.Layout.OutOfFlowChildren++;
        }

        if (c.Depth != p.Depth + 1 || c.IsAttached != p.IsAttached)
        {
            SetDepth(child, p.Depth + 1, p.IsAttached);
        }

        if ((Nodes.GetRef(parent).Pseudo & PseudoState.Disabled) != 0 && (Nodes.GetRef(child).Pseudo & PseudoState.Disabled) == 0)
        {
            SetDisabledStates(child, inherited: true);
        }

        Invalidation.MarkDirty(this, parent, DirtyFlags.Layout);
    }

    /// <summary>
    /// Takes <paramref name="node"/> out of the children of <paramref name="parent"/>, given the child right before it (none for the
    /// first child), and marks the parent's layout dirty; the node and its subtree stay alive, with no parent (design §2.7, "The tree's
    /// moves"). It costs no walk of the children. Its focus is the caller's: a move keeps it, a removal loses it first.
    /// </summary>
    /// <param name="parent">The parent.</param>
    /// <param name="previous">The child right before <paramref name="node"/>, or none.</param>
    /// <param name="node">The node.</param>
    internal void DetachAfter(NodeHandle parent, NodeHandle previous, NodeHandle node)
    {
        ref UiNode n = ref Nodes.GetRef(node);
        if (n.Parent != parent)
        {
            Invariant.Fail("DetachAfter was given a node that is not a child of the parent.");
        }

        NodeHandle first = Nodes.GetRef(parent).FirstChild;
        if (previous.IsNone ? first != node : Nodes.GetRef(previous).NextSibling != node)
        {
            Invariant.Fail("DetachAfter was given a previous sibling that is not right before the node.");
        }

        Detach(parent, previous, node, n.Layout.Style.IsInFlow);
        Invalidation.MarkDirty(this, parent, DirtyFlags.Layout);
    }

    /// <summary>
    /// Puts <paramref name="replacement"/>, a node with no parent, in the place of <paramref name="node"/> among the children of its
    /// parent, and leaves <paramref name="node"/> with no parent (design §2.7, <c>Show</c>). It walks the children before the node
    /// to find its previous sibling: one walk of its siblings for one change of a condition.
    /// </summary>
    /// <param name="node">The child replaced, which has a parent.</param>
    /// <param name="replacement">The node put in its place.</param>
    internal void ReplaceChild(NodeHandle node, NodeHandle replacement)
    {
        NodeHandle parent = Nodes.GetRef(node).Parent;
        NodeHandle previous = default;
        for (NodeHandle child = Nodes.GetRef(parent).FirstChild; child != node; child = Nodes.GetRef(child).NextSibling)
        {
            previous = child;
            SiblingWalks++;
        }

        Detach(parent, previous, node, Nodes.GetRef(node).Layout.Style.IsInFlow);
        InsertAfter(parent, previous, replacement);
    }

    /// <summary>
    /// Takes every child out of a parent in one pass and marks its layout dirty; the children stay alive, with no parent (design
    /// §2.7: a <c>For</c> that goes away leaves its rows so, and their removal then walks no siblings).
    /// </summary>
    /// <param name="parent">The parent.</param>
    internal void DetachChildren(NodeHandle parent)
    {
        NodeHandle child = Nodes.GetRef(parent).FirstChild;
        while (!child.IsNone)
        {
            ref UiNode c = ref Nodes.GetRef(child);
            NodeHandle next = c.NextSibling;
            c.Parent = default;
            c.NextSibling = default;
            child = next;
        }

        ref UiNode p = ref Nodes.GetRef(parent);
        p.FirstChild = default;
        p.LastChild = default;
        p.Layout.OutOfFlowChildren = 0;
        p.Versions.Children++;
        Invalidation.MarkDirty(this, parent, DirtyFlags.Layout);
    }

    /// <summary>
    /// Returns whether a node is <paramref name="ancestor"/> or one of its descendants.
    /// </summary>
    /// <param name="node">The node to check, or <see langword="default"/>.</param>
    /// <param name="ancestor">The root of the subtree.</param>
    /// <returns><see langword="true"/> if <paramref name="node"/> is in the subtree; otherwise, <see langword="false"/>.</returns>
    internal bool IsInSubtree(NodeHandle node, NodeHandle ancestor)
    {
        for (; !node.IsNone; node = Nodes.GetRef(node).Parent)
        {
            if (node == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Takes a node and its subtree out of the tree and frees their slots (design §2.13, "The component API"): the parent, if any,
    /// is marked for layout, and the pending layout requests, content snapshots, and deferred requests of the subtree's nodes are
    /// dropped, so that no pass reads a freed node. A node that is the root stops being the root.
    /// </summary>
    /// <param name="node">The node, alive.</param>
    internal void Remove(NodeHandle node)
    {
        // The focused node gets its focus-out while it is still in the tree (design §7, "Removed, hidden, or disabled").
        Focus.Lose(node);
        ref UiNode n = ref Nodes.GetRef(node);
        NodeHandle parent = n.Parent;
        bool inFlow = n.Layout.Style.IsInFlow;
        if (!parent.IsNone)
        {
            Unlink(parent, node, inFlow);
            Invalidation.MarkDirty(this, parent, DirtyFlags.Layout);
        }

        if (Root == node)
        {
            Root = default;
        }

        FreeSubtree(node);
        DropDead(_layoutRoots);
        DropDead(_deferred);
        for (int i = ContentSnapshots.Count - 1; i >= 0; i--)
        {
            if (!Nodes.IsAlive(ContentSnapshots[i].Node))
            {
                int last = ContentSnapshots.Count - 1;
                if (i != last)
                {
                    ContentSnapshots[i] = ContentSnapshots[last];
                    Nodes.GetRef(ContentSnapshots[i].Node).Layout.ContentSnapshotSlot = i + 1;
                }

                ContentSnapshots.RemoveAt(last);
            }
        }
    }

    /// <summary>
    /// Replaces the layout style of a node and marks it and its parent for layout: the parent's algorithm reads the style (the
    /// size, the margins, the flex factors), so a boundary does not stop the change at the node itself.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="style">The layout style.</param>
    public void SetStyle(NodeHandle node, LayoutStyle style)
    {
        ref UiNode n = ref Nodes.GetRef(node);
        if (!n.Parent.IsNone && n.Layout.Style.IsInFlow != style.IsInFlow)
        {
            Nodes.GetRef(n.Parent).Layout.OutOfFlowChildren += style.IsInFlow ? -1 : 1;
        }

        ref UiNode changed = ref Nodes.GetRef(node);
        bool hides = style.Hidden && !changed.Layout.Style.Hidden;
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

        // A hidden subtree takes no focus (design §7, "Removed, hidden, or disabled").
        if (hides)
        {
            Focus.Lose(node);
        }
    }

    /// <summary>
    /// Returns the rectangle of the node's last layout.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The node's rectangle.</returns>
    public Rect GetRect(NodeHandle node) => Nodes.GetRef(node).Layout.Rect;

    /// <summary>
    /// Makes <paramref name="node"/> the root, the first time a window size is given. The root has no parent, and a tree has one.
    /// </summary>
    /// <param name="node">The node with no parent to make the root.</param>
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
    /// <param name="node">The live node.</param>
    /// <returns>The index of the node's snapshot, or -1 when it has none.</returns>
    internal int FindContentSnapshot(NodeHandle node) => Nodes.GetRef(node).Layout.ContentSnapshotSlot - 1;

    /// <summary>
    /// Adds the snapshot of a node that has none.
    /// </summary>
    /// <param name="snapshot">The snapshot of a live node.</param>
    internal void AddContentSnapshot(in ContentSnapshot snapshot)
    {
        ContentSnapshots.Add(snapshot);
        Nodes.GetRef(snapshot.Node).Layout.ContentSnapshotSlot = ContentSnapshots.Count;
    }

    /// <summary>
    /// Removes the snapshot at <paramref name="index"/>; the last one takes its place.
    /// </summary>
    /// <param name="index">The index in <see cref="ContentSnapshots"/>.</param>
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
    /// <param name="request">The request whose layout threw.</param>
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
    /// <returns>The layout roots of this cycle.</returns>
    internal List<LayoutRequest> TakeLayoutRoots()
    {
        (_layoutRoots, _layoutRootsSpare) = (_layoutRootsSpare, _layoutRoots);
        return _layoutRootsSpare;
    }

    // The node's visual, made the first time: the tree owns it, and a later change writes into it.
    private NodeVisual VisualOf(NodeHandle node) => Nodes.GetRef(node).Visual ??= new NodeVisual();

    /// <summary>
    /// Sets how far a <c>Show</c>'s part is in, and where it is when wholly out (design §2.14); a change marks the node to be drawn
    /// again, with no layout.
    /// </summary>
    /// <param name="node">The part's root node.</param>
    /// <param name="presence">How far it is in, 0 to 1.</param>
    /// <param name="offset">Where it is when wholly out, in logical pixels.</param>
    internal void SetPresence(NodeHandle node, float presence, Vector2 offset)
    {
        NodeVisual visual = VisualOf(node);
        if (visual.Presence != presence || visual.PresenceOffset != offset)
        {
            visual.Presence = presence;
            visual.PresenceOffset = offset;
            Invalidation.Invalidate(this, node, ChangeKind.Color);
        }
    }

    /// <summary>
    /// Makes one of a node's paint values follow its changes over a duration along a curve (design §2.14, "Transitions"); a
    /// duration of 0 stops it following.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="kind">The value.</param>
    /// <param name="duration">The duration.</param>
    /// <param name="curve">The curve.</param>
    internal void SetTransition(NodeHandle node, NodeTransitions.Kind kind, TimeSpan duration, Curve curve)
    {
        NodeVisual visual = VisualOf(node);
        (visual.Transitions ??= new NodeTransitions(this, node)).Set(kind, duration, curve);
    }

    // The node's pseudo-state looks, made the first time one is set (PR #319's review).
    private PseudoStyles StatesOf(NodeHandle node) => VisualOf(node).States ??= new PseudoStyles();

    // The node's focus looks, made the first time one is set.
    private FocusStyles FocusStatesOf(NodeHandle node) => StatesOf(node).Focus ??= new FocusStyles();

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

    // The requests whose node was freed, taken out of a list in place.
    private void DropDead(List<LayoutRequest> requests)
    {
        int kept = 0;
        for (int i = 0; i < requests.Count; i++)
        {
            if (Nodes.IsAlive(requests[i].Node))
            {
                requests[kept++] = requests[i];
            }
        }

        requests.RemoveRange(kept, requests.Count - kept);
    }

    // Takes node out of parent's list of children, walking the children before it to find its previous sibling: the removal of a
    // node whose previous sibling nobody knows. For gives the previous sibling of its rows (DetachAfter).
    private void Unlink(NodeHandle parent, NodeHandle node, bool inFlow)
    {
        NodeHandle previous = default;
        for (NodeHandle child = Nodes.GetRef(parent).FirstChild; child != node; child = Nodes.GetRef(child).NextSibling)
        {
            previous = child;
            SiblingWalks++;
        }

        Detach(parent, previous, node, inFlow);
    }

    // Takes node out of parent's list of children, given the child right before it.
    private void Detach(NodeHandle parent, NodeHandle previous, NodeHandle node, bool inFlow)
    {
        ref UiNode p = ref Nodes.GetRef(parent);
        NodeHandle next = Nodes.GetRef(node).NextSibling;
        if (previous.IsNone)
        {
            p.FirstChild = next;
        }
        else
        {
            Nodes.GetRef(previous).NextSibling = next;
        }

        if (p.LastChild == node)
        {
            p.LastChild = previous;
        }

        p.Versions.Children++;
        if (!inFlow)
        {
            p.Layout.OutOfFlowChildren--;
        }

        ref UiNode n = ref Nodes.GetRef(node);
        n.Parent = default;
        n.NextSibling = default;
    }

    // Frees a node and its subtree, children first. The links are read before each slot is freed (design §2.5).
    private void FreeSubtree(NodeHandle node)
    {
        NodeHandle child = Nodes.GetRef(node).FirstChild;
        while (!child.IsNone)
        {
            NodeHandle next = Nodes.GetRef(child).NextSibling;
            FreeSubtree(child);
            child = next;
        }

        if (_scrollStates.Count > 0 && _scrollStates.Remove(node, out ScrollState? state))
        {
            state.Detach();
        }

        if (_focusSignals.Count > 0)
        {
            _ = _focusSignals.Remove(node);
        }

        if (_testIds.Count > 0)
        {
            _ = _testIds.Remove(node);
        }

        if (ForContainers.Count > 0)
        {
            _ = ForContainers.Remove(node);
        }

        if (LinkLeaves > 0 && Nodes.GetRef(node).Text?.Ranges?.HasLinks == true)
        {
            LinkLeaves--;
        }

        Nodes.Free(node);
    }

    // The disabled state of a node and its subtree: its own mark or its parent's state. Values are copied before each step down.
    private void SetDisabledStates(NodeHandle node, bool inherited)
    {
        ref UiNode n = ref Nodes.GetRef(node);
        bool disabled = inherited || n.Input?.Disabled == true;
        SetPseudoState(node, PseudoState.Disabled, disabled);

        // A disabled node is neither hovered nor pressed (design §7): a press a key made, which no pointer's up clears, ends here
        // (PR #360's review). The pointer's press list clears its own nodes on the up.
        if (disabled)
        {
            SetPseudoState(node, PseudoState.Pressed, on: false);
        }

        NodeHandle child = Nodes.GetRef(node).FirstChild;
        while (!child.IsNone)
        {
            NodeHandle next = Nodes.GetRef(child).NextSibling;
            SetDisabledStates(child, disabled);
            child = next;
        }
    }

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
