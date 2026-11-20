using System.Diagnostics;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The frames scene (Scene.Build) as a UI tree drawn by WindowFrames (row V1b): the marker, the 1,000 rectangles as absolutely
// positioned boxes that move with the frame as the scene's do, and the line of text as a text leaf in the scene's font. A frame
// moves the boxes (SetStyle), lays the tree out, and changes the background of a hidden node, so that the frame draws although the
// moves are layout changes; then WindowFrames builds the display list from the tree. The target records when Draw and Present start
// and end, so that the draw stage's time is apart from the layout's. With hover (row V1c), the boxes stay where the first frame put
// them, each with a hovered background, and a frame moves the pointer from one box to another through the window's input instead:
// the frame is drawn for the hover's change of color alone, with no layout. With scroll (row V2), the boxes are in a scroll container
// as large as the window, with a box far below that gives it room to scroll, and stay where the first frame put them; a frame
// changes the scroll position, which draws the boxes moved inside a clip with no layout (design §3.4.1). With a list (row P02e),
// the tree holds, in place of the boxes, a box as large as the window that scrolls vertically over 10,000 rows 20 high, made with
// VirtualFor ("virtual") or with For in a column ("for"); a frame scrolls it 7 more pixels, so that the virtualized list makes a row
// and takes one out about every third frame. Its layout time is the materialization stage's rounds with their layout passes.
//
// The tree is the window core's of a hosted Scene (row PL1, design §6.0): its frames, its input, and the loop's scheduler are the
// ones an app's window has, and its WindowFrames tells the loop's scheduler of each present itself. The nodes hang under the core's
// root node, which takes the window's size, as the scene's own root did; the scene does not mount a root function. The frames stay
// run by the caller's own callback (Frames.Run, External.App), which runs RunFrame, so that the figures compare with the records
// made before the host (Issue #928).
internal sealed class TreeScene : IDisposable
{
    private readonly Scene _scene;
    private readonly WindowCore _core;
    private readonly UiTree _tree;
    private readonly NodeHandle _root;
    private readonly NodeHandle[] _boxes = new NodeHandle[Scene.Rectangles];
    private readonly NodeHandle _hidden;
    private readonly NodeHandle _marker;
    private readonly TimingTarget _target;
    private readonly WindowInput? _input;
    private readonly ScrollState? _scroll;
    private readonly ScopeHandle _listScope;
    private readonly bool _list;

    public TreeScene(Scene scene, bool noText, bool hover = false, bool scroll = false, string list = "")
    {
        _scene = scene;
        _core = scene.Core ?? throw new ArgumentException("A tree scene draws in the host's window: make the scene with hosted.", nameof(scene));
        _target = scene.Timing ?? throw new ArgumentException("The host gave the core no timing target.", nameof(scene));
        _tree = _core.Tree;
        _root = _core.RootNode;
        _marker = Absolute(_root, 0, 0, 40, 40);
        _tree.SetBackground(_marker, new Paint(Scene.Marker));
        if (list.Length > 0)
        {
            _list = true;
            float s = scene.Window.ScaleFactor;
            (_scroll, _listScope) = MountList(list, scene.Renderer.Width / s, scene.Renderer.Height / s);
            _hidden = _tree.CreateNode(new LayoutStyle { Hidden = true });
            _tree.AppendChild(_root, _hidden);
            FirstFrame();
            return;
        }

        NodeHandle parent = _root;
        if (scroll)
        {
            var all = LengthPercentageAuto.Px(0);
            parent = _tree.CreateNode(
                new LayoutStyle
                {
                    Position = Position.Absolute,
                    Inset = new EdgeInsets<LengthPercentageAuto>(all, all, all, all),
                    OverflowY = Overflow.Scroll,
                }
            );
            _tree.AppendChild(_root, parent);
            _ = Absolute(parent, 0, 2_000, 16, 16);
            _scroll = _tree.ScrollStateFor(parent);
        }

        for (int i = 0; i < _boxes.Length; i++)
        {
            _boxes[i] = Absolute(parent, 0, 0, 16, 16);
            _tree.SetBackground(_boxes[i], new Paint((i & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66)));
            if (hover)
            {
                _tree.SetHoveredStyle(_boxes[i], new PaintStyle { Background = Color.FromRgb(0xE8E8E8) });
            }
        }

        // The scene draws the line at the baseline 30; the leaf's top is its ascent above (the test font's 1160 in 1000 units per em).
        if (!noText)
        {
            NodeHandle line = _tree.CreateTextNode(
                Place(60, 30 - (24 * 1.16f), float.NaN, float.NaN),
                new TextStyle(FontRequest.Default, 24, TextLanguage.FromTag("ja"), float.NaN)
            );
            _tree.SetText(line, Scene.Line);
            _tree.SetTextColor(line, Color.FromRgb(0xE8E8E8));
            _tree.AppendChild(_root, line);
        }

        _hidden = _tree.CreateNode(new LayoutStyle { Hidden = true });
        _tree.AppendChild(_root, _hidden);
        if (hover)
        {
            _input = _core.Input;
            _ = Update(0);
        }
        else if (scroll)
        {
            _ = Update(0);
        }

        FirstFrame();
    }

