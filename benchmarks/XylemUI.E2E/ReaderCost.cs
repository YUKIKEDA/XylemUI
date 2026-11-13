using System.Diagnostics;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of standing readers while nodes are attached and detached (row KM3, design §7, "The notices to read again"): a window of
// 420 x 360 with text and no GPU, whose column holds a row of readers, a text field of one line with the focus, and a box of
// 400 x 260 that scrolls over a virtualized list of 10,000 rows 20 high. A reader is what a toolbar button of one of the field's
// actions would be: a box, a computed value that reads CanDispatch(action), another that reads KeyBindingFor(action), both from
// the focused node, and an effect that reads the two. The 20 readers name 20 actions of the field, each with a row in the default
// keymap. A frame scrolls the list by one row's height and runs: the list makes a row and takes one out, and each node attached
// or detached tells every reader to read again.
//
// The scene runs twice, with 0 readers and with 20, 1,000 frames each after 100 unmeasured. With N the number of readers:
//   readers_N_frame_us_*            the time of a frame, the change of the position and RunFrame, as a series.
//   readers_N_bytes_per_frame       the bytes the frames allocated (GC.GetAllocatedBytesForCurrentThread around each frame),
//                                   divided by the frames. The rows a frame makes allocate with no reader too; the readers' share
//                                   is the difference of the two runs.
//   readers_N_rows_made_per_frame   the calls of the list's row function, divided by the frames.
//   readers_N_frames_with_rows      the frames in which the row function was called.
//   readers_N_can_reads_per_frame   the runs of the computed values that read CanDispatch, all readers together, divided by the
//                                   frames; readers_N_key_reads_per_frame the same for KeyBindingFor.
//   readers_N_effect_runs_per_frame the runs of the readers' effects. The answers do not change while the list scrolls, so an
//                                   effect runs only if a computed value that was read again is taken as changed.
//   readers_N_can                   the readers whose action can be dispatched when the frames start; every reader has keys, or
//                                   the scene throws.
// The scene throws when a frame did not move the list, and when no frame made a row.
//
// Two more pairs of runs have rows that take the focus (PR #831, the review):
//   rows_N_*   the focus is on a row of the list, and each frame moves it to the row two below the top after the scroll: the
//              route of the readers passes a row that the list attaches and detaches. The readers find no keys here (the
//              rows have no context), which the scene does not check.
//   order_N_*  the focus stays on the field, and the readers (0 and 1) read whether focus::Next can be dispatched, which
//              depends on the order of the focus and so on every row attached or detached.
internal static class ReaderCost
{
    private const int Rows = 10_000;
    private const int RowHeight = 20;
    private const int WarmUp = 100;
    private const int Frames = 1_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("readers");
        report.Number("rows", Rows);
        report.Number("frames", Frames);
        foreach ((string name, int readers, Kind kind) in Runs)
        {
            using var scene = new ReaderScene(readers, kind);
            scene.Measure(report, $"{name}_{readers}_");
        }

