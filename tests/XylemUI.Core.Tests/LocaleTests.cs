namespace XylemUI.Tests;

// The locale value of row LC1 (design §4.11, "Locale"): how a BCP 47 tag is read, canonicalized, and refused.
public sealed class LocaleTests
{
    /// <summary>
    /// A tag is read without regard to case, and its fields come out in canonical case: the language in lower case, the script
    /// with its first letter in upper case, and the region in upper case.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "ZH-hant-tw" and "zh-Hant-TW".
    ///
    /// Expected:
    /// - Both have the language "zh", the script "Hant", and the region "TW", and both write "zh-Hant-TW".
    /// </remarks>
    [Fact]
    public void Parse_MixedCase_PutsEachFieldInItsCanonicalCase()
    {
        Locale mixed = Locale.Parse("ZH-hant-tw");
        Locale canonical = Locale.Parse("zh-Hant-TW");

        AssertFields(mixed, "zh", "Hant", "TW");
        AssertFields(canonical, "zh", "Hant", "TW");
        Assert.Equal("zh-Hant-TW", mixed.ToLanguageTag());
    }

    /// <summary>
    /// A region of 3 digits (a UN M.49 code) is a region, kept as it is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "es-419".
    ///
    /// Expected:
    /// - The language is "es", there is no script, the region is "419", and the tag written is "es-419".
    /// </remarks>
    [Fact]
    public void Parse_RegionOfThreeDigits_KeepsTheDigits()
    {
        Locale locale = Locale.Parse("es-419");

        AssertFields(locale, "es", null, "419");
        Assert.Equal("es-419", locale.ToLanguageTag());
    }

    /// <summary>
    /// Variants, extensions, and trailing private use are read and dropped: the locale keeps the language, the script, and the
    /// region only.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse each tag: "ca-ES-valencia" (a variant), "de-DE-u-co-phonebk" (an extension), "en-x-foo" (private use),
    ///   "en-1234" (a variant of a digit and 3 characters), "ja-JP-u-ca-japanese-x-foo" (an extension and private use), and
    ///   "zh-Hant-TW-x-a-b" (private use after all three fields).
    ///
    /// Expected:
    /// - The tags written are "ca-ES", "de-DE", "en", "en", "ja-JP", and "zh-Hant-TW".
    /// </remarks>
    [Theory]
    [InlineData("ca-ES-valencia", "ca-ES")]
    [InlineData("de-DE-u-co-phonebk", "de-DE")]
    [InlineData("en-x-foo", "en")]
    [InlineData("en-1234", "en")]
    [InlineData("ja-JP-u-ca-japanese-x-foo", "ja-JP")]
    [InlineData("zh-Hant-TW-x-a-b", "zh-Hant-TW")]
    public void Parse_VariantsExtensionsAndPrivateUse_DropsThem(string tag, string expected) =>
        Assert.Equal(expected, Locale.Parse(tag).ToLanguageTag());

    /// <summary>
    /// A deprecated language or region is replaced by its preferred value when the locale is made, so the field, the equality,
    /// and the tag written all show the new code (design §4.11, "廃止されたコードの読み替え").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "iw", "in-ID", and "my-BU".
    ///
    /// Expected:
    /// - "iw" has the language "he", equals Parse("he"), has the same hash, and writes "he".
    /// - "in-ID" has the language "id" and writes "id-ID".
    /// - "my-BU" has the region "MM", equals Parse("my-MM"), and writes "my-MM".
    /// </remarks>
    [Fact]
    public void Parse_DeprecatedLanguageOrRegion_ReplacesItWithThePreferredValue()
    {
        Locale hebrew = Locale.Parse("iw");
        Locale indonesian = Locale.Parse("in-ID");
        Locale burmese = Locale.Parse("my-BU");

        Assert.Equal("he", hebrew.Language);
        Assert.Equal(Locale.Parse("he"), hebrew);
        Assert.Equal(Locale.Parse("he").GetHashCode(), hebrew.GetHashCode());
        Assert.Equal("he", hebrew.ToLanguageTag());
        Assert.Equal("id", indonesian.Language);
        Assert.Equal("id-ID", indonesian.ToLanguageTag());
        Assert.Equal("MM", burmese.Region);
        Assert.Equal(Locale.Parse("my-MM"), burmese);
        Assert.Equal("my-MM", burmese.ToLanguageTag());
    }

