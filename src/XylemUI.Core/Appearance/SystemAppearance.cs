namespace XylemUI;

/// <summary>
/// What the OS says about how the user wants the window to look (design §13.2, "OS の見た目"): light or dark, whether high
/// contrast is on, the accent color, and the colors of the high-contrast theme. A window provides it at its root, and a theme reads it
/// there to choose its own values. The default value is light, with high contrast off and no accent color: what a host that does
/// not read the OS gives.
/// </summary>
/// <param name="Scheme">Whether the OS shows light or dark colors.</param>
/// <param name="HighContrast">Whether high contrast is on.</param>
/// <param name="Accent">The OS's accent color; <see langword="null"/> when it cannot be read.</param>
/// <param name="Contrast">
/// The colors the user chose for high contrast while <paramref name="HighContrast"/> is on; <see langword="null"/> while it is off.
/// A host refuses a value where only one of the two says high contrast is on (<see cref="ArgumentException"/>).
/// </param>
public readonly record struct SystemAppearance(ColorScheme Scheme, bool HighContrast, Color? Accent, HighContrastColors? Contrast)
{
    /// <summary>
    /// Gets the key of the OS's appearance (design §2.13): a window provides a signal of it at its root, which changes when the OS's
    /// appearance does. The key has no default, so a lookup outside a window throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static Context<IReadOnlySignal<SystemAppearance>> Current { get; } = new();
}
