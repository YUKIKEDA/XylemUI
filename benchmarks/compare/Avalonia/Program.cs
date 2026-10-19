// The comparison scene in Avalonia (roadmap row M1a; the same as benchmarks/XylemUI.E2E/Scene.cs): an 800 x 600 window cleared
// to #2A2A2A, the marker #3366CC at (0, 0, 40, 40), 1,000 rectangles that move with the frame, and one line of Japanese text at
// 24 px at (60, 30) in the test font. Animated through RequestAnimationFrame; with --static it draws once and stays idle.
// With --redirection it composes through a DXGI swap chain on the window (Win32CompositionMode.RedirectionSurface) instead of the
// default Windows.UI.Composition, whose updates PresentMon does not record for the app (M1a): only the frame measurements use it.
// With --layout <file> it shows no window: it times the layout of XylemUI's layout scene (row L1a, LayoutScene below) in a window
// of Avalonia's headless platform, and writes one JSON line to the file.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Themes.Fluent;

internal static class Program
{
    public static bool Static { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        int layout = Array.IndexOf(args, "--layout");
        if (layout >= 0)
        {
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
            LayoutScene.Run(args[layout + 1]);
            return 0;
        }

        Static = args.Contains("--static");
        AppBuilder builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (args.Contains("--redirection"))
        {
            builder = builder.With(new Win32PlatformOptions { CompositionMode = [Win32CompositionMode.RedirectionSurface] });
        }

        return builder.StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Avalonia compare",
                Width = 800,
                Height = 600,
                Content = new SceneControl(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class SceneControl : Control
{
    private static readonly IBrush Clear = new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly IBrush Marker = new ImmutableSolidColorBrush(Color.FromRgb(0x33, 0x66, 0xCC));
    private static readonly IBrush Red = new ImmutableSolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33));
    private static readonly IBrush Green = new ImmutableSolidColorBrush(Color.FromRgb(0x33, 0xAA, 0x66));
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly Typeface Font = new(new FontFamily("avares://CompareAvalonia/Assets#Noto Sans JP Thin"));
    private int _frame;

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        context.FillRectangle(Clear, new Rect(0, 0, width, height));
        context.FillRectangle(Marker, new Rect(0, 0, 40, 40));
        for (int i = 0; i < 1_000; i++)
        {
            double x = ((i * 37) + (_frame * 3)) % Math.Max(1, width - 20);
            double y = 50 + ((i * 11) % Math.Max(1, height - 70));
            context.FillRectangle((i & 1) == 0 ? Red : Green, new Rect(x, y, 16, 16));
        }

        var text = new FormattedText("Hello, World! 日本語のテキスト", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 24, Ink);
        context.DrawText(text, new Point(60, 30 - text.Baseline));
        if (!Program.Static)
        {
            _frame++;
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => InvalidateVisual());
        }
    }
}

