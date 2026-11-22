using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Theme.Fluent.Tests.FluentHeadless;

namespace XylemUI.Theme.Fluent.Tests;

// The root wrapper of the Fluent theme (design §13.2, "FluentRoot.Wrap がすること", "OS の見た目からテーマを選ぶ", "根の箱", and "題名の
// 棒"), in headless apps given it with HeadlessOptions.RootWrapper. The expected colors are those of the token files at microsoft/fluentui
// 1f59fc2 (packages/tokens/src/alias/lightColor.ts and darkColor.ts lines 8 and 67), read by hand.
public sealed class FluentRootTests
{
    /// <summary>
    /// The window's theme is made from the OS's appearance: high contrast with the OS's colors while it is on, else dark or light,
    /// with the OS's accent color and the standard density; the root function finds it under the theme's key.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: light, dark, light with the measured blue, and high contrast on (with the scheme dark, and with the scheme light
    ///   to show that high contrast wins) with eight OS colors, the blue as the accent.
    ///
    /// Steps:
    /// - Start a wrapped app whose root function reads <see cref="FluentTheme.Current"/>.
    ///
    /// Expected:
    /// - The variant is the case's; the density is standard; the accent is the OS's.
    /// - colorNeutralBackground1 is white (light), #292929 (dark), or the OS's window color (high contrast); colorBrandBackground is
    ///   brandWeb's #0f6cbd in light, #115ea3 in dark, the blue's step 80 #006cc0 in light with the blue, and the OS's button face
    ///   color in high contrast.
    /// </remarks>
    /// <param name="scheme">The OS's scheme.</param>
    /// <param name="highContrast">Whether high contrast is on.</param>
    /// <param name="withAccent">Whether the OS's accent color is the blue.</param>
    /// <param name="variant">The variant expected.</param>
    /// <param name="background">The colorNeutralBackground1 expected, as 0xRRGGBB.</param>
    /// <param name="brand">The colorBrandBackground expected, as 0xRRGGBB.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [UiTheory]
    [InlineData(ColorScheme.Light, false, false, ThemeVariant.Light, 0xFFFFFFu, 0x0F6CBDu)]
    [InlineData(ColorScheme.Dark, false, false, ThemeVariant.Dark, 0x292929u, 0x115EA3u)]
    [InlineData(ColorScheme.Light, false, true, ThemeVariant.Light, 0xFFFFFFu, 0x006CC0u)]
    [InlineData(ColorScheme.Dark, true, true, ThemeVariant.HighContrast, 0x100000u, 0x700000u)]
    [InlineData(ColorScheme.Light, true, true, ThemeVariant.HighContrast, 0x100000u, 0x700000u)]
    public async Task Wrap_TheOsAppearance_ChoosesTheWindowsTheme(
        ColorScheme scheme,
        bool highContrast,
        bool withAccent,
        ThemeVariant variant,
        uint background,
        uint brand
    )
    {
        Color? accent = withAccent ? Blue : null;
        var appearance = new SystemAppearance(scheme, highContrast, accent, highContrast ? DarkContrast : null);
        FluentTheme? found = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                found = FluentTheme.Current.Use().Get();
                return Box();
            },
            Options(appearance)
        );
        _ = app.PumpAndSettle();

        Assert.NotNull(found);
        Assert.Equal((variant, Density.Standard, accent), (found.Variant, found.Density, found.Accent));
        Assert.Equal(Color.FromRgb(background), found.Colors.ColorNeutralBackground1);
        Assert.Equal(Color.FromRgb(brand), found.Colors.ColorBrandBackground);
    }

    /// <summary>
    /// The root box takes the theme's background and color of text, and tells the window's surface the background and whether the
    /// title bar is dark; each change of the OS's appearance changes them, and the root function does not run again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light at the start, whose root function counts its runs.
    ///
    /// Steps:
    /// - Read the root box's background and color of text, the app's backdrop, and its title bar. Set the OS's appearance to dark,
    ///   to high contrast with a dark window color, to high contrast with a light window color, and to light; pump and read the
    ///   same after each.
    ///
    /// Expected:
    /// - Light: white and #242424, the backdrop white, the title bar light. Dark: #292929 and white, the backdrop #292929, the title
    ///   bar dark. High contrast: the OS's window and window text colors, the backdrop the window color, the title bar dark with the
    ///   dark window color and light with the light one. Light again: as at the start. The root function ran once.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Wrap_OsAppearanceChanges_TheRootBoxAndTheSurfaceFollow()
    {
        int roots = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                roots++;
                return Box();
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();
        List<(Paint?, Color?, Color?, bool?)> seen = [Read(app)];
        SystemAppearance[] steps = [Dark, HighContrast, new SystemAppearance(ColorScheme.Light, true, null, LightContrast), Light];
        foreach (SystemAppearance step in steps)
        {
            app.SetSystemAppearance(step);
            _ = app.PumpAndSettle();
            seen.Add(Read(app));
        }

        Color white = Color.FromRgb(0xFFFFFF);
        Color dark = Color.FromRgb(0x292929);
        (Paint?, Color?, Color?, bool?)[] expected =
        [
            (white, Color.FromRgb(0x242424), white, false),
            (dark, white, dark, true),
            (DarkContrast.Window, DarkContrast.WindowText, DarkContrast.Window, true),
            (LightContrast.Window, LightContrast.WindowText, LightContrast.Window, false),
            (white, Color.FromRgb(0x242424), white, false),
        ];
        Assert.Equal(expected, seen);
        Assert.Equal(1, roots);
    }

    /// <summary>
    /// The root box fills the window and holds the root function's node: it is the window's one node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app of 300 x 200 whose root function returns a box of 10 x 10.
    ///
    /// Steps:
    /// - Pump; read the root box's rectangle and the parent of the root function's node.
    ///
    /// Expected:
    /// - The root box is at 0, 0, 300 x 200, is the only child of the window's root node, and is the parent of the root function's
    ///   node.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Wrap_RootBox_FillsTheWindowAndHoldsTheRootsNode()
    {
        Element content = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => content = Box().Width(10).Height(10),
            Options(Light) with
            {
                Width = 300,
                Height = 200,
            }
        );
        _ = app.PumpAndSettle();
        Handle<UiNode> box = RootBoxNode(app);

        Assert.Equal(new Rect(0, 0, 300, 200), app.Tree.GetRect(box));
        Assert.True(app.Tree.Nodes.GetRef(box).NextSibling.IsNone);
        Assert.Equal(box, app.Tree.Nodes.GetRef(content.Node).Parent);
    }

    /// <summary>
    /// The wrapper refuses a missing root function, and it throws outside a window, where the OS's appearance is not provided.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tests' UI thread, outside any window's root.
    ///
    /// Steps:
    /// - Call <see cref="FluentRoot.Wrap"/> with <see langword="null"/>; call it with a root function inside a scope of no window.
    ///
    /// Expected:
    /// - <see cref="ArgumentNullException"/>, then <see cref="InvalidOperationException"/>; the root function did not run.
    /// </remarks>
    [UiFact]
    public void Wrap_NullOrOutsideAWindow_Throws()
    {
        int roots = 0;
        _ = Assert.Throws<ArgumentNullException>(() => FluentRoot.Wrap(null!));
        ScopeHandle scope = Scope.Create(() =>
        {
            _ = Assert.Throws<InvalidOperationException>(() =>
                FluentRoot.Wrap(() =>
                {
                    roots++;
                    return default;
                })
            );
        });
        Scope.Dispose(scope);

        Assert.Equal(0, roots);
    }

    /// <summary>
    /// The title bar of a high-contrast theme is dark when its window color reads as dark by Microsoft's test of a light color, and
    /// the bound itself is dark.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Colors around the bound 5 G + 2 R + B = 1,024: gray 128 (exactly 1,024), gray 129 (1,032), and black and white.
    /// - Two colors that tell the green's and the red's weights apart: pure green (5 x 255 = 1,275, and 510 with the weights
    ///   swapped) and pure red (2 x 255 = 510, and 1,275 with the weights swapped).
    ///
    /// Steps:
    /// - Ask whether each is dark.
    ///
    /// Expected:
    /// - Black and gray 128 are dark; gray 129 and white are not. Pure green is not dark; pure red is.
    /// </remarks>
    [Fact]
    public void IsDark_AtTheBound_IsDark()
    {
        bool[] dark =
        [
            FluentRoot.IsDark(Color.FromRgb(0x000000)),
            FluentRoot.IsDark(Color.FromRgb(0x808080)),
            FluentRoot.IsDark(Color.FromRgb(0x818181)),
            FluentRoot.IsDark(Color.FromRgb(0xFFFFFF)),
            FluentRoot.IsDark(Color.FromRgb(0x00FF00)),
            FluentRoot.IsDark(Color.FromRgb(0xFF0000)),
        ];

        Assert.Equal([true, true, false, false, false, true], dark);
    }

    /// <summary>
    /// The title bar's darkness follows the theme's variant, not its background: a dark theme whose background is replaced with a
    /// light color keeps a dark title bar, and a light theme with a dark background keeps a light one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, that asks for a theme from a signal: at the start the dark theme with colorNeutralBackground1
    ///   replaced by white (with <see langword="with"/>).
    ///
    /// Steps:
    /// - Pump; read the app's backdrop and title bar. Set the light theme with colorNeutralBackground1 replaced by black; pump; read
    ///   the same.
    ///
    /// Expected:
    /// - White and a dark title bar; black and a light title bar.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Wrap_ReplacedBackground_TheTitleBarFollowsTheVariant()
    {
        Color white = Color.FromRgb(0xFFFFFF);
        Color black = Color.FromRgb(0x000000);
        FluentTheme dark = FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null);
        FluentTheme light = FluentTheme.Create(ThemeVariant.Light, Density.Standard, null);
        Signal<FluentTheme> window = Signal.Create(dark with { Colors = dark.Colors with { ColorNeutralBackground1 = white } });
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                FluentTheme.RequestForWindow(window);
                return Box();
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();
        (Color?, bool?) first = (app.Backdrop, app.DarkTitleBar);

        window.Set(light with { Colors = light.Colors with { ColorNeutralBackground1 = black } });
        _ = app.PumpAndSettle();
        (Color?, bool?) second = (app.Backdrop, app.DarkTitleBar);

        Assert.Equal(((Color?)white, (bool?)true), first);
        Assert.Equal(((Color?)black, (bool?)false), second);
    }

    private static (Paint? BoxBackground, Color? BoxForeground, Color? Backdrop, bool? DarkTitleBar) Read(HeadlessApp app)
    {
        NodeVisual box = RootBoxVisual(app);
        return (box.Background, box.Foreground, app.Backdrop, app.DarkTitleBar);
    }
}
