using System.Diagnostics;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The frames scene (Scene.Build) as a UI tree drawn by WindowFrames (row V1b): the marker, the 1,000 rectangles as absolutely
// positioned boxes that move with the frame as the scene's do, and the line of text as a text leaf in the scene's font. A frame
// moves the boxes (SetStyle), lays the tree out, and changes the background of a hidden node, so that the frame draws although the
// moves are layout changes; then WindowFrames builds the display list from the tree. The target records when Draw and Present start
// and end, so that the draw stage's time is apart from the layout's.
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

    public TreeScene(Scene scene, Font font)
    {
        _scene = scene;
        _text = new TextContext(new OneFont(font));
        _tree = new UiTree(_text);
        _root = _tree.CreateNode(new LayoutStyle());
        NodeHandle marker = Absolute(0, 0, 40, 40);
        _tree.SetBackground(marker, new Paint(Scene.Marker));
        for (int i = 0; i < _boxes.Length; i++)
        {
            _boxes[i] = Absolute(0, 0, 16, 16);
            _tree.SetBackground(_boxes[i], new Paint((i & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66)));
        }

        // The scene draws the line at the baseline 30; the leaf's top is its ascent above (the test font's 1160 in 1000 units per em).
        NodeHandle line = _tree.CreateTextNode(
            Place(60, 30 - (24 * 1.16f), float.NaN, float.NaN),
            new TextStyle(FontRequest.Default, 24, TextLanguage.FromTag("ja"), float.NaN)
        );
        _tree.SetText(line, Scene.Line);
        _tree.SetTextColor(line, Color.FromRgb(0xE8E8E8));
        _tree.AppendChild(_root, line);
        _hidden = _tree.CreateNode(new LayoutStyle { Hidden = true });
        _tree.AppendChild(_root, _hidden);
        _target = new TimingTarget(scene);
        _frames = new WindowFrames(_tree, _root, _target, scene.Atlas, new NoScheduler(), scene.Clear, scene.Coverage);
        float scale = scene.Window.ScaleFactor;
        _frames.Resize(new Size(scene.Renderer.Width / scale, scene.Renderer.Height / scale), scale);
    }

    public TimingTarget Target => _target;

    // Moves the boxes as Scene.Build moves the rectangles, and lays the tree out; returns the milliseconds of the layout pass.
    public double Update(int frame)
    {
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

    private NodeHandle Absolute(float x, float y, float width, float height)
    {
        NodeHandle node = _tree.CreateNode(Place(x, y, width, height));
        _tree.AppendChild(_root, node);
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
