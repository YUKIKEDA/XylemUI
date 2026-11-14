using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// One entry of the generated tables: a class that derives from <c>LocaleData</c>, and, except for root, a property of
/// <c>CldrLocales</c>.
/// </summary>
/// <param name="Tag">The tag of the entry's <c>Locale</c>, as cldr-json writes it (<c>und</c> for root).</param>
/// <param name="Identifier">The name of the property, which the class's name is <c>Cldr</c> and this (<c>EnGB</c>).</param>
/// <param name="DataTag">The locale whose data the entry holds: its own tag, or the tag an alias leads to (<c>fil</c> for
/// <c>tl</c>).</param>
/// <param name="Region">The region whose week data the entry holds: the tag's, or the likely one (design §4.11, "The region of a
/// locale's values").</param>
internal sealed record LocaleEntry(string Tag, string Identifier, string DataTag, string Region);

/// <summary>
/// The set of locales the tables hold (design §4.11, "The set of locales"): the locales whose coverage level is modern and that have
/// both a number and a Gregorian date file, without those whose tag has a variant; the languages of row LC4 that the set does not
/// have, from their own data or, with no data, as an alias entry; and root.
/// </summary>
internal sealed class LocaleSet
{
    // The 82 languages of row LC4, as Flutter 3.47.8 lists them in kMaterialSupportedLanguages
    // (packages/flutter_localizations/lib/src/l10n/generated_material_localizations.dart, lines 46518 to 46601).
    private static readonly string[] Lc4Languages =
    [
        "af",
        "am",
        "ar",
        "as",
        "az",
        "be",
        "bg",
        "bn",
        "bo",
        "bs",
        "ca",
        "cs",
        "cy",
        "da",
        "de",
        "el",
        "en",
        "es",
        "et",
        "eu",
        "fa",
        "fi",
        "fil",
        "fr",
        "ga",
        "gl",
        "gsw",
        "gu",
        "he",
        "hi",
        "hr",
        "hu",
        "hy",
        "id",
        "is",
        "it",
        "ja",
        "ka",
        "kk",
        "km",
        "kn",
        "ko",
        "ky",
        "lo",
        "lt",
        "lv",
        "mk",
        "ml",
        "mn",
        "mr",
        "ms",
        "my",
        "nb",
        "ne",
        "nl",
        "no",
        "or",
        "pa",
        "pl",
        "ps",
        "pt",
        "ro",
        "ru",
        "si",
        "sk",
        "sl",
        "sq",
        "sr",
        "sv",
        "sw",
        "ta",
        "te",
        "th",
        "tl",
        "tr",
        "ug",
        "uk",
        "ur",
        "uz",
        "vi",
        "zh",
        "zu",
    ];

    private LocaleSet(
        List<string> tables,
        SortedDictionary<string, string> aliases,
        List<string> variants,
        List<string> added,
        List<LocaleEntry> entries,
        LocaleEntry root
    )
    {
        Tables = tables;
        Aliases = aliases;
        Variants = variants;
        Added = added;
        Entries = entries;
        Root = root;
    }

    /// <summary>
    /// Gets the tags of the locales that have tables of their own, root left out, in ordinal order.
    /// </summary>
    public IReadOnlyList<string> Tables { get; }

    /// <summary>
    /// Gets the alias entries: the tag of each, and the tag of the tables it holds.
    /// </summary>
    public IReadOnlyDictionary<string, string> Aliases { get; }

    /// <summary>
    /// Gets the modern locales left out because their tag has a variant.
    /// </summary>
    public IReadOnlyList<string> Variants { get; }

    /// <summary>
    /// Gets the languages of row LC4 added from their own data.
    /// </summary>
    public IReadOnlyList<string> Added { get; }

    /// <summary>
    /// Gets the entries of <c>CldrLocales</c>: the tables and the aliases, in ordinal order of their tags, root left out.
    /// </summary>
    public IReadOnlyList<LocaleEntry> Entries { get; }

    /// <summary>
    /// Gets the entry of root, whose data is cldr-json's <c>und</c> and whose region is the world (<c>001</c>).
    /// </summary>
    public LocaleEntry Root { get; }

