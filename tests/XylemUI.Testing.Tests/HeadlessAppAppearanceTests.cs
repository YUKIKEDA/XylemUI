using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// What the headless app provides at a window's root for a theme (row TH1, design §13.2, "ヘッドレス" and "ヘッドレスとテスト"): the
// OS's appearance from its options and SetSystemAppearance, the wrapper of its options and never the process's, and the values the
// wrapper tells the window's surface. The trees here hold no text, so these tests run where HarfBuzz is not built.
public sealed class HeadlessAppAppearanceTests
{
    private static readonly Color LightBackdrop = Color.FromRgb(0xFAFAFA);
    private static readonly Color DarkBackdrop = Color.FromRgb(0x292929);

    private static readonly HighContrastColors Black = new(
        Color.FromRgb(0x000000),
        Color.FromRgb(0xFFFFFF),
        Color.FromRgb(0x75E9FC),
        Color.FromRgb(0xA6A6A6),
        Color.FromRgb(0x8EE3F0),
        Color.FromRgb(0x263B50),
        Color.FromRgb(0x000000),
        Color.FromRgb(0xFFFFFF)
    );

    /// <summary>
    /// With the defaults of the options, the root function finds the appearance of a host that does not read the OS: light, with
    /// high contrast off and no accent color; with an appearance in the options, it finds that one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One app with default options, and one whose options give dark with high contrast on and the black theme's colors.
    ///
    /// Steps:
    /// - Read the OS's appearance in each app's root function.
    ///
    /// Expected:
    /// - The default value, then the value of the options.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SystemAppearance_IsTheOptions_LightByDefault()
    {
        var contrast = new SystemAppearance(ColorScheme.Dark, true, Color.FromRgb(0xA1BFDE), Black);
        SystemAppearance? plain = null;
        SystemAppearance? given = null;
        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(() =>
            {
                plain = SystemAppearance.Current.Use().Get();
                return Box();
            })
        )
        {
            _ = app.PumpAndSettle();
        }

        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(
                () =>
                {
                    given = SystemAppearance.Current.Use().Get();
                    return Box();
                },
                new HeadlessOptions { SystemAppearance = contrast }
            )
        )
        {
            _ = app.PumpAndSettle();
        }

        Assert.Equal(new SystemAppearance(ColorScheme.Light, false, null, null), plain);
        Assert.Equal(contrast, given);
    }

    /// <summary>
    /// A change of the OS's appearance runs again what reads it, and only that, and only when the value changed: the root function
    /// and an effect that reads another signal do not run again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app whose root function counts its runs, makes an effect that records the scheme it reads from the OS's appearance, and
    ///   an effect that counts its runs and reads another signal.
    ///
    /// Steps:
    /// - Set the OS's appearance to the default again, to dark, to dark again, then to light; pump after each.
    ///
    /// Expected:
    /// - The reader recorded light, dark, light: the default and the second dark ran nothing. The root function ran once and the
    ///   other effect once.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SetSystemAppearance_ChangedValue_RunsOnlyItsReaders()
    {
        var dark = new SystemAppearance(ColorScheme.Dark, false, null, null);
        Signal<int> other = Signal.Create(0);
        var schemes = new List<ColorScheme>();
        int roots = 0;
        int others = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            roots++;
            IReadOnlySignal<SystemAppearance> appearance = SystemAppearance.Current.Use();
            _ = Effect.Create(() => schemes.Add(appearance.Get().Scheme));
            _ = Effect.Create(() =>
            {
                _ = other.Get();
                others++;
            });
            return Box();
        });
        _ = app.PumpAndSettle();

        app.SetSystemAppearance(default);
        _ = app.PumpAndSettle();
        app.SetSystemAppearance(dark);
        _ = app.PumpAndSettle();
        app.SetSystemAppearance(dark);
        _ = app.PumpAndSettle();
        app.SetSystemAppearance(new SystemAppearance(ColorScheme.Light, false, null, null));
        _ = app.PumpAndSettle();

        ColorScheme[] expected = [ColorScheme.Light, ColorScheme.Dark, ColorScheme.Light];
        Assert.Equal(expected, schemes);
        Assert.Equal(1, roots);
        Assert.Equal(1, others);
    }

    /// <summary>
    /// An appearance whose high contrast and high-contrast colors disagree is refused by the options before the app starts, and by
    /// <see cref="HeadlessApp.SetSystemAppearance"/>, which then leaves the appearance as it was.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The two values that disagree: high contrast on with no colors, and off with the black theme's colors. An app with default
    ///   options whose root function keeps the OS's appearance's signal.
    ///
    /// Steps:
    /// - Start an app with each value in the options. Set each value on the running app.
    ///
    /// Expected:
    /// - Each start throws <see cref="ArgumentException"/> naming <c>options</c>, before the root function runs; each set throws
    ///   <see cref="ArgumentException"/>, and the appearance is still the default.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ContrastColors_DisagreeingWithHighContrast_AreRefused()
    {
        SystemAppearance[] wrong =
        [
            new SystemAppearance(ColorScheme.Dark, true, null, null),
            new SystemAppearance(ColorScheme.Dark, false, null, Black),
        ];
        int started = 0;
        foreach (SystemAppearance value in wrong)
        {
            ArgumentException thrown = await Assert.ThrowsAsync<ArgumentException>(() =>
                HeadlessApp.StartAsync(
                    () =>
                    {
                        started++;
                        return Box();
                    },
                    new HeadlessOptions { SystemAppearance = value }
                )
            );
            Assert.Equal("options", thrown.ParamName);
        }

        IReadOnlySignal<SystemAppearance>? appearance = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            appearance = SystemAppearance.Current.Use();
            return Box();
        });
        _ = app.PumpAndSettle();
        foreach (SystemAppearance value in wrong)
        {
            _ = Assert.Throws<ArgumentException>(() => app.SetSystemAppearance(value));
        }

        Assert.Equal(0, started);
        Assert.Equal(new SystemAppearance(ColorScheme.Light, false, null, null), appearance!.Get());
    }

    /// <summary>
    /// The options' wrapper runs inside the locale, the OS's appearance, and the window's surface, is given the app's root
    /// function, and its node is the window's: the root function runs inside it and finds the same values.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app that supports en and ja, with the OS's language "ja-JP" and a dark appearance. Its wrapper records the locale, the
    ///   scheme, and whether a surface is provided, calls the root function, and returns a box that holds the root's node. The root
    ///   function records the same and returns a box.
    ///
    /// Steps:
    /// - Start the app.
    ///
    /// Expected:
    /// - The log is "wrap ja dark surface", "root ja dark surface", "wrapped": the root ran inside the wrapper. The window's node
    ///   holds the wrapper's box, and that box holds the root's box.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task RootWrapper_Given_RunsInsideTheRootValuesAroundTheRoot()
    {
        var log = new List<string>();
        Element outer = default;
        Element inner = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                log.Add("root " + Seen());
                return inner = Box();
            },
            new HeadlessOptions
            {
                SupportedLocales = [Locale.Parse("en"), Locale.Parse("ja")],
                SystemLocales = ["ja-JP"],
                SystemAppearance = new SystemAppearance(ColorScheme.Dark, false, null, null),
                RootWrapper = root =>
                {
                    log.Add("wrap " + Seen());
                    outer = Box(root());
                    log.Add("wrapped");
                    return outer;
                },
            }
        );
        _ = app.PumpAndSettle();

        string[] expected = ["wrap ja dark surface", "root ja dark surface", "wrapped"];
        Assert.Equal(expected, log);
        Assert.Equal(app.RootNode, app.Tree.Nodes.GetRef(outer.Node).Parent);
        Assert.Equal(outer.Node, app.Tree.Nodes.GetRef(inner.Node).Parent);

        static string Seen() =>
            Localization.Locale.Use().Get().ToLanguageTag()
            + (SystemAppearance.Current.Use().Get().Scheme == ColorScheme.Dark ? " dark" : " light")
            + (WindowSurface.Current.TryUse(out _) ? " surface" : " no surface");
    }

    /// <summary>
    /// The headless app never takes the process's wrapper (<see cref="WindowRoot.SetWrapper"/>): with no wrapper in the options the
    /// root function's node is the window's, and with one only the options' runs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The process's wrapper is set to one that counts its runs; no other test of this project sets it, since it cannot be put
    ///   back. The test project is an executable, as an app is, so a wrapper the source generator set would stay there too
    ///   (design §13.2, "ヘッドレスとテスト").
    ///
    /// Steps:
    /// - Start an app with default options. Start one whose options' wrapper counts its runs and returns a box around the root's.
    ///
    /// Expected:
    /// - The process's wrapper never ran. In the first app the root's node is the window's; in the second the options' wrapper ran
    ///   once.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task StartAsync_TheProcessWrapper_IsNotRead()
    {
        int processWraps = 0;
        int optionWraps = 0;
        WindowRoot.SetWrapper(root =>
        {
            processWraps++;
            return root();
        });
        Element plain = default;
        await using (HeadlessApp app = await HeadlessApp.StartAsync(() => plain = Box()))
        {
            _ = app.PumpAndSettle();
            Assert.Equal(app.RootNode, app.Tree.Nodes.GetRef(plain.Node).Parent);
        }

        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(
                Box,
                new HeadlessOptions
                {
                    RootWrapper = root =>
                    {
                        optionWraps++;
                        return Box(root());
                    },
                }
            )
        )
        {
            _ = app.PumpAndSettle();
        }

        Assert.Equal(0, processWraps);
        Assert.Equal(1, optionWraps);
    }

    /// <summary>
    /// The headless host shows the backdrop and the title bar the wrapper told the window's surface last, none before any, and
    /// follows the wrapper's effect when the OS's appearance changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with no wrapper. An app whose wrapper makes an effect that reads the OS's appearance and tells the surface the
    ///   backdrop #292929 and a dark title bar when it is dark, and #FAFAFA and a light title bar when it is light.
    ///
    /// Steps:
    /// - Read the first app's values. Read the second's after its start; set the OS's appearance to dark and read them; set it to
    ///   light and read them.
    ///
    /// Expected:
    /// - The first app: none and none. The second: (#FAFAFA, light), (#292929, dark), (#FAFAFA, light).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task BackdropAndDarkTitleBar_WrapperTellsTheSurface_ShowTheLastValues()
    {
        (Color? Backdrop, bool? Dark) none;
        await using (HeadlessApp app = await HeadlessApp.StartAsync(Box))
        {
            _ = app.PumpAndSettle();
            none = (app.Backdrop, app.DarkTitleBar);
        }

        var seen = new List<(Color? Backdrop, bool? Dark)>();
        await using HeadlessApp themed = await HeadlessApp.StartAsync(Box, new HeadlessOptions { RootWrapper = Wrap });
        _ = themed.PumpAndSettle();
        seen.Add((themed.Backdrop, themed.DarkTitleBar));
        themed.SetSystemAppearance(new SystemAppearance(ColorScheme.Dark, false, null, null));
        _ = themed.PumpAndSettle();
        seen.Add((themed.Backdrop, themed.DarkTitleBar));
        themed.SetSystemAppearance(default);
        _ = themed.PumpAndSettle();
        seen.Add((themed.Backdrop, themed.DarkTitleBar));

        Assert.Null(none.Backdrop);
        Assert.Null(none.Dark);
        (Color? Backdrop, bool? Dark)[] expected = [(LightBackdrop, false), (DarkBackdrop, true), (LightBackdrop, false)];
        Assert.Equal(expected, seen);

        static Element Wrap(Func<Element> root)
        {
            IReadOnlySignal<SystemAppearance> appearance = SystemAppearance.Current.Use();
            WindowSurface surface = WindowSurface.Current.Use();
            _ = Effect.Create(() =>
            {
                bool dark = appearance.Get().Scheme == ColorScheme.Dark;
                surface.SetBackdrop(dark ? DarkBackdrop : LightBackdrop);
                surface.SetDarkTitleBar(dark);
            });
            return root();
        }
    }
}