// The layout scenes of XylemUI's `layout` scenario (benchmarks/XylemUI.E2E/LayoutCost.cs, roadmap rows L1a and L1b) in Avalonia's
// layout, in a 2,000 x 2,000 window, timed as one pass of the window's layout manager (UpdateLayout): a vertical StackPanel of
// 100 horizontal StackPanels of 100 controls that measure 10 x 10, and a WrapPanel of 10,000 of them (the `wrap_` figures). The
// window is in place before the scene is set, so its own layout is not timed. A control outside a window has no layout manager,
// so a change inside it would not reach its parent. The four times of the comparison: the first layout, a layout after one
// control's width changes, a layout after one control is measured again at the same size (InvalidateMeasure, as a change of
// its content that keeps its size; row L5), and a layout at a new width.
internal static class LayoutScene
{
    public static void Run(string output)
    {
        (double First, double Change, double Content, double Width) stack = Measure(BuildStacks);
        (double First, double Change, double Content, double Width) wrap = Measure(BuildWrap);
        (double First, double Change) absolute = MeasureAbsolute();
        File.WriteAllText(
            output,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"name\":\"Avalonia\",\"first_us_p50\":{stack.First:F1},\"change_us_p50\":{stack.Change:F1},\"content_us_p50\":{stack.Content:F1},\"width_us_p50\":{stack.Width:F1},\"wrap_first_us_p50\":{wrap.First:F1},\"wrap_change_us_p50\":{wrap.Change:F1},\"wrap_content_us_p50\":{wrap.Content:F1},\"wrap_width_us_p50\":{wrap.Width:F1},\"absolute_first_us_p50\":{absolute.First:F1},\"absolute_change_us_p50\":{absolute.Change:F1}}}{Environment.NewLine}"
            )
        );
    }

    private static (double First, double Change, double Content, double Width) Measure(Func<(Panel Root, Leaf Target)> build)
    {
        var first = new List<double>();
        var change = new List<double>();
        var content = new List<double>();
        var width = new List<double>();
        for (int s = 0; s < 18; s++)
        {
            (Panel root, Leaf target) = build();
            var window = new Window { Width = 2_000, Height = 2_000 };
            window.Show();
            window.UpdateLayout();
            window.Content = root;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            window.UpdateLayout();
            double firstUs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            double changeUs = 0;
            for (int i = 0; i < 2; i++)
            {
                target.Width = i % 2 == 0 ? 20 : 10;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                window.UpdateLayout();
                changeUs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            double contentUs = 0;
            for (int i = 0; i < 2; i++)
            {
                target.InvalidateMeasure();
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                window.UpdateLayout();
                contentUs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            double widthUs = 0;
            for (int i = 0; i < 2; i++)
            {
                window.Width = i % 2 == 0 ? 1_600 : 2_000;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                window.UpdateLayout();
                widthUs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            // The width change reached the scene: it is as wide as the window.
            if (root.Bounds.Width != 2_000 || target.Bounds.Width != 10)
            {
                throw new InvalidOperationException($"The layout did not follow: root {root.Bounds}, target {target.Bounds}.");
            }

            window.Close();

            if (s >= 3)
            {
                first.Add(firstUs);
                change.Add(changeUs);
                content.Add(contentUs);
                width.Add(widthUs);
            }
        }

        return (Median(first), Median(change), Median(content), Median(width));
    }

    // The absolute scene (row L2): a vertical StackPanel of 100 Canvases of 2,000 x 20, each with 100 leaves of 10 x 10 at
    // Canvas.Left 20 each. The change is one leaf's width, 10 or 20 in turn.
    private static (double First, double Change) MeasureAbsolute()
    {
        var first = new List<double>();
        var change = new List<double>();
        for (int s = 0; s < 18; s++)
        {
            var root = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical };
            Leaf? target = null;
            for (int r = 0; r < 100; r++)
            {
                var canvas = new Canvas { Width = 2_000, Height = 20 };
                for (int c = 0; c < 100; c++)
                {
                    var leaf = new Leaf();
                    Canvas.SetLeft(leaf, c * 20);
                    Canvas.SetTop(leaf, 0);
                    canvas.Children.Add(leaf);
                    if (r == 50 && c == 50)
                    {
                        target = leaf;
                    }
                }

                root.Children.Add(canvas);
            }

            var window = new Window { Width = 2_000, Height = 2_000 };
            window.Show();
            window.UpdateLayout();
            window.Content = root;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            window.UpdateLayout();
            double firstUs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            double changeUs = 0;
            for (int i = 0; i < 2; i++)
            {
                target!.Width = i % 2 == 0 ? 20 : 10;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                window.UpdateLayout();
                changeUs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            if (target!.Bounds.Width != 10)
            {
                throw new InvalidOperationException($"The absolute scene did not follow the change: target {target.Bounds}.");
            }

            window.Close();
            if (s >= 3)
            {
                first.Add(firstUs);
                change.Add(changeUs);
            }
        }

        return (Median(first), Median(change));
    }

    private static (Panel Root, Leaf Target) BuildStacks()
    {
        var root = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical };
        Leaf? target = null;
        for (int r = 0; r < 100; r++)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            for (int c = 0; c < 100; c++)
            {
                var leaf = new Leaf();
                row.Children.Add(leaf);
                if (r == 50 && c == 50)
                {
                    target = leaf;
                }
            }

            root.Children.Add(row);
        }

        return (root, target!);
    }

    private static (Panel Root, Leaf Target) BuildWrap()
    {
        var root = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        Leaf? target = null;
        for (int i = 0; i < 10_000; i++)
        {
            var leaf = new Leaf();
            root.Children.Add(leaf);
            if (i == 5_050)
            {
                target = leaf;
            }
        }

        return (root, target!);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    // A control whose content measures 10 x 10, like the leaves of XylemUI's scene.
    private sealed class Leaf : Control
    {
        protected override Size MeasureOverride(Size availableSize) => new(10, 10);
    }
}
