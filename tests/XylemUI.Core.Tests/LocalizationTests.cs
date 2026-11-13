namespace XylemUI.Tests;

// The resolution of the supported locales against the user's languages, and the direction of a locale (LC1, design §4.11,
// "Resolution" and "The direction of writing"). The resolution is the body of Flutter's basicLocaleListResolution.
public sealed class LocalizationTests
{
    /// <summary>
    /// The steps of the resolution, each with the lists that reach it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The user's languages and the supported locales of each row, as comma-separated tags.
    ///
    /// Steps:
    /// - Resolve.
    ///
    /// Expected:
    /// - The row's tag: the user's list empty gives the first supported; all three fields equal; language and script; language
    ///   and region; a language-only match of the first language returned at once when the second is another language, and held
    ///   back for a better match of the second when that is the same language; a language-only match of an earlier language when
    ///   a later one fits no better; a region-only match after no language fits; the first supported when nothing fits; among
    ///   several that fit equally, the earlier supported one.
    /// </remarks>
    /// <param name="preferred">The user's languages.</param>
    /// <param name="supported">The supported locales.</param>
    /// <param name="expected">The resolved locale.</param>
    [Theory]
    [InlineData("", "en,ja", "en")]
    [InlineData("zh-Hant-TW", "zh,zh-Hant-TW", "zh-Hant-TW")]
    [InlineData("zh-Hant-HK", "zh-Hans,zh-Hant", "zh-Hant")]
    [InlineData("pt-PT", "pt-BR,pt-PT", "pt-PT")]
    [InlineData("de-AT,en-US", "de-DE,en-US", "de-DE")]
    [InlineData("fr-CA,fr-FR", "fr,fr-FR", "fr-FR")]
    [InlineData("fr-CA,fr-BE", "fr,en", "fr")]
    [InlineData("fr-CA,fr-BE,en-US", "fr,en-US", "fr")]
    [InlineData("ko-KR,ja-JP", "en,ja", "ja")]
    [InlineData("nl-BE,de-CH", "fr-BE,it-CH", "fr-BE")]
    [InlineData("ko-KR", "en,ja", "en")]
    [InlineData("es-MX", "es-ES,es-AR", "es-ES")]
    public void Resolve_FollowsTheSteps(string preferred, string supported, string expected) =>
        Assert.Equal(Locale.Parse(expected), Localization.Resolve(List(preferred), List(supported)));

    /// <summary>
    /// With no supported locale, the resolution gives the first of the user's languages, or the undetermined locale when the user
    /// has none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No supported locale.
    ///
    /// Steps:
    /// - Resolve against "ja-JP, en-US", and against no language.
    ///
    /// Expected:
    /// - "ja-JP", and the default locale, whose tag is "und".
    /// </remarks>
    [Fact]
    public void Resolve_NothingSupported_GivesTheFirstLanguageOrUndetermined()
    {
        Assert.Equal(Locale.Parse("ja-JP"), Localization.Resolve(List("ja-JP,en-US"), []));
        Assert.Equal(default, Localization.Resolve([], []));
        Assert.Equal("und", Localization.Resolve([], []).ToLanguageTag());
    }

    /// <summary>
    /// Six languages are written right to left by default; every other is written left to right, whatever its script or region.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tags ar, fa, he, ps, sd, ur, the deprecated iw (read as he), ar-EG, and en, ja, und, zh-Hant-TW.
    ///
    /// Steps:
    /// - Ask each locale's direction.
    ///
    /// Expected:
    /// - Right to left for the first eight, left to right for the others.
    /// </remarks>
    /// <param name="tag">The locale's tag.</param>
    /// <param name="expected">The direction.</param>
    [Theory]
    [InlineData("ar", Direction.Rtl)]
    [InlineData("fa", Direction.Rtl)]
    [InlineData("he", Direction.Rtl)]
    [InlineData("ps", Direction.Rtl)]
    [InlineData("sd", Direction.Rtl)]
    [InlineData("ur", Direction.Rtl)]
    [InlineData("iw", Direction.Rtl)]
    [InlineData("ar-EG", Direction.Rtl)]
    [InlineData("en", Direction.Ltr)]
    [InlineData("ja", Direction.Ltr)]
    [InlineData("und", Direction.Ltr)]
    [InlineData("zh-Hant-TW", Direction.Ltr)]
    public void DirectionOf_FollowsTheLanguage(string tag, Direction expected) => Assert.Equal(expected, Localization.DirectionOf(Locale.Parse(tag)));

    private static List<Locale> List(string tags) => tags.Length == 0 ? [] : [.. tags.Split(',').Select(static tag => Locale.Parse(tag))];
}
