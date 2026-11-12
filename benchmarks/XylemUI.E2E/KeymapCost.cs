using System.Diagnostics;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of the keymap on a key's delivery (row KM1b, design §7, "Shortcuts and actions"): the chain of 50 nested boxes of the
// input scenario (us_per_key_depth_50), each with a key-down handler in both phases, with the focus on its leaf. Three keymaps: the
// default rows alone (row KM2a: those of XylemUI.Core and XylemUI.Primitives, which this executable's module initializer
// registered); 200 more rows for other keys, half of them with a condition, so that the key is looked up, matches none, and goes on
// to the 100 key handlers; and the same rows with one more whose key is pressed, whose condition is "Workspace > Editor" and whose
// action the root handles, so that the key ends at the action's handler and reaches no key handler. Before them, the work the
// default rows add to an app's start: making the first keymap, which reads the text of every default row, and then one more.
internal static class KeymapCost
{
    private const int Depth = 50;
    private const int Keys = 10_000;
    private const int Samples = 21;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("keymap");

        // The first keymap of the process, before any tree makes the app's: what an app's start pays for the default rows.
        long first = Stopwatch.GetTimestamp();
        var defaults = new Keymap();
        report.Number("us_first_default_keymap", Stopwatch.GetElapsedTime(first).TotalMicroseconds);
        report.Number("default_rows", defaults.Bindings.Count);
        var again = new double[Samples];
        for (int sample = 0; sample < Samples; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 100; i++)
            {
                _ = new Keymap();
            }

            again[sample] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / 100;
        }

        Array.Sort(again);
        report.Number("us_per_default_keymap_p50", again[Samples / 2]);
        var plain = new KeyInput(KeyEventKind.Down, Key.A, PhysicalKey.KeyA, default, KeyModifiers.None, false, TimeSpan.Zero);
        var bound = new KeyInput(KeyEventKind.Down, Key.S, PhysicalKey.KeyS, default, KeyModifiers.Control, false, TimeSpan.Zero);

        using (var none = new Chain(rows: 0, bind: false))
        {
            (double us, double bytes) = none.Time(plain);
            report.Number("us_per_key_depth_50_default_rows_p50", us);
            report.Number("bytes_per_key_default_rows", bytes);
            Check(none, keyHandlers: 2 * Depth, actions: 0);
        }

        using (var miss = new Chain(rows: 200, bind: false))
        {
            (double us, double bytes) = miss.Time(plain);
            report.Number("us_per_key_depth_50_200_rows_miss_p50", us);
            report.Number("bytes_per_key_200_rows_miss", bytes);
            Check(miss, keyHandlers: 2 * Depth, actions: 0);
        }

        using (var hit = new Chain(rows: 200, bind: true))
        {
            (double us, double bytes) = hit.Time(bound);
            report.Number("us_per_bound_key_depth_50_200_rows_p50", us);
            report.Number("bytes_per_bound_key_200_rows", bytes);
            Check(hit, keyHandlers: 0, actions: 1);
        }

        return 0;
    }

    private static void Check(Chain chain, int keyHandlers, int actions)
    {
        if (chain.KeyHandlersPerKey != keyHandlers || chain.ActionsPerKey != actions)
        {
            throw new InvalidOperationException(
                $"A key reached {chain.KeyHandlersPerKey} key handlers and {chain.ActionsPerKey} action handlers, not {keyHandlers} and {actions}."
            );
        }
    }

    private sealed class Chain : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly WindowInput _input;
        private long _keyHandlers;
        private long _actions;
        private long _keys;

        public Chain(int rows, bool bind)
        {
            var tree = new UiTree { Keymap = new Keymap() };
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            tree.SetKeyContext(root, "Workspace");
            tree.AddActionHandler(root, EventPhase.Bubble, (in ActionEvent<Probe> _) => _actions++);
            NodeHandle node = root;
            KeyHandler count = (in KeyEvent _) => _keyHandlers++;
            for (int i = 0; i < Depth; i++)
            {
                NodeHandle box = tree.CreateNode(new LayoutStyle { Width = Length.Px(300), Height = Length.Px(200) });
                tree.AppendChild(node, box);
                tree.AddKeyHandler(box, KeyEventKind.Down, EventPhase.Capture, count);
                tree.AddKeyHandler(box, KeyEventKind.Down, EventPhase.Bubble, count);
                if (i % 10 == 5)
                {
                    tree.SetKeyContext(box, "Pane");
                }

                node = box;
            }

            tree.SetKeyContext(node, "Editor mode = full");
            tree.SetFocusable(node, true);
            for (int i = 0; i < rows; i++)
            {
                string keys = $"ctrl-alt-{(char)('a' + (i % 26))}{(i >= 26 ? $" f{(i % 24) + 1}" : string.Empty)}";
                _ = tree.Keymap.Bind(keys, new Probe(), i % 2 == 0 ? "Pane > Editor && mode == full" : null);
            }

            if (bind)
            {
                _ = tree.Keymap.Bind("ctrl-s", new Probe(), "Workspace > Editor");
            }

            var frames = new WindowFrames(tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            frames.Resize(new Size(400, 300), 1);
            _input = new WindowInput(tree, frames, new NoPlatform());
            _input.SetActive(true);
            if (!tree.Focus.Focus(node))
            {
                throw new InvalidOperationException("The leaf of the chain did not take the focus.");
            }
        }

        public double KeyHandlersPerKey => (double)_keyHandlers / _keys;

        public double ActionsPerKey => (double)_actions / _keys;

        // The median microseconds and the bytes of one key's down, over 21 samples of 10,000 keys after as many unmeasured.
        public (double Microseconds, double Bytes) Time(in KeyInput key)
        {
            Press(key);
            _keyHandlers = 0;
            _actions = 0;
            _keys = 0;
            var microseconds = new double[Samples];

            // The clock's first call in the process allocates (docs/benchmarks/km1a-actions.md), so it is made before the samples.
            _ = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp()).TotalMicroseconds;
            long bytes = 0;
            for (int sample = 0; sample < Samples; sample++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                Press(key);
                microseconds[sample] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Keys;
                bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
            }

            Array.Sort(microseconds);
            return (microseconds[Samples / 2], (double)bytes / ((long)Keys * Samples));
        }

        public void Dispose() => _atlas.Dispose();

        private void Press(in KeyInput key)
        {
            for (int i = 0; i < Keys; i++)
            {
                _ = _input.Handle(key);
                _keys++;
            }
        }
    }

    private sealed class Probe : IAction;

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas, TextureCache images) { }

        public void Present() { }
    }

    // The hover's marks ask for frames, which nothing runs here: the moves time the input alone.
    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }

        // A history of as many points as asked, as Win32 gives a pen's coalesced moves.
        public int ReadHistory(int pointerId, Span<PointerHistoryPoint> destination)
        {
            destination.Clear();
            return destination.Length;
        }
    }

    private sealed class NoGlyphs : IGlyphRasterizer
    {
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) => throw new InvalidOperationException("No glyph is drawn.");
    }
}
