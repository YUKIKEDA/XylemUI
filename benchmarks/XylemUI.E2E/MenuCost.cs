using System.Diagnostics;
using Todo;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of a menu (row P02d, design §7.3, "Checks"): a menu of 10 items of text, each with a hovered and a focused look, opened
// from a box over the todo sample with 200 items, in a window of 420 x 360 with text and no GPU. The frame that opens it is timed in
// three parts, as the todo scene does: the change (the signal set, whose effect makes the popup's part), the layout pass, and the
// rest of the frame (the window's list and the popup's built, their glyphs rasterized by DirectWrite); the close after it is not
// timed. With the menu open, a frame of Down (the focus to the next item, round at the end) and a frame of the pointer moved over the
// next item (its hover and its focus) are timed whole, input and frame, with the bytes they allocate and the builds of the window's
// list they make.
//
// Row KM2c (design §7, "Menu items that name actions") adds two more menus, each of 10 items with a label and the keys of an
// action of the text field: one whose items are plain and whose keys are written in, and one whose items name the actions, so that
// each reads its keys from the keymap and whether its action can run. The todo's field has the focus before they open, so it is
// their context node. The difference of the two is what naming an action costs: the reads when the menu opens (10 items against
// the default rows), and nothing on a frame that moves the focus between the items. keys_read_us and can_read_us are one read of
// each kind at the field, outside a frame.
internal static class MenuCost
{
    private const int Samples = 31;
    private const int Frames = 1_000;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("menu");
        using (var scene = new MenuScene(MenuKind.Plain))
        {
            report.Number("items", 10);
            Measure(report, scene, string.Empty, hover: true);
        }

        using (var scene = new MenuScene(MenuKind.WrittenKeys))
        {
            Measure(report, scene, "written_", hover: false);
        }

        using (var scene = new MenuScene(MenuKind.NamedActions))
        {
            Measure(report, scene, "named_", hover: false);
            (double keysUs, double keysBytes) = scene.TimeRead(static (element, action) => _ = element.KeyBindingFor(action));
            report.Number("keys_read_us_p50", keysUs);
            report.Number("keys_read_bytes", keysBytes);
            (double canUs, double canBytes) = scene.TimeRead(static (element, action) => _ = element.CanDispatch(action));
            report.Number("can_read_us_p50", canUs);
            report.Number("can_read_bytes", canBytes);
            report.Number("default_rows", scene.Rows);
        }

