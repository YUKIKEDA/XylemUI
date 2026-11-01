using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XylemUI.Text;
using XylemUI.Text.LineBreak;

namespace XylemUI;

/// <summary>
/// Runs the frames of one window (design §5.0, "The draw stage and the frame drive"): lays its tree out, draws the tree into a
/// display list, and draws and presents the list. A frame with neither a layout request nor a paint update draws and presents
/// nothing.
/// </summary>
/// <remarks>
/// It belongs to the UI thread, as the tree does. The tree asks for a frame (<see cref="UiTree.FrameNeeded"/>) when work is first
/// queued after a frame started, so work queued while a frame is drawn asks for the next one. Effects need no step of their own:
/// they have run when the batch that set their signals closed (design §2.2).
/// </remarks>
internal sealed partial class WindowFrames
{
    // How far a clip reaches past the box on an axis that does not clip (overflow-x: clip with overflow-y: visible), in logical
    // pixels: farther than a window shows.
    private const float UnclippedReach = 1_000_000;

    private readonly UiTree _tree;
    private readonly NodeHandle _root;
    private readonly IRenderTarget _target;
    private readonly GlyphAtlas _atlas;
    private readonly IFrameScheduler _scheduler;
    private readonly Action _runFrame;
    private readonly TimeProvider _time;
    private static readonly Color RootText = Color.FromRgb(0x000000);

    private readonly DisplayList _list = new();

    // The list being built: the window's, or a popup's; and how many nodes that take no pointer the node being drawn is in.
    private DisplayList _building;
    private int _noHitDepth;

