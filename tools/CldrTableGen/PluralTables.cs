using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// One rule of a set: a category and its condition, which the C# expression evaluates.
/// </summary>
/// <param name="Category">The category's name in CLDR (<c>one</c>).</param>
/// <param name="Condition">The condition as CLDR writes it, without its samples.</param>
/// <param name="Expression">The C# expression of the condition.</param>
internal sealed record PluralRule(string Category, string Condition, string Expression);

/// <summary>
/// A set of plural rules that one generated method selects with, shared by every locale whose rules are the same (design §4.11,
/// "Plural rules").
/// </summary>
/// <param name="Method">The method's name (<c>Cardinal3</c>).</param>
/// <param name="Rules">The rules other than <c>other</c>, in the order zero, one, two, few, many; a value no rule holds for is
/// <c>other</c>.</param>
/// <param name="Keys">The keys of plurals.json or ordinals.json that have this set.</param>
internal sealed record PluralRuleSet(string Method, IReadOnlyList<PluralRule> Rules, List<string> Keys);

/// <summary>
/// The plural rules of the entries: the sets of cardinal and ordinal rules, which set each entry selects with, and the samples of
/// each key for the tests.
/// </summary>
internal sealed class PluralTables
{
    private static readonly string[] Categories = ["zero", "one", "two", "few", "many", "other"];

    private PluralTables(
        List<PluralRuleSet> cardinal,
        List<PluralRuleSet> ordinal,
        Dictionary<string, (PluralRuleSet Cardinal, string CardinalKey, PluralRuleSet Ordinal, string OrdinalKey)> byTag,
        int pairs,
        SortedDictionary<string, List<(string Category, string Samples)>> cardinalSamples,
        SortedDictionary<string, List<(string Category, string Samples)>> ordinalSamples
    )
    {
        Cardinal = cardinal;
        Ordinal = ordinal;
        ByTag = byTag;
        Pairs = pairs;
        CardinalSamples = cardinalSamples;
        OrdinalSamples = ordinalSamples;
    }

    /// <summary>
    /// Gets the sets of cardinal rules, in the order their methods are numbered.
    /// </summary>
    public IReadOnlyList<PluralRuleSet> Cardinal { get; }

    /// <summary>
    /// Gets the sets of ordinal rules, in the order their methods are numbered.
    /// </summary>
    public IReadOnlyList<PluralRuleSet> Ordinal { get; }

    /// <summary>
    /// Gets, for the data tag of each entry and of root, its two sets and the keys of plurals.json and ordinals.json they come from.
    /// </summary>
    public IReadOnlyDictionary<string, (PluralRuleSet Cardinal, string CardinalKey, PluralRuleSet Ordinal, string OrdinalKey)> ByTag { get; }

    /// <summary>
    /// Gets the number of distinct pairs of a cardinal and an ordinal set among the entries and root.
    /// </summary>
    public int Pairs { get; }

    /// <summary>
    /// Gets the samples of each cardinal key an entry uses: each category with the text after its <c>@</c>.
    /// </summary>
    public IReadOnlyDictionary<string, List<(string Category, string Samples)>> CardinalSamples { get; }

    /// <summary>
    /// Gets the samples of each ordinal key an entry uses.
    /// </summary>
    public IReadOnlyDictionary<string, List<(string Category, string Samples)>> OrdinalSamples { get; }

    /// <summary>
    /// Reads the rules of every entry and of root.
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <param name="locales">The set of locales.</param>
    /// <returns>The tables.</returns>
    public static PluralTables Read(CldrZip cldr, LocaleSet locales)
    {
        JsonElement cardinalData = cldr.Supplemental("plurals").GetProperty("plurals-type-cardinal");
        JsonElement ordinalData = cldr.Supplemental("ordinals").GetProperty("plurals-type-ordinal");
        var cardinal = new List<PluralRuleSet>();
        var ordinal = new List<PluralRuleSet>();
        var cardinalByKey = new Dictionary<string, PluralRuleSet>(StringComparer.Ordinal);
        var ordinalByKey = new Dictionary<string, PluralRuleSet>(StringComparer.Ordinal);
        var cardinalSamples = new SortedDictionary<string, List<(string, string)>>(StringComparer.Ordinal);
        var ordinalSamples = new SortedDictionary<string, List<(string, string)>>(StringComparer.Ordinal);
        var byTag = new Dictionary<string, (PluralRuleSet, string, PluralRuleSet, string)>(StringComparer.Ordinal);
        var pairs = new HashSet<(string, string)>();
        Dictionary<string, string> parents = PluralParents(cldr);

        // Root first, then the entries in the order of their tags: the methods are numbered in the order of first use, so that
        // the output does not depend on the order of the json's keys.
        foreach (LocaleEntry entry in locales.Entries.Prepend(locales.Root))
        {
            if (byTag.ContainsKey(entry.DataTag))
            {
                continue;
            }

            string cardinalKey = Find(cardinalData, parents, entry.DataTag);
            string ordinalKey = Find(ordinalData, parents, entry.DataTag);
            PluralRuleSet cardinalSet = SetOf(cardinalData, cardinalKey, "Cardinal", cardinal, cardinalByKey, cardinalSamples);
            PluralRuleSet ordinalSet = SetOf(ordinalData, ordinalKey, "Ordinal", ordinal, ordinalByKey, ordinalSamples);
            byTag.Add(entry.DataTag, (cardinalSet, cardinalKey, ordinalSet, ordinalKey));
            pairs.Add((cardinalSet.Method, ordinalSet.Method));
        }

        return new PluralTables(cardinal, ordinal, byTag, pairs.Count, cardinalSamples, ordinalSamples);
    }

