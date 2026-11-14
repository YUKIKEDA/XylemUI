namespace XylemUI.Tests;

// The resolution of the supported locales against the user's languages, and the direction of a locale (LC1, design §4.11,
// "Resolution" and "The direction of writing"). The resolution is the body of Flutter's basicLocaleListResolution, and from LC2 its
// language steps first take a supported locale of the user's likely script.
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
    [InlineData("nl-BE,de-CH", "it-CH,fr-BE", "fr-BE")]
    [InlineData("zh-Hant-TW", "zh-Hant-HK,zh-Hant-TW", "zh-Hant-TW")]
    [InlineData("pt-PT", "pt-BR,pt-Latn-PT", "pt-Latn-PT")]
    [InlineData("ko-KR,fr-CA,de-DE", "fr,de-DE", "de-DE")]
    [InlineData("ko-CH,fr-FR", "it-CH,fr", "fr")]
    [InlineData("ko-KR", "en,ja", "en")]
    [InlineData("es-MX", "es-ES,es-AR", "es-ES")]
    public void Resolve_FollowsTheSteps(string preferred, string supported, string expected) =>
        Assert.Equal(Locale.Parse(expected), Localization.Resolve(List(preferred), List(supported)));

    /// <summary>
    /// The steps that compare the language and the region, and the language alone, first take a supported locale whose likely
    /// script is the user's, and else one of another script (LC2, design §4.11, "Adding the script a region implies").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The user's languages and the supported locales of each row. The likely scripts are CLDR 49's: zh-TW, zh-HK, und-TW Hant;
    ///   zh, zh-CN, zh-SG Hans; sr Cyrl, sr-ME Latn; pa Guru, pa-PK Arab; az Latn, az-IR Arab; uz-AF Arab; xx none.
    /// - Without the likely scripts (row LC1) the rows gave, in order: zh-Hans, sr, zh-CN, zh-Hans, pa, az, uz-Latn, zh-CN, sr,
    ///   zh-Hans-TW, en, xx-Latn, und-Hans, zh-Hans, zh-SG.
    ///
    /// Steps:
    /// - Resolve.
    ///
    /// Expected:
    /// - The row's tag. The language-only step takes the supported locale of the user's likely script, written (zh-Hant) or likely
    ///   (zh-TW for zh-Hant-HK); the language-and-region step does the same (zh-Hant-TW for zh-TW); a match kept for the next
    ///   language is the one of the likely script (zh-TW then zh-CN). A locale of another script is still taken before the first
    ///   supported (zh-Hans for zh-TW, with en first); a region the likely subtags would add is not compared (zh against ko-CN); a
    ///   language with no likely script takes the first that fits, as before.
    /// - The steps that compare written fields do not read likely scripts: zh-HK for zh-Hant then zh-HK (not zh-TW at the
    ///   language-and-script step), and zh-Hant for zh-Hant-TW (not zh-TW at the step of all three).
    /// - A locale of the language in the likely script comes before one of the region in another script: zh-Hant for zh-TW with
    ///   zh-Hans-TW first; it is kept for the next language as a language-only match is (zh-Hant for zh-TW then zh-HK, and
    ///   zh-Hant-HK when that is supported). With none of the likely script, the one of the region is taken (zh-Hans-TW, with en),
    ///   and a language with no likely script takes the one of the region as before (xx-Latn-TW).
    /// </remarks>
    /// <param name="preferred">The user's languages.</param>
    /// <param name="supported">The supported locales.</param>
    /// <param name="expected">The resolved locale.</param>
    [Theory]
    [InlineData("zh-TW", "zh-Hans,zh-Hant", "zh-Hant")]
    [InlineData("sr-ME", "sr,sr-Latn", "sr-Latn")]
    [InlineData("zh-Hant-HK", "zh-CN,zh-TW", "zh-TW")]
    [InlineData("zh-TW", "en,zh-Hans", "zh-Hans")]
    [InlineData("pa-PK", "pa,pa-Arab", "pa-Arab")]
    [InlineData("az-IR", "az,az-Arab", "az-Arab")]
    [InlineData("uz-AF", "uz-Latn,uz-Arab", "uz-Arab")]
    [InlineData("zh-Hant", "zh-CN,zh-TW", "zh-TW")]
    [InlineData("sr-Latn", "sr,sr-ME", "sr-ME")]
    [InlineData("zh-TW", "zh-Hans-TW,zh-Hant-TW", "zh-Hant-TW")]
    [InlineData("zh", "en,ko-CN", "en")]
    [InlineData("xx-TW", "xx-Latn,xx-Hant", "xx-Latn")]
    [InlineData("und-TW", "und-Hans,und-Hant", "und-Hant")]
    [InlineData("zh-TW,zh-CN", "zh-Hans,zh-Hant", "zh-Hant")]
    [InlineData("zh-Hant", "zh-SG,zh-HK", "zh-HK")]
    [InlineData("zh-Hant,zh-HK", "zh-TW,zh-HK", "zh-HK")]
    [InlineData("zh-Hant-TW", "zh-Hant,zh-TW", "zh-Hant")]
    [InlineData("zh-TW", "zh-Hans-TW,zh-Hant", "zh-Hant")]
    [InlineData("zh-TW,zh-HK", "zh-Hans-TW,zh-Hant", "zh-Hant")]
    [InlineData("zh-TW,zh-HK", "zh-Hans-TW,zh-Hant,zh-Hant-HK", "zh-Hant-HK")]
    [InlineData("zh-TW", "zh-Hans-TW,en", "zh-Hans-TW")]
    [InlineData("xx-TW", "xx-Latn-TW,xx-Hant", "xx-Latn-TW")]
    public void Resolve_LikelyScript_IsPreferredInTheLanguageSteps(string preferred, string supported, string expected) =>
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
