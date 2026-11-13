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
//
// Row KM3 (design §7, "Shortcuts and actions") adds the time of a key against the number of rows, and where the time of the first
// keymap goes.
//
// The rows (us_per_key_depth_D_part_rows_N_*): a chain 1 and 50 deep, with the default rows alone ("default") and with the keymap
// grown to 200, 400, and 800 rows by rows shaped like the parts': each has the keys of a default row, in the default rows' order,
// and the condition of that row with another part's name in place of its own (PartA, PartB, and so on), so the keys many parts
// share (Enter, Space, the arrows) are in as many more rows, as they would be with more parts. None of the added rows applies at
// the focused node. Three keys are timed in each keymap:
//   miss    A, which no row has: the first key of every row is compared, and the key goes on to the key handlers.
//   shared  Space, which the rows of several parts have (part_rows_N_space_rows): the condition of each such row is tried on the
//           contexts of the route, none holds, and the key goes on to the key handlers.
//   hit     Enter, which the rows of several parts have, and one more row, added after the others, whose condition holds and
//           whose action the root handles (part_rows_N_enter_rows counts it): the key ends at the action's handler. miss and
//           shared are timed before that row is added, so the keymap has N rows for them and N + 1 for hit.
//
// The first keymap: page_faults_first_default_keymap is the page faults of the process (PROCESS_MEMORY_COUNTERS.PageFaultCount,
// soft ones included) across the first keymap, read right before the clock starts and right after it stops.
// us_second_default_keymap and its page faults are the next keymap alone, one call, where us_per_default_keymap_p50 is a call
// among 100 in a row. The second is what making the rows costs; the first less the second is what the first touch costs. With
// --no-faults the page faults are not read, to check that reading them does not move us_first_default_keymap.
//
// keymap --first-pieces is a run of its own (scenario "keymap-first-pieces"), in a new process: before any keymap, it runs the
// pieces a row is made of, each for the first time and then once more, and then makes the first keymap and the second. Each
// figure is one call, as us_<name> and page_faults_<name>:
//   first_keystroke_parse, second_keystroke_parse   Keystroke.Parse of "ctrl-shift-left", then of "ctrl-shift-right".
//   first_predicate_parse, second_predicate_parse   KeyContextPredicate.Parse of "TextField && !multiline", then of
//                                                   "TextField && multiline".
//   first_keymap_after_pieces, second_keymap_after_pieces   new Keymap(), after the pieces ran.
// us_first_default_keymap of a plain run less us_first_keymap_after_pieces is the part of the first touch that the two parsers
// account for. us_first_keymap_after_pieces less us_second_keymap_after_pieces is the rest: the action types, the arrays of the
// rows, and the methods that add them.
internal static class KeymapCost
{
    private const int Depth = 50;
    private const int Keys = 10_000;
    private const int Samples = 21;

    // Keeps what a timed call made, so that the call is not optimized away.
    private static object? _sink;

    public static int Run(bool countFaults = true)
    {
        UiThread.Initialize();
        using var report = new Report("keymap");

        // The first read of the page faults faults pages of its own, so one is made before the first that counts.
        if (countFaults)
        {
            _ = PageFaults();
        }

        long faults = countFaults ? PageFaults() : 0;

        // The first keymap of the process, before any tree makes the app's: what an app's start pays for the default rows.
        long first = Stopwatch.GetTimestamp();
        var defaults = new Keymap();
        report.Number("us_first_default_keymap", Stopwatch.GetElapsedTime(first).TotalMicroseconds);
        if (countFaults)
        {
            report.Number("page_faults_first_default_keymap", PageFaults() - faults);
            faults = PageFaults();
        }

        long second = Stopwatch.GetTimestamp();
        _sink = new Keymap();
        report.Number("us_second_default_keymap", Stopwatch.GetElapsedTime(second).TotalMicroseconds);
        if (countFaults)
        {
            report.Number("page_faults_second_default_keymap", PageFaults() - faults);
        }

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

        var space = new KeyInput(KeyEventKind.Down, Key.Space, PhysicalKey.Space, default, KeyModifiers.None, false, TimeSpan.Zero);
        var enter = new KeyInput(KeyEventKind.Down, Key.Enter, PhysicalKey.Enter, default, KeyModifiers.None, false, TimeSpan.Zero);
        foreach (int depth in new[] { 1, Depth })
        {
            foreach (int total in new[] { 0, 200, 400, 800 })
            {
                string size = total == 0 ? "default" : total.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string name = $"depth_{depth}_part_rows_{size}";
                using var chain = new Chain(rows: 0, bind: false, depth, total);
                if (depth == 1)
                {
                    report.Number($"part_rows_{size}_rows", chain.Rows);
                    report.Number($"part_rows_{size}_space_rows", chain.RowsWith(Key.Space));
                    report.Number($"part_rows_{size}_enter_rows", chain.RowsWith(Key.Enter) + 1);
                }

                (double us, double bytes) = chain.Time(plain);
                report.Number($"us_per_key_{name}_miss_p50", us);
                report.Number($"bytes_per_key_{name}_miss", bytes);
                Check(chain, keyHandlers: 2 * depth, actions: 0);

                (us, bytes) = chain.Time(space);
                report.Number($"us_per_key_{name}_shared_p50", us);
                report.Number($"bytes_per_key_{name}_shared", bytes);
                Check(chain, keyHandlers: 2 * depth, actions: 0);

                chain.Bind("enter");
                (us, bytes) = chain.Time(enter);
                report.Number($"us_per_key_{name}_hit_p50", us);
                report.Number($"bytes_per_key_{name}_hit", bytes);
                Check(chain, keyHandlers: 0, actions: 1);
            }
        }

        return 0;
    }

