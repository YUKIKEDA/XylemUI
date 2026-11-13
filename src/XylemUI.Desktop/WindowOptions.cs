namespace XylemUI;

/// <summary>
/// The window <see cref="XylemApp.Run"/> opens (design §2.13).
/// </summary>
/// <param name="Title">The title bar text.</param>
/// <param name="Width">The width of the client area, in logical pixels: times the display's scale on the screen.</param>
/// <param name="Height">The height of the client area, in logical pixels.</param>
public sealed record WindowOptions(string Title = "XylemUI", float Width = 800, float Height = 600)
{
    /// <summary>
    /// Gets the locales the app supports (design §4.11, "Resolution"): the window's locale is the one of them that fits the
    /// user's languages best. With none, the window's locale is the first of the user's languages.
    /// </summary>
    public IReadOnlyList<Locale> SupportedLocales { get; init; } = [];

    /// <summary>
    /// Gets what chooses the window's locale from the user's languages, most preferred first, and
    /// <see cref="SupportedLocales"/>; <see langword="null"/> for <see cref="Localization.Resolve"/>. A function that returns one
    /// locale whatever it is given fixes the locale.
    /// </summary>
    public Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? ResolveLocale { get; init; }
}
