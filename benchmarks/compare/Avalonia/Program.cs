// The comparison scene in Avalonia (roadmap row M1a; the same as benchmarks/XylemUI.E2E/Scene.cs): an 800 x 600 window cleared
// to #2A2A2A, the marker #3366CC at (0, 0, 40, 40), 1,000 rectangles that move with the frame, and one line of Japanese text at
// 24 px at (60, 30) in the test font. Animated through RequestAnimationFrame; with --static it draws once and stays idle.
// With --redirection it composes through a DXGI swap chain on the window (Win32CompositionMode.RedirectionSurface) instead of the
// default Windows.UI.Composition, whose updates PresentMon does not record for the app (M1a): only the frame measurements use it.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Themes.Fluent;

internal static class Program
{
    public static bool Static { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
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