    /// <summary>
    /// A grandfathered tag with a preferred value is replaced as a whole, in any case, and the replacement is read like any tag.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "art-lojban", "i-klingon", "en-GB-oed" (its replacement "en-GB-oxendict" has a variant), "zh-min-nan",
    ///   "Zh-Min-NaN", and "en-gb-OED".
    ///
    /// Expected:
    /// - The tags written are "jbo", "tlh", "en-GB", "nan", "nan", and "en-GB".
    /// </remarks>
    [Theory]
    [InlineData("art-lojban", "jbo")]
    [InlineData("i-klingon", "tlh")]
    [InlineData("en-GB-oed", "en-GB")]
    [InlineData("zh-min-nan", "nan")]
    [InlineData("Zh-Min-NaN", "nan")]
    [InlineData("en-gb-OED", "en-GB")]
    public void Parse_GrandfatheredWithPreferredValue_ReplacesTheWholeTag(string tag, string expected) =>
        Assert.Equal(expected, Locale.Parse(tag).ToLanguageTag());

    /// <summary>
    /// A grandfathered tag with no preferred value is read as a langtag when it matches one, and is malformed when it does not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "cel-gaulish" and "zh-min"; TryParse "i-default", "i-mingo", and "i-enochian".
    ///
    /// Expected:
    /// - "cel-gaulish" writes "cel" (gaulish is a variant), and "zh-min" writes "zh" (min extends ms, not zh).
    /// - TryParse returns false for the three irregular tags.
    /// </remarks>
    [Fact]
    public void Parse_GrandfatheredWithoutPreferredValue_ReadsALangtagAndRefusesTheIrregular()
    {
        Assert.Equal("cel", Locale.Parse("cel-gaulish").ToLanguageTag());
        Assert.Equal("zh", Locale.Parse("zh-min").ToLanguageTag());
        Assert.False(Locale.TryParse("i-default", out _));
        Assert.False(Locale.TryParse("i-mingo", out _));
        Assert.False(Locale.TryParse("i-enochian", out _));
    }

    /// <summary>
    /// A redundant tag with a preferred value is replaced as a whole, as a grandfathered one is (RFC 5646 §4.5, step 2).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "sgn-BR", "SGN-br", and "zh-cmn-Hans".
    ///
    /// Expected:
    /// - The tags written are "bzs", "bzs", and "cmn-Hans" (the language "cmn" and the script "Hans").
    /// </remarks>
    [Fact]
    public void Parse_RedundantWithPreferredValue_ReplacesTheWholeTag()
    {
        Assert.Equal("bzs", Locale.Parse("sgn-BR").ToLanguageTag());
        Assert.Equal("bzs", Locale.Parse("SGN-br").ToLanguageTag());
        AssertFields(Locale.Parse("zh-cmn-Hans"), "cmn", "Hans", null);
    }

    /// <summary>
    /// An extended language replaces the language when its registered prefix is that language, and the script and region after
    /// it are kept.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "zh-yue", "zh-yue-Hant-HK", "sgn-ase", and "ZH-CMN-hans-cn".
    ///
    /// Expected:
    /// - The tags written are "yue", "yue-Hant-HK", "ase", and "cmn-Hans-CN".
    /// </remarks>
    [Theory]
    [InlineData("zh-yue", "yue")]
    [InlineData("zh-yue-Hant-HK", "yue-Hant-HK")]
    [InlineData("sgn-ase", "ase")]
    [InlineData("ZH-CMN-hans-cn", "cmn-Hans-CN")]
    public void Parse_ExtlangWithItsPrefix_ReplacesTheLanguage(string tag, string expected) =>
        Assert.Equal(expected, Locale.Parse(tag).ToLanguageTag());

    /// <summary>
    /// An extended language whose prefix is another language, an unregistered extlang-shaped subtag, and the second and third
    /// extlang positions are read and dropped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "zh-min-HK" (min has the prefix ms), "zh-abc" (abc is not registered), "zh-yue-abc-def" (two more extlang
    ///   positions), and "ms-min" (min under its own prefix, for contrast).
    ///
    /// Expected:
    /// - The tags written are "zh-HK", "zh", "yue", and "min".
    /// </remarks>
    [Theory]
    [InlineData("zh-min-HK", "zh-HK")]
    [InlineData("zh-abc", "zh")]
    [InlineData("zh-yue-abc-def", "yue")]
    [InlineData("ms-min", "min")]
    public void Parse_ExtlangNotUnderItsPrefix_DropsIt(string tag, string expected) => Assert.Equal(expected, Locale.Parse(tag).ToLanguageTag());

