using System.Diagnostics;
using System.Globalization;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// A spike for row TH1 (the theme's Grill, Q5): how a themed part follows a change of the theme, measured on 1,000 buttons in a
// window of 1,600 x 800 with text and no GPU, as the menu scene runs one. The theme is one immutable object in a Signal<Theme>,
// provided around the buttons through a Context<IReadOnlySignal<Theme>>. Each button is Primitive.Button over a text of its
// number, 40 x 32, in 25 rows of 40; every fifth is an accent button and every tenth is disabled. Its looks come from the theme:
// background, border paint, text color, corner radius, padding, and the hovered, pressed, and disabled looks. The way it follows
// the theme is the scene's argument:
//
// A  a signal per property: each look is bound to a Computed over the theme signal (8 computed values and 8 effects a button).
//    The pseudo-state looks and the padding have no signal overload in Core; the scene makes NodeProperty values for them with
//    the internal constructor, as a Core with those overloads would (design's Q5-A).
// B  one effect a part: each button makes one effect that reads the theme signal and writes all its looks with the tree's
//    setters; a setter given the value it holds changes nothing, and the padding is written only when the theme's differs.
// C  named C' in the record: one effect for the whole tree walks every node slot, as LC1's walk of the default text's language
//    does, and writes the looks of each node tagged with a part kind from the new theme's table. The tag is a byte array the
//    scene keeps by the node's slot index, in place of a field on UiNode. A real C would store token slots in the node's paint
//    and resolve them in the draw stage, with no write at the switch; Core has no hook for that.
// D  rebuild: the buttons are made with the theme's values under a Show keyed on the theme's darkness, so a switch disposes
//    the 1,000 buttons and makes them again (state such as the focus is lost).
//
// Figures: the build of the buttons (time, bytes allocated, median of 31 builds, each disposed after its first frame), and its
// first frame; the live scopes after one build (effects' scopes included) and the computed values and effects the scene made;
// the managed heap retained a button after the build and after its first frame (GC.GetTotalMemory(true), approximate); a switch
// from light to dark and back, each in three parts as the menu scene times an open (the signal set, the layout pass, the rest
// of the frame) with the bytes it allocated; and a frame of the pointer moved between the first two buttons (time and bytes,
// which must be 0).
internal static class ThemeFollowCost
{
    private const int Samples = 31;
    private const int Frames = 1_000;
    private const int Buttons = 1_000;
    private const int PerRow = 40;
    private const byte StandardKind = 1;
    private const byte AccentKind = 2;

    private static readonly Context<IReadOnlySignal<Theme>> ThemeKey = new();
    private static readonly Theme Light = MakeLight();
    private static readonly Theme Dark = MakeDark();
    private static readonly string[] Labels = MakeLabels();
    private static readonly Action NoClick = static () => { };

    // The looks Core has no signal overload for (design's Q5-A would add them).
    private static readonly NodeProperty<PaintStyle?> HoveredLook = new("Hovered", static (tree, node, value) => tree.SetHoveredStyle(node, value));
    private static readonly NodeProperty<PaintStyle?> PressedLook = new("Pressed", static (tree, node, value) => tree.SetPressedStyle(node, value));
    private static readonly NodeProperty<PaintStyle?> DisabledLook = new(
        "DisabledLook",
        static (tree, node, value) => tree.SetDisabledStyle(node, value)
    );

    private static readonly NodeProperty<Insets> PaddingLook = new(
        "Padding",
        static (tree, node, value) => _ = new Element(tree, node).Padding(value.X, value.Y)
    );

