namespace XylemUI;

/// <summary>
/// The locale of a part of the screen (design §4.11): the key the resolved locale is provided under, the resolution of the
/// locales an app supports against the languages the user prefers, and the direction a locale is written in.
/// </summary>
public static class Localization
{
    /// <summary>
    /// Gets the key of the locale (design §2.13): a window provides the resolved locale at its root, and a part of the screen
    /// that is wrapped in another value has that one. The key has no default, so a lookup outside a window throws.
    /// </summary>
    public static Context<IReadOnlySignal<Locale>> Locale { get; } = new();

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
    /// script is the preferred one's, and take one of another script only when there is none (design §4.11, "Adding the script a
    /// region implies"): with <c>zh-TW</c> preferred, <c>zh-Hant</c> is taken before <c>zh-Hans</c>, and <c>zh-Hans</c> still before
    /// the first supported locale. A likely script is the one the locale writes, or else the one CLDR's likely subtags give its
    /// language and region. The steps that compare written fields (all three, the language and the script, the region alone) do not
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

            if (user.Region is not null && First(supported, user, Fit.LanguageAndRegion, userScript) is { } region)
            {
                return region;
            }

            if (languageMatch is not null)
            {
                return languageMatch.Value;
            }

            if (First(supported, user, Fit.Language, userScript) is { } language)
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

    // The first supported locale that fits the user's in the given fields and has the user's likely script, or else the first that
    // fits in the given fields; with no script (0), the first that fits. The list is walked each time: an app supports tens of
    // locales, and the resolution runs when a window opens and when the OS's languages change.
    private static Locale? First(IReadOnlyList<Locale> supported, Locale user, Fit fit, uint script)
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

        return First(supported, user, fit);
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
