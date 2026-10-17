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

// The layout scene of XylemUI's `layout` scenario (benchmarks/XylemUI.E2E/LayoutCost.cs, row L1a) in Avalonia's layout: a
// vertical StackPanel of 100 horizontal StackPanels of 100 controls that measure 10 x 10, in a 2,000 x 2,000 window, timed as
// one pass of the window's layout manager (UpdateLayout). The window is in place before the scene is set, so its own layout is
// not timed. A control outside a window has no layout manager, so a change inside it would not reach its parent. The three
// times of the comparison: the first layout, a layout after one control's width changes, and a layout at a new width.
internal static class LayoutScene
{
    public static void Run(string output)
    {
        var first = new List<double>();
        var change = new List<double>();
        var width = new List<double>();
        for (int s = 0; s < 18; s++)
        {
            (StackPanel root, Leaf target) = Build();
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

            double widthUs = 0;
            for (int i = 0; i < 2; i++)
            {
                window.Width = i % 2 == 0 ? 1_600 : 2_000;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                window.UpdateLayout();
                widthUs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            // The width change reached the scene: its rows are as wide as the window.
            if (root.Bounds.Width != 2_000 || target.Bounds.Width != 10)
            {
                throw new InvalidOperationException($"The layout did not follow: root {root.Bounds}, target {target.Bounds}.");
            }

            window.Close();

            if (s >= 3)
            {
                first.Add(firstUs);
                change.Add(changeUs);
                width.Add(widthUs);
            }
        }

        File.WriteAllText(
            output,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"name\":\"Avalonia\",\"first_us_p50\":{Median(first):F1},\"change_us_p50\":{Median(change):F1},\"width_us_p50\":{Median(width):F1}}}{Environment.NewLine}"
            )
        );
    }

    private static (StackPanel Root, Leaf Target) Build()
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
