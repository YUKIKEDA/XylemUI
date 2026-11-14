namespace XylemUI.Theme.Fluent;

/// <summary>
/// The colors a <see cref="FluentTheme"/> is made for (design §13.2, "テーマの値").
/// </summary>
public enum ThemeVariant
{
    /// <summary>
    /// Dark text on light backgrounds, Fluent's light alias colors.
    /// </summary>
    Light,

    /// <summary>
    /// Light text on dark backgrounds, Fluent's dark alias colors.
    /// </summary>
    Dark,

    /// <summary>
    /// Fluent's high-contrast alias colors, with the eight system colors of the OS in place of Fluent's.
    /// </summary>
    HighContrast,
}
