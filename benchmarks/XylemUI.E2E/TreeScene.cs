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
// changes the scroll position, which draws the boxes moved inside a clip with no layout (design §3.4.1).
internal sealed class TreeScene : IDisposable
{
    private readonly Scene _scene;
    private readonly TextContext _text;
    private readonly UiTree _tree;
    private readonly NodeHandle _root;
    private readonly NodeHandle[] _boxes = new NodeHandle[Scene.Rectangles];
    private readonly NodeHandle _hidden;
    private readonly WindowFrames _frames;
    private readonly TimingTarget _target;
    private readonly WindowInput? _input;
    private readonly ScrollState? _scroll;

    public TreeScene(Scene scene, Font font, bool noText, bool hover = false, bool scroll = false)
    {
        _scene = scene;
        _text = new TextContext(new OneFont(font));
        _tree = new UiTree(_text);
        _root = _tree.CreateNode(new LayoutStyle());
        NodeHandle marker = Absolute(_root, 0, 0, 40, 40);
        _tree.SetBackground(marker, new Paint(Scene.Marker));
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
        _target = new TimingTarget(scene);
        _frames = new WindowFrames(_tree, _root, _target, scene.Atlas, new NoScheduler(), scene.Clear, scene.Renderer.TextRendering);
        scene.Window.TextRenderingChanged += (_, _) => _frames.TextRendering = scene.Renderer.TextRendering;
        float scale = scene.Window.ScaleFactor;
        if (hover)
        {
            _input = new WindowInput(_tree, _frames, new NoPlatform());
            _ = Update(0);
        }
        else if (scroll)
        {
            _ = Update(0);
        }

        _frames.Resize(new Size(scene.Renderer.Width / scale, scene.Renderer.Height / scale), scale);
    }

    public TimingTarget Target => _target;

    // With hover, the milliseconds the input took to deliver the frame's move.
    public double InputMilliseconds { get; private set; }

    // Moves the boxes as Scene.Build moves the rectangles, and lays the tree out; returns the milliseconds of the layout pass. With
    // hover after the first frame, moves the pointer over the first box or the second instead, which changes the hover.
    public double Update(int frame)
    {
        if (_input is not null && frame > 0)
        {
            float pixels = _scene.Window.ScaleFactor;
            Rect box = _tree.GetRect(_boxes[frame & 1]);
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

    public void RunFrame() => _frames.RunFrame();

    public void Dispose() => _text.Dispose();

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

    private NodeHandle Absolute(NodeHandle parent, float x, float y, float width, float height)
    {
        NodeHandle node = _tree.CreateNode(Place(x, y, width, height));
        _tree.AppendChild(parent, node);
        return node;
    }

    // Draws on the scene's renderer, and records when Draw and Present start and end.
    internal sealed class TimingTarget(Scene scene) : IRenderTarget
    {
        public long DrawStart { get; private set; }

        public long DrawEnd { get; private set; }

        public long PresentEnd { get; private set; }

        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas)
        {
            DrawStart = Stopwatch.GetTimestamp();
            scene.Renderer.Draw(list, clearColor, atlas);
            DrawEnd = Stopwatch.GetTimestamp();
        }

        public void Present()
        {
            scene.Renderer.Present();
            PresentEnd = Stopwatch.GetTimestamp();
        }
    }

    // The scene's pointer is made up, so nothing is captured or shown.
    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }

    // The frames here are run by Frames.Run's own callback.
    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }

    private sealed class OneFont(Font font) : IFontProvider
    {
        public FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request) =>
            new(font, text.Length - start, 1);
    }
}