    /// <summary>
    /// Reads the set from a zip.
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <returns>The set.</returns>
    public static LocaleSet Read(CldrZip cldr)
    {
        // The json-full release lists no modern locale in availableLocales.json, so the coverage levels are the source (design
        // §4.11, checked in the spike).
        JsonElement levels = cldr.Json("cldr-core/coverageLevels.json").GetProperty("effectiveCoverageLevels");
        var modern = new List<string>();
        foreach (JsonProperty level in levels.EnumerateObject())
        {
            if (level.Value.GetString() == "modern" && level.Name != "und" && cldr.HasNumbersAndDates(level.Name))
            {
                modern.Add(level.Name);
            }
        }

        // Locale reads a variant and drops it, so a table of be-tarask could not be told from be's (design §4.11).
        var variants = modern.Where(HasVariant).ToList();
        var tables = modern.Where(tag => !HasVariant(tag)).ToList();
        var languages = new HashSet<string>(tables.Select(Language), StringComparer.Ordinal);
        JsonElement languageAliases = cldr.Supplemental("aliases").GetProperty("metadata").GetProperty("alias").GetProperty("languageAlias");
        var added = new List<string>();
        var aliases = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string language in Lc4Languages)
        {
            if (languages.Contains(language))
            {
                continue;
            }

            if (cldr.HasNumbersAndDates(language))
            {
                added.Add(language);
            }
            else if (
                languageAliases.TryGetProperty(language, out JsonElement alias)
                && alias.GetProperty("_replacement").GetString() is { } replacement
                && tables.Contains(replacement)
            )
            {
                aliases.Add(language, replacement);
            }
            else
            {
                throw new InvalidDataException(
                    "The language " + language + " of row LC4 has neither number and date files nor an alias to a locale of the set."
                );
            }
        }

        tables.AddRange(added);
        tables.Sort(StringComparer.Ordinal);
        variants.Sort(StringComparer.Ordinal);
        added.Sort(StringComparer.Ordinal);
        if (!cldr.HasNumbersAndDates("und"))
        {
            throw new InvalidDataException("The zip has no number and date files of root (und).");
        }

        JsonElement likely = cldr.Supplemental("likelySubtags").GetProperty("likelySubtags");
        var entries = new List<LocaleEntry>();
        foreach (string tag in tables)
        {
            entries.Add(new LocaleEntry(tag, Identifier(tag), tag, Region(likely, tag)));
        }

        foreach ((string tag, string target) in aliases)
        {
            entries.Add(new LocaleEntry(tag, Identifier(tag), target, Region(likely, target)));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Tag, right.Tag));
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (LocaleEntry entry in entries)
        {
            if (!identifiers.Add(entry.Identifier))
            {
                throw new InvalidDataException("Two entries have the name " + entry.Identifier + ".");
            }
        }

        return new LocaleSet(tables, aliases, variants, added, entries, new LocaleEntry("und", "Root", "und", "001"));
    }

    /// <summary>
    /// Returns the language subtag of a tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The first subtag.</returns>
    public static string Language(string tag) => tag.Split('-')[0];

    // The name of an entry: its subtags joined, each with its first letter in upper case (En, EnGB, ZhHantHK, Es419; design §4.11,
    // "Names").
    private static string Identifier(string tag) => string.Concat(tag.Split('-').Select(subtag => char.ToUpperInvariant(subtag[0]) + subtag[1..]));

    // A variant is a subtag of 5 to 8 characters, or of 4 that starts with a digit (RFC 5646 §2.1).
    private static bool HasVariant(string tag) =>
        tag.Split('-').Skip(1).Any(subtag => (subtag.Length is >= 5 and <= 8) || (subtag.Length == 4 && char.IsAsciiDigit(subtag[0])));

    // The region of a tag, or the one Add Likely Subtags gives it (UTS #35 part 1, §4.3): the table is looked up with the language
    // and the script, then with the language alone; a tag that has a region is not looked up. With no entry, the world.
    private static string Region(JsonElement likely, string tag)
    {
        string[] subtags = tag.Split('-');
        string? script = null;
        foreach (string subtag in subtags.Skip(1))
        {
            if ((subtag.Length == 2 && subtag.All(char.IsAsciiLetter)) || (subtag.Length == 3 && subtag.All(char.IsAsciiDigit)))
            {
                return subtag;
            }

            if (subtag.Length == 4 && char.IsAsciiLetter(subtag[0]))
            {
                script = subtag;
            }
        }

        string[] keys = script is null ? [subtags[0]] : [subtags[0] + "-" + script, subtags[0]];
        foreach (string key in keys)
        {
            if (likely.TryGetProperty(key, out JsonElement value))
            {
                string[] maximized = (value.GetString() ?? string.Empty).Split('-');
                if (maximized.Length != 3)
                {
                    throw new InvalidDataException("The likely subtags of " + key + " are not a language, a script, and a region.");
                }

                return maximized[2];
            }
        }

        return "001";
    }
}
