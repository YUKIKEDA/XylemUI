using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The locale of a part of the screen (design §4.11): the keys the resolved locale, its CLDR tables, and the OS's 24-hour setting
/// are provided under, the resolution of the locales an app supports against the languages the user prefers, and the direction a
/// locale is written in.
/// </summary>
public static class Localization
{
    // The locale signal made for each signal of tables that Provide was given: providing the same signal again gives the same locale
    // signal, so the text of the default language keeps one effect for it (design §4.11, "How text is shaped again").
    private static readonly ConditionalWeakTable<IReadOnlySignal<LocaleData>, IReadOnlySignal<Locale>> LocaleSignals = new();

    /// <summary>
    /// Gets the key of the locale (design §2.13): a window provides the locale of its tables (<see cref="Data"/>) at its root, and a
    /// part of the screen that is wrapped in another value has that one. The key has no default, so a lookup outside a window
    /// throws.
    /// </summary>
    public static Context<IReadOnlySignal<Locale>> Locale { get; } = new();

    /// <summary>
    /// Gets the key of the CLDR tables the formats use (design §4.11, "The key of the tables"): a window provides the tables of its
    /// resolved locale at its root, and <see cref="Provide"/> provides other tables to a part of the screen. Where the two keys come
    /// from one signal, the <see cref="LocaleData.Locale"/> of the tables is the value of <see cref="Locale"/>; a part wrapped in
    /// <see cref="Locale"/> alone keeps the tables from outside it. The key has no default, so a lookup outside a window throws.
    /// </summary>
    public static Context<IReadOnlySignal<LocaleData>> Data { get; } = new();

    /// <summary>
    /// Gets the key of whether the OS asks for times in 24-hour form (design §4.11, "The key of the 24-hour form"): a window provides
    /// it at its root, from Windows' time format on the desktop and from <c>HeadlessOptions.Use24HourFormat</c> in a headless app,
    /// and the value changes when the OS's setting does. The key has no default, so a lookup outside a window throws.
    /// </summary>
    public static Context<IReadOnlySignal<bool>> Use24HourFormat { get; } = new();