    // The pieces a row is made of, each the first time in the process and once more, and then the first keymap and the second.
    public static int FirstPieces()
    {
        UiThread.Initialize();
        using var report = new Report("keymap-first-pieces");

        // The first read of the page faults faults pages of its own, and the clock's first call allocates
        // (docs/benchmarks/km1a-actions.md).
        _ = PageFaults();
        _ = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp()).TotalMicroseconds;
        Once(report, "first_keystroke_parse", static () => _ = Keystroke.Parse("ctrl-shift-left"));
        Once(report, "second_keystroke_parse", static () => _ = Keystroke.Parse("ctrl-shift-right"));
        Once(report, "first_predicate_parse", static () => _sink = KeyContextPredicate.Parse("TextField && !multiline"));
        Once(report, "second_predicate_parse", static () => _sink = KeyContextPredicate.Parse("TextField && multiline"));
        Keymap? last = null;
        Once(report, "first_keymap_after_pieces", () => last = new Keymap());
        Once(report, "second_keymap_after_pieces", () => last = new Keymap());
        report.Number("default_rows", last!.Bindings.Count);
        return 0;
    }

    // One call of a piece: its microseconds as us_<name>, and the page faults of the process across it as page_faults_<name>.
    private static void Once(Report report, string name, Action piece)
    {
        long faults = PageFaults();
        long start = Stopwatch.GetTimestamp();
        piece();
        double us = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        long after = PageFaults();
        report.Number("us_" + name, us);
        report.Number("page_faults_" + name, after - faults);
    }

    // The page faults of the process since it started, soft ones included.
    private static unsafe long PageFaults()
    {
        TerraFX.Interop.Windows.PROCESS_MEMORY_COUNTERS counters;
        TerraFX.Interop.Windows.HANDLE process = TerraFX.Interop.Windows.Windows.GetCurrentProcess();
        uint size = (uint)sizeof(TerraFX.Interop.Windows.PROCESS_MEMORY_COUNTERS);
        if (TerraFX.Interop.Windows.Windows.K32GetProcessMemoryInfo(process, &counters, size).Value == 0)
        {
            throw new InvalidOperationException("GetProcessMemoryInfo did not return the page faults of the process.");
        }

        return counters.PageFaultCount;
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
        private readonly Keymap _keymap = new();
        private long _keyHandlers;
        private long _actions;
        private long _keys;

        // rows: that many rows for other keys (row KM1b). total: the keymap grown to that many rows by rows shaped like the
        // parts' (row KM3); 0 leaves the default rows alone.
        public Chain(int rows, bool bind, int depth = Depth, int total = 0)
        {
            var tree = new UiTree { Keymap = _keymap };
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            tree.SetKeyContext(root, "Workspace");
            tree.AddActionHandler(root, EventPhase.Bubble, (in ActionEvent<Probe> _) => _actions++);
            NodeHandle node = root;
            KeyHandler count = (in KeyEvent _) => _keyHandlers++;
            for (int i = 0; i < depth; i++)
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
                Bind("ctrl-s");
            }

            // The keys of the default rows in their order, again and again, each round under the name of another part in place of
            // the row's own (the first name of its condition); what follows the name, as "&& multiline", stays.
            KeyBinding[] defaults = _keymap.Rows;
            for (int i = 0; _keymap.Rows.Length < total; i++)
            {
                KeyBinding row = defaults[i % defaults.Length];
                string condition = row.Context ?? string.Empty;
                int end = condition.IndexOf(' ', StringComparison.Ordinal);
                string part = $"Part{(char)('A' + (i / defaults.Length))}";
                _ = _keymap.Bind(row.ToString(), new Probe(), end < 0 ? part : part + condition[end..]);
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

        public int Rows => _keymap.Rows.Length;

        // The number of rows whose first key is the key, with no modifier.
        public int RowsWith(Key key) => _keymap.Rows.Count(row => row.Steps[0].Matches(key, default, KeyModifiers.None));

        // Adds a row for the keys, whose condition holds at the leaf and whose action the root handles.
        public void Bind(string keys) => _ = _keymap.Bind(keys, new Probe(), "Workspace > Editor");

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
