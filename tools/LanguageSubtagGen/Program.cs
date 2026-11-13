// Generates the tables of XylemUI.Core's Locale from the IANA Language Subtag Registry (design §4.11, "Locale"): the Preferred-Value
// of the deprecated languages and regions and of the grandfathered and redundant tags, and the Prefix of the extended languages.
// The registry is at https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry; download it and pass its
// path:
//
//   dotnet run --project tools/LanguageSubtagGen -c Release -- <path to language-subtag-registry>
//
// Writes src/XylemUI.Core/Localization/LocaleSubtags.g.cs with the registry's File-Date in it. The output is committed; the gate does
// not run this tool. The output is sorted and does not depend on the order of the registry's records.
using System.Globalization;
using System.Text;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: LanguageSubtagGen <path to language-subtag-registry>");
    return 1;
}

string root = FindRoot();
List<List<(string Name, string Body)>> records = ReadRecords(File.ReadAllText(args[0], Encoding.UTF8));
string fileDate = Required(records[0], "File-Date");
var languages = new SortedDictionary<string, string>(StringComparer.Ordinal);
var regions = new SortedDictionary<string, string>(StringComparer.Ordinal);
var extlangs = new SortedDictionary<string, string>(StringComparer.Ordinal);
var tags = new SortedDictionary<string, string>(StringComparer.Ordinal);
int grandfathered = 0;
int redundant = 0;
foreach (List<(string Name, string Body)> record in records.Skip(1))
{
    string type = Required(record, "Type");
    string? preferred = Field(record, "Preferred-Value");
    switch (type)
    {
        case "language" when preferred is not null:
            // Locale replaces the language subtag alone, so a Preferred-Value of several subtags would need another shape.
            if (!IsAlpha(preferred))
            {
                throw new InvalidOperationException($"The language {Required(record, "Subtag")} has the Preferred-Value {preferred}.");
            }

            languages.Add(Required(record, "Subtag").ToLowerInvariant(), preferred.ToLowerInvariant());
            break;
        case "region" when preferred is not null:
            if (!((preferred.Length == 2 && IsAlpha(preferred)) || (preferred.Length == 3 && IsDigits(preferred))))
            {
                throw new InvalidOperationException($"The region {Required(record, "Subtag")} has the Preferred-Value {preferred}.");
            }

            regions.Add(Required(record, "Subtag").ToUpperInvariant(), preferred.ToUpperInvariant());
            break;
        case "extlang":
            // Locale takes the extended language itself as the language when its one Prefix is the primary language, so the
            // table holds the Prefix alone; a registry that breaks either assumption stops the tool.
            string subtag = Required(record, "Subtag");
            List<string> prefixes = Fields(record, "Prefix");
            if (preferred is null || !string.Equals(preferred, subtag, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The extended language {subtag} has the Preferred-Value {preferred ?? "(none)"}.");
            }

            if (prefixes.Count != 1 || !IsAlpha(prefixes[0]))
            {
                throw new InvalidOperationException($"The extended language {subtag} has the Prefix {string.Join(", ", prefixes)}.");
            }

            extlangs.Add(subtag.ToLowerInvariant(), prefixes[0].ToLowerInvariant());
            break;
        case "script" when preferred is not null:
            // The design replaces deprecated languages and regions (§4.11); no script has had a Preferred-Value, and one would need
            // a decision first.
            throw new InvalidOperationException($"The script {Required(record, "Subtag")} has the Preferred-Value {preferred}.");
        case "grandfathered" or "redundant" when preferred is not null:
            tags.Add(Required(record, "Tag").ToLowerInvariant(), preferred);
            if (type == "grandfathered")
            {
                grandfathered++;
            }
            else
            {
                redundant++;
            }

            break;
        default:
            // Variants are dropped by Locale, so their Preferred-Value (heploc) is not read; the other records have none.
            break;
    }
}

// Locale parses a replacement as a tag of its own; a replacement that was itself a key would be replaced once and not again.
foreach (string replacement in tags.Values)
{
    if (tags.ContainsKey(replacement.ToLowerInvariant()))
    {
        throw new InvalidOperationException($"The tag {replacement} is both a key and a Preferred-Value.");
    }
}

string output = Path.Combine(root, "src", "XylemUI.Core", "Localization", "LocaleSubtags.g.cs");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, Emit(fileDate, languages, regions, extlangs, tags, grandfathered, redundant));
Console.WriteLine(
    $"File-Date {fileDate}: {languages.Count} languages, {regions.Count} regions, {extlangs.Count} extended languages, "
        + $"{grandfathered} grandfathered and {redundant} redundant tags"
);
Console.WriteLine($"wrote {output}");
return 0;

static string FindRoot()
{
    for (string? directory = Directory.GetCurrentDirectory(); directory is not null; directory = Path.GetDirectoryName(directory))
    {
        if (File.Exists(Path.Combine(directory, "XylemUI.slnx")))
        {
            return directory;
        }
    }

    throw new InvalidOperationException("Run the tool inside the repository: no XylemUI.slnx above the current directory.");
}

// The records of the registry (RFC 5646 §3.1.1): "%%" lines separate them, and a line that starts with a space continues the body
// of the field before it.
static List<List<(string Name, string Body)>> ReadRecords(string text)
{
    var records = new List<List<(string Name, string Body)>>();
    var fields = new List<(string Name, string Body)>();
    foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
    {
        if (line == "%%")
        {
            records.Add(fields);
            fields = [];
        }
        else if (line.StartsWith(' '))
        {
            (string name, string body) = fields[^1];
            fields[^1] = (name, body + " " + line.Trim());
        }
        else if (line.Length > 0)
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                throw new InvalidOperationException($"The registry line \"{line}\" has no field name.");
            }

            fields.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
    }

    records.Add(fields);
    return records;
}

