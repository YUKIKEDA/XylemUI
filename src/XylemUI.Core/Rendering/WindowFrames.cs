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
internal sealed class WindowFrames
{
    private readonly UiTree _tree;
    private readonly NodeHandle _root;
    private readonly IRenderTarget _target;
    private readonly GlyphAtlas _atlas;
    private readonly IFrameScheduler _scheduler;
    private readonly Action _runFrame;
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
    public WindowFrames(
        UiTree tree,
        NodeHandle root,
        IRenderTarget target,
        GlyphAtlas atlas,
        IFrameScheduler scheduler,
        Color clearColor,
        TextRenderingParameters? textRendering = null
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
        _textRendering = textRendering ?? TextRenderingParameters.Default;
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
    public TextRenderingParameters TextRendering
    {
        get { return _textRendering; }
        set
        {
            if (value != _textRendering)
            {
                _textRendering = value;
                Invalidation.Invalidate(_tree, _root, ChangeKind.Color);
            }
        }
    }

    /// <summary>
    /// Gets the number of frames that drew and presented.
    /// </summary>
    public int PresentCount { get; private set; }

    /// <summary>
    /// Gets the display list of the last frame that drew.
    /// </summary>
    internal DisplayList List => _list;

    /// <summary>
    /// Runs one frame: the layout pass; then, when the frame had a layout request or a paint update, the display list built
    /// again from the tree, its glyphs resolved, and drawn and presented. A frame with neither ends after the layout pass.
    /// </summary>
    public void RunFrame()
    {
        bool layout = _tree.LayoutRoots.Count > 0;
        LayoutEngine.RunLayoutPass(_tree);

        // Cleared after the pass: the requests a pass puts back after an exception, or leaves at its cycle limit, ask for no frame
        // of their own, and are tried by the next frame something else asks for (design §2.4.1); a leaf that keeps throwing would
        // otherwise draw and present every frame (PR #313's review). A mark made from here on asks for the next frame.
        _tree.ClearFrameRequest();
        _resizing = false;
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
        DrawNode(_root, 0, 0);
        _list.ResolveGlyphs(_atlas);
        _target.Draw(_list, ClearColor, _atlas);
        _target.Present();
        PresentCount++;
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

    private void RequestFrame()
    {
        if (!_resizing)
        {
            _scheduler.RequestFrame(_runFrame);
        }
    }

    // A node and its subtree, in tree order: the shadow, the background and border, the draw function, the text, then the
    // children. The refs are taken again after each call that can run the user's code (design §2.5).
    private void DrawNode(NodeHandle handle, float parentX, float parentY)
    {
        ref UiNode node = ref _tree.Nodes.GetRef(handle);
        if (node.Layout.Style.Hidden)
        {
            return;
        }

        Rect rect = node.Layout.Rect;
        float x = parentX + rect.X;
        float y = parentY + rect.Y;
        NodeVisual? visual = node.Visual;
        if (visual is not null)
        {
            DrawBox(handle, visual, node.Layout.Border, new Rect(x, y, rect.Width, rect.Height));
        }

        ref UiNode after = ref _tree.Nodes.GetRef(handle);
        if (after.Text is { } text)
        {
            Edges inset = text.PaddingBorder;
            DrawText(text, x + inset.Left, y + inset.Top);
        }

        NodeHandle child = _tree.Nodes.GetRef(handle).FirstChild;
        while (!child.IsNone)
        {
            DrawNode(child, x, y);
            child = _tree.Nodes.GetRef(child).NextSibling;
        }
    }

    private void DrawBox(NodeHandle handle, NodeVisual visual, Edges border, Rect box)
    {
        if (visual.Shadow is { } shadow)
        {
            _list.DrawShadow(box.X, box.Y, box.Width, box.Height, visual.Radii, shadow);
        }

        bool hasBorder = visual.BorderPaint is not null && (border.Left > 0 || border.Top > 0 || border.Right > 0 || border.Bottom > 0);
        if (visual.Background is not null || hasBorder)
        {
            Paint fill = visual.Background ?? new Paint(default(Color));
            Border? edge = hasBorder
                ? new Border(new BorderWidths(border.Left, border.Top, border.Right, border.Bottom), visual.BorderPaint!.Value)
                : null;
            _list.FillRect(box.X, box.Y, box.Width, box.Height, fill, visual.Radii, edge);
        }

        visual.Draw?.Invoke(new DrawingContext(_list, handle), box);
    }

    // The lines of the width the layout committed, from the content box's top left, each at its baseline.
    private void DrawText(TextLeaf text, float x, float y)
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
            _list.DrawLine(
                glyphs.Slice(line.GlyphStart, line.GlyphCount),
                result.Runs,
                result.Size,
                _atlas,
                x,
                top + baseline,
                text.Color,
                _textRendering.Coverage,
                _textRendering.IsAliased
            );
            top += height;
        }
    }
}
