using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Theme.Fluent.Tests.FluentHeadless;

namespace XylemUI.Theme.Fluent.Tests;

// The theme of a window and of a part of its tree (design §13.2, "窓のテーマ", "アプリが選ぶ時", and "一部だけ別のテーマ"):
// FluentTheme.RequestForWindow, a theme provided under FluentTheme.Current, and FluentTheme.ProvideVariant, in headless apps wrapped
// by FluentRoot. colorNeutralBackground1 is white in light, #292929 in dark, and the OS's window color in high contrast
// (alias/lightColor.ts, darkColor.ts, and highContrastColor.ts line 77 at microsoft/fluentui 1f59fc2).
public sealed class FluentThemeWindowTests
{
    private static readonly Color White = Color.FromRgb(0xFFFFFF);
    private static readonly Color Gray16 = Color.FromRgb(0x292929);

    /// <summary>
    /// The theme a window asks for reaches the root box, the title bar, and the parts; a second request replaces the first, the
    /// OS's changes do not show while one is asked for, and <see langword="null"/> goes back to the OS's theme.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, whose root function makes an effect that asks for the theme an app-level signal holds
    ///   (<see langword="null"/> at the start), and a button.
    ///
    /// Steps:
    /// - Ask for a dark theme; pump; read. Ask for a high-contrast theme of the OS's colors; set the OS's appearance to dark;
    ///   pump; read. Ask for <see langword="null"/>; pump; read. Set the OS's appearance to light; pump; read.
    ///
    /// Expected:
    /// - Each read is the root box's background, the backdrop, the title bar, and the button's background: #292929, #292929, dark,
    ///   #292929; the OS's window color twice, dark, the window color; #292929 twice, dark, #292929 (the OS's dark theme now);
    ///   white twice, light, white.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RequestForWindow_AskedTheme_ReachesTheWindow_ReplacesAndGoesBack()
    {
        Signal<IReadOnlySignal<FluentTheme>?> asked = Signal.Create<IReadOnlySignal<FluentTheme>?>(null);
        Element button = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                _ = Effect.Create(() => FluentTheme.RequestForWindow(asked.Get()));
                return button = Fluent.Button(Box().Width(8).Height(8), () => { });
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();
        var seen = new List<(Paint?, Color?, bool?, Paint?)>();

        asked.Set(Signal.Create(FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null)));
        _ = app.PumpAndSettle();
        seen.Add(Read(app, button));
        asked.Set(Signal.Create(FluentTheme.Create(ThemeVariant.HighContrast, Density.Standard, null, DarkContrast)));
        app.SetSystemAppearance(Dark);
        _ = app.PumpAndSettle();
        seen.Add(Read(app, button));
        asked.Set(null);
        _ = app.PumpAndSettle();
        seen.Add(Read(app, button));
        app.SetSystemAppearance(Light);
        _ = app.PumpAndSettle();
        seen.Add(Read(app, button));