    public static int Run(string way)
    {
        Way parsed = way switch
        {
            "A" => Way.A,
            "B" => Way.B,
            "C" => Way.C,
            "D" => Way.D,
            _ => throw new ArgumentException("The way is A, B, C, or D.", nameof(way)),
        };
        UiThread.Initialize();
        using var report = new Report("theme-follow");
        report.Text("way", way);
        string wayName = parsed switch
        {
            Way.A => "signal-per-property",
            Way.B => "effect-per-part",
            Way.C => "tree-walk",
            _ => "rebuild",
        };
        report.Text("way_name", wayName);
        report.Number("buttons", Buttons);
        using var scene = new ThemeScene(parsed);

        var build = new double[Samples];
        var buildBytes = new long[Samples];
        var first = new double[Samples];
        for (int i = -3; i < Samples; i++)
        {
            (double us, long bytes) = scene.Build();
            double frame = scene.Frame();
            scene.Clear();
            if (i >= 0)
            {
                (build[i], buildBytes[i], first[i]) = (us, bytes, frame);
            }
        }

        report.Number("build_us_p50", Median(build));
        report.Number("build_bytes_per_button", (double)Median(buildBytes) / Buttons);
        report.Number("first_frame_us_p50", Median(first));

        // The build that stays: the heap before and after it, collected.
        long heap = GC.GetTotalMemory(true);
        int scopes = Scope.LiveCount;
        _ = scene.Build();
        long heapBuilt = GC.GetTotalMemory(true);
        report.Number("live_scopes", Scope.LiveCount - scopes);
        report.Number("computeds", scene.Computeds);
        report.Number("effects", scene.Effects);
        _ = scene.Frame();
        long heapFramed = GC.GetTotalMemory(true);
        report.Number("retained_bytes_per_button_build", (double)(heapBuilt - heap) / Buttons);
        report.Number("retained_bytes_per_button_frame", (double)(heapFramed - heap) / Buttons);
        scene.TakePoints();

        Switches(report, scene);

        (double hoverUs, long hoverBytes, int missed) = scene.TimeHover();
        report.Number("hover_frame_us_p50", hoverUs);
        report.Number("hover_bytes_per_frame", (double)hoverBytes / Frames);
        report.Number("hover_missed", missed);
        return 0;
    }

