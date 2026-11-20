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
    /// Gets the CLDR tables of the locales the app supports, such as <c>[CldrLocales.En, CldrLocales.Ja]</c> (design §4.11, "Where
    /// locales are declared"): the window's locale is the one of their locales that fits the user's languages best, and its tables
    /// are the ones the formats use (<see cref="Localization.Data"/>). Only the tables named somewhere stay in a NativeAOT binary.
    /// With none, the window's locale is the first of the user's languages, with the tables of <see cref="LocaleData.Root"/>.
    /// </summary>
    public IReadOnlyList<LocaleData> SupportedLocales { get; init; } = [];

    /// <summary>
    /// Gets what chooses the window's locale from the user's languages, most preferred first, and the locales of
    /// <see cref="SupportedLocales"/> in their order; <see langword="null"/> for <see cref="Localization.Resolve"/>. A function that
    /// returns one locale whatever it is given fixes the locale. The tables are the supported ones of the locale returned; else the
    /// first supported ones of its language and likely script; else <see cref="LocaleData.Root"/>; any of them under the returned
    /// locale's tag (design §4.11, "When there is no table"). The function does not change the lists it is given: the second is the
    /// window's own.
    /// </summary>
    public Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? ResolveLocale { get; init; }
}