        Color window = DarkContrast.Window;
        (Paint?, Color?, bool?, Paint?)[] expected =
        [
            (Gray16, Gray16, true, Gray16),
            (window, window, true, window),
            (Gray16, Gray16, true, Gray16),
            (White, White, false, White),
        ];
        Assert.Equal(expected, seen);
    }

    /// <summary>
    /// Each window keeps the theme it asked for: another window's request does not reach it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two wrapped apps, both light; the first asks for a dark theme in its root function, the second asks for none.
    ///
    /// Steps:
    /// - Pump both; read their backdrops and title bars.
    ///
    /// Expected:
    /// - The first is #292929 with a dark title bar; the second is white with a light one.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RequestForWindow_TwoWindows_EachKeepsItsOwn()
    {
        Signal<FluentTheme> dark = Signal.Create(FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null));
        await using HeadlessApp first = await HeadlessApp.StartAsync(
            () =>
            {
                FluentTheme.RequestForWindow(dark);
                return Box();
            },
            Options(Light)
        );
        await using HeadlessApp second = await HeadlessApp.StartAsync(() => Box(), Options(Light));
        _ = first.PumpAndSettle();
        _ = second.PumpAndSettle();

        Assert.Equal(((Color?)Gray16, (bool?)true), (first.Backdrop, first.DarkTitleBar));
        Assert.Equal(((Color?)White, (bool?)false), (second.Backdrop, second.DarkTitleBar));
    }

    /// <summary>
    /// A click handler of the window's tree asks for the window's theme as the root function does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, of a button whose click handler asks for a dark theme.
    ///
    /// Steps:
    /// - Click the button; pump; read the backdrop, the title bar, and the button's background.
    ///
    /// Expected:
    /// - #292929, dark, and #292929.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RequestForWindow_FromAClickHandler_ChangesTheWindow()
    {
        Signal<FluentTheme> dark = Signal.Create(FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null));
        Element button = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => button = Fluent.Button(Box().Width(8).Height(8), () => FluentTheme.RequestForWindow(dark)),
            Options(Light)
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(button, PointerButton.Left);
        _ = app.PumpAndSettle();

        Assert.Equal(((Color?)Gray16, (bool?)true, (Paint?)Gray16), (app.Backdrop, app.DarkTitleBar, Visual(app, button).Background));
    }

    /// <summary>
    /// A window with no wrapper of the theme refuses a request, and a part of the theme made there throws; both name the attribute
    /// and the option that provide the theme, and the part's content is left with no parent.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with no root wrapper.
    ///
    /// Steps:
    /// - In the root function: ask for a theme; make a box and a button of it; make a variant of the theme around a box. Return a
    ///   column of the box.
    ///
    /// Expected:
    /// - Three <see cref="InvalidOperationException"/>s, each naming <c>WindowTheme(typeof(FluentRoot))</c> and
    ///   <c>HeadlessOptions.RootWrapper</c>; the box had no parent after the button's throw, and the variant's body did not run.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task NoWrapper_RequestPartAndVariant_ThrowNamingTheAttribute()
    {
        var thrown = new List<InvalidOperationException>();
        bool orphan = false;
        int bodies = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            thrown.Add(Assert.Throws<InvalidOperationException>(() => FluentTheme.RequestForWindow(null)));
            Element content = Box();
            thrown.Add(Assert.Throws<InvalidOperationException>(() => Fluent.Button(content, () => { })));
            orphan = content.Tree!.Nodes.GetRef(content.Node).Parent.IsNone;
            thrown.Add(
                Assert.Throws<InvalidOperationException>(() =>
                    FluentTheme.ProvideVariant(
                        ThemeVariant.Dark,
                        () =>
                        {
                            bodies++;
                            return Box();
                        }
                    )
                )
            );
            return Column(content);
        });
        _ = app.PumpAndSettle();

        Assert.Equal(3, thrown.Count);
        Assert.All(thrown, e => Assert.Contains("WindowTheme(typeof(FluentRoot))", e.Message, StringComparison.Ordinal));
        Assert.All(thrown, e => Assert.Contains("HeadlessOptions.RootWrapper", e.Message, StringComparison.Ordinal));
        Assert.True(orphan);
        Assert.Equal(0, bodies);
    }

    /// <summary>
    /// A theme provided for a part of the tree reaches the parts in it, and the nearest one wins, but it does not reach the window's
    /// background or title bar.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light. Its root function makes a column of a button, and a dark theme provided around a column of a button
    ///   and a high-contrast theme provided around a third button.
    ///
    /// Steps:
    /// - Pump; read the three buttons' backgrounds, the root box's, the backdrop, and the title bar.
    ///
    /// Expected:
    /// - White, #292929, and the OS's window color; the root box and the backdrop white; the title bar light.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Provide_PartOfTheTree_ReachesItsPartsNearestFirst_NotTheWindow()
    {
        Signal<FluentTheme> dark = Signal.Create(FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null));
        Signal<FluentTheme> contrast = Signal.Create(FluentTheme.Create(ThemeVariant.HighContrast, Density.Standard, null, DarkContrast));
        Element outer = default;
        Element inner = default;
        Element innermost = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                outer = Fluent.Button(Box(), () => { });
                Element part = FluentTheme.Current.Provide(
                    dark,
                    () =>
                    {
                        inner = Fluent.Button(Box(), () => { });
                        Element nearest = FluentTheme.Current.Provide(contrast, () => innermost = Fluent.Button(Box(), () => { }));
                        return Column(inner, nearest);
                    }
                );
                return Column(outer, part);
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();

        Assert.Equal(Fill(0xFFFFFF), Visual(app, outer).Background);
        Assert.Equal(Fill(0x292929), Visual(app, inner).Background);
        Assert.Equal(new Paint(DarkContrast.Window), Visual(app, innermost).Background);
        Assert.Equal(Fill(0xFFFFFF), RootBoxVisual(app).Background);
        Assert.Equal(((Color?)White, (bool?)false), (app.Backdrop, app.DarkTitleBar));
    }

    /// <summary>
    /// A variant of the theme around keeps its density and accent color, follows it as it changes, and leaves the window's background
    /// and title bar to the window's theme.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, that asks for a light, compact theme with the measured blue as its accent. Its root function makes a
    ///   dark variant around a body that keeps the theme's signal and makes a button.
    ///
    /// Steps:
    /// - Pump; read the variant's theme, the button's background, the backdrop, and the title bar. Ask for a light, standard theme
    ///   with no accent; pump; read the variant's theme and the button's background.
    ///
    /// Expected:
    /// - First: dark, compact, the blue; the button #292929; the backdrop white and the title bar light. Then: dark, standard, no
    ///   accent; the button #292929.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ProvideVariant_Dark_KeepsDensityAndAccent_AndFollowsTheThemeAround()
    {
        Signal<FluentTheme> window = Signal.Create(FluentTheme.Create(ThemeVariant.Light, Density.Compact, Blue));
        IReadOnlySignal<FluentTheme>? variant = null;
        Element button = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                FluentTheme.RequestForWindow(window);
                return FluentTheme.ProvideVariant(
                    ThemeVariant.Dark,
                    () =>
                    {
                        variant = FluentTheme.Current.Use();
                        return button = Fluent.Button(Box(), () => { });
                    }
                );
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();
        FluentTheme first = variant!.Get();
        Paint? firstBackground = Visual(app, button).Background;
        (Color?, bool?) surface = (app.Backdrop, app.DarkTitleBar);

        window.Set(FluentTheme.Create(ThemeVariant.Light, Density.Standard, null));
        _ = app.PumpAndSettle();
        FluentTheme second = variant.Get();

        Assert.Equal((ThemeVariant.Dark, Density.Compact, (Color?)Blue), (first.Variant, first.Density, first.Accent));
        Assert.Equal(Fill(0x292929), firstBackground);
        Assert.Equal(((Color?)White, (bool?)false), surface);
        Assert.Equal((ThemeVariant.Dark, Density.Standard, (Color?)null), (second.Variant, second.Density, second.Accent));
        Assert.Equal(Fill(0x292929), Visual(app, button).Background);
    }

    /// <summary>
    /// Under a high-contrast theme a variant keeps that theme; a variant of the variant the theme already has is that theme.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two wrapped apps: one in high contrast with the OS's colors, one light. Each root function makes a dark and a light variant,
    ///   whose bodies keep the theme they find, and keeps the window's theme.
    ///
    /// Steps:
    /// - Pump both; read the themes.
    ///
    /// Expected:
    /// - In high contrast both variants are the window's theme instance. In light the light variant is the window's theme instance,
    ///   and the dark one is dark.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ProvideVariant_UnderHighContrastOrTheSameVariant_KeepsTheThemeAround()
    {
        (FluentTheme Window, FluentTheme Dark, FluentTheme Light) contrast = await VariantsOf(HighContrast);
        (FluentTheme Window, FluentTheme Dark, FluentTheme Light) light = await VariantsOf(Light);

        Assert.Same(contrast.Window, contrast.Dark);
        Assert.Same(contrast.Window, contrast.Light);
        Assert.Same(light.Window, light.Light);
        Assert.Equal(ThemeVariant.Dark, light.Dark.Variant);
    }

    /// <summary>
    /// Variants of the same theme around in the same variant are one instance; a group of tokens the theme around replaced with
    /// <see langword="with"/> is not carried into another variant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app that asks for a light theme whose borderRadiusMedium is replaced with 8. Its root function makes two dark
    ///   variants side by side, whose bodies keep the theme they find.
    ///
    /// Steps:
    /// - Pump; read the two themes.
    ///
    /// Expected:
    /// - The same instance, dark, with borderRadiusMedium 4 (the token's value, global/borderRadius.ts).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ProvideVariant_SameThemeAndVariant_IsOneInstance_WithoutTheReplacedTokens()
    {
        FluentTheme light = FluentTheme.Create(ThemeVariant.Light, Density.Standard, null);
        Signal<FluentTheme> rounder = Signal.Create(light with { Radii = light.Radii with { BorderRadiusMedium = 8 } });
        FluentTheme? left = null;
        FluentTheme? right = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                FluentTheme.RequestForWindow(rounder);
                Element a = FluentTheme.ProvideVariant(ThemeVariant.Dark, () => Keep(theme => left = theme));
                Element b = FluentTheme.ProvideVariant(ThemeVariant.Dark, () => Keep(theme => right = theme));
                return Row(a, b);
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();

        Assert.NotNull(left);
        Assert.Same(left, right);
        Assert.Equal((ThemeVariant.Dark, 4f), (left.Variant, left.Radii.BorderRadiusMedium));
    }

    /// <summary>
    /// A variant is light or dark: high contrast and a value out of the enum are refused, as a missing body is, before the body runs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light.
    ///
    /// Steps:
    /// - In the root function, make a variant of high contrast, of <c>(ThemeVariant)3</c>, and with no body.
    ///
    /// Expected:
    /// - <see cref="ArgumentOutOfRangeException"/> twice, naming <c>variant</c>, then <see cref="ArgumentNullException"/>; no body
    ///   ran.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ProvideVariant_HighContrastOrNoBody_Throws()
    {
        int bodies = 0;
        var names = new List<string?>();
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element Body()
                {
                    bodies++;
                    return Box();
                }

                ArgumentException contrast = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    FluentTheme.ProvideVariant(ThemeVariant.HighContrast, Body)
                );
                ArgumentException outOfEnum = Assert.Throws<ArgumentOutOfRangeException>(() => FluentTheme.ProvideVariant((ThemeVariant)3, Body));
                ArgumentException noBody = Assert.Throws<ArgumentNullException>(() => FluentTheme.ProvideVariant(ThemeVariant.Dark, null!));
                names.AddRange([contrast.ParamName, outOfEnum.ParamName, noBody.ParamName]);
                return Box();
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();

        Assert.Equal(["variant", "variant", "body"], names);
        Assert.Equal(0, bodies);
    }

    /// <summary>
    /// A popup's content takes the theme where the popup is declared, not the window's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, whose root function declares, inside a dark variant, a popup on a box whose content is a button.
    ///
    /// Steps:
    /// - Open the popup; pump; read the button's background.
    ///
    /// Expected:
    /// - #292929, the dark theme's.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ProvideVariant_PopupContent_TakesTheThemeWhereItIsDeclared()
    {
        Signal<bool> open = Signal.Create(false);
        Element inPopup = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                FluentTheme.ProvideVariant(
                    ThemeVariant.Dark,
                    () => Box().Width(20).Height(20).Popup(open, () => inPopup = Fluent.Button(Box().Width(8).Height(8), () => { }))
                ),
            Options(Light)
        );
        _ = app.PumpAndSettle();

        open.Set(true);
        _ = app.PumpAndSettle();

        Assert.Equal(Fill(0x292929), Visual(app, inPopup).Background);
    }

    // The window's theme and its dark and light variants, read in a wrapped app with the OS's appearance given.
    private static async Task<(FluentTheme Window, FluentTheme Dark, FluentTheme Light)> VariantsOf(SystemAppearance appearance)
    {
        FluentTheme? window = null;
        FluentTheme? dark = null;
        FluentTheme? light = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                window = FluentTheme.Current.Use().Get();
                Element a = FluentTheme.ProvideVariant(ThemeVariant.Dark, () => Keep(theme => dark = theme));
                Element b = FluentTheme.ProvideVariant(ThemeVariant.Light, () => Keep(theme => light = theme));
                return Row(a, b);
            },
            Options(appearance)
        );
        _ = app.PumpAndSettle();
        return (window!, dark!, light!);
    }

    // A box made where a theme is provided, which hands the theme found there to keep.
    private static Element Keep(Action<FluentTheme> keep)
    {
        keep(FluentTheme.Current.Use().Get());
        return Box();
    }

    private static (Paint? BoxBackground, Color? Backdrop, bool? DarkTitleBar, Paint? ButtonBackground) Read(HeadlessApp app, Element button) =>
        (RootBoxVisual(app).Background, app.Backdrop, app.DarkTitleBar, Visual(app, button).Background);
}