        return 0;
    }

    private static readonly (string Name, int Readers, Kind Kind)[] Runs =
    [
        ("readers", 0, Kind.Field),
        ("readers", 20, Kind.Field),
        ("rows", 0, Kind.Rows),
        ("rows", 20, Kind.Rows),
        ("order", 0, Kind.Order),
        ("order", 1, Kind.Order),
    ];

    // Where the focus is and what the readers read: the field and its actions; a row of the list, moved each frame, and the
    // same actions; the field, and whether the focus can move to the next node.
    private enum Kind
    {
        Field,
        Rows,
        Order,
    }

    private sealed class ReaderScene : IDisposable
    {
        // 20 actions of a text field of one line, each with a row of the field's context in the default keymap.
        private static readonly IAction[] Actions =
        [
            new TextActions.MoveLeft(),
            new TextActions.MoveRight(),
            new TextActions.SelectLeft(),
            new TextActions.SelectRight(),
            new TextActions.MoveWordLeft(),
            new TextActions.MoveWordRight(),
            new TextActions.SelectWordLeft(),
            new TextActions.SelectWordRight(),
            new TextActions.MoveToLineStart(),
            new TextActions.MoveToLineEnd(),
            new TextActions.SelectToLineStart(),
            new TextActions.SelectToLineEnd(),
            new TextActions.MoveToStart(),
            new TextActions.MoveToEnd(),
            new TextActions.SelectToStart(),
            new TextActions.SelectToEnd(),
            new TextActions.DeleteBackward(),
            new TextActions.DeleteForward(),
            new TextActions.DeleteWordBackward(),
            new TextActions.DeleteWordForward(),
        ];

        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly VirtualListState _list = new();
        private readonly List<Computed<bool>> _can = [];
        private readonly List<Computed<KeyBinding?>> _keys = [];
        private readonly Element[] _rowElements = new Element[Rows];
        private readonly Kind _kind;
        private readonly UiTree _tree;
        private readonly WindowFrames _frames;
        private readonly ScopeHandle _scope;
        private long _canReads;
        private long _keyReads;
        private long _effectRuns;
        private long _rowsMade;
        private float _offset;

        // Counts the answers the readers' effects got that were true or had keys, so that the reads are used.
        private long _answers;

        public ReaderScene(int readers, Kind kind)
        {
            _kind = kind;
            _tree = new UiTree(new TextContext(_fonts));
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            var input = new WindowInput(_tree, _frames, new NoPlatform());
            IReadOnlySignal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, Rows)]);
            Element field = default;
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    var buttons = new Element[readers];
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        buttons[i] = Reader(kind == Kind.Order ? new FocusActions.Next() : Actions[i % Actions.Length]);
                    }

                    field = TextField(Signal.Create("Hello world"));
                    Element list = Box()
                        .Width(400)
                        .Height(260)
                        .Overflow(Overflow.Visible, Overflow.Scroll)
                        .VirtualFor(items, static i => i, ListRow, _list);
                    _tree.AppendChild(root, Column(Row(buttons), field, list).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            // The window is active and the field has the focus, so that the readers read the field's handlers and rows.
            input.SetActive(true);
            _frames.Resize(new Size(420, 360), 1);
            _frames.RunFrame();
            if (!field.Focus() || !_tree.Actions.IsAvailable(_tree.Focus.Focused, Actions[0]))
            {
                throw new InvalidOperationException("The text field did not take the focus.");
            }

            _frames.RunFrame();
            foreach (Computed<KeyBinding?> keys in _keys)
            {
                if (kind == Kind.Field && keys.Get() is null)
                {
                    throw new InvalidOperationException("A reader found no keys for its action at the text field.");
                }
            }
        }

        // The figures of the scene's frames, each under the prefix.
        public void Measure(Report report, string prefix)
        {
            for (int i = 0; i < WarmUp; i++)
            {
                Frame();
            }

            report.Number(prefix + "can", _can.Count(static can => can.Get()));

            // The clock's first call in the process allocates (docs/benchmarks/km1a-actions.md), so it is made before the frames.
            _ = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp()).TotalMicroseconds;
            var times = new List<double>(Frames);
            long bytes = 0;
            int framesWithRows = 0;
            int unmoved = 0;
            (_canReads, _keyReads, _effectRuns, _rowsMade) = (0, 0, 0, 0);
            for (int i = 0; i < Frames; i++)
            {
                long rows = _rowsMade;
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                Frame();
                long end = Stopwatch.GetTimestamp();
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                times.Add(Stopwatch.GetElapsedTime(start, end).TotalMicroseconds);
                framesWithRows += _rowsMade != rows ? 1 : 0;
                unmoved += _list.Scroll.OffsetValue.Y != _offset ? 1 : 0;
            }

            // A frame that did not move the list, or frames that made no row, attached and detached nothing: they would time
            // another scene than the one named.
            if (unmoved > 0 || _rowsMade == 0)
            {
                throw new InvalidOperationException($"{unmoved} frames did not move the list, and the frames made {_rowsMade} rows.");
            }

            report.Series(prefix + "frame_us", times);
            report.Number(prefix + "bytes_per_frame", (double)bytes / Frames);
            report.Number(prefix + "rows_made_per_frame", (double)_rowsMade / Frames);
            report.Number(prefix + "frames_with_rows", framesWithRows);
            report.Number(prefix + "can_reads_per_frame", (double)_canReads / Frames);
            report.Number(prefix + "key_reads_per_frame", (double)_keyReads / Frames);
            report.Number(prefix + "effect_runs_per_frame", (double)_effectRuns / Frames);
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
            _fonts.Dispose();
        }

        // The list one row further down, and the frame of it.
        private void Frame()
        {
            _offset += RowHeight;
            _ = _list.Scroll.SetOffset(new System.Numerics.Vector2(0, _offset));
            _frames.RunFrame();
            if (_kind == Kind.Rows && !_rowElements[(int)(_offset / RowHeight) + 2].Focus())
            {
                throw new InvalidOperationException("A row of the list did not take the focus.");
            }
        }

        // A box of 16 x 16 that reads, as a toolbar button would, whether its action can be dispatched from the focused node and
        // the keys that ask for it there.
        private Element Reader(IAction action)
        {
            Element button = Box().Width(16).Height(16);
            Computed<bool> can = Computed.Create(() =>
            {
                _canReads++;
                return button.CanDispatch(action);
            });
            Computed<KeyBinding?> keys = Computed.Create(() =>
            {
                _keyReads++;
                return button.KeyBindingFor(action);
            });
            _ = Effect.Create(() =>
            {
                _effectRuns++;
                _answers += can.Get() ? 1 : 0;
                _answers += keys.Get() is null ? 0 : 1;
            });
            _can.Add(can);
            _keys.Add(keys);
            return button;
        }

        private Element ListRow(IReadOnlySignal<int> item)
        {
            _rowsMade++;
            Element row = Box().Height(RowHeight).Background(new Paint((item.Get() & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66)));
            if (_kind != Kind.Field)
            {
                row = row.Focusable();
                _rowElements[item.Get()] = row;
            }

            return row;
        }
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas, TextureCache images) { }

        public void Present() { }
    }

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

        public int ReadHistory(int pointerId, Span<PointerHistoryPoint> destination) => 0;
    }
}
