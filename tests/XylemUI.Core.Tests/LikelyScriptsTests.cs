namespace XylemUI.Tests;

// The likely script of a locale (LC2, design §4.11, "Resolution", "Adding the script a region implies"): the script a locale writes,
// or else the one CLDR 49's likelySubtags gives its language and region, then its language (UTS #35 part 1, "Likely Subtags", Add
// Likely Subtags, step 2). The expected scripts were read from cldr-core/supplemental/likelySubtags.json of
// cldr-49.0.0-json-full.zip, not from the generated table.
public sealed class LikelyScriptsTests
{
    private static readonly string[] Tags = ["zh-TW", "zh", "sr-ME", "pa-PK", "en-AU", "und", "xx", "zh-Latn-TW"];

    /// <summary>
    /// A locale with no script takes the script of its language and region in likelySubtags, or else the script of its language.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scripts of CLDR 49's likelySubtags: the keys zh-TW, zh-HK, and zh-MO give Hant, zh Hans (zh-SG has no key), sr-ME Latn,
    ///   sr Cyrl (sr-RS has no key), pa Guru, pa-PK Arab, az Latn, az-IR Arab, uz Latn, uz-AF Arab, en Latn (en-AU has no key), und
    ///   Latn, und-TW Hant, und-AF Arab, yue Hant, yue-CN Hans, kk Cyrl, kk-CN Arab, ja Jpan, tl Latn.
    ///
    /// Steps:
    /// - Infer the script of each row's tag.
    ///
    /// Expected:
    /// - The script of the key of the language and the region when there is one, else of the language.
    /// </remarks>
    /// <param name="tag">The locale's tag.</param>
    /// <param name="script">The likely script.</param>
    [Theory]
    [InlineData("zh-TW", "Hant")]
    [InlineData("zh-HK", "Hant")]
    [InlineData("zh-MO", "Hant")]
    [InlineData("zh", "Hans")]
    [InlineData("zh-SG", "Hans")]
    [InlineData("sr-ME", "Latn")]
    [InlineData("sr", "Cyrl")]
    [InlineData("sr-RS", "Cyrl")]
    [InlineData("pa", "Guru")]
    [InlineData("pa-PK", "Arab")]
    [InlineData("az", "Latn")]
    [InlineData("az-IR", "Arab")]
    [InlineData("uz", "Latn")]
    [InlineData("uz-AF", "Arab")]
    [InlineData("en", "Latn")]
    [InlineData("en-AU", "Latn")]
    [InlineData("und", "Latn")]
    [InlineData("und-TW", "Hant")]
    [InlineData("und-AF", "Arab")]
    [InlineData("yue", "Hant")]
    [InlineData("yue-CN", "Hans")]
    [InlineData("kk", "Cyrl")]
    [InlineData("kk-CN", "Arab")]
    [InlineData("ja", "Jpan")]
    [InlineData("tl", "Latn")]
    public void Infer_NoScript_LooksUpLanguageAndRegion_ThenLanguage(string tag, string script) =>
        Assert.Equal(LikelyScripts.Pack(script), LikelyScripts.Infer(Locale.Parse(tag)));

    /// <summary>
    /// A script the locale writes is kept, even one likelySubtags does not name, and Zzzz and ZZ count as empty (Add Likely
    /// Subtags, step 1, and its example ZH-ZZZZ-SG, which maximizes to zh-Hans-SG).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tags of the rows.
    ///
    /// Steps:
    /// - Infer the script of each.
    ///
    /// Expected:
    /// - zh-Latn-TW Latn (not Hant), sr-Latn Latn, und-Cyrl Cyrl, zh-Qaaa Qaaa; ZH-ZZZZ-SG Hans, zh-Zzzz-TW Hant, zh-ZZ Hans,
    ///   und-Zzzz-ZZ Latn.
    /// </remarks>
    /// <param name="tag">The locale's tag.</param>
    /// <param name="script">The likely script.</param>
    [Theory]
    [InlineData("zh-Latn-TW", "Latn")]
    [InlineData("sr-Latn", "Latn")]
    [InlineData("und-Cyrl", "Cyrl")]
    [InlineData("zh-Qaaa", "Qaaa")]
    [InlineData("ZH-ZZZZ-SG", "Hans")]
    [InlineData("zh-Zzzz-TW", "Hant")]
    [InlineData("zh-ZZ", "Hans")]
    [InlineData("und-Zzzz-ZZ", "Latn")]
    public void Infer_WrittenScript_IsKept_AndZzzzIsEmpty(string tag, string script) =>
        Assert.Equal(LikelyScripts.Pack(script), LikelyScripts.Infer(Locale.Parse(tag)));