    /// <summary>
    /// A tag is judged by the langtag syntax alone: subtags the registry does not have are read.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse "xx-YY", "abcd-Latn" (a language of 4 letters), and "abcdefgh-Qaaa-999".
    ///
    /// Expected:
    /// - "xx-YY" has the language "xx" and the region "YY"; the others write "abcd-Latn" and "abcdefgh-Qaaa-999".
    /// </remarks>
    [Fact]
    public void Parse_UnregisteredButWellFormed_ReadsIt()
    {
        AssertFields(Locale.Parse("xx-YY"), "xx", null, "YY");
        Assert.Equal("abcd-Latn", Locale.Parse("abcd-Latn").ToLanguageTag());
        Assert.Equal("abcdefgh-Qaaa-999", Locale.Parse("abcdefgh-Qaaa-999").ToLanguageTag());
    }

    /// <summary>
    /// A malformed tag makes Parse throw <see cref="FormatException"/> naming the tag, and makes TryParse return false with the
    /// default locale.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - For each tag, call TryParse and Parse. The tags are: empty; "ja_JP" (the wrong separator); "x-foo" (private use alone);
    ///   "-ja", "ja-", and "ja--JP" (empty subtags); "j" (a language of 1 letter); "abcdefghi" (a subtag of 9 characters);
    ///   "ja-JP-JP" (a second region); "ja-Latn-Latn" (a second script); "jä" and "ja-ＪＰ" (letters outside
    ///   ASCII); " ja", "ja ", and "ja -JP" (spaces); "en-a" (an extension with no subtag); "en-a-b" (an extension subtag of 1
    ///   character); "en-x" (private use with no subtag); "zh-aaa-bbb-ccc-ddd" (a fourth extlang); "1a" (a digit in the
    ///   language); and "en-US-Latn" (a script after the region).
    ///
    /// Expected:
    /// - TryParse returns false and the default locale.
    /// - Parse throws <see cref="FormatException"/> whose message contains the tag.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("ja_JP")]
    [InlineData("x-foo")]
    [InlineData("-ja")]
    [InlineData("ja-")]
    [InlineData("ja--JP")]
    [InlineData("j")]
    [InlineData("abcdefghi")]
    [InlineData("ja-JP-JP")]
    [InlineData("ja-Latn-Latn")]
    [InlineData("jä")]
    [InlineData("ja-ＪＰ")]
    [InlineData(" ja")]
    [InlineData("ja ")]
    [InlineData("ja -JP")]
    [InlineData("en-a")]
    [InlineData("en-a-b")]
    [InlineData("en-x")]
    [InlineData("zh-aaa-bbb-ccc-ddd")]
    [InlineData("1a")]
    [InlineData("en-US-Latn")]
    public void ParseAndTryParse_MalformedTag_ThrowAndReturnFalse(string tag)
    {
        bool read = Locale.TryParse(tag, out Locale locale);
        FormatException error = Assert.Throws<FormatException>(() => Locale.Parse(tag));

        Assert.False(read);
        Assert.Equal(default, locale);
        Assert.Contains("'" + tag + "'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A null tag makes Parse throw <see cref="ArgumentNullException"/> and TryParse return false.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Call Parse(null) and TryParse(null).
    ///
    /// Expected:
    /// - Parse throws <see cref="ArgumentNullException"/> for the parameter "tag"; TryParse returns false and the default locale.
    /// </remarks>
    [Fact]
    public void ParseAndTryParse_Null_ThrowArgumentNullAndReturnFalse()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => Locale.Parse(null!));
        bool read = Locale.TryParse(null, out Locale locale);

        Assert.Equal("tag", error.ParamName);
        Assert.False(read);
        Assert.Equal(default, locale);
    }

