using System.Diagnostics;
using XylemUI.Text;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of animations on a window's frames (row AN1, design §2.14), with no GPU, in a window of 400 x 300: a column of 1,000
// boxes, of which the first N have their opacity bound to an animation of their own moving over an hour. Each sample moves the clock
// by 16 ms and runs a frame; it is timed in two parts: the animation list's move (the effects of the bound values run in it) and the
// whole frame. Then one box's width is bound to an animation, and the layout pass of its frame is timed: a layout value that moves
// lays out each frame.
internal static class AnimationCost
{
    private const int Boxes = 1_000;
    private const int Samples = 201;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("animations");
        foreach (int count in new[] { 0, 1, 100, 1_000 })
        {
            using var scene = new AnimationScene(count, widthAnimated: false);
            (double tick, double frame, _) = scene.Time(Samples);
            report.Number($"tick_us_p50_{count}", tick);
            report.Number($"frame_us_p50_{count}", frame);
        }

        using (var width = new AnimationScene(0, widthAnimated: true))
        {
            (double tick, double frame, double layout) = width.Time(Samples);
            report.Number("width_tick_us_p50", tick);
            report.Number("width_layout_us_p50", layout);
            report.Number("width_frame_us_p50", frame);
        }

        return 0;
    }

    private sealed class AnimationScene : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly UiTree _tree = new();
        private readonly StepClock _time = new();
        private readonly WindowFrames _frames;
        private readonly ScopeHandle _scope;
        private readonly List<Animation> _animations = [];

        public AnimationScene(int animated, bool widthAnimated)
        {
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default, time: _time);
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    var boxes = new Element[Boxes];
                    for (int i = 0; i < Boxes; i++)
                    {
                        Element box = Box().Height(0.25f).Width(100).Background(new Paint(Color.FromRgb(0x808080)));
                        if (i < animated)
                        {
                            var animation = Animation.Create(TimeSpan.FromHours(1));
                            _animations.Add(animation);
                            box = box.Bind(NodeProperties.Opacity, animation.Value);
                        }
                        else if (i == 0 && widthAnimated)
                        {
                            var animation = Animation.Create(TimeSpan.FromHours(1));
                            _animations.Add(animation);
                            Element target = box;
                            _ = Effect.Create(() => target.Width(100 + (200 * animation.Value.Get())));
                        }

                        boxes[i] = box;
                    }

                    _tree.AppendChild(root, Column(boxes).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            _frames.Resize(new Size(400, 300), 1);
            foreach (Animation animation in _animations)
            {
                animation.AnimateTo(1);
            }
        }

        // The medians of the list's move, of the frame (the move included), and of the layout pass, in microseconds. Each sample is
        // a frame 16 ms after the one before; the frame runs the list's move itself, so the move is timed apart on a frame of its
        // own first, and the frame after it is timed whole.
        public (double Tick, double Frame, double Layout) Time(int samples)
        {
            var ticks = new double[samples];
            var frames = new double[samples];
            var layouts = new double[samples];
            for (int i = -5; i < samples; i++)
            {
                _time.Now += TimeSpan.FromMilliseconds(16);
                long start = Stopwatch.GetTimestamp();
                _tree.Animations.Tick(_time.Now);
                long ticked = Stopwatch.GetTimestamp();
                LayoutEngine.RunLayoutPass(_tree);
                long laidOut = Stopwatch.GetTimestamp();
                _time.Now += TimeSpan.FromMilliseconds(16);
                long frameStart = Stopwatch.GetTimestamp();
                _frames.RunFrame();
                long frameEnd = Stopwatch.GetTimestamp();
                if (i >= 0)
                {
                    ticks[i] = Stopwatch.GetElapsedTime(start, ticked).TotalMicroseconds;
                    layouts[i] = Stopwatch.GetElapsedTime(ticked, laidOut).TotalMicroseconds;
                    frames[i] = Stopwatch.GetElapsedTime(frameStart, frameEnd).TotalMicroseconds;
                }
            }

            return (Median(ticks), Median(frames), Median(layouts));
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
        }

        private static double Median(double[] values)
        {
            Array.Sort(values);
            return values[values.Length / 2];
        }
    }

    // A clock that moves only when the scene moves it.
    private sealed class StepClock : TimeProvider
    {
        public TimeSpan Now { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Now.Ticks;
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() { }
    }

    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }

    private sealed class NoGlyphs : IGlyphRasterizer
    {
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) => throw new InvalidOperationException("No glyph is drawn.");
    }
}
