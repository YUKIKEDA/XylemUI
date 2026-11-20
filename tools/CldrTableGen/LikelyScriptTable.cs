using System.Globalization;
using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// The likely subtags table cut down to what the resolution reads (design §4.11, "Resolution", "Where the table lives"): the likely
/// script of a language, and of a language in a region. A key that has a script is left out, since a locale that writes its script
/// keeps it; a key of a language and a region whose script is the language's own is left out too, since the lookup of the language
/// alone then gives the same script.
/// </summary>
/// <remarks>
/// A key is packed in 32 bits: the language in the upper 16, its 2 or 3 letters each 1 to 26 in base 27 (a third letter of 0 for 2
/// letters); the region in the lower 16, 0 for none, 1 + 26 × first + second for 2 letters (A is 0), 1000 + the number for 3
/// digits. <c>LikelyScripts</c> in XylemUI.Core packs a locale the same way.
/// </remarks>
internal sealed class LikelyScriptTable
{
    private LikelyScriptTable(List<string> scripts, List<(uint Key, byte Script)> entries, int sourceKeys, int keysWithScript, int regionKeysLeftOut)
    {
        Scripts = scripts;
        Entries = entries;
        SourceKeys = sourceKeys;
        KeysWithScript = keysWithScript;
        RegionKeysLeftOut = regionKeysLeftOut;
    }

    /// <summary>
    /// Gets the scripts the entries name, in ordinal order.
    /// </summary>
    public IReadOnlyList<string> Scripts { get; }

    /// <summary>
    /// Gets the entries in ascending order of their keys: each packed key, and the index of its script in <see cref="Scripts"/>.
    /// </summary>
    public IReadOnlyList<(uint Key, byte Script)> Entries { get; }

    /// <summary>
    /// Gets the number of keys of likelySubtags.json.
    /// </summary>
    public int SourceKeys { get; }

    /// <summary>
    /// Gets the number of keys left out because they have a script.
    /// </summary>
    public int KeysWithScript { get; }

    /// <summary>
    /// Gets the number of keys of a language and a region left out because their script is the language's own.
    /// </summary>
    public int RegionKeysLeftOut { get; }

    /// <summary>
    /// Reads the table from a zip.
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <returns>The table.</returns>
    public static LikelyScriptTable Read(CldrZip cldr)
    {
        JsonElement likely = cldr.Supplemental("likelySubtags").GetProperty("likelySubtags");
        var languages = new Dictionary<string, string>(StringComparer.Ordinal);
        var regions = new List<(string Language, string Region, string Script)>();
        int sourceKeys = 0;
        int keysWithScript = 0;
        foreach (JsonProperty property in likely.EnumerateObject())
        {
            sourceKeys++;
            (string language, string? script, string? region) = Split(property.Name);
            if (script is not null)
            {
                keysWithScript++;
                continue;
            }

            string value = property.Value.GetString() ?? string.Empty;
            (_, string? likelyScript, _) = Split(value);
            if (likelyScript is null)
            {
                throw new InvalidDataException("The likely subtags of " + property.Name + " are " + value + ", with no script.");
            }

            if (region is null)
            {
                if (!languages.TryAdd(language, likelyScript))
                {
                    throw new InvalidDataException("The likely subtags have the key " + property.Name + " twice.");
                }
            }
            else
            {
                regions.Add((language, region, likelyScript));
            }
        }

        var scripts = new SortedSet<string>(StringComparer.Ordinal);
        var keyed = new List<(uint Key, string Script)>();
        foreach ((string language, string script) in languages)
        {
            keyed.Add((Key(language, null), script));
        }

        int regionKeysLeftOut = 0;
        foreach ((string language, string region, string script) in regions)
        {
            if (languages.TryGetValue(language, out string? own) && own == script)
            {
                regionKeysLeftOut++;
                continue;
            }

            keyed.Add((Key(language, region), script));
        }

        foreach ((_, string script) in keyed)
        {
            scripts.Add(script);
        }

        if (scripts.Count > 256)
        {
            throw new InvalidDataException(Output.Number(scripts.Count) + " likely scripts do not fit an index of a byte.");
        }

        List<string> scriptList = [.. scripts];
        keyed.Sort((left, right) => left.Key.CompareTo(right.Key));
        var entries = new List<(uint Key, byte Script)>(keyed.Count);
        foreach ((uint key, string script) in keyed)
        {
            entries.Add((key, (byte)scriptList.IndexOf(script)));
        }

        return new LikelyScriptTable(scriptList, entries, sourceKeys, keysWithScript, regionKeysLeftOut);
    }

    // A key of likelySubtags.json, or a value: the language, the script, and the region (cldr-json writes them with hyphens).
    private static (string Language, string? Script, string? Region) Split(string tag)
    {
        string[] subtags = tag.Split('-');
        string? script = null;
        string? region = null;
        foreach (string subtag in subtags.Skip(1))
        {
            if (subtag.Length == 4 && subtag.All(char.IsAsciiLetter))
            {
                script = subtag;
            }
            else if ((subtag.Length == 2 && subtag.All(char.IsAsciiLetterUpper)) || (subtag.Length == 3 && subtag.All(char.IsAsciiDigit)))
            {
                region = subtag;
            }
            else
            {
                throw new InvalidDataException("The likely subtags tag " + tag + " has the subtag " + subtag + ".");
            }
        }

        // Add Likely Subtags removes Zzzz and ZZ before its lookup (UTS #35 part 1, "Likely Subtags", step 1), so a key with one
        // would never be found; XylemUI.Core's lookup counts on the table having none.
        if (script == "Zzzz" || region == "ZZ")
        {
            throw new InvalidDataException("The likely subtags tag " + tag + " has Zzzz or ZZ.");
        }

        return (subtags[0], script, region);
    }

    private static uint Key(string language, string? region)
    {
        if (language.Length is < 2 or > 3 || !language.All(char.IsAsciiLetterLower))
        {
            throw new InvalidDataException("The likely subtags language " + language + " is not 2 or 3 lower-case letters.");
        }

        uint packedLanguage = 0;
        for (int i = 0; i < 3; i++)
        {
            packedLanguage = (packedLanguage * 27) + (i < language.Length ? (uint)(language[i] - 'a' + 1) : 0);
        }

        uint packedRegion = region switch
        {
            null => 0,
            { Length: 2 } => 1 + (uint)(((region[0] - 'A') * 26) + (region[1] - 'A')),
            _ => 1000 + uint.Parse(region, NumberStyles.None, CultureInfo.InvariantCulture),
        };
        return (packedLanguage << 16) | packedRegion;
    }
}
