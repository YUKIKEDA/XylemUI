namespace XylemUI.Theme.Fluent;

/// <summary>
/// The root wrapper of the Fluent theme (design §13.2, "根で置くもの"): an app chooses it with
/// <c>[assembly: WindowTheme(typeof(FluentRoot))]</c>, and a test with <c>HeadlessOptions.RootWrapper = FluentRoot.Wrap</c>.
/// </summary>
public static class FluentRoot
{
    /// <summary>
    /// Gets the key of what the app asked for as the window's theme with <see cref="FluentTheme.RequestForWindow"/>: each window's
    /// wrapper provides a signal of its own, which holds <see langword="null"/> while the app asks for none.
    /// </summary>
    internal static Context<Signal<IReadOnlySignal<FluentTheme>?>> Request { get; } = new();

    /// <summary>
    /// Wraps a window's root function (design §13.2, "FluentRoot.Wrap がすること"): provides the window's theme under
    /// <see cref="FluentTheme.Current"/> around it, and puts its node in a box that fills the window. The window's theme is the one
    /// the app asked for with <see cref="FluentTheme.RequestForWindow"/>, or else the one made from the OS's appearance
    /// (<see cref="SystemAppearance.Current"/>): high contrast with the OS's colors while it is on, else dark or light, with the
    /// OS's accent color and <see cref="Density.Standard"/>. The box takes the theme's background and color of text, and the
    /// window's surface (<see cref="WindowSurface.Current"/>) is told the background and whether the title bar is dark, each time
    /// the theme changes.
    /// </summary>
    /// <param name="root">The function that runs the window's root function; it is called once.</param>
    /// <returns>The box.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">It runs outside a window's root, where the OS's appearance and the window's
    /// surface are not provided.</exception>
    [UiThreadOnly]
    public static Element Wrap(Func<Element> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        IReadOnlySignal<SystemAppearance> system = SystemAppearance.Current.Use();
        WindowSurface surface = WindowSurface.Current.Use();
        Signal<IReadOnlySignal<FluentTheme>?> request = Signal.Create<IReadOnlySignal<FluentTheme>?>(null);

        // Two computed values, so that going back from the app's theme to the OS's finds the theme made before, and the OS's
        // changes are not read while the app's theme is shown.
        Computed<FluentTheme> fromSystem = Computed.Create(() => FromSystem(system.Get()));
        Computed<FluentTheme> theme = Computed.Create(() => request.Get() is { } asked ? asked.Get() : fromSystem.Get());
        return Request.Provide(request, () => FluentTheme.Current.Provide(theme, () => RootBox(root(), theme, surface)));
    }

    /// <summary>
    /// Returns whether a color reads as dark, by the negation of <c>IsColorLight</c> in Microsoft's "Support Dark and Light themes
    /// in Win32 apps" (https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes), which reads a
    /// color as light when <c>((5 * G) + (2 * R) + B) &gt; (8 * 128)</c> (design §13.2, "題名の棒"): the title bar of a
    /// high-contrast theme is dark when its window color is.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns><see langword="true"/> when <c>5 G + 2 R + B</c> is at most 8 × 128, 1,024 itself included.</returns>
    internal static bool IsDark(Color color) => (5 * color.G) + (2 * color.R) + color.B <= 8 * 128;

    // The theme made from the OS's appearance (design §13.2, "OS の見た目からテーマを選ぶ").
    private static FluentTheme FromSystem(SystemAppearance appearance)
    {
        if (appearance.HighContrast)
        {
            return FluentTheme.Create(ThemeVariant.HighContrast, Density.Standard, appearance.Accent, appearance.Contrast);
        }

        ThemeVariant variant = appearance.Scheme == ColorScheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        return FluentTheme.Create(variant, Density.Standard, appearance.Accent);
    }

    // The box that fills the window, and its one effect, which follows the theme as a part does (design §13.2, "根の箱" and "題名の
    // 棒"). The background and the color of text are those of Fluent's provider (useFluentProviderStyles.styles.ts lines 15 and 16).
    private static Element RootBox(Element content, Computed<FluentTheme> theme, WindowSurface surface)
    {
        Element box = Elements.Box(content).WidthFraction(1).HeightFraction(1);
        _ = Effect.Create(() =>
        {
            FluentTheme value = theme.Get();
            Color background = value.Colors.ColorNeutralBackground1;
            _ = box.Background(background).Foreground(value.Colors.ColorNeutralForeground1);
            bool dark = value.Variant == ThemeVariant.Dark || (value.Variant == ThemeVariant.HighContrast && IsDark(background));
            surface.SetBackdrop(background);
            surface.SetDarkTitleBar(dark);
        });
        return box;
    }
}
