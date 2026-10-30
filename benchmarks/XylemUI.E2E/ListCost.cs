using System.Diagnostics;
using XylemUI.Text;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of Show and For (row V3a, design §2.7, "The shape of V3"), with no GPU: a column filled by For with 10,000 rows, each a
// box 10 high whose background follows its item, in a window of 400 x 300. Each change is timed in three parts: the diff (the
// signal's Set, whose effect reconciles at once), the layout pass, and the rest of the frame (the display list built). The changes
// come in pairs that bring the list back, so every sample starts from the same rows: a row added at the end and removed again, the
// middle row removed and put back, the order reversed and restored, and the middle row's value changed and changed back. A Show in a
// column of three boxes is toggled.
internal static class ListCost
{
    private const int Rows = 10_000;
    private const int Samples = 31;

    // testIds: each row has a test ID (design §12.5, "The keys"), so that removing a row also removes its ID (row P02a).
    public static int Run(bool testIds = false)
    {
        UiThread.Initialize();
        using var report = new Report(testIds ? "lists-test-ids" : "lists");
        using var list = new ListScene(testIds);
        report.Number("rows", Rows);
        Measure(report, "append", list, () => list.Set(list.Appended), () => list.Set(list.Forward));
        Measure(report, "remove_middle", list, () => list.Set(list.WithoutMiddle), () => list.Set(list.Forward));
        Measure(report, "reverse", list, () => list.Set(list.Reversed), () => list.Set(list.Forward));
        Measure(report, "value", list, () => list.Set(list.Changed), () => list.Set(list.Forward));
        report.Number("show_toggle_us_p50", list.TimeShow(Samples));
        return 0;
    }

    // The median of each part over the samples of a change; the change back is not timed.
    private static void Measure(Report report, string name, ListScene list, Action change, Action back)
    {
        var diff = new double[Samples];
        var layout = new double[Samples];
        var draw = new double[Samples];
        for (int i = -3; i < Samples; i++)
        {
            (double d, double l, double f) = list.Time(change);
            list.Time(back);
            if (i >= 0)
            {
                (diff[i], layout[i], draw[i]) = (d, l, f);
            }
        }

        report.Number($"{name}_diff_us_p50", Median(diff));
        report.Number($"{name}_layout_us_p50", Median(layout));
        report.Number($"{name}_draw_stage_us_p50", Median(draw));
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private sealed record Item(int Id, Color Color);

    // The window, its tree, and the lists set in turn, made beforehand so that making them is not timed.
    private sealed class ListScene : IDisposable
    {
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly UiTree _tree = new();
        private readonly WindowFrames _frames;
        private readonly Signal<IReadOnlyList<Item>> _items;
        private readonly Signal<bool> _shown = Signal.Create(true);
        private readonly ScopeHandle _scope;
        private readonly NodeHandle _root;

        public ListScene(bool testIds)
        {
            Color gray = Color.FromRgb(0x808080);
            Color red = Color.FromRgb(0xCC3333);
            List<Item> forward = Enumerable.Range(0, Rows).Select(n => new Item(n, (n & 1) == 0 ? gray : red)).ToList();
            Forward = forward;
            Appended = [.. forward, new Item(Rows, gray)];
            WithoutMiddle = forward.Where(item => item.Id != Rows / 2).ToList();
            Reversed = Enumerable.Reverse(forward).ToList();
            Changed = forward.Select(item => item.Id == Rows / 2 ? item with { Color = Color.FromRgb(0x3333CC) } : item).ToList();
            _items = Signal.Create(Forward);

            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            _root = root;
            _frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    Element toggled = Column(Box().Height(10), Show(_shown, () => Box().Height(10)), Box().Height(10));
                    Element rows = Column()
                        .For(
                            _items,
                            item => item.Id,
                            item =>
                            {
                                Element row = Box().Height(10).Background(Computed.Create(() => new Paint(item.Get().Color)));
                                return testIds ? row.TestId("row") : row;
                            }
                        );
                    _tree.AppendChild(root, Column(toggled, rows).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            _frames.Resize(new Size(400, 300), 1);
        }

        public IReadOnlyList<Item> Forward { get; }

        public IReadOnlyList<Item> Appended { get; }

        public IReadOnlyList<Item> WithoutMiddle { get; }

        public IReadOnlyList<Item> Reversed { get; }

        public IReadOnlyList<Item> Changed { get; }

        public void Set(IReadOnlyList<Item> items) => _items.Set(items);

        // The microseconds of a change's diff, of the layout pass after it, and of the rest of the frame.
        public (double Diff, double Layout, double Draw) Time(Action change)
        {
            long start = Stopwatch.GetTimestamp();
            change();
            long diffed = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(_tree);
            long laidOut = Stopwatch.GetTimestamp();

            // The pass ran apart, so the frame would see no work and skip the display list. The list is built again whole in any
            // frame that draws (design §5.0.1), so a mark on the root makes it do the draw stage the change's own frame does.
            Invalidation.Invalidate(_tree, _root, ChangeKind.Color);
            _frames.RunFrame();
            long drawn = Stopwatch.GetTimestamp();
            return (
                Stopwatch.GetElapsedTime(start, diffed).TotalMicroseconds,
                Stopwatch.GetElapsedTime(diffed, laidOut).TotalMicroseconds,
                Stopwatch.GetElapsedTime(laidOut, drawn).TotalMicroseconds
            );
        }

        // The median microseconds of one change of the Show's condition, with no frame.
        public double TimeShow(int samples)
        {
            var times = new double[samples];
            for (int i = -3; i < samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                _shown.Set(!_shown.Get());
                double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                _frames.RunFrame();
                if (i >= 0)
                {
                    times[i] = microseconds;
                }
            }

            return Median(times);
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
        }
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