    /// <summary>
    /// Runs <paramref name="content"/> with <paramref name="data"/> provided under <see cref="Data"/> and the locale of its tables
    /// under <see cref="Locale"/>, so that the part of the screen it makes has another locale for its text and its formats alike
    /// (design §4.11, "Replacing"). An app whose user chooses the language wraps its root function in a signal of its own. A reader
    /// of <see cref="Locale"/> runs again only when the locale of the new tables is another.
    /// </summary>
    /// <param name="data">The tables, such as one of the app's supported locales.</param>
    /// <param name="content">The code that makes the part of the screen.</param>
    /// <returns>The element <paramref name="content"/> returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, or a computed value is computing.
    /// </exception>
    [UiThreadOnly]
    public static Element Provide(IReadOnlySignal<LocaleData> data, Func<Element> content)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(content);
        IReadOnlySignal<Locale> locale = LocaleSignals.GetValue(data, static tables => Computed.Create(() => tables.Get().Locale));
        return Data.Provide(data, () => Locale.Provide(locale, content, nameof(Provide)), nameof(Provide));
    }

    /// <summary>
    /// Returns the locale to use: the one of <paramref name="supported"/> that fits the user's languages best, as Flutter's
    /// <c>basicLocaleListResolution</c> chooses it. For each preferred language in order: a locale equal in language, script, and
    /// region; then one equal in language and script, when the preferred one has a script; then one equal in language and region,
    /// when it has a region; then a locale found equal in language alone for an earlier preferred language; then one equal in
    /// language alone, which is returned at once only for the first preferred language when the second is not the same language.
    /// After the last: the language-only match, the first locale found equal in region alone, or the first supported locale.
    /// </summary>
    /// <remarks>
    /// The steps that compare the language and the region, and the language alone, first look for a supported locale whose likely
    /// script is the preferred one's, in that order, and take one of another script only when neither step finds one (design §4.11,
    /// "Adding the script a region implies"): with <c>zh-TW</c> preferred, <c>zh-Hant</c> is taken before <c>zh-Hans</c> and before
    /// <c>zh-Hans-TW</c>, and <c>zh-Hans</c> still before the first supported locale. A likely script is the one the locale writes,
    /// or else the one CLDR's likely subtags give its language and region; when the preferred language has none, the steps are
    /// those of the summary. The steps that compare written fields (all three, the language and the script, the region alone) do not
    /// read likely scripts, and the locale returned is the supported one, without the fields the likely subtags would add.
    /// </remarks>
    /// <param name="preferred">The user's languages, most preferred first.</param>
    /// <param name="supported">The locales the app supports; among several that fit equally, the earlier is taken.</param>
    /// <returns>
    /// A locale of <paramref name="supported"/>; when that is empty, the first of <paramref name="preferred"/>, or the
    /// undetermined locale when both are empty.
    /// </returns>
    public static Locale Resolve(IReadOnlyList<Locale> preferred, IReadOnlyList<Locale> supported)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        ArgumentNullException.ThrowIfNull(supported);
        if (supported.Count == 0)
        {
            return preferred.Count == 0 ? default : preferred[0];
        }

        if (preferred.Count == 0)
        {
            return supported[0];
        }

        Locale? languageMatch = null;
        Locale? regionMatch = null;
        for (int i = 0; i < preferred.Count; i++)
        {
            Locale user = preferred[i];
            uint userScript = LikelyScripts.Infer(user);
            if (First(supported, user, Fit.All) is { } all)
            {
                return all;
            }

            if (user.Script is not null && First(supported, user, Fit.LanguageAndScript) is { } script)
            {
                return script;
            }

            if (user.Region is not null && FirstOfScript(supported, user, Fit.LanguageAndRegion, userScript) is { } region)
            {
                return region;
            }

            // A locale of the language in the user's likely script comes before one of the region in another script: with zh-TW
            // preferred, zh-Hant is taken before zh-Hans-TW. It is held as every language-only match is (below).
            Locale? sameScript = FirstOfScript(supported, user, Fit.Language, userScript);
            if (sameScript is null && user.Region is not null && First(supported, user, Fit.LanguageAndRegion) is { } otherScript)
            {
                return otherScript;
            }

            if (languageMatch is not null)
            {
                return languageMatch.Value;
            }

            if ((sameScript ?? First(supported, user, Fit.Language)) is { } language)
            {
                languageMatch = language;
                if (i == 0 && !(preferred.Count > 1 && preferred[1].Language == user.Language))
                {
                    return language;
                }
            }

            if (regionMatch is null && user.Region is not null)
            {
                regionMatch = First(supported, user, Fit.Region);
            }
        }

        return languageMatch ?? regionMatch ?? supported[0];
    }

    /// <summary>
    /// Returns the direction a locale's language is written in by default: right to left for Arabic, Persian, Hebrew, Pashto,
    /// Sindhi, and Urdu, and left to right for every other language.
    /// </summary>
    /// <param name="locale">The locale.</param>
    /// <returns>The direction.</returns>
    public static Direction DirectionOf(Locale locale) =>
        locale.Language is "ar" or "fa" or "he" or "ps" or "sd" or "ur" ? Direction.Rtl : Direction.Ltr;

    // The first supported locale that fits the user's in the given fields and has the user's likely script; none when the user's
    // likely script is not known (0). The list is walked each time: an app supports tens of locales, and the resolution runs when a
    // window opens and when the OS's languages change.
    private static Locale? FirstOfScript(IReadOnlyList<Locale> supported, Locale user, Fit fit, uint script)
    {
        if (script != 0)
        {
            for (int i = 0; i < supported.Count; i++)
            {
                Locale candidate = supported[i];
                if (Fits(candidate, user, fit) && LikelyScripts.Infer(candidate) == script)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    // The first supported locale that fits the user's in the given fields.
    private static Locale? First(IReadOnlyList<Locale> supported, Locale user, Fit fit)
    {
        for (int i = 0; i < supported.Count; i++)
        {
            Locale candidate = supported[i];
            if (Fits(candidate, user, fit))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool Fits(Locale candidate, Locale user, Fit fit) =>
        fit switch
        {
            Fit.All => candidate == user,
            Fit.LanguageAndScript => candidate.Language == user.Language && candidate.Script == user.Script,
            Fit.LanguageAndRegion => candidate.Language == user.Language && candidate.Region == user.Region,
            Fit.Language => candidate.Language == user.Language,
            _ => candidate.Region == user.Region,
        };

    private enum Fit
    {
        All,
        LanguageAndScript,
        LanguageAndRegion,
        Language,
        Region,
    }
}
