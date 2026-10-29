using System.Numerics;
using System.Runtime.CompilerServices;
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
        _runFrame = RunFrame;
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
    /// Gets the display list of the last frame that drew, whose hit regions the input tests (design §7, "Pointer input").
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
    /// Gets the number of times the display list was built: a node's hit region index is valid for the build it was written in
    /// (<see cref="NodeInput.HitBuild"/>).
    /// </summary>
    internal int Builds { get; private set; }

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

        // Before the layout pass: a touchpad's pan scrolls with this frame's time, and its changes are this frame's (row I1c). The
        // next frame it needs is asked for once a resize's frame no longer holds the requests back.
        bool gestures = Input?.UpdateGestures(FrameTime) == true;
        bool layout = _tree.LayoutRoots.Count > 0;
        LayoutEngine.RunLayoutPass(_tree);

        // Cleared after the pass: the requests a pass puts back after an exception, or leaves at its cycle limit, ask for no frame
        // of their own, and are tried by the next frame something else asks for (design §2.4.1); a leaf that keeps throwing would
        // otherwise draw and present every frame (PR #313's review). A mark made from here on asks for the next frame.
        _tree.ClearFrameRequest();
        _resizing = false;
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

        if (!layout && _tree.PaintUpdates.Count == 0)
        {
            return;
        }

        // The marks go before the list is built, so that one made while it is built asks for the next frame.
        foreach (NodeHandle node in _tree.TakePaintUpdates())
        {
            if (_tree.Nodes.IsAlive(node))
            {
                _tree.Nodes.GetRef(node).Dirty.SelfDirty &= DirtyFlags.Layout;
            }
        }

        _atlas.BeginFrame();
        _list.Reset(_scale);
        Builds++;
        DrawNode(_root, 0, 0, RootText, hitsOnly: false);
        _list.ResolveGlyphs(_atlas);
        _target.Draw(_list, ClearColor, _atlas);
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
        RunFrame();
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

        Rect rect = node.Layout.Rect;
        var box = new Rect(parentX + rect.X, parentY + rect.Y, rect.Width, rect.Height);
        NodeVisual? visual = node.Visual;

        // A scroll container takes the wheel over the parts of it no child takes (design §3.4.1), and a text leaf with links takes
        // the pointer over them (design §4.8).
        bool writesHit = node.Input?.WritesHitRegion == true || node.Layout.Style.IsScrollContainer || node.Text?.Ranges?.HasLinks == true;
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
            if (styled || visual.Scoped)
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

        if (writesHit)
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
            _list.Pop();
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
        _list.PushClip(x, y, width, height, inner);
        if (!scrolls)
        {
            return 1;
        }

        Vector2 offset = _tree.ScrollOffsetOf(handle);
        if (offset == Vector2.Zero)
        {
            return 1;
        }

        _list.PushTransform(Matrix3x2.CreateTranslation(-offset));
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

        text = foreground ?? text;
        int pushed = 0;
        if (transform is { } matrix)
        {
            // About the box's center: moved there, transformed, and moved back (System.Numerics applies the left one first).
            var center = new Vector2(box.X + (box.Width * 0.5f), box.Y + (box.Height * 0.5f));
            _list.PushTransform(Matrix3x2.CreateTranslation(-center) * matrix * Matrix3x2.CreateTranslation(center));
            pushed++;
        }

        if (opacity == 0)
        {
            hitsOnly = true;
        }
        else if (opacity < 1 && !hitsOnly)
        {
            _list.PushOpacity(opacity);
            pushed++;
        }

        if (!hitsOnly)
        {
            DrawBox(visual.Shadow, visual.Radii, background, borderPaint, border, box);
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
        int depth = _list.ScopeDepth;
        try
        {
            draw(new DrawingContext(_list, handle, this), box);
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            ReactiveRuntime.ReportException(exception);
        }

        _list.PopTo(depth);
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
            input.HitEntry = _list.HitEntries.Length;
            input.HitBuild = Builds;
        }

        if (node.Text?.Ranges is { } ranges)
        {
            ranges.HitEntry = _list.HitEntries.Length;
            ranges.HitBuild = Builds;
        }

        _list.HitRegion(handle, box.X, box.Y, box.Width, box.Height, radii);
    }

    private void DrawBox(BoxShadow? shadow, in CornerRadii radii, Paint? background, Paint? borderPaint, Edges border, Rect box)
    {
        if (shadow is { } cast)
        {
            _list.DrawShadow(box.X, box.Y, box.Width, box.Height, radii, cast);
        }

        bool hasBorder = borderPaint is not null && (border.Left > 0 || border.Top > 0 || border.Right > 0 || border.Bottom > 0);
        if (background is not null || hasBorder)
        {
            Paint fill = background ?? new Paint(default(Color));
            Border? edge = hasBorder ? new Border(new BorderWidths(border.Left, border.Top, border.Right, border.Bottom), borderPaint!.Value) : null;
            _list.FillRect(box.X, box.Y, box.Width, box.Height, fill, radii, edge);
        }
    }

    /// <summary>
    /// Draws the lines a text layout committed, from its box's top left (<see cref="DrawingContext.DrawText(TextLayout, float, float, Color)"/>).
    /// </summary>
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
        float top = y;
        foreach (LaidLine line in layout.Lines)
        {
            (float height, float baseline) = LayoutEngine.TextLine(result, line.Span, text.Style);
            if (text.Ranges is { } ranges)
            {
                DrawRanges(ranges, result, glyphs.Slice(line.GlyphStart, line.GlyphCount), x, top, height, top + baseline, color);
                top += height;
                continue;
            }

            _list.DrawLine(
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