    /// <summary>
    /// A language likelySubtags does not have gives no script, and so does a language only CLDR's aliases replace: the locale is
    /// not canonicalized with aliases.json (design §4.11), and the lookup does not fall back to und with the region.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - xx and sh have no key; und-TW has one (Hant), which xx-TW must not take.
    ///
    /// Steps:
    /// - Infer the script of xx, xx-TW, sh, and of the 8-letter language abcdefgh.
    ///
    /// Expected:
    /// - 0 for each.
    /// </remarks>
    /// <param name="tag">The locale's tag.</param>
    [Theory]
    [InlineData("xx")]
    [InlineData("xx-TW")]
    [InlineData("sh")]
    [InlineData("abcdefgh")]
    public void Infer_UnknownLanguage_GivesNone(string tag) => Assert.Equal(0u, LikelyScripts.Infer(Locale.Parse(tag)));

    /// <summary>
    /// The table holds the keys of likelySubtags without a script, less the keys of a language and a region whose script is the
    /// language's own, in ascending order (design §4.11, "Where the table lives": the values are cut down to the script).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The generated table. Counted from the json with a script (Python): 7,868 keys, 331 with a script, 7,284 of a language
    ///   alone and 253 of a language and a region, 132 of which have the language's script; 120 scripts.
    ///
    /// Steps:
    /// - Read the counts and walk the keys, the values, and the scripts.
    ///
    /// Expected:
    /// - The counts above and 7,405 entries; the keys strictly ascending, 4 bytes each; each value an index of a script; each script
    ///   4 ASCII letters, the first upper case and the others lower case.
    /// </remarks>
    [Fact]
    public void Table_HoldsTheScriptOnlyKeys()
    {
        Assert.Equal(7_868, CldrLikelyScripts.SourceKeyCount);
        Assert.Equal(331, CldrLikelyScripts.KeysWithScript);
        Assert.Equal(132, CldrLikelyScripts.RegionKeysLeftOut);
        Assert.Equal(7_405, CldrLikelyScripts.EntryCount);
        Assert.Equal(120, CldrLikelyScripts.ScriptCount);
        Assert.Equal(CldrLikelyScripts.EntryCount * 4, CldrLikelyScripts.Keys.Length);
        Assert.Equal(CldrLikelyScripts.EntryCount, CldrLikelyScripts.Values.Length);
        Assert.Equal(CldrLikelyScripts.ScriptCount * 4, CldrLikelyScripts.Scripts.Length);
        uint previous = 0;
        for (int i = 0; i < CldrLikelyScripts.EntryCount; i++)
        {
            uint key = BitConverter.ToUInt32(CldrLikelyScripts.Keys.Slice(i * 4, 4));
            Assert.True(i == 0 || key > previous, "The key at " + i + " is not above the one before.");
            Assert.True(CldrLikelyScripts.Values[i] < CldrLikelyScripts.ScriptCount);
            previous = key;
        }

        for (int i = 0; i < CldrLikelyScripts.ScriptCount; i++)
        {
            ReadOnlySpan<byte> script = CldrLikelyScripts.Scripts.Slice(i * 4, 4);
            Assert.True(char.IsAsciiLetterUpper((char)script[0]) && char.IsAsciiLetterLower((char)script[1]), "Script " + i);
            Assert.True(char.IsAsciiLetterLower((char)script[2]) && char.IsAsciiLetterLower((char)script[3]), "Script " + i);
        }
    }

    /// <summary>
    /// Inferring a script allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Locales parsed before the measured runs: a key of a language and a region, of a language, the undetermined language, an
    ///   unknown language, and a written script.
    ///
    /// Steps:
    /// - 1,000 times, in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: infer the script of one of them, by the index.
    ///
    /// Expected:
    /// - No byte is allocated, and every run found what the first run found for the same locale.
    /// </remarks>
    [Fact]
    public void Infer_AllocatesNothing()
    {
        Locale[] locales = [.. Tags.Select(Locale.Parse)];
        uint[] expected = [.. locales.Select(LikelyScripts.Infer)];
        int mismatches = 0;

        PerformanceAssert.DoesNotAllocate(
            1_000,
            i =>
            {
                int at = ((i % locales.Length) + locales.Length) % locales.Length;
                if (LikelyScripts.Infer(locales[at]) != expected[at])
                {
                    mismatches++;
                }
            }
        );

        Assert.Equal(0, mismatches);
        Assert.Equal(LikelyScripts.Pack("Hant"), expected[0]);
    }
}