    // The parent locales of the plurals component (parentLocales.json, "plurals"), child to parent. They stand alone and are not
    // merged with the main ones (UTS #35 part 1, "Parent Locales"). CLDR 49 lists none; a release without the list has none either.
    private static Dictionary<string, string> PluralParents(CldrZip cldr)
    {
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        JsonElement all = cldr.Supplemental("parentLocales").GetProperty("parentLocales");
        if (all.TryGetProperty("plurals", out JsonElement plurals))
        {
            foreach (JsonProperty parent in plurals.EnumerateObject())
            {
                string notString = "The plural parent of " + parent.Name + " is not a string.";
                string named = parent.Value.GetString() ?? throw new InvalidDataException(notString);
                parents.Add(parent.Name, named == "root" ? "und" : named);
            }
        }

        return parents;
    }

    // The key whose rules a tag takes: the tag's own, or else its plural parent's when the list names one, or else the tag truncated
    // by its last subtag, until a key has rules; und, the rules of root, ends every path ("Following the inheritance path, using
    // parentLocale where available and otherwise truncating the locale, must always lead eventually to the root locale", UTS #35
    // part 1, "Parent Locales"). With no plural parents, pt-AO takes pt's rules, not those of pt-PT, its main parent.
    private static string Find(JsonElement data, Dictionary<string, string> parents, string tag)
    {
        string current = tag;
        for (int steps = 0; !data.TryGetProperty(current, out _); steps++)
        {
            if (steps > 32)
            {
                throw new InvalidDataException("The plural parents of " + tag + " go round in a cycle.");
            }

            if (parents.TryGetValue(current, out string? parent))
            {
                current = parent;
                continue;
            }

            int hyphen = current.LastIndexOf('-');
            if (hyphen < 0)
            {
                return data.TryGetProperty("und", out _) ? "und" : throw new InvalidDataException("The plural rules have no und (root).");
            }

            current = current[..hyphen];
        }

        return current;
    }

    private static PluralRuleSet SetOf(
        JsonElement data,
        string key,
        string kind,
        List<PluralRuleSet> sets,
        Dictionary<string, PluralRuleSet> byRules,
        SortedDictionary<string, List<(string Category, string Sample)>> samples
    )
    {
        var rules = new List<PluralRule>();
        var keySamples = new List<(string, string)>();
        bool hasOther = false;
        foreach (JsonProperty property in data.GetProperty(key).EnumerateObject())
        {
            const string Prefix = "pluralRule-count-";
            if (!property.Name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The plural rules of " + key + " have the key " + property.Name + ".");
            }

            string category = property.Name[Prefix.Length..];
            if (!Categories.Contains(category))
            {
                throw new InvalidDataException("The plural category " + category + " of " + key + " is not one of LDML's six.");
            }

            string text = property.Value.GetString() ?? string.Empty;
            int at = text.IndexOf('@', StringComparison.Ordinal);
            string condition = string.Join(' ', (at < 0 ? text : text[..at]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            keySamples.Add((category, at < 0 ? string.Empty : text[at..].Trim()));
            if (category == "other")
            {
                // A value no rule holds for is other, so other's own condition must be empty (LDML part 3, "Plural rules syntax").
                if (condition.Length != 0)
                {
                    throw new InvalidDataException("The rule other of " + key + " has the condition " + condition + ".");
                }

                hasOther = true;
                continue;
            }

            if (condition.Length == 0)
            {
                throw new InvalidDataException("The rule " + category + " of " + key + " has no condition.");
            }

            rules.Add(new PluralRule(category, condition, PluralRuleCompiler.Compile(condition)));
        }

        if (!hasOther)
        {
            throw new InvalidDataException("The plural rules of " + key + " have no rule other.");
        }

        rules.Sort((left, right) => Array.IndexOf(Categories, left.Category).CompareTo(Array.IndexOf(Categories, right.Category)));
        keySamples.Sort((left, right) => Array.IndexOf(Categories, left.Item1).CompareTo(Array.IndexOf(Categories, right.Item1)));
        samples.TryAdd(key, keySamples);
        string identity = string.Join("; ", rules.Select(rule => rule.Category + ": " + rule.Condition));
        if (!byRules.TryGetValue(identity, out PluralRuleSet? set))
        {
            set = new PluralRuleSet(kind + Output.Number(sets.Count), rules, []);
            sets.Add(set);
            byRules.Add(identity, set);
        }

        if (!set.Keys.Contains(key))
        {
            set.Keys.Add(key);
            set.Keys.Sort(StringComparer.Ordinal);
        }

        return set;
    }
}