    public TimingTarget Target => _target;

    // Draws the marker in Scene.PressedMarker after an odd number of key presses (the input measurement, row R3): a paint update
    // of the node, drawn by the next RunFrame.
    public void SetPressed(bool pressed) => _tree.SetBackground(_marker, new Paint(pressed ? Scene.PressedMarker : Scene.Marker));

    // With hover, the milliseconds the input took to deliver the frame's move.
    public double InputMilliseconds { get; private set; }

    // With hover, the moves after which the node the window's hit test gives at the move's point, in the display list the input
    // tested (WindowInput.HitAt: the frames' list, with no popup), was not hovered: the move did not reach the node under it
    // (.cursor/rules/bench.mdc, a scene checks each input).
    public int HoverMisses { get; private set; }

    // With hover, the moves whose point the hit test gives to a box other than the target (_boxes[frame & 1]): a later box in the
    // tree's order lies over the target's middle and is the front-most region there (DisplayList.HitTestAt walks the regions from
    // the last). The move still changes the hovered box. The index in _boxes of the first such box, or -1.
    public int HoverOnOtherBox { get; private set; }

    public int HoverOtherBoxIndex { get; private set; } = -1;

    // The accessibility stage (design §2.10), which the scene runs before the frame and times on its own, so that build_ms keeps
    // what it timed on main's direct window, where the tree has no accessibility tree (PL1-e2e, "計測の後の調べ"; Issue #928). The
    // stage in the frame after it finds no mark and returns. Returns the milliseconds.
    public double UpdateAccessibility()
    {
        long start = Stopwatch.GetTimestamp();
        _tree.UpdateAccessibility();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    // The number of times the window's own display list was built, the first frame's and the synchronous resizes' included.
    public int MainBuilds => _core.Frames.MainBuilds;

    // Moves the boxes as Scene.Build moves the rectangles, and lays the tree out; returns the milliseconds of the layout pass. With
    // hover after the first frame, moves the pointer over the first box or the second instead, which changes the hover.
    public double Update(int frame)
    {
        if (_list && _scroll is not null)
        {
            float range = Math.Max(1, _scroll.MaxOffsetValue.Y);
            _ = _scroll.SetOffset(new System.Numerics.Vector2(0, (frame * 7) % range));
            long listStart = Stopwatch.GetTimestamp();
            for (int round = 0; round <= 32 && _tree.Materialize(round); round++)
            {
                LayoutEngine.RunLayoutPass(_tree);
            }

            LayoutEngine.RunLayoutPass(_tree);
            _tree.PublishScrollRanges();
            double ms = Stopwatch.GetElapsedTime(listStart).TotalMilliseconds;
            _tree.SetBackground(_hidden, new Paint((frame & 1) == 0 ? Color.FromRgb(0x000000) : Color.FromRgb(0xFFFFFF)));
            return ms;
        }

        if (_input is not null && frame > 0)
        {
            float pixels = _scene.Window.ScaleFactor;
            NodeHandle target = _boxes[frame & 1];
            Rect box = _tree.GetRect(target);
            var move = new PointerInput(
                PointerInputKind.Move,
                (int)((box.X + 8) * pixels),
                (int)((box.Y + 8) * pixels),
                PointerButton.None,
                PointerButtons.None,
                KeyModifiers.None,
                0,
                TimeSpan.Zero,
                PointerType.Mouse
            );
            long started = Stopwatch.GetTimestamp();
            _input.Handle(move);
            InputMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            NodeHandle hit = _core.Frames.List.HitTest(move.X, move.Y);
            if (hit.IsNone || !Hovered(hit))
            {
                HoverMisses++;
            }

            if (hit != target)
            {
                NoteOtherBox(hit);
            }

            return 0;
        }

        // A new position each frame, within the room the far box gives: no box moves in the layout.
        if (_scroll is not null && frame > 0)
        {
            _ = _scroll.SetOffset(new System.Numerics.Vector2(0, (frame * 3) % 1_000));
            long layoutStart = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(_tree);
            return Stopwatch.GetElapsedTime(layoutStart).TotalMilliseconds;
        }

        float scale = _scene.Window.ScaleFactor;
        int width = (int)(_scene.Renderer.Width / scale);
        int height = (int)(_scene.Renderer.Height / scale);
        for (int i = 0; i < _boxes.Length; i++)
        {
            float x = ((i * 37) + (frame * 3)) % Math.Max(1, width - 20);
            float y = 50 + ((i * 11) % Math.Max(1, height - 70));
            _tree.SetStyle(_boxes[i], Place(x, y, 16, 16));
        }

        _tree.SetBackground(_hidden, new Paint((frame & 1) == 0 ? Color.FromRgb(0x000000) : Color.FromRgb(0xFFFFFF)));
        long start = Stopwatch.GetTimestamp();
        LayoutEngine.RunLayoutPass(_tree);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public void RunFrame() => _core.Frames.RunFrame();

    // Sends the tree's own requests for a frame to the caller's frame callback, which runs RunFrame, in place of the window core's
    // WindowFrames.RunFrame. The loop's scheduler holds one callback, and a request of the tree made between two frames (a key
    // that changes the marker, a click, an activation) replaced the callback that animates the scene, which then drew only when
    // something asked (PR #943's measurement of `app --tree`). On main the tree's frames had a scheduler of their own that did
    // nothing, so every frame was the callback's; this keeps that. The window core's other requests (a touchpad's gesture, a
    // window shown again) still go to WindowFrames.
    public void SendFrameRequestsTo(Action callback)
    {
        IFrameScheduler scheduler = _scene.HostWindow!.FrameScheduler;
        _tree.FrameNeeded = () => scheduler.RequestFrame(callback);
    }

    // Counts a move whose point the hit test gave to a node other than the target, and keeps the index of the first such box.
    private void NoteOtherBox(NodeHandle hit)
    {
        HoverOnOtherBox++;
        if (HoverOtherBoxIndex >= 0)
        {
            return;
        }

        for (int i = 0; i < _boxes.Length; i++)
        {
            if (_boxes[i] == hit)
            {
                HoverOtherBoxIndex = i;
                return;
            }
        }
    }

    private bool Hovered(NodeHandle node) => (_tree.Nodes.GetRef(node).Pseudo & PseudoState.Hovered) != 0;

    // One frame on the synchronous resize path at the window's size, through the window core's Resize as the host's WM_SIZE calls
    // it, without the message and the swap chain's resize (design §2.11.1; the WM_SIZE path is HostResize's).
    public void PresentOnResizePath()
    {
        float scale = _scene.Window.ScaleFactor;
        _core.Resize(new Size(_scene.Renderer.Width / scale, _scene.Renderer.Height / scale), scale);
    }

    public void Dispose()
    {
        if (_list)
        {
            Scope.Dispose(_listScope);
        }
    }

    // The first frame as the window core's Mount runs it after the root function (design §6.0, "The order of creation", step 7):
    // the frame the new nodes asked for is cancelled, and the frame runs at once on the synchronous resize path; then assistive
    // technology that asked while the window showed gets the tree.
    private void FirstFrame()
    {
        _scene.HostWindow!.FrameScheduler.CancelFrame();
        PresentOnResizePath();
        _scene.HostWindow.RootMounted();
    }

    private static LayoutStyle Place(float x, float y, float width, float height) =>
        new()
        {
            Position = Position.Absolute,
            Inset = new EdgeInsets<LengthPercentageAuto>(
                LengthPercentageAuto.Px(x),
                LengthPercentageAuto.Px(y),
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Auto
            ),
            Width = float.IsNaN(width) ? Length.Auto : Length.Px(width),
            Height = float.IsNaN(height) ? Length.Auto : Length.Px(height),
        };

    // The box that scrolls over 10,000 rows, in a scope of its own, put under the root: a virtualized list, or For in a column.
    private (ScrollState Scroll, ScopeHandle Scope) MountList(string list, float width, float height)
    {
        IReadOnlySignal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 10_000)]);
        var state = new VirtualListState();
        Element box = default;
        UiTree.Current = _tree;
        ScopeHandle scope;
        try
        {
            scope = Scope.Create(() =>
            {
                // A row's key is its item, so the item of a row never changes and one read is the row's color (XYLEM110).
#pragma warning disable XYLEM110
                Element Row(IReadOnlySignal<int> item) =>
                    Elements.Box().Height(20).Background(new Paint((item.Get() & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66)));
#pragma warning restore XYLEM110
                box =
                    list == "virtual"
                        ? Elements
                            .Box()
                            .Width(width)
                            .Height(height)
                            .Overflow(Overflow.Visible, Overflow.Scroll)
                            .VirtualFor(items, static i => i, Row, state)
                        : Elements.ScrollView(Elements.Column().For(items, static i => i, Row), state.Scroll).Width(width).Height(height);
                _tree.AppendChild(_root, box.Node);
            });
        }
        finally
        {
            UiTree.Current = null;
        }

        return (state.Scroll, scope);
    }

    private NodeHandle Absolute(NodeHandle parent, float x, float y, float width, float height)
    {
        NodeHandle node = _tree.CreateNode(Place(x, y, width, height));
        _tree.AppendChild(parent, node);
        return node;
    }

    // What the host gives the window core as its target (Win32PlatformApp.WrapTarget): passes the draws and presents on to the
    // renderer, and records when the last Draw and Present started and ended, and how many presents passed.
    internal sealed class TimingTarget(IRenderTarget inner) : IRenderTarget
    {
        public long DrawStart { get; private set; }

        public long DrawEnd { get; private set; }

        public long PresentEnd { get; private set; }

        public int Presents { get; private set; }

        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas, TextureCache images)
        {
            DrawStart = Stopwatch.GetTimestamp();
            inner.Draw(lists, clearColor, atlas, images);
            DrawEnd = Stopwatch.GetTimestamp();
        }

        public void Present()
        {
            inner.Present();
            PresentEnd = Stopwatch.GetTimestamp();
            Presents++;
        }
    }
}