    /// <summary>
    /// The default locale is the undetermined language: "und", with no script and no region, equal to the locale read from "und".
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - default(Locale).
    ///
    /// Steps:
    /// - Read its fields and its tag; parse "und" and "UND"; parse "und-Latn".
    ///
    /// Expected:
    /// - The language is "und", the script and the region are null, and ToLanguageTag and ToString return "und".
    /// - It equals Parse("und") and Parse("UND"), with the same hash, and == holds.
    /// - "und-Latn" writes "und-Latn" and does not equal the default.
    /// </remarks>
    [Fact]
    public void Default_IsUndetermined_AndEqualsTheParsedUnd()
    {
        Locale none = default;
        Locale und = Locale.Parse("und");

        AssertFields(none, "und", null, null);
        Assert.Equal("und", none.ToLanguageTag());
        Assert.Equal("und", none.ToString());
        Assert.Equal(und, none);
        Assert.Equal(Locale.Parse("UND"), none);
        Assert.Equal(und.GetHashCode(), none.GetHashCode());
        Assert.True(none == und);
        Assert.Equal("und-Latn", Locale.Parse("und-Latn").ToLanguageTag());
        Assert.NotEqual(none, Locale.Parse("und-Latn"));
    }

    /// <summary>
    /// Two locales are equal when all three fields are, and differ when any one of them does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The locale of "zh-Hant-TW".
    ///
    /// Steps:
    /// - Compare it with "ZH-HANT-TW" (the same fields), and with "en-Hant-TW", "zh-Hans-TW", "zh-Hant-HK", "zh-TW", and
    ///   "zh-Hant" (one field changed or missing each).
    ///
    /// Expected:
    /// - "ZH-HANT-TW" is equal by Equals, ==, and Equals(object), and has the same hash; != is false.
    /// - Each of the others is not equal by Equals, ==, and Equals(object); != is true.
    /// </remarks>
    [Fact]
    public void Equals_ComparesLanguageScriptAndRegion()
    {
        Locale locale = Locale.Parse("zh-Hant-TW");
        Locale same = Locale.Parse("ZH-HANT-TW");

        Assert.True(locale.Equals(same));
        Assert.True(locale == same);
        Assert.False(locale != same);
        Assert.True(locale.Equals((object)same));
        Assert.Equal(locale.GetHashCode(), same.GetHashCode());
        foreach (string other in new[] { "en-Hant-TW", "zh-Hans-TW", "zh-Hant-HK", "zh-TW", "zh-Hant" })
        {
            Locale different = Locale.Parse(other);
            Assert.False(locale.Equals(different), other);
            Assert.False(locale == different, other);
            Assert.True(locale != different, other);
            Assert.False(locale.Equals((object)different), other);
        }
    }

    /// <summary>
    /// The generated tables carry the registry's File-Date, and each kind of table answers for one of its entries and refuses a
    /// subtag it does not have.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - LocaleSubtags as generated by tools/LanguageSubtagGen.
    ///
    /// Steps:
    /// - Read FileDate; look up the language "iw", the region "BU", the extlang "yue", and the tags "art-lojban" and "sgn-br";
    ///   look up the language "ja", the region "JP", the extlang "abc", and the tag "zh-min".
    ///
    /// Expected:
    /// - FileDate is a date of the form yyyy-mm-dd.
    /// - The lookups give "he", "MM", "zh", "jbo", and "bzs".
    /// - The other four lookups return false.
    /// </remarks>
    [Fact]
    public void LocaleSubtags_SampleOfEachTable_Resolves()
    {
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", LocaleSubtags.FileDate);

        Assert.True(LocaleSubtags.TryGetLanguageReplacement("iw", out string? language));
        Assert.Equal("he", language);
        Assert.True(LocaleSubtags.TryGetRegionReplacement("BU", out string? region));
        Assert.Equal("MM", region);
        Assert.True(LocaleSubtags.TryGetExtlangPrefix("yue", out string? prefix));
        Assert.Equal("zh", prefix);
        Assert.True(LocaleSubtags.TryGetTagReplacement("art-lojban", out string? grandfathered));
        Assert.Equal("jbo", grandfathered);
        Assert.True(LocaleSubtags.TryGetTagReplacement("sgn-br", out string? redundant));
        Assert.Equal("bzs", redundant);

        Assert.False(LocaleSubtags.TryGetLanguageReplacement("ja", out _));
        Assert.False(LocaleSubtags.TryGetRegionReplacement("JP", out _));
        Assert.False(LocaleSubtags.TryGetExtlangPrefix("abc", out _));
        Assert.False(LocaleSubtags.TryGetTagReplacement("zh-min", out _));
    }

    private static void AssertFields(Locale locale, string language, string? script, string? region)
    {
        Assert.Equal(language, locale.Language);
        Assert.Equal(script, locale.Script);
        Assert.Equal(region, locale.Region);
    }
}