        return 0;
    }

    // The figures of one menu, each under the prefix.
    private static void Measure(Report report, MenuScene scene, string prefix, bool hover)
    {
        var change = new double[Samples];
        var layout = new double[Samples];
        var draw = new double[Samples];
        for (int i = -3; i < Samples; i++)
        {
            (double c, double l, double d) = scene.TimeOpen();
            scene.Close();
            if (i >= 0)
            {
                (change[i], layout[i], draw[i]) = (c, l, d);
            }
        }

        report.Number(prefix + "open_change_us_p50", Median(change));
        report.Number(prefix + "open_layout_us_p50", Median(layout));
        report.Number(prefix + "open_draw_stage_us_p50", Median(draw));

        scene.Open();
        (double keyUs, long keyBytes, int keyBuilds) = scene.TimeFrames(scene.Down);
        report.Number(prefix + "key_frame_us_p50", keyUs);
        report.Number(prefix + "key_bytes_per_frame", (double)keyBytes / Frames);
        report.Number(prefix + "key_window_builds", keyBuilds);
        if (hover)
        {
            (double hoverUs, long hoverBytes, int hoverBuilds) = scene.TimeFrames(scene.Hover);
            report.Number(prefix + "hover_frame_us_p50", hoverUs);
            report.Number(prefix + "hover_bytes_per_frame", (double)hoverBytes / Frames);
            report.Number(prefix + "hover_window_builds", hoverBuilds);
        }

        scene.Close();
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    // What the items of the scene's menu are.
    private enum MenuKind
    {
        // Items of text that run a function.
        Plain,

        // Items of a label and keys written in, that run a function.
        WrittenKeys,

        // Items of a label and keys read from the keymap, that name an action.
        NamedActions,
    }

    // The todo sample under a 100 x 30 box at the top left, with the menu below the box.
    private sealed class MenuScene : IDisposable
    {
        private static readonly (string Label, IAction Action)[] Actions =
        [
            ("Left", new TextActions.MoveLeft()),
            ("Right", new TextActions.MoveRight()),
            ("Select left", new TextActions.SelectLeft()),
            ("Select right", new TextActions.SelectRight()),
            ("Word left", new TextActions.MoveWordLeft()),
            ("Word right", new TextActions.MoveWordRight()),
            ("Line start", new TextActions.MoveToLineStart()),
            ("Line end", new TextActions.MoveToLineEnd()),
            ("Start", new TextActions.MoveToStart()),
            ("End", new TextActions.MoveToEnd()),
        ];

        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly Signal<bool> _open = Signal.Create(false);
        private readonly UiTree _tree;
        private readonly WindowFrames _frames;
        private readonly WindowInput _input;
        private readonly ScopeHandle _scope;
        private int _step;
        private Element _anchor;

        public MenuScene(MenuKind kind)
        {
            _tree = new UiTree(new TextContext(_fonts));
            NodeHandle root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, root, new NoTarget(), _atlas, new NoScheduler(), default);
            _input = new WindowInput(_tree, _frames, new NoPlatform());
            var model = new TodoModel();
            UiTree.Current = _tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    for (int i = 0; i < 200; i++)
                    {
                        _ = model.Add($"Item {i + 1}");
                    }

                    Func<Element> panel = kind switch
                    {
                        MenuKind.Plain => Panel,
                        MenuKind.WrittenKeys => WrittenPanel,
                        _ => NamedPanel,
                    };
                    _anchor = Box().Width(100).Height(30);
                    _tree.AppendChild(root, Column(_anchor.Menu(_open, panel), TodoApp.Todo(model)).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            // The window is active, so that the menu's focus moves.
            _input.SetActive(true);
            _frames.Resize(new Size(420, 360), 1);
            _frames.RunFrame();

            // The todo's field has the focus before a menu of the field's actions opens: the node after the selection area of the
            // counts in the order of Tab.
            if (kind != MenuKind.Plain)
            {
                _ = _tree.Focus.MoveToTabEnd(last: false);
                _ = _tree.Focus.MoveInTabOrder(backward: false);
                if (!_tree.Actions.IsAvailable(_tree.Focus.Focused, Actions[0].Action))
                {
                    throw new InvalidOperationException("The todo's field did not take the focus.");
                }
            }

            _frames.RunFrame();
        }

        public int Rows => _tree.Keymap.Rows.Length;

        // The median microseconds and the bytes of one read of each of the 10 actions at the focused node, the menu closed.
        public (double Us, double Bytes) TimeRead(Action<Element, IAction> read)
        {
            var times = new double[Samples];
            long bytes = 0;
            for (int sample = -3; sample < Samples; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int round = 0; round < 100; round++)
                {
                    foreach ((_, IAction action) in Actions)
                    {
                        read(_anchor, action);
                    }
                }

                double us = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                if (sample >= 0)
                {
                    times[sample] = us / (100 * Actions.Length);
                    bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                }
            }

            return (Median(times), (double)bytes / (100 * Actions.Length));
        }

        public void Open()
        {
            _open.Set(true);
            _frames.RunFrame();
        }

        public void Close()
        {
            _open.Set(false);
            _frames.RunFrame();
        }

        // The microseconds of the signal set, of the layout pass after it, and of the rest of the frame.
        public (double Change, double Layout, double Draw) TimeOpen()
        {
            long start = Stopwatch.GetTimestamp();
            _open.Set(true);
            long changed = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(_tree);
            long laidOut = Stopwatch.GetTimestamp();
            _frames.RunFrame();
            long drawn = Stopwatch.GetTimestamp();
            return (
                Stopwatch.GetElapsedTime(start, changed).TotalMicroseconds,
                Stopwatch.GetElapsedTime(changed, laidOut).TotalMicroseconds,
                Stopwatch.GetElapsedTime(laidOut, drawn).TotalMicroseconds
            );
        }

        // The median microseconds of a frame of an input over the samples, and the bytes and the window's builds of 1,000 such
        // frames after a warm-up.
        public (double Us, long Bytes, int Builds) TimeFrames(Action input)
        {
            for (int i = 0; i < 100; i++)
            {
                Frame(input);
            }

            // Each input moves the focus to another item; one that did not would time nothing.
            NodeHandle focused = _tree.Focus.Focused;
            Frame(input);
            if (_tree.Focus.Focused == focused || _tree.Popups.PopupContaining(_tree.Focus.Focused) is null)
            {
                throw new InvalidOperationException("The input did not move the focus between the menu's items.");
            }

            var times = new double[Samples];
            for (int i = 0; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Frame(input);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            int builds = _frames.MainBuilds;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++)
            {
                Frame(input);
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            return (Median(times), bytes, _frames.MainBuilds - builds);
        }

        public void Down() =>
            _ = _input.Handle(new KeyInput(KeyEventKind.Down, Key.Down, PhysicalKey.None, default, KeyModifiers.None, false, TimeSpan.Zero));

        // The pointer over the item after the one it was over: the items are 28.4 px high from y 34 (30 for the box, 4 for the panel's
        // padding).
        public void Hover()
        {
            _step = (_step + 1) % 10;
            int y = 34 + 14 + (int)(_step * 28.4f);
            _input.Handle(
                new PointerInput(
                    PointerInputKind.Move,
                    60,
                    y,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _atlas.Dispose();
            _fonts.Dispose();
        }

        private void Frame(Action input)
        {
            input();
            _frames.RunFrame();
        }

        // The menu's panel: 10 items of text with a hovered and a focused background, in a column with a background and padding.
        private static Element Panel()
        {
            var items = new Element[10];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = MenuItem(Elements.Text($"Item {i + 1}").Padding(8, 4), static () => { })
                    .Hovered(new PaintStyle { Background = Color.FromRgb(0x3F4A66) })
                    .Focused(new PaintStyle { Background = Color.FromRgb(0x3F4A66) });
            }

            return Column(items).Width(160).Padding(4).Background(Color.FromRgb(0x2B3245)).Foreground(Color.FromRgb(0xFFFFFF));
        }

        // 10 plain items of a label and the keys of the field's action, written in.
        private static Element WrittenPanel()
        {
            var items = new Element[Actions.Length];
            for (int i = 0; i < items.Length; i++)
            {
                string keys = Keymap.Default.Rows.Last(row => row.Action?.GetType() == Actions[i].Action.GetType()).ToDisplayString();
                items[i] = Look(MenuItem(Content(Actions[i].Label, () => keys), static () => { }));
            }

            return Shell(items);
        }

        // 10 items that name the field's actions, each with a label and the keys the keymap has for the action.
        private static Element NamedPanel()
        {
            var items = new Element[Actions.Length];
            for (int i = 0; i < items.Length; i++)
            {
                string label = Actions[i].Label;
                items[i] = Look(MenuItem(Actions[i].Action, keys => Content(label, () => keys.Get()?.ToDisplayString() ?? string.Empty)));
            }

            return Shell(items);
        }

        private static Element Content(string label, Func<string> keys) => Row(Elements.Text(label).Width(110), Elements.Text(keys)).Padding(8, 4);

        private static Element Look(Element item) =>
            item.Hovered(new PaintStyle { Background = Color.FromRgb(0x3F4A66) }).Focused(new PaintStyle { Background = Color.FromRgb(0x3F4A66) });

        private static Element Shell(Element[] items) =>
            Column(items).Width(240).Padding(4).Background(Color.FromRgb(0x2B3245)).Foreground(Color.FromRgb(0xFFFFFF));
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