    // Light to dark and back, each switch timed in three parts, with the bytes it allocated.
    private static void Switches(Report report, ThemeScene scene)
    {
        var toDark = new SwitchTimes();
        var toLight = new SwitchTimes();
        for (int i = -3; i < Samples; i++)
        {
            toDark.Take(i, scene.Switch(Dark));
            toLight.Take(i, scene.Switch(Light));
        }

        toDark.Write(report, "switch_dark_");
        toLight.Write(report, "switch_light_");
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static long Median(long[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static string[] MakeLabels()
    {
        var labels = new string[Buttons];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = i.ToString("D3", CultureInfo.InvariantCulture);
        }

        return labels;
    }

    // Fluent-like values for the spike; their exact colors do not matter, only that light and dark differ.
    private static Theme MakeLight() =>
        new(
            false,
            new ButtonLooks(
                Color.FromRgb(0xFFFFFF),
                Color.FromRgb(0xD1D1D1),
                Color.FromRgb(0x242424),
                new CornerRadii(4, 4, 4, 4),
                new Insets(12, 5),
                new PaintStyle { Background = Color.FromRgb(0xF5F5F5) },
                new PaintStyle { Background = Color.FromRgb(0xE0E0E0) },
                new PaintStyle { Background = Color.FromRgb(0xF0F0F0), Foreground = Color.FromRgb(0xBDBDBD) }
            ),
            new ButtonLooks(
                Color.FromRgb(0x0F6CBD),
                Color.FromRgb(0x0F6CBD),
                Color.FromRgb(0xFFFFFF),
                new CornerRadii(4, 4, 4, 4),
                new Insets(12, 5),
                new PaintStyle { Background = Color.FromRgb(0x115EA3) },
                new PaintStyle { Background = Color.FromRgb(0x0C3B5E) },
                new PaintStyle { Background = Color.FromRgb(0xF0F0F0), Foreground = Color.FromRgb(0xBDBDBD) }
            )
        );

    private static Theme MakeDark() =>
        new(
            true,
            new ButtonLooks(
                Color.FromRgb(0x292929),
                Color.FromRgb(0x666666),
                Color.FromRgb(0xFFFFFF),
                new CornerRadii(4, 4, 4, 4),
                new Insets(12, 5),
                new PaintStyle { Background = Color.FromRgb(0x3D3D3D) },
                new PaintStyle { Background = Color.FromRgb(0x1F1F1F) },
                new PaintStyle { Background = Color.FromRgb(0x141414), Foreground = Color.FromRgb(0x5C5C5C) }
            ),
            new ButtonLooks(
                Color.FromRgb(0x115EA3),
                Color.FromRgb(0x115EA3),
                Color.FromRgb(0xFFFFFF),
                new CornerRadii(4, 4, 4, 4),
                new Insets(12, 5),
                new PaintStyle { Background = Color.FromRgb(0x0F6CBD) },
                new PaintStyle { Background = Color.FromRgb(0x0C3B5E) },
                new PaintStyle { Background = Color.FromRgb(0x141414), Foreground = Color.FromRgb(0x5C5C5C) }
            )
        );

    // How a button follows the theme.
    private enum Way
    {
        A,
        B,
        C,
        D,
    }

    private readonly record struct Insets(float X, float Y);

    // The looks of one kind of button.
    private sealed record ButtonLooks(
        Paint Background,
        Paint Border,
        Color Foreground,
        CornerRadii Radius,
        Insets Padding,
        PaintStyle? Hovered,
        PaintStyle? Pressed,
        PaintStyle? Disabled
    );

    // The theme: one immutable object, swapped whole. Looks is its table of part kinds.
    private sealed record Theme(bool IsDark, ButtonLooks Standard, ButtonLooks Accent)
    {
        public ButtonLooks Looks(byte kind) => kind == AccentKind ? Accent : Standard;
    }

    // The medians of the switches of one direction.
    private sealed class SwitchTimes
    {
        private readonly double[] _change = new double[Samples];
        private readonly double[] _layout = new double[Samples];
        private readonly double[] _rest = new double[Samples];
        private readonly double[] _total = new double[Samples];
        private readonly long[] _bytes = new long[Samples];

        public void Take(int sample, (double Change, double Layout, double Other, long Bytes) times)
        {
            if (sample < 0)
            {
                return;
            }

            _change[sample] = times.Change;
            _layout[sample] = times.Layout;
            _rest[sample] = times.Other;
            _total[sample] = times.Change + times.Layout + times.Other;
            _bytes[sample] = times.Bytes;
        }

        public void Write(Report report, string prefix)
        {
            report.Number(prefix + "us_p50", Median(_total));
            report.Number(prefix + "change_us_p50", Median(_change));
            report.Number(prefix + "layout_us_p50", Median(_layout));
            report.Number(prefix + "rest_us_p50", Median(_rest));
            report.Number(prefix + "bytes_p50", Median(_bytes));
        }
    }

    // The 1,000 buttons under the theme's key, in a tree with a window's frames and input and no GPU.
    private sealed class ThemeScene : IDisposable
    {
        private readonly DirectWriteFontProvider _fonts = new();
        private readonly GlyphAtlas _atlas = new(static () => new DirectWriteGlyphRasterizer());
        private readonly Signal<Theme> _theme = Signal.Create(Light);
        private readonly NodeHandle[] _nodes = new NodeHandle[Buttons];
        private readonly (int X, int Y)[] _points = new (int X, int Y)[2];
        private readonly UiTree _tree;
        private readonly NodeHandle _root;
        private readonly WindowFrames _frames;
        private readonly WindowInput _input;
        private readonly Way _way;
        private readonly Action _body;
        private readonly Func<Element> _content;
        private byte[] _kinds = new byte[4096];
        private ScopeHandle _scope;
        private int _hover;

        public ThemeScene(Way way)
        {
            _way = way;
            _body = Body;
            _content = Content;
            _tree = new UiTree(new TextContext(_fonts));
            _root = _tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(_tree, _root, new NoTarget(), _atlas, new NoScheduler(), default);
            _input = new WindowInput(_tree, _frames, new NoPlatform());
            _input.SetActive(true);
            _frames.Resize(new Size(1600, 800), 1);
            _frames.RunFrame();
        }

        // The computed values the scene made in its last build.
        public int Computeds { get; private set; }

        // The effects the scene made in its last build, by its own calls (a Bind makes one; Show's are not counted).
        public int Effects { get; private set; }

        // The microseconds and the bytes of one build of the buttons, put in the root.
        public (double Us, long Bytes) Build()
        {
            Computeds = 0;
            Effects = 0;
            UiTree.Current = _tree;
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                _scope = Scope.Create(_body);
                double us = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                return (us, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            finally
            {
                UiTree.Current = null;
            }
        }

        // The microseconds of one frame.
        public double Frame()
        {
            long start = Stopwatch.GetTimestamp();
            _frames.RunFrame();
            return Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        }

        // Disposes the buttons and draws the empty root.
        public void Clear()
        {
            Scope.Dispose(_scope);
            _scope = default;
            Array.Clear(_kinds);
            _frames.RunFrame();
        }

        // The centers of the first two buttons in the window, which a rebuild keeps.
        public void TakePoints()
        {
            for (int i = 0; i < _points.Length; i++)
            {
                Rect rect = _tree.Nodes.GetRef(_nodes[i]).Layout.Rect;
                float x = rect.X + (rect.Width / 2);
                float y = rect.Y + (rect.Height / 2);
                for (NodeHandle parent = _tree.Nodes.GetRef(_nodes[i]).Parent; !parent.IsNone; parent = _tree.Nodes.GetRef(parent).Parent)
                {
                    Rect box = _tree.Nodes.GetRef(parent).Layout.Rect;
                    x += box.X;
                    y += box.Y;
                }

                _points[i] = ((int)x, (int)y);
            }
        }

        // One switch of the theme: the microseconds of the signal set, of the layout pass after it, and of the rest of the frame,
        // and the bytes of the three. The first button's looks are checked after it.
        public (double Change, double Layout, double Other, long Bytes) Switch(Theme to)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            _theme.Set(to);
            long changed = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(_tree);
            long laidOut = Stopwatch.GetTimestamp();
            _frames.RunFrame();
            long drawn = Stopwatch.GetTimestamp();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            NodeVisual? visual = _tree.Nodes.GetRef(_nodes[0]).Visual;
            if (visual is null || visual.Background != to.Standard.Background || visual.States?.Hovered != to.Standard.Hovered)
            {
                throw new InvalidOperationException("The switch did not write the theme's looks to the first button.");
            }

            return (
                Stopwatch.GetElapsedTime(start, changed).TotalMicroseconds,
                Stopwatch.GetElapsedTime(changed, laidOut).TotalMicroseconds,
                Stopwatch.GetElapsedTime(laidOut, drawn).TotalMicroseconds,
                bytes
            );
        }

        // The median microseconds of a frame of the pointer moved to the other of the first two buttons, the bytes of 1,000 such
        // frames after a warm-up, counted around each frame, and the frames whose button did not take the hover.
        public (double Us, long Bytes, int Missed) TimeHover()
        {
            for (int i = 0; i < 100; i++)
            {
                HoverFrame();
            }

            var times = new double[Samples];
            for (int i = 0; i < Samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                HoverFrame();
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                if (!Hovered())
                {
                    throw new InvalidOperationException("The pointer's move did not hover the button under it.");
                }
            }

            long bytes = 0;
            int missed = 0;
            for (int i = 0; i < Frames; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                HoverFrame();
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                if (!Hovered())
                {
                    missed++;
                }
            }

            return (Median(times), bytes, missed);
        }

        public void Dispose()
        {
            if (!_scope.IsNone)
            {
                Scope.Dispose(_scope);
            }

            _atlas.Dispose();
            _fonts.Dispose();
        }

        // What a theme switch writes to a button, and what the static ways write once: each setter changes nothing for the value
        // it holds; the padding, a layout change, is written only when it differs from the one the previous theme gave.
        private static void Apply(UiTree tree, NodeHandle node, ButtonLooks looks, ButtonLooks? previous)
        {
            tree.SetBackground(node, looks.Background);
            tree.SetBorderPaint(node, looks.Border);
            tree.SetForeground(node, looks.Foreground);
            tree.SetCornerRadii(node, looks.Radius);
            tree.SetHoveredStyle(node, looks.Hovered);
            tree.SetPressedStyle(node, looks.Pressed);
            tree.SetDisabledStyle(node, looks.Disabled);
            if (previous is null || previous.Padding != looks.Padding)
            {
                _ = new Element(tree, node).Padding(looks.Padding.X, looks.Padding.Y);
            }
        }

        private void Body() => _tree.AppendChild(_root, ThemeKey.Provide(_theme, _content).Node);

        // The buttons under the provided theme, made by the scene's way.
        private Element Content()
        {
            IReadOnlySignal<Theme> theme = ThemeKey.Use();
            Theme start = theme.Get();
            if (_way == Way.D)
            {
                Computeds++;
                return Show(Computed.Create(() => theme.Get().IsDark), () => MakeGrid(theme, Dark), () => MakeGrid(theme, Light));
            }

            Element grid = MakeGrid(theme, start);
            if (_way == Way.C)
            {
                // C': one effect for the tree; its first run only reads the signal.
                Theme applied = start;
                Effects++;
                _ = Effect.Create(() =>
                {
                    Theme now = theme.Get();
                    if (!ReferenceEquals(now, applied))
                    {
                        Restyle(now, applied);
                        applied = now;
                    }
                });
            }

            return grid;
        }

        // 25 rows of 40 buttons, their looks first written from start.
        private Element MakeGrid(IReadOnlySignal<Theme> theme, Theme start)
        {
            var rows = new Element[Buttons / PerRow];
            var cells = new Element[PerRow];
            for (int row = 0; row < rows.Length; row++)
            {
                for (int cell = 0; cell < PerRow; cell++)
                {
                    cells[cell] = MakeButton((row * PerRow) + cell, theme, start);
                }

                rows[row] = Row(cells);
            }

            return Column(rows);
        }

        private Element MakeButton(int index, IReadOnlySignal<Theme> theme, Theme start)
        {
            byte kind = index % 5 == 4 ? AccentKind : StandardKind;
            ButtonLooks looks = start.Looks(kind);
            Element button = Button(Elements.Text(Labels[index]), NoClick).Width(40).Height(32).Border(1, looks.Border);
            if (index % 10 == 9)
            {
                _ = button.Disabled();
            }

            _nodes[index] = button.Node;
            switch (_way)
            {
                case Way.A:
                    BindLooks(button, theme, kind);
                    break;
                case Way.B:
                    Follow(button, theme, kind);
                    break;
                case Way.C:
                    Apply(_tree, button.Node, looks, null);
                    Tag(button.Node, kind);
                    break;
                default:
                    Apply(_tree, button.Node, looks, null);
                    break;
            }

            return button;
        }

        // A: each look bound to a computed value of the theme.
        private void BindLooks(Element button, IReadOnlySignal<Theme> theme, byte kind)
        {
            _ = button
                .Background(Computed.Create(() => theme.Get().Looks(kind).Background))
                .Bind(NodeProperties.BorderPaint, Computed.Create(() => theme.Get().Looks(kind).Border))
                .Foreground(Computed.Create(() => theme.Get().Looks(kind).Foreground))
                .Bind(NodeProperties.CornerRadius, Computed.Create(() => theme.Get().Looks(kind).Radius))
                .Bind(PaddingLook, Computed.Create(() => theme.Get().Looks(kind).Padding))
                .Bind(HoveredLook, Computed.Create(() => theme.Get().Looks(kind).Hovered))
                .Bind(PressedLook, Computed.Create(() => theme.Get().Looks(kind).Pressed))
                .Bind(DisabledLook, Computed.Create(() => theme.Get().Looks(kind).Disabled));
            Computeds += 8;
            Effects += 8;
        }

        // B: one effect that writes every look of the button.
        private void Follow(Element button, IReadOnlySignal<Theme> theme, byte kind)
        {
            UiTree tree = _tree;
            NodeHandle node = button.Node;
            ButtonLooks? applied = null;
            Effects++;
            _ = Effect.Create(() =>
            {
                ButtonLooks looks = theme.Get().Looks(kind);
                if (tree.Nodes.IsAlive(node))
                {
                    Apply(tree, node, looks, applied);
                    applied = looks;
                }
            });
        }

        // C': the part kind of a node, by its slot, as a byte on the node would hold it.
        private void Tag(NodeHandle node, byte kind)
        {
            if (node.Index >= _kinds.Length)
            {
                Array.Resize(ref _kinds, Math.Max(node.Index + 1, _kinds.Length * 2));
            }

            _kinds[node.Index] = kind;
        }

        // C': the walk over every node slot, as LC1's ShapeDefaultTextAgain walks them.
        private void Restyle(Theme now, Theme previous)
        {
            for (int index = 0; index < _tree.Nodes.SlotCount; index++)
            {
                if (_tree.Nodes.TryGetHandleAt(index, out NodeHandle node) && index < _kinds.Length && _kinds[index] != 0)
                {
                    byte kind = _kinds[index];
                    Apply(_tree, node, now.Looks(kind), previous.Looks(kind));
                }
            }
        }

        private void HoverFrame()
        {
            _hover ^= 1;
            (int x, int y) = _points[_hover];
            _input.Handle(
                new PointerInput(
                    PointerInputKind.Move,
                    x,
                    y,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
            _frames.RunFrame();
        }

        private bool Hovered() => (_tree.Nodes.GetRef(_nodes[_hover]).Pseudo & PseudoState.Hovered) != 0;
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