static string? Field(List<(string Name, string Body)> record, string name)
{
    foreach ((string fieldName, string body) in record)
    {
        if (fieldName == name)
        {
            return body;
        }
    }

    return null;
}

static List<string> Fields(List<(string Name, string Body)> record, string name) =>
    [.. record.Where(field => field.Name == name).Select(field => field.Body)];

static string Required(List<(string Name, string Body)> record, string name) =>
    Field(record, name) ?? throw new InvalidOperationException($"A registry record has no {name} field.");

static bool IsAlpha(string value) => value.Length > 0 && value.All(char.IsAsciiLetter);

static bool IsDigits(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

// Every line ends with "\n" whatever the OS, so that the output is the same on each.
static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');

static string Emit(
    string fileDate,
    SortedDictionary<string, string> languages,
    SortedDictionary<string, string> regions,
    SortedDictionary<string, string> extlangs,
    SortedDictionary<string, string> tags,
    int grandfathered,
    int redundant
)
{
    var text = new StringBuilder();
    Line(text, "// <auto-generated>");
    Line(text, "// Generated by tools/LanguageSubtagGen from the IANA Language Subtag Registry of File-Date " + fileDate + ".");
    string counts =
        "// Preferred-Value: "
        + Count(languages.Count)
        + " languages, "
        + Count(regions.Count)
        + " regions, "
        + Count(grandfathered)
        + " grandfathered tags, "
        + Count(redundant)
        + " redundant tags. Prefix: "
        + Count(extlangs.Count)
        + " extended languages.";
    Line(text, counts);
    Line(text, "// Do not edit: run the tool again.");
    Line(text, "// </auto-generated>");

    // Generated code has no nullable context unless it says so, and the lookups return string?.
    Line(text, "#nullable enable");
    Line(text, string.Empty);
    Line(text, "using System.Diagnostics.CodeAnalysis;");
    Line(text, string.Empty);
    Line(text, "namespace XylemUI;");
    Line(text, string.Empty);
    Line(text, "/// <summary>");
    Line(text, "/// Looks up what <see cref=\"Locale\"/> reads from the IANA Language Subtag Registry (design §4.11, \"Locale\").");
    Line(text, "/// </summary>");
    Line(text, "/// <remarks>");
    Line(
        text,
        "/// Each table is a <see langword=\"switch\"/> on a span of constant strings: the compiler dispatches on the span itself, so a lookup"
    );
    Line(text, "/// allocates nothing, and the type has no static array to build when it is first used.");
    Line(text, "/// </remarks>");
    Line(text, "internal static class LocaleSubtags");
    Line(text, "{");
    Line(text, "    /// <summary>");
    Line(text, "    /// The File-Date of the registry the tables come from.");
    Line(text, "    /// </summary>");
    Line(text, "    public const string FileDate = \"" + fileDate + "\";");
    Line(text, string.Empty);
    Line(text, "    /// <summary>");
    Line(text, "    /// The length of the longest tag that <see cref=\"TryGetTagReplacement\"/> knows.");
    Line(text, "    /// </summary>");
    Line(text, "    public const int LongestTag = " + Count(tags.Keys.Max(key => key.Length)) + ";");
    AppendTable(
        text,
        "TryGetLanguageReplacement",
        "lowerLanguage",
        "replacement",
        "Finds the Preferred-Value of a deprecated language subtag.",
        "The language subtag, in lower case.",
        "The language subtag that replaces it, in lower case.",
        languages
    );
    AppendTable(
        text,
        "TryGetRegionReplacement",
        "upperRegion",
        "replacement",
        "Finds the Preferred-Value of a deprecated region subtag.",
        "The region subtag, in upper case.",
        "The region subtag that replaces it, in upper case.",
        regions
    );
    AppendTable(
        text,
        "TryGetExtlangPrefix",
        "lowerExtlang",
        "prefix",
        "Finds the Prefix of an extended language subtag, whose Preferred-Value is the subtag itself.",
        "The extended language subtag, in lower case.",
        "The language subtag it extends, in lower case.",
        extlangs
    );
    AppendTable(
        text,
        "TryGetTagReplacement",
        "lowerTag",
        "replacement",
        "Finds the Preferred-Value of a grandfathered or redundant tag, which replaces the whole tag (RFC 5646 §4.5, step 2).",
        "The whole tag, in lower case.",
        "The tag that replaces it, as the registry writes it.",
        tags
    );
    Line(text, "}");
    return text.ToString();
}

static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

static void AppendTable(
    StringBuilder text,
    string name,
    string key,
    string result,
    string summary,
    string keyDoc,
    string resultDoc,
    SortedDictionary<string, string> entries
)
{
    Line(text, string.Empty);
    Line(text, "    /// <summary>");
    Line(text, "    /// " + summary);
    Line(text, "    /// </summary>");
    Line(text, "    /// <param name=\"" + key + "\">" + keyDoc + "</param>");
    Line(text, "    /// <param name=\"" + result + "\">" + resultDoc + "</param>");
    Line(text, "    /// <returns><see langword=\"true\"/> when the registry has an entry for <paramref name=\"" + key + "\"/>.</returns>");
    Line(text, "    public static bool " + name + "(ReadOnlySpan<char> " + key + ", [NotNullWhen(true)] out string? " + result + ")");
    Line(text, "    {");
    Line(text, "        " + result + " = " + key + " switch");
    Line(text, "        {");
    foreach ((string entryKey, string value) in entries)
    {
        Line(text, "            \"" + entryKey + "\" => \"" + value + "\",");
    }

    Line(text, "            _ => null,");
    Line(text, "        };");
    Line(text, "        return " + result + " is not null;");
    Line(text, "    }");
}
