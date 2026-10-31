using System.Diagnostics;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of a text field (row P02b, design §4.9, "Cost"), in the input report, with no window: a field of many lines, 400 x 300,
// holding 1,000 characters of an English sentence at 14 px in the Windows fonts, which wrap into about 20 lines, with the focus.
// Each sample is the input's delivery and the frame it asks for; *_us is the whole, and *_input_us, *_layout_us, and *_draw_us its
// three parts: the delivery (the handlers and the signals), the layout pass, and the rest of the frame (the display list).
//
// - text_field_type_1000_chars_us: a character typed at the end; a Backspace after each, untimed, keeps the length.
// - text_field_word_move_us: Control+Left from the end, whose word boundary is walked from the text's start; a Control+End after
//   each, untimed.
// - text_field_blink_frame_us: a tick of the caret's timer, which hides or shows it, and the frame that draws it.
// - text_field_drag_frame_us: a move of the mouse with its left button down, from a press on the first line, alternately to two
//   points on other lines; the selection changes at each.
internal static class TextFieldCost
{
    private const string Sentence = "The quick brown fox jumps over the lazy dog. ";
    private const int Samples = 400;
    private const int Warm = 20;

    public static void Run(Report report)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;

        // The caret's blink posts itself to the UI thread's context and does not run with none.
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        try
        {
            using var scene = new Scene();
            Write(report, "text_field_type_1000_chars", scene.Time(scene.Type, scene.Erase));
            Write(report, "text_field_word_move", scene.Time(scene.WordLeft, scene.ToEnd));
            Write(report, "text_field_blink_frame", scene.Time(scene.Blink, null));
            scene.PressForDrag();
            Write(report, "text_field_drag_frame", scene.Time(scene.Drag, null));
            scene.ReleaseDrag();
            report.Number("text_field_chars", scene.Text.Get().Length);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void Write(Report report, string name, Parts samples)
    {
        report.Series(name + "_us", samples.Total);
        report.Number(name + "_input_us_p50", Median(samples.Input));
        report.Number(name + "_layout_us_p50", Median(samples.Layout));
        report.Number(name + "_draw_us_p50", Median(samples.Draw));
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private sealed record Parts(List<double> Total, List<double> Input, List<double> Layout, List<double> Draw);

    private sealed class Scene : IDisposable
    {
        private readonly DirectWriteFontProvider _fonts = new();
        private readonly DirectWriteGlyphRasterizer _rasterizer = new();
        private readonly GlyphAtlas _atlas;
        private readonly TextContext _context;
        private readonly UiTree _tree;
        private readonly HeldFrames _scheduler = new();
        private readonly ManualTime _time = new();
        private readonly WindowInput _input;
        private readonly ScopeHandle _scope;
        private TimeSpan _now = TimeSpan.FromSeconds(1);
        private int _drags;

        public Scene()
        {
            _atlas = new GlyphAtlas(_rasterizer);
            _context = new TextContext(_fonts);
            _tree = new UiTree(_context);
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            var frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, _scheduler, Color.FromRgb(0xFFFFFF), time: _time);
            string text = string.Concat(Enumerable.Repeat(Sentence, (1_000 / Sentence.Length) + 1))[..1_000];
            Text = Signal.Create(text);
            NodeHandle field = default;
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    field = Primitive.TextField(Text, new TextFieldOptions { Multiline = true, Selection = Selection }).Width(400).Height(300).Node;
                    _tree.AppendChild(root, field);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            frames.Resize(new Size(420, 320), 1);
            frames.RunFrame();
            _input = new WindowInput(_tree, frames, new NoPlatform());
            _input.SetActive(true);
            if (!_tree.Focus.Focus(field))
            {
                throw new InvalidOperationException("The text field did not take the focus.");
            }

            Press(Key.End, KeyModifiers.Control);
        }

        public Signal<string> Text { get; }

        public Signal<TextSelection> Selection { get; } = Signal.Create(default(TextSelection));

        // Delivers a character typed at the end.
        public void Type() => _ = _input.Handle(new TextInput("a", Next()));

        // Deletes the character, with its frame, and checks that the typing went in.
        public void Erase()
        {
            if (Text.Get().Length != 1_001)
            {
                throw new InvalidOperationException("The typed character did not go in.");
            }

            Press(Key.Back, KeyModifiers.None);
        }

        // Delivers Control+Left.
        public void WordLeft() => Down(Key.Left, KeyModifiers.Control);

        // Releases Control+Left, checks that it moved, and goes back to the end, with its frame.
        public void ToEnd()
        {
            _ = _input.Handle(new KeyInput(KeyEventKind.Up, Key.Left, PhysicalKey.None, default, KeyModifiers.Control, false, Next()));
            if (Selection.Get().Caret == Text.Get().Length)
            {
                throw new InvalidOperationException("The caret did not move a word.");
            }

            Press(Key.End, KeyModifiers.Control);
        }

        // Ticks the caret's timer.
        public void Blink()
        {
            if (_time.Fire() != 1)
            {
                throw new InvalidOperationException("The caret's timer did not tick.");
            }
        }

        // Presses the left button on the first line, which starts the drag.
        public void PressForDrag()
        {
            _input.Handle(Pointer(PointerInputKind.Down, 20, 10, PointerButton.Left));
            _scheduler.Run();
        }

        // Delivers a move of the drag, alternately to two points on other lines.
        public void Drag()
        {
            bool far = (_drags++ & 1) == 0;
            _input.Handle(Pointer(PointerInputKind.Move, far ? 300 : 150, far ? 150 : 90, PointerButton.None));
        }

        // Releases the button, after checking that the drag selected.
        public void ReleaseDrag()
        {
            if (Selection.Get().IsCollapsed)
            {
                throw new InvalidOperationException("The drag selected nothing.");
            }

            _input.Handle(Pointer(PointerInputKind.Up, 150, 90, PointerButton.Left));
            _scheduler.Run();
        }

        // The microseconds of each run of a delivery, of the layout pass after it, and of the rest of its frame; then an untimed run
        // of what undoes it.
        public Parts Time(Action deliver, Action? undo)
        {
            var samples = new Parts(new List<double>(Samples), new List<double>(Samples), new List<double>(Samples), new List<double>(Samples));
            for (int i = 0; i < Samples + Warm; i++)
            {
                long start = Stopwatch.GetTimestamp();
                deliver();
                long delivered = Stopwatch.GetTimestamp();
                LayoutEngine.RunLayoutPass(_tree);
                long laidOut = Stopwatch.GetTimestamp();
                _scheduler.Run();
                long drawn = Stopwatch.GetTimestamp();
                undo?.Invoke();
                if (i >= Warm)
                {
                    samples.Total.Add(Stopwatch.GetElapsedTime(start, drawn).TotalMicroseconds);
                    samples.Input.Add(Stopwatch.GetElapsedTime(start, delivered).TotalMicroseconds);
                    samples.Layout.Add(Stopwatch.GetElapsedTime(delivered, laidOut).TotalMicroseconds);
                    samples.Draw.Add(Stopwatch.GetElapsedTime(laidOut, drawn).TotalMicroseconds);
                }
            }

            if (Text.Get().Length != 1_000)
            {
                throw new InvalidOperationException($"The text is {Text.Get().Length} characters long after the runs.");
            }

            return samples;
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _context.Dispose();
            _atlas.Dispose();
            _rasterizer.Dispose();
            _fonts.Dispose();
        }

        private void Press(Key key, KeyModifiers modifiers)
        {
            Down(key, modifiers);
            _scheduler.Run();
            _ = _input.Handle(new KeyInput(KeyEventKind.Up, key, PhysicalKey.None, default, modifiers, false, Next()));
        }

        private void Down(Key key, KeyModifiers modifiers) =>
            _ = _input.Handle(new KeyInput(KeyEventKind.Down, key, PhysicalKey.None, default, modifiers, false, Next()));

        private PointerInput Pointer(PointerInputKind kind, int x, int y, PointerButton button) =>
            new(kind, x, y, button, PointerButtons.Left, KeyModifiers.None, kind == PointerInputKind.Down ? 1 : 0, Next(), PointerType.Mouse);

        // A second between inputs, so that no two edits are one group of the undo.
        private TimeSpan Next() => _now += TimeSpan.FromSeconds(1);
    }

    // A clock whose timers tick only when told to.
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<Timer> _timers = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            _timers.Add(timer);
            return timer;
        }

        // Ticks every timer once; returns how many ticked.
        public int Fire()
        {
            int count = _timers.Count;
            for (int i = 0; i < count; i++)
            {
                _timers[i].Tick();
            }

            return count;
        }

        private sealed class Timer(ManualTime time, TimerCallback callback, object? state) : ITimer
        {
            public void Tick() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => _ = time._timers.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class HeldFrames : IFrameScheduler
    {
        private Action? _callback;

        public void RequestFrame(Action callback) => _callback = callback;

        public void CancelFrame() => _callback = null;

        public void Run()
        {
            Action? callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() { }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