    // The lists drawn in a frame, the window's first and the popups' back to front (design §7.3, "Display lists").
    private DisplayList[] _drawn = new DisplayList[4];
    private float _scale = 1;
    private TextRenderingParameters _textRendering;
    private bool _resizing;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowFrames"/> class, and asks the scheduler for a frame whenever the tree
    /// queues work.
    /// </summary>
    /// <param name="tree">The window's tree.</param>
    /// <param name="root">The tree's root, which takes the window's size.</param>
    /// <param name="target">What the frames draw on.</param>
    /// <param name="atlas">The glyph atlas the target draws glyphs from.</param>
    /// <param name="scheduler">The frame scheduler of the UI thread.</param>
    /// <param name="clearColor">The color behind the tree.</param>
    /// <param name="textRendering">How the window draws text (<see cref="TextRendering"/>); the OS's defaults when not given.</param>
    /// <param name="time">The clock a frame reads its time from (design §2.11, "The frame's time"); the system's when not given.</param>
    /// <exception cref="ArgumentException"><paramref name="textRendering"/> is bilevel with ClearType coverage.</exception>
    public WindowFrames(
        UiTree tree,
        NodeHandle root,
        IRenderTarget target,
        GlyphAtlas atlas,
        IFrameScheduler scheduler,
        Color clearColor,
        TextRenderingParameters? textRendering = null,
        TimeProvider? time = null
    )
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(scheduler);
        _tree = tree;
        _root = root;
        _target = target;
        _atlas = atlas;
        _scheduler = scheduler;
        ClearColor = clearColor;
        _textRendering = CheckTextRendering(textRendering ?? TextRenderingParameters.Default, nameof(textRendering));
        _time = time ?? TimeProvider.System;
        tree.Time = _time;
        _runFrame = RunFrame;
        _building = _list;
        tree.FrameNeeded = RequestFrame;
    }

    /// <summary>
    /// Gets the color behind the tree.
    /// </summary>
    public Color ClearColor { get; }

    /// <summary>
    /// Gets or sets how the window draws text: grayscale or ClearType, and bilevel where the OS does not smooth fonts (design
    /// §5.0.3). The window sets it again when its value changes (<c>Win32Window.TextRenderingChanged</c>, row T2), and the next
    /// frame draws the whole tree again with it.
    /// </summary>
    /// <exception cref="ArgumentException">The value is bilevel with ClearType coverage.</exception>
    public TextRenderingParameters TextRendering
    {
        get => _textRendering;
        set => SetTextRendering(value);
    }

    /// <summary>
    /// Gets the number of frames that drew and presented.
    /// </summary>
    public int PresentCount { get; private set; }

    /// <summary>
    /// Gets the window's display list of the last frame that drew, whose hit regions the input tests (design §7, "Pointer input");
    /// each popup has a list of its own (<see cref="PopupHandle.List"/>).
    /// </summary>
    internal DisplayList List => _list;

    /// <summary>
    /// Gets the window's tree.
    /// </summary>
    internal UiTree Tree => _tree;

    /// <summary>
    /// Gets the window's scale, from logical to physical pixels, that the last <see cref="Resize"/> gave.
    /// </summary>
    internal float Scale => _scale;

    /// <summary>
    /// Gets the number of times a display list was built, the window's or a popup's: a node's hit region index is valid in the list
    /// of the build it was written in (<see cref="NodeInput.HitBuild"/>, <see cref="ListOf(int)"/>).
    /// </summary>
    internal int Builds { get; private set; }

    /// <summary>
    /// Gets the number of times the window's own display list was built: a frame where only a popup changed does not build it
    /// (design §7.3, "Display lists").
    /// </summary>
    internal int MainBuilds { get; private set; }

    /// <summary>
    /// Returns the display list of a build, if it is still the list's last: the window's, or an open popup's.
    /// </summary>
    /// <param name="build">The build (<see cref="NodeInput.HitBuild"/>).</param>
    /// <returns>The list, or <see langword="null"/> when no list holds that build any more.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal DisplayList? ListOf(int build) => build > 0 && _list.Build == build ? _list : PopupListOf(build);

    // A popup's list of the build, apart from ListOf, which each handler of a pointer's event calls.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private DisplayList? PopupListOf(int build)
    {
        if (build <= 0)
        {
            return null;
        }

        IReadOnlyList<PopupHandle> open = _tree.Popups.Opened;
        for (int i = 0; i < open.Count; i++)
        {
            if (open[i].List.Build == build)
            {
                return open[i].List;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the display list of the render root a node is in: the window's, or its popup's.
    /// </summary>
    /// <param name="node">A live node.</param>
    /// <returns>The list.</returns>
    internal DisplayList ListFor(NodeHandle node) => _tree.Popups.PopupContaining(node)?.List ?? _list;

    /// <summary>
    /// Gets the time of the frame running, or of the last one: read once from the clock at the start of each frame, so that what
    /// moves with time in the frame (a touch's fling) sees the same time (design §2.11, "The frame's time").
    /// </summary>
    internal TimeSpan FrameTime { get; private set; }

    /// <summary>
    /// Gets or sets the window's input, whose hover a frame that built the display list tests again after presenting
    /// (<see cref="WindowInput.Rehit"/>).
    /// </summary>
    internal WindowInput? Input { get; set; }

    /// <summary>
    /// Runs one frame: the layout pass; then, when the frame had a layout request or a paint update, the display list built
    /// again from the tree, its glyphs resolved, and drawn and presented. A frame with neither ends after the layout pass.
    /// </summary>
    public void RunFrame()
    {
        FrameTime = _time.GetElapsedTime(0);

        // The popups asked for while their anchors were in no root open now, and are laid out in this frame (design §7.3).
        if (_tree.Popups.HasWaiting)
        {
            _tree.Popups.OpenWaiting();
        }

        // The animations first, at the frame's time: what they set is laid out and drawn in this frame (design §2.14).
        _tree.Animations.Tick(FrameTime);

        // Before the layout pass: a touchpad's pan scrolls with this frame's time, and its changes are this frame's (row I1c). The
        // next frame it needs is asked for once a resize's frame no longer holds the requests back.
        bool gestures = Input?.UpdateGestures(FrameTime) == true;
        bool layout = _tree.LayoutRoots.Count > 0;
        bool popups = _tree.Popups.Count > 0;
        bool mainLayout = layout && (!popups || NoteLayoutRoots());
        LayoutEngine.RunLayoutPass(_tree);

        // Cleared after the pass: the requests a pass puts back after an exception, or leaves at its cycle limit, ask for no frame
        // of their own, and are tried by the next frame something else asks for (design §2.4.1); a leaf that keeps throwing would
        // otherwise draw and present every frame (PR #313's review). A mark made from here on asks for the next frame.
        _tree.ClearFrameRequest();
        _resizing = false;

        // An animation that still runs asks for the next frame; with none running, no frame is asked for (design §2.14).
        if (_tree.Animations.Count > 0)
        {
            _tree.NoteWork();
        }

        if (gestures)
        {
            RequestFrame();
        }

        // After the pass and its frame request: an effect that reads a scroll range and changes the tree asks for the next frame.
        if (layout)
        {
            _tree.PublishScrollRanges();
        }

        // With the ranges written: a position it changes is a paint update of this frame (design §7, "Bring into view"), and a fling
        // that goes on asks for the next frame (design §7, "Touch and pen").
        if (Input?.AdvanceFlings(FrameTime) == true)
        {
            RequestFrame();
        }

        _tree.Focus.RevealPending();

        // After the layout: each popup's frame against its anchor, which may close a popup whose anchor scrolled (design §7.3). A
        // scroll marks the container for paint alone, so a frame with paint updates places them too.
        if (popups && (layout || _tree.PaintUpdates.Count > 0))
        {
            PlacePopups();
        }

        bool rootsChanged = _tree.TakePopupsChanged();
        if (!layout && _tree.PaintUpdates.Count == 0 && !rootsChanged)
        {
            return;
        }

        // The marks go before the list is built, so that one made while it is built asks for the next frame.
        bool main = mainLayout || rootsChanged || _tree.Popups.Count == 0;
        foreach (NodeHandle node in _tree.TakePaintUpdates())
        {
            if (_tree.Nodes.IsAlive(node))
            {
                _tree.Nodes.GetRef(node).Dirty.SelfDirty &= DirtyFlags.Layout;
                if (!main)
                {
                    main = NoteRootToBuild(node);
                }
            }
        }

        if (!Build(main))
        {
            return;
        }

        _target.Present();
        _scheduler.FramePresented();
        PresentCount++;

        // The hit regions changed: a pointer that did not move may be over other nodes now (design §7, "Pointer input").
        Input?.Rehit();
    }

    /// <summary>
    /// Gives the window's new size to the tree and runs a frame at once: the synchronous resize path (design §2.11.1), which a
    /// window calls from its size message after resizing its target.
    /// </summary>
    /// <param name="size">The client size, in logical pixels.</param>
    /// <param name="scale">The window's scale, from logical to physical pixels.</param>
    public void Resize(Size size, float scale)
    {
        // The new size's layout request would ask for a frame; this one runs it now, so no other is asked for until the frame
        // clears the request (RunFrame), after which a mark asks for the next one as usual.
        _resizing = true;
        _scale = scale;
        Invalidation.NotifyWindowResized(_tree, _root, size);
        _tree.Popups.ResizeAll(size);
        RunFrame();
    }

    // Builds the lists that changed and draws them all, the window's first. With the window's, every popup's is built too, since
    // their frames were placed again; with a popup's alone, the atlas does not start a frame, so that the glyphs the other lists drew
    // with stay in it (design §7.3, "Display lists"). Returns whether a list was built and drawn.
    private bool Build(bool main)
    {
        IReadOnlyList<PopupHandle> open = _tree.Popups.Opened;
        int count = 1 + open.Count;
        if (_drawn.Length < count)
        {
            Array.Resize(ref _drawn, Math.Max(count, _drawn.Length * 2));
        }

        int refused = _atlas.RefusedCount;
        bool built = false;
        if (main)
        {
            _atlas.BeginFrame();
            BuildList(_list, _root);
            MainBuilds++;
            built = true;
        }

        _drawn[0] = _list;
        for (int i = 0; i < open.Count; i++)
        {
            PopupHandle popup = open[i];
            if (main || popup.NeedsBuild)
            {
                popup.NeedsBuild = false;
                BuildList(popup.List, popup.Root);
                built = true;
            }

            _drawn[i + 1] = popup.List;
        }

        if (!built)
        {
            return false;
        }

        // Without a frame of the atlas, the plots the window's list used cannot be evicted, and an atlas at its page limit refuses
        // the popup's new glyphs until the next one; the next frame builds the window's list, which starts it, and draws them (PR
        // #552's review).
        if (!main && _atlas.RefusedCount != refused)
        {
            _tree.MarkRootsToBuild();
        }

        _target.Draw(_drawn.AsSpan(0, count), ClearColor, _atlas);
        _drawn.AsSpan(0, count).Clear();
        return true;
    }

    private void BuildList(DisplayList list, NodeHandle root)
    {
        list.Reset(_scale);
        list.Build = ++Builds;
        _building = list;
        try
        {
            DrawNode(root, 0, 0, RootText, hitsOnly: false);
        }
        finally
        {
            _building = _list;
            _noHitDepth = 0;
        }

        list.ResolveGlyphs(_atlas);
    }

    // Before the layout pass, while popups are open: marks the popups whose roots have a layout request to be built, and returns
    // whether the window's tree has one.
    private bool NoteLayoutRoots()
    {
        bool main = false;
        foreach (LayoutRequest request in CollectionsMarshal.AsSpan(_tree.LayoutRoots))
        {
            if (_tree.Nodes.IsAlive(request.Node))
            {
                main |= NoteRootToBuild(request.Node);
            }
        }

        return main;
    }

    // Marks the popup a node is in to be built; returns whether the node is in the window's tree instead.
    private bool NoteRootToBuild(NodeHandle node)
    {
        if (_tree.Popups.PopupContaining(node) is { } popup)
        {
            popup.NeedsBuild = true;
            return false;
        }

        return true;
    }

    // Places each popup's frame against its anchor after a layout (design §7.3, "Placement"): its anchor rectangle in the window, its
    // size, and the window's client area as the bounds. A popup that is not modal closes when a scroll container its anchor is in has
    // scrolled since it was first placed, or when its anchor is gone.
    private void PlacePopups()
    {
        Rect window = _tree.Nodes.GetRef(_root).Layout.Rect;
        var bounds = new Rect(0, 0, window.Width, window.Height);
        IReadOnlyList<PopupHandle> open = _tree.Popups.Opened;
        for (int i = 0; i < open.Count; i++)
        {
            PopupHandle popup = open[i];
            if (popup.Closing)
            {
                continue;
            }

            if (!_tree.Nodes.IsAlive(popup.Anchor) || ScrolledAway(popup))
            {
                // Its children close with it; the walk starts again, as the list changed.
                popup.Block.Dismiss();
                i = -1;
                continue;
            }

            PopupPlacement placement = popup.Options.Placement;
            if (placement.Fills)
            {
                continue;
            }

            ref LayoutState frame = ref _tree.Nodes.GetRef(popup.Frame).Layout;
            Rect anchor = placement.AnchorRect ?? WindowBox(popup.Anchor);
            Rect placed = PopupPositioner.Place(placement, anchor, new Size(frame.Rect.Width, frame.Rect.Height), bounds);
            if (placed.X != frame.Rect.X || placed.Y != frame.Rect.Y)
            {
                frame.Rect = frame.Rect with { X = placed.X, Y = placed.Y };
                popup.NeedsBuild = true;
            }
        }
    }

    // Whether a scroll container above the anchor of a popup that is not modal scrolled since the popup was first placed.
    private bool ScrolledAway(PopupHandle popup)
    {
        Vector2 scroll = ScrollAbove(popup.Anchor);
        if (!popup.Placed)
        {
            popup.Placed = true;
            popup.AnchorScroll = scroll;
            return false;
        }

        return scroll != popup.AnchorScroll && !popup.Options.IsModal;
    }

    // The sum of the scroll positions of the scroll containers above a node, up to its render root.
    private Vector2 ScrollAbove(NodeHandle node)
    {
        Vector2 sum = Vector2.Zero;
        for (NodeHandle at = _tree.Nodes.GetRef(node).Parent; !at.IsNone; at = _tree.Nodes.GetRef(at).Parent)
        {
            if (_tree.Nodes.GetRef(at).Layout.Style.IsScrollContainer)
            {
                sum += _tree.ScrollOffsetOf(at);
            }
        }

        return sum;
    }

    /// <summary>
    /// Returns a node's border box in the window's logical pixels, as the frame draws it: its position in each parent, the scroll
    /// positions of the scroll containers above it, and the transforms of its own and its ancestors' looks, up to its render root. A
    /// transformed box is the rectangle around its corners.
    /// </summary>
    /// <param name="node">A live node.</param>
    /// <returns>The box.</returns>
    internal Rect WindowBox(NodeHandle node)
    {
        Rect rect = _tree.Nodes.GetRef(node).Layout.Rect;
        Span<Vector2> corners = [new(0, 0), new(rect.Width, 0), new(0, rect.Height), new(rect.Width, rect.Height)];
        for (NodeHandle at = node; !at.IsNone; at = _tree.Nodes.GetRef(at).Parent)
        {
            ref UiNode n = ref _tree.Nodes.GetRef(at);
            Rect box = n.Layout.Rect;
            var origin = new Vector2(box.X, box.Y);
            Matrix3x2? transform = n.Visual?.Transform;
            var center = new Vector2(box.X + (box.Width * 0.5f), box.Y + (box.Height * 0.5f));
            NodeHandle parent = n.Parent;
            Vector2 scroll =
                !parent.IsNone && _tree.Nodes.GetRef(parent).Layout.Style.IsScrollContainer ? _tree.ScrollOffsetOf(parent) : Vector2.Zero;
            for (int c = 0; c < corners.Length; c++)
            {
                Vector2 p = corners[c] + origin;
                if (transform is { } matrix)
                {
                    p = Vector2.Transform(p - center, matrix) + center;
                }

                corners[c] = p - scroll;
            }
        }

        Vector2 min = Vector2.Min(Vector2.Min(corners[0], corners[1]), Vector2.Min(corners[2], corners[3]));
        Vector2 max = Vector2.Max(Vector2.Max(corners[0], corners[1]), Vector2.Max(corners[2], corners[3]));
        return new Rect(min.X, min.Y, max.X - min.X, max.Y - min.Y);
    }

    // A new way of drawing text marks the root to be drawn again, so the next frame draws the whole tree with it.
    private void SetTextRendering(TextRenderingParameters value)
    {
        CheckTextRendering(value, nameof(value));
        if (value != _textRendering)
        {
            _textRendering = value;
            Invalidation.Invalidate(_tree, _root, ChangeKind.Color);
        }
    }

    // Bilevel text has no ClearType coverage, and the frame's DrawLine would throw on it. Refusing the value where it is given keeps
    // the throw out of the frame, which would stop in the middle after the marks are cleared (PR #317's review).
    private static TextRenderingParameters CheckTextRendering(TextRenderingParameters value, string name) =>
        value.IsAliased && value.Coverage == GlyphCoverage.Rgb ? throw new ArgumentException("Bilevel text has no ClearType coverage.", name) : value;

    /// <summary>
    /// Asks the scheduler for the next frame, unless a resize runs one now.
    /// </summary>
    internal void RequestFrame()
    {
        if (!_resizing)
        {
            _scheduler.RequestFrame(_runFrame);
        }
    }

    // A node and its subtree, in tree order (design §5.0, "Opacity, transforms, and pseudo-states"): the look resolved with the
    // node's pseudo-states, the transform and the opacity around the subtree, then the shadow, the background and border, the hit
    // region, the draw function, the text, and the children. Under an opacity of 0 only the hit regions are written. The refs are
    // taken again after each call that can run the user's code (design §2.5).
    private void DrawNode(NodeHandle handle, float parentX, float parentY, Color inheritedText, bool hitsOnly)
    {
        ref UiNode node = ref _tree.Nodes.GetRef(handle);
        if (node.Layout.Style.Hidden)
        {
            return;
        }

        // A subtree marked to take no pointer, or a popup's that leaves, writes no hit region (design §7.3, "Passing the pointer"):
        // counted in a field, as a parameter of this method made each node's draw about 10 ns longer (the PR for Issue #551).
        NodeInput? input = node.Input;
        bool marked = input is { TakesNoPointer: true };
        if (marked)
        {
            _noHitDepth++;
        }

        Rect rect = node.Layout.Rect;
        var box = new Rect(parentX + rect.X, parentY + rect.Y, rect.Width, rect.Height);
        NodeVisual? visual = node.Visual;

        // A scroll container takes the wheel over the parts of it no child takes (design §3.4.1), a text leaf with links takes
        // the pointer over them (design §4.8), and a text leaf a selection area selects is found by its region (§4.10), looked for
        // only while the tree has an area.
        bool writesHit =
            input?.WritesHitRegion == true
            || node.Layout.Style.IsScrollContainer
            || node.Text?.Ranges?.HasLinks == true
            || (_tree.SelectionAreas > 0 && node.Text is not null && _tree.IsSelectableText(handle));
        Color text = inheritedText;
        int pushed = 0;
        if (visual is not null)
        {
            PseudoStyles? styles = visual.States;
            if (styles is not null)
            {
                writesHit |= styles.Hovered is not null || styles.Pressed is not null;
            }

            // Most nodes have no look of the state they are in and no scope: their values go to the box as they are. The others are
            // resolved apart, so that the style's and the scopes' locals are not in this frame, which every node pays for: with them
            // here the frame was 968 bytes, zeroed on each call, and the draw stage of `frames --tree` took 12 µs longer for 1,000
            // boxes (row V1c).
            bool styled = styles is not null && node.Pseudo != PseudoState.None;
            if (styled || visual.Scoped || visual.Transitions is not null || visual.Presence < 1)
            {
                pushed = DrawResolved(visual, node.Pseudo, node.Layout.Border, box, ref text, ref hitsOnly);
            }
            else
            {
                text = visual.Foreground ?? text;
                if (!hitsOnly)
                {
                    DrawBox(visual, node.Layout.Border, box);
                }
            }
        }

        if (writesHit && _noHitDepth == 0)
        {
            WriteHitRegion(handle, box, visual?.Radii ?? default);
        }

        if (!hitsOnly && visual?.Draw is { } draw)
        {
            CallDraw(draw, handle, box);
        }

        ref UiNode after = ref _tree.Nodes.GetRef(handle);
        if (!hitsOnly && after.Text is { } leaf)
        {
            Edges inset = leaf.PaddingBorder;
            DrawText(leaf, box.X + inset.Left, box.Y + inset.Top, leaf.Color ?? text);
        }

        ref UiNode container = ref _tree.Nodes.GetRef(handle);
        NodeHandle child = container.FirstChild;
        if (!child.IsNone && container.Layout.Style.ClipsChildren)
        {
            pushed += PushChildScopes(handle, box);
        }

        while (!child.IsNone)
        {
            DrawNode(child, box.X, box.Y, text, hitsOnly);
            child = _tree.Nodes.GetRef(child).NextSibling;
        }

        for (; pushed > 0; pushed--)
        {
            _building.Pop();
        }

        if (marked)
        {
            _noHitDepth--;
        }
    }

    // The scopes of a node's children when its overflow is not visible (design §3.4.1, "The scroll container"): a clip to the
    // padding box, rounded by the inner radii (the outer ones less the border, CSS's inner border edge), on the axes that clip; a
    // scroll container clips both. Inside it, a scroll container moves its children by its scroll position, which is a translation
    // the list folds into the coordinates (§5.0). Its own box and hit region stay. Returns the scopes pushed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private int PushChildScopes(NodeHandle handle, Rect box)
    {
        ref UiNode node = ref _tree.Nodes.GetRef(handle);
        ref readonly LayoutStyle style = ref node.Layout.Style;
        Edges border = node.Layout.Border;
        CornerRadii outer = node.Visual?.Radii ?? default;

        // Each inner radius is the outer one less the wider of the corner's two borders: CSS takes the horizontal radius less the
        // side's width and the vertical one less the top's or bottom's, an ellipse that a circular radius cannot hold. The two
        // agree for a uniform border.
        bool scrolls = style.IsScrollContainer;
        bool clipX = scrolls || style.OverflowX != Overflow.Visible;
        bool clipY = scrolls || style.OverflowY != Overflow.Visible;
        float x = clipX ? box.X + border.Left : box.X - UnclippedReach;
        float y = clipY ? box.Y + border.Top : box.Y - UnclippedReach;
        float width = clipX ? Math.Max(0, box.Width - border.Left - border.Right) : box.Width + (2 * UnclippedReach);
        float height = clipY ? Math.Max(0, box.Height - border.Top - border.Bottom) : box.Height + (2 * UnclippedReach);
        var inner = new CornerRadii(
            Math.Max(0, outer.TopLeft - Math.Max(border.Left, border.Top)),
            Math.Max(0, outer.TopRight - Math.Max(border.Right, border.Top)),
            Math.Max(0, outer.BottomRight - Math.Max(border.Right, border.Bottom)),
            Math.Max(0, outer.BottomLeft - Math.Max(border.Left, border.Bottom))
        );
        _building.PushClip(x, y, width, height, inner);
        if (!scrolls)
        {
            return 1;
        }

        Vector2 offset = _tree.ScrollOffsetOf(handle);
        if (offset == Vector2.Zero)
        {
            return 1;
        }

        _building.PushTransform(Matrix3x2.CreateTranslation(-offset));
        return 2;
    }

    // A node with a look of its state or a scope: its values, with the hovered, the pressed, and the focus styles laid over them where
    // the node is in that state; the transform and the opacity pushed around the subtree; then its box. Returns the scopes pushed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private int DrawResolved(NodeVisual visual, PseudoState state, Edges border, Rect box, ref Color text, ref bool hitsOnly)
    {
        Paint? background = visual.Background;
        Paint? borderPaint = visual.BorderPaint;
        Color? foreground = visual.Foreground;
        float opacity = visual.Opacity;
        Matrix3x2? transform = visual.Transform;
        if (visual.States is { } styles)
        {
            if ((state & PseudoState.Hovered) != 0 && styles.Hovered is { } hovered)
            {
                Over(hovered, ref background, ref borderPaint, ref foreground, ref opacity, ref transform);
            }

            if ((state & PseudoState.Pressed) != 0 && styles.Pressed is { } pressed)
            {
                Over(pressed, ref background, ref borderPaint, ref foreground, ref opacity, ref transform);
            }

            // The focus states over the pressed one, the later the stronger (design §2.13).
            if (styles.Focus is { } focus)
            {
                if ((state & PseudoState.FocusWithin) != 0 && focus.FocusWithin is { } within)
                {
                    Over(within, ref background, ref borderPaint, ref foreground, ref opacity, ref transform);
                }

                if ((state & PseudoState.Focused) != 0 && focus.Focused is { } focused)
                {
                    Over(focused, ref background, ref borderPaint, ref foreground, ref opacity, ref transform);
                }

                if ((state & PseudoState.FocusVisible) != 0 && focus.FocusVisible is { } visible)
                {
                    Over(visible, ref background, ref borderPaint, ref foreground, ref opacity, ref transform);
                }
            }

            // Last: a disabled node is neither pressed nor focused, so its look wins (design §2.13).
            if ((state & PseudoState.Disabled) != 0 && styles.Disabled is { } disabled)
            {
                Over(disabled, ref background, ref borderPaint, ref foreground, ref opacity, ref transform);
            }
        }

        CornerRadii radii = visual.Radii;
        BoxShadow? shadow = visual.Shadow;
        visual.Transitions?.Apply(FrameTime, ref background, ref borderPaint, ref foreground, ref opacity, ref transform, ref radii, ref shadow);

        // A Show's part on its way in or out: its own opacity times how far in it is, and moved toward where it is when out.
        if (visual.Presence < 1)
        {
            float presence = visual.Presence;
            opacity *= presence;
            Vector2 offset = visual.PresenceOffset * (1 - presence);
            if (offset != Vector2.Zero)
            {
                transform = (transform ?? Matrix3x2.Identity) * Matrix3x2.CreateTranslation(offset);
            }
        }

        text = foreground ?? text;
        int pushed = 0;
        if (transform is { } matrix)
        {
            // About the box's center: moved there, transformed, and moved back (System.Numerics applies the left one first).
            var center = new Vector2(box.X + (box.Width * 0.5f), box.Y + (box.Height * 0.5f));
            _building.PushTransform(Matrix3x2.CreateTranslation(-center) * matrix * Matrix3x2.CreateTranslation(center));
            pushed++;
        }

        if (opacity == 0)
        {
            hitsOnly = true;
        }
        else if (opacity < 1 && !hitsOnly)
        {
            _building.PushOpacity(opacity);
            pushed++;
        }

        if (!hitsOnly)
        {
            DrawBox(shadow, radii, background, borderPaint, border, box);
        }

        return pushed;
    }

    // The box of a node with its own values, read here so that their copies are not in DrawNode's frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawBox(NodeVisual visual, Edges border, Rect box) =>
        DrawBox(visual.Shadow, visual.Radii, visual.Background, visual.BorderPaint, border, box);

    // The user's draw function, apart from DrawNode so that only a node with one pays for the exception's frame. An exception goes to
    // the handler of unhandled exceptions, and the scopes the function left open are closed, so that the rest of the frame is drawn
    // and presented (design §5.0, "The draw function's exceptions"). Flutter reports a paint's exception and paints the siblings.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CallDraw(DrawFunc draw, NodeHandle handle, Rect box)
    {
        DisplayList list = _building;
        int depth = list.ScopeDepth;
        list.HitsSuppressed = _noHitDepth > 0;
        try
        {
            draw(new DrawingContext(list, handle, this), box);
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            ReactiveRuntime.ReportException(exception);
        }
        finally
        {
            list.HitsSuppressed = false;
        }

        list.PopTo(depth);
    }

    // A style's set values replace the look's; the unset ones keep it.
    private static void Over(
        in PaintStyle style,
        ref Paint? background,
        ref Paint? borderPaint,
        ref Color? foreground,
        ref float opacity,
        ref Matrix3x2? transform
    )
    {
        background = style.Background ?? background;
        borderPaint = style.BorderPaint ?? borderPaint;
        foreground = style.Foreground ?? foreground;
        opacity = style.Opacity ?? opacity;
        transform = style.Transform ?? transform;
    }

    // The node's box as its hit region, and where it is in the list, so that the input finds the node's state for its local position.
    private void WriteHitRegion(NodeHandle handle, Rect box, in CornerRadii radii)
    {
        ref UiNode node = ref _tree.Nodes.GetRef(handle);
        if (node.Input is { } input)
        {
            input.HitEntry = _building.HitEntries.Length;
            input.HitBuild = _building.Build;
        }

        if (node.Text?.Ranges is { } ranges)
        {
            ranges.HitEntry = _building.HitEntries.Length;
            ranges.HitBuild = _building.Build;
        }

        _building.HitRegion(handle, box.X, box.Y, box.Width, box.Height, radii);
    }

    private void DrawBox(BoxShadow? shadow, in CornerRadii radii, Paint? background, Paint? borderPaint, Edges border, Rect box)
    {
        if (shadow is { } cast)
        {
            _building.DrawShadow(box.X, box.Y, box.Width, box.Height, radii, cast);
        }

        bool hasBorder = borderPaint is not null && (border.Left > 0 || border.Top > 0 || border.Right > 0 || border.Bottom > 0);
        if (background is not null || hasBorder)
        {
            Paint fill = background ?? new Paint(default(Color));
            Border? edge = hasBorder ? new Border(new BorderWidths(border.Left, border.Top, border.Right, border.Bottom), borderPaint!.Value) : null;
            _building.FillRect(box.X, box.Y, box.Width, box.Height, fill, radii, edge);
        }
    }

    /// <summary>
    /// Draws the lines a text layout committed, from its box's top left (<see cref="DrawingContext.DrawText(TextLayout, float, float, Color)"/>).
    /// </summary>
    /// <param name="text">The text leaf whose layout committed its lines.</param>
    /// <param name="x">The left edge of the box, in logical pixels.</param>
    /// <param name="y">The top edge of the box, in logical pixels.</param>
    /// <param name="color">The color of the text.</param>
    internal void DrawLaidText(TextLeaf text, float x, float y, Color color) => DrawText(text, x, y, color);

    // The lines of the width the layout committed, from the content box's top left, each at its baseline.
    private void DrawText(TextLeaf text, float x, float y, Color color)
    {
        ShapingResult? result = text.Shaped?.Result;
        LineBreakCache? cache = text.LineBreaks;
        if (result is null || cache is null || cache.Committed < 0)
        {
            return;
        }

        LineLayoutResult layout = cache[cache.Committed].Layout;
        ReadOnlySpan<LaidGlyph> glyphs = layout.Glyphs;
        LeafSelection? selection = text.Selection is { IsEmpty: false } selected ? selected : null;
        float top = y;
        foreach (LaidLine line in layout.Lines)
        {
            (float height, float baseline) = LayoutEngine.TextLine(text, result, line.Span);

            if (selection is not null && selection.Start < line.Span.End && selection.End > line.Span.Start)
            {
                ReadOnlySpan<LaidGlyph> on = glyphs.Slice(line.GlyphStart, line.GlyphCount);
                DrawSelected(text.Ranges, selection, result, on, x, top, height, top + baseline, color);
                top += height;
                continue;
            }

            if (text.Ranges is { } ranges)
            {
                DrawRanges(ranges, result, glyphs.Slice(line.GlyphStart, line.GlyphCount), x, top, height, top + baseline, color);
                top += height;
                continue;
            }

            _building.DrawLine(
                glyphs.Slice(line.GlyphStart, line.GlyphCount),
                result.Runs,
                result.Styles,
                _atlas,
                x,
                top + baseline,
                color,
                _textRendering.Coverage,
                _textRendering.IsAliased
            );
            top += height;
        }
    }
}
