using System.Diagnostics;
using Todo;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of the todo sample's operations (row V3c, design §2.7): the sample's own TodoApp over a model of 1,000 items, half of
// them done, in a window of 420 x 360 with text, with no GPU. Each operation is timed in three parts, as the lists scene does: the
// change (the model's method, whose effects reconcile the rows at once), the layout pass, and the rest of the frame (the display
// list built, its glyphs rasterized by DirectWrite into the atlas). Each comes with the operation that undoes it, untimed, so that
// every sample starts from the same 1,000 items: an item added and removed again, the middle item marked and unmarked, the
// middle item removed and an item added in its stead, the list filtered to the items not done and back, and the order reversed
// and back. The filter back to every item is timed too, since it makes again the 500 rows the filter removed.
internal static class TodoCost
{
    private const int Items = 1_000;
    private const int Samples = 31;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("todo");
        using var scene = new TodoScene();
        report.Number("items", Items);
        TodoModel model = scene.Model;
        Measure(report, "add", scene, model.Add, () => model.Remove(model.Items.Get()[^1].Id));
        Measure(report, "toggle", scene, () => model.Toggle(Middle(model)), () => model.Toggle(Middle(model)));
        Measure(report, "remove", scene, () => model.Remove(Middle(model)), model.Add);
        Measure(report, "filter_active", scene, () => model.Show(TodoFilter.Active), () => model.Show(TodoFilter.All));
        Measure(
            report,
            "filter_all",
            scene,
            () => model.Show(TodoFilter.All),
            () => model.Show(TodoFilter.Active),
            before: () => model.Show(TodoFilter.Active)
        );
        model.Show(TodoFilter.All);
        Measure(report, "reverse", scene, model.Reverse, model.Reverse);
        return 0;
    }

    private static int Middle(TodoModel model) => model.Items.Get()[Items / 2].Id;

    // The median of each part over the samples of an operation; the operation back is not timed.
    private static void Measure(Report report, string name, TodoScene scene, Action change, Action back, Action? before = null)
    {
        before?.Invoke();
        scene.Frame();
        var diff = new double[Samples];
        var layout = new double[Samples];
        var draw = new double[Samples];
        for (int i = -3; i < Samples; i++)
        {
            (double d, double l, double f) = scene.Time(change);
            scene.Time(back);
            if (i >= 0)
            {
                (diff[i], layout[i], draw[i]) = (d, l, f);
            }
        }

        report.Number($"{name}_change_us_p50", Median(diff));
        report.Number($"{name}_layout_us_p50", Median(layout));
        report.Number($"{name}_draw_stage_us_p50", Median(draw));
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    // The tree with text tools, its frames, and the sample mounted over a model of 1,000 items, half of them done.
    private sealed class TodoScene : IDisposable
    {
        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly UiTree _tree;
        private readonly WindowFrames _frames;
        private readonly NodeHandle _root;
        private readonly ScopeHandle _scope;

        public TodoScene()
        {
            _tree = new UiTree(new TextContext(_fonts));
            _root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, _root, new NoTarget(), _atlas, new NoScheduler(), default);
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    for (int i = 0; i < Items; i++)
                    {
                        Model.Add();
                    }

                    for (int i = 0; i < Items; i += 2)
                    {
                        Model.Toggle(Model.Items.Get()[i].Id);
                    }

                    _tree.AppendChild(_root, TodoApp.Todo(Model).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            _frames.Resize(new Size(420, 360), 1);
            Frame();
        }

        public TodoModel Model { get; } = new();

        public void Frame()
        {
            Invalidation.Invalidate(_tree, _root, ChangeKind.Color);
            _frames.RunFrame();
        }

        // The microseconds of a change, of the layout pass after it, and of the rest of the frame.
        public (double Change, double Layout, double Draw) Time(Action change)
        {
            long start = Stopwatch.GetTimestamp();
            change();
            long changed = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(_tree);
            long laidOut = Stopwatch.GetTimestamp();

            // The pass ran apart, so the frame would see no work and skip the display list: a mark on the root makes it build the
            // list, which a frame that draws builds whole (design §5.0.1).
            Invalidation.Invalidate(_tree, _root, ChangeKind.Color);
            _frames.RunFrame();
            long drawn = Stopwatch.GetTimestamp();
            return (
                Stopwatch.GetElapsedTime(start, changed).TotalMicroseconds,
                Stopwatch.GetElapsedTime(changed, laidOut).TotalMicroseconds,
                Stopwatch.GetElapsedTime(laidOut, drawn).TotalMicroseconds
            );
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
            _fonts.Dispose();
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
}
