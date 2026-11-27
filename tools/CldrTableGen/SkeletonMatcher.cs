using System.Text;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// Finds the pattern of a skeleton among a locale's <c>availableFormats</c> and its standard date and time patterns (design §4.11,
/// "Skeleton formats"; UTS #35 part 4, "Matching Skeletons"): the format whose skeleton is closest, with its fields made as long as
/// the requested ones. The distance and the length rules are those of ICU's <c>DateTimePatternGenerator</c>, which UTS #35 describes
/// in prose: a field of another form of the same kind (<c>MMM</c> for <c>MMMM</c>, <c>v</c> for <c>z</c>) is near, a missing field is
/// far, and a field the request does not have is farther. A requested field that the closest format does not have, or a closest
/// format with a field that was not requested, stops the tool: no skeleton of the 41 needs the <c>appendItems</c> in CLDR 49. An
/// instance holds one locale's formats.
/// </summary>
internal sealed class SkeletonMatcher
{
    // The distances of ICU's DateTimePatternGenerator (ICU main, icu4c/source/i18n/dtptngen_impl.h and dtptngen.cpp, read on
    // 2026-11-21; design §4.11, "Nearness and lengths"): a text form is a negative constant and a numeric form 0x100 and its length,
    // so that forms of one kind are near and text is far from numbers.
    private const int Numeric = 0x100;
    private const int Narrow = -0x101;
    private const int Shorter = -0x102;
    private const int Short = -0x103;
    private const int Long = -0x104;
    private const int Delta = 0x10;
    private const int MissingField = 0x1000;
    private const int ExtraField = 0x10000;

    // ICU adds one format of each of these symbols, the symbol alone, for a skeleton the locale has no format of
    // (DateTimePatternGenerator's Canonical_Items, ICU main, icu4c/source/i18n/dtptngen.cpp, lines 140 to 145): QQQ takes Q, and s
    // takes s.
    private const string CanonicalItems = "GyQMwWEDFdaHmsSv";

    // The forms of each symbol: its kind, the lengths, and the distance value (UTS #35 part 4, "Date Field Symbol Table").
    private static readonly Form[] Forms =
    [
        new('G', DateField.Era, 1, 3, Short),
        new('G', DateField.Era, 4, 4, Long),
        new('G', DateField.Era, 5, 5, Narrow),
        new('y', DateField.Year, 1, 20, Numeric),
        new('Y', DateField.Year, 1, 20, Numeric + Delta),
        new('u', DateField.Year, 1, 20, Numeric + (2 * Delta)),
        new('Q', DateField.Quarter, 1, 2, Numeric),
        new('Q', DateField.Quarter, 3, 3, Short),
        new('Q', DateField.Quarter, 4, 4, Long),
        new('Q', DateField.Quarter, 5, 5, Narrow),
        new('q', DateField.Quarter, 1, 2, Numeric + Delta),
        new('q', DateField.Quarter, 3, 3, Short - Delta),
        new('q', DateField.Quarter, 4, 4, Long - Delta),
        new('q', DateField.Quarter, 5, 5, Narrow - Delta),
        new('M', DateField.Month, 1, 2, Numeric),
        new('M', DateField.Month, 3, 3, Short),
        new('M', DateField.Month, 4, 4, Long),
        new('M', DateField.Month, 5, 5, Narrow),
        new('L', DateField.Month, 1, 2, Numeric + Delta),
        new('L', DateField.Month, 3, 3, Short - Delta),
        new('L', DateField.Month, 4, 4, Long - Delta),
        new('L', DateField.Month, 5, 5, Narrow - Delta),
        new('E', DateField.Weekday, 1, 3, Short),
        new('E', DateField.Weekday, 4, 4, Long),
        new('E', DateField.Weekday, 5, 5, Narrow),
        new('E', DateField.Weekday, 6, 6, Shorter),
        new('c', DateField.Weekday, 1, 2, Numeric + (2 * Delta)),
        new('c', DateField.Weekday, 3, 3, Short - (2 * Delta)),
        new('c', DateField.Weekday, 4, 4, Long - (2 * Delta)),
        new('c', DateField.Weekday, 5, 5, Narrow - (2 * Delta)),
        new('c', DateField.Weekday, 6, 6, Shorter - (2 * Delta)),
        new('e', DateField.Weekday, 1, 2, Numeric + Delta),
        new('e', DateField.Weekday, 3, 3, Short - Delta),
        new('e', DateField.Weekday, 4, 4, Long - Delta),
        new('e', DateField.Weekday, 5, 5, Narrow - Delta),
        new('e', DateField.Weekday, 6, 6, Shorter - Delta),
        new('d', DateField.Day, 1, 2, Numeric),
        new('a', DateField.DayPeriod, 1, 3, Short),
        new('a', DateField.DayPeriod, 4, 4, Long),
        new('a', DateField.DayPeriod, 5, 5, Narrow),
        new('b', DateField.DayPeriod, 1, 3, Short - Delta),
        new('b', DateField.DayPeriod, 4, 4, Long - Delta),
        new('b', DateField.DayPeriod, 5, 5, Narrow - Delta),
        new('B', DateField.DayPeriod, 1, 3, Short - (3 * Delta)),
        new('B', DateField.DayPeriod, 4, 4, Long - (3 * Delta)),
        new('B', DateField.DayPeriod, 5, 5, Narrow - (3 * Delta)),
        new('h', DateField.Hour, 1, 2, Numeric),
        new('K', DateField.Hour, 1, 2, Numeric + Delta),
        new('H', DateField.Hour, 1, 2, Numeric + (10 * Delta)),
        new('k', DateField.Hour, 1, 2, Numeric + (11 * Delta)),
        new('m', DateField.Minute, 1, 2, Numeric),
        new('s', DateField.Second, 1, 2, Numeric),
        new('z', DateField.Zone, 1, 3, Short),
        new('z', DateField.Zone, 4, 4, Long),
        new('v', DateField.Zone, 1, 1, Short - (2 * Delta)),
        new('v', DateField.Zone, 4, 4, Long - (2 * Delta)),
    ];

    private readonly string _tag;

    // ICU's PatternMap (dtptngen.cpp): the entries under the first letter of their base skeleton, A to Z and then a to z, each list
    // in the order the entries were added. Match keeps the first entry at the best distance in this order, as
    // DateTimePatternGenerator::getBestRaw does (Issue #906).
    private readonly List<Candidate>?[] _entries = new List<Candidate>?[52];

    /// <summary>
    /// Initializes a new instance of the <see cref="SkeletonMatcher"/> class with a locale's formats, in the order and with the
    /// conflict rules of ICU's <c>DateTimePatternGenerator</c>: a canonical item for each symbol of <see cref="CanonicalItems"/>
    /// (<c>addCanonicalItems</c>), then the standard patterns (<c>addICUPatterns</c>), then the <c>availableFormats</c>
    /// (<c>addCLDRData</c>).
    /// </summary>
    /// <param name="tag">The locale, for the messages.</param>
    /// <param name="availableFormats">The locale's <c>availableFormats</c>, taken in byte order of the skeletons: each skeleton and its pattern, which replace an entry of the
    /// same base skeleton and the same forms. A skeleton with an alternative or a count (<c>hm-alt-ascii</c>, <c>yw-count-one</c>) and
    /// a skeleton with a symbol or a length the table of forms does not have (<c>MMMddd</c>) are not candidates.</param>
    /// <param name="standardPatterns">The locale's <c>timeFormats</c> and then its <c>dateFormats</c> (full, long, medium, short), the
    /// order of ICU's <c>DateTimePatterns</c>. Each is an entry under the skeleton of its fields, unless an earlier canonical item or
    /// standard pattern has the same base skeleton (<see cref="Base"/>). A pattern with a field the table of forms does not have, or
    /// with two fields of one kind, is not a candidate.</param>
    public SkeletonMatcher(string tag, IEnumerable<KeyValuePair<string, string>> availableFormats, IEnumerable<string> standardPatterns)
    {
        _tag = tag;
        foreach (char symbol in CanonicalItems)
        {
            string item = symbol.ToString();
            if (Parse(item) is { } fields)
            {
                Add(new Candidate(item, item, fields, Base(fields), Specified: false), overrides: false);
            }
        }

        foreach (string pattern in standardPatterns)
        {
            if (SkeletonOf(pattern) is { } skeleton && Parse(skeleton) is { } fields)
            {
                Add(new Candidate(skeleton, pattern, fields, Base(fields), Specified: false), overrides: false);
            }
        }

        // ICU reads a locale's availableFormats from its resource table, whose keys are in byte order.
        foreach ((string skeleton, string pattern) in availableFormats.OrderBy(format => format.Key, StringComparer.Ordinal))
        {
            if (!skeleton.Contains('-', StringComparison.Ordinal) && Parse(skeleton) is { } fields)
            {
                Add(new Candidate(skeleton, pattern, fields, Base(fields), Specified: true), overrides: true);
            }
        }
    }

    /// <summary>
    /// The kinds of field of a skeleton; a skeleton has at most one field of each.
    /// </summary>
    private enum DateField : byte
    {
        Era,
        Year,
        Quarter,
        Month,
        Weekday,
        Day,
        DayPeriod,
        Hour,
        Minute,
        Second,
        Zone,
    }

    /// <summary>
    /// Returns the pattern of a skeleton.
    /// </summary>
    /// <param name="skeleton">The skeleton, with <c>j</c> already replaced by an hour symbol.</param>
    /// <returns>The pattern.</returns>
    /// <exception cref="InvalidDataException">The closest format lacks a requested field or has another one.</exception>
    public string Match(string skeleton)
    {
        Dictionary<DateField, Field> requested =
            Parse(skeleton) ?? throw new InvalidDataException("The skeleton " + skeleton + " has a field the tool does not know.");

        // The first entry at the best distance in the order of the map wins (design §4.11, "The same distance"; Issue #906). ICU also
        // prefers, at the same distance, the larger mask of missing fields; below MissingField no field is missing.
        Candidate? closest = null;
        int bestDistance = int.MaxValue;
        foreach (List<Candidate>? entries in _entries)
        {
            foreach (Candidate candidate in entries ?? [])
            {
                int distance = Distance(requested, candidate.Fields);
                if (distance < bestDistance && YearLengthIsCompatible(requested, candidate.Fields))
                {
                    closest = candidate;
                    bestDistance = distance;
                }
            }
        }

        if (closest is null || bestDistance >= MissingField)
        {
            throw new InvalidDataException(
                "No format of " + _tag + " has the fields of the skeleton " + skeleton + " and no other: the closest is " + closest?.Skeleton + "."
            );
        }

        return Adjust(closest.Pattern, requested, closest.Specified ? closest.Fields : null);
    }

    /// <summary>
    /// Returns the fields of a pattern: each run of one ASCII letter outside quotes. Two quotes in a row are a quote character and do
    /// not change the quoting.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The symbol and the length of each field, in order.</returns>
    internal static List<(char Symbol, int Length)> Fields(string pattern)
    {
        var fields = new List<(char, int)>();
        bool quoted = false;
        int i = 0;
        while (i < pattern.Length)
        {
            char character = pattern[i];
            int length = 1;
            while (i + length < pattern.Length && pattern[i + length] == character)
            {
                length++;
            }

            if (character == '\'')
            {
                quoted ^= length % 2 == 1;
            }
            else if (!quoted && char.IsAsciiLetter(character))
            {
                fields.Add((character, length));
            }

            i += length;
        }

        return fields;
    }

    // Adds an entry as ICU's addPatternWithSkeleton does (dtptngen.cpp). An entry that does not override (a canonical item or a
    // standard pattern) is dropped when the list has one of its base that was not given a skeleton; an entry is dropped when the list
    // has its skeleton and it does not override, or both were given their skeletons. Otherwise an entry of the same base and forms is
    // replaced in place, with the new pattern and the old skeleton, as PatternMap::add does; and any other is added at the end.
    private void Add(Candidate candidate, bool overrides)
    {
        List<Candidate> entries = _entries[BootIndex(candidate.Base[0])] ??= [];
        Candidate? sameBase = entries.Find(entry => entry.Base == candidate.Base);
        if (!overrides && sameBase is not null && (!sameBase.Specified || candidate.Specified))
        {
            return;
        }

        Candidate? sameSkeleton = entries.Find(entry => SameSkeleton(entry.Fields, candidate.Fields));
        if (sameSkeleton is not null && (!overrides || (candidate.Specified && sameSkeleton.Specified)))
        {
            return;
        }

        int duplicate = entries.FindIndex(entry => SameForms(entry, candidate));
        if (duplicate >= 0)
        {
            entries[duplicate] = entries[duplicate] with { Pattern = candidate.Pattern, Specified = candidate.Specified };
        }
        else
        {
            entries.Add(candidate);
        }
    }

    // The distance of a format's fields from the requested ones (ICU's DateTimeMatcher::getDistance, dtptngen.cpp): the difference
    // of the values of a field both have, MissingField for a requested field it lacks, and ExtraField for a field it adds.
    private static int Distance(Dictionary<DateField, Field> requested, Dictionary<DateField, Field> candidate)
    {
        int distance = 0;
        foreach ((DateField kind, Field field) in requested)
        {
            distance += candidate.TryGetValue(kind, out Field other) ? Math.Abs(field.Value - other.Value) : MissingField;
        }

        foreach (DateField kind in candidate.Keys)
        {
            if (!requested.ContainsKey(kind))
            {
                distance += ExtraField;
            }
        }

        return distance;
    }

    // A request with a numeric year that is not of two digits does not take a skeleton with a two-digit year, whose pattern can
    // have text that fits that year only (en's yyMMMd, MMM d, ’yy; yearLengthIsCompatible in ICU main's dtptngen.cpp, which ICU 78
    // does not have; CLDR 49's tables are the same either way).
    private static bool YearLengthIsCompatible(Dictionary<DateField, Field> requested, Dictionary<DateField, Field> candidate) =>
        !requested.TryGetValue(DateField.Year, out Field wanted)
        || !candidate.TryGetValue(DateField.Year, out Field year)
        || year.Length != 2
        || wanted.Length == 2;

    // The index of a base skeleton's first letter among A to Z and a to z, as ICU's PatternMap keeps its lists.
    private static int BootIndex(char letter) => char.IsAsciiLetterUpper(letter) ? letter - 'A' : 26 + (letter - 'a');

    // Two skeletons are one when each kind has the same symbol and length (ICU compares PtnSkeleton's original).
    private static bool SameSkeleton(Dictionary<DateField, Field> one, Dictionary<DateField, Field> other) =>
        one.Count == other.Count && one.All(pair => other.TryGetValue(pair.Key, out Field field) && field == pair.Value);

    // Two entries are duplicates when they have the same base and each kind the same form (ICU's PatternMap::getDuplicateElem
    // compares the type of each field, so E and EEE are one).
    private static bool SameForms(Candidate one, Candidate other) =>
        one.Base == other.Base
        && one.Fields.Count == other.Fields.Count
        && one.Fields.All(pair => other.Fields.TryGetValue(pair.Key, out Field field) && field.Value == pair.Value.Value);

    // The pattern with each field of a requested kind made as long as the request's (ICU's adjustFieldTypes, dtptngen.cpp); a
    // requested E of one or two letters counts as EEE. An hour, a minute, or a second keeps its length. When the entry was added
    // with its skeleton (matched is not null), so does a field whose skeleton already had the requested length, or whose pattern is
    // numeric where the request is text or the other way round (ja's MMM is M月, so LLLL stays M月). A year, a month, a weekday, and
    // an hour keep the pattern's symbol (gd's Y, L, c, and the locale's hour); the day period and the zone take the request's (a for
    // zh-Hant's B, z for v). Text in quotes and the other characters are kept as they are.
    private static string Adjust(string pattern, Dictionary<DateField, Field> requested, Dictionary<DateField, Field>? matched)
    {
        var text = new StringBuilder();
        bool quoted = false;
        int i = 0;
        while (i < pattern.Length)
        {
            char character = pattern[i];
            if (character == '\'')
            {
                quoted = !quoted;
                text.Append(character);
                i++;
                continue;
            }

            int length = 1;
            while (i + length < pattern.Length && pattern[i + length] == character)
            {
                length++;
            }

            if (quoted || !char.IsAsciiLetter(character))
            {
                text.Append(pattern, i, length);
                i += length;
                continue;
            }

            Form form =
                FormOf(character, length) ?? throw new InvalidDataException("The pattern " + pattern + " has a field the tool does not know.");
            char symbol = character;
            int adjusted = length;

            // The day period takes the requested letter, as ICU's adjustFieldTypes writes it (ICU main, dtptngen.cpp, read on
            // 2026-11-21; Issue #906): j asks for a, so zh-Hant's Bh:mm is written ah:mm (上午7:03).
            bool ownSymbol = form.Kind is DateField.Year or DateField.Month or DateField.Weekday;
            if (requested.TryGetValue(form.Kind, out Field wanted) && form.Kind is not (DateField.Hour or DateField.Minute or DateField.Second))
            {
                // ICU: "1-3 for E are equivalent to 3 for c,e". The 41 skeletons ask for no c or e, whose lengths ICU always adjusts.
                int wantedLength = wanted.Symbol == 'E' && wanted.Length < 3 ? 3 : wanted.Length;
                bool keep = matched is not null && (matched[form.Kind].Length == wantedLength || (form.Value > 0) != (wanted.Value > 0));
                adjusted = keep ? length : wantedLength;
                symbol = ownSymbol ? character : wanted.Symbol;
            }

            text.Append(symbol, adjusted);
            i += length;
        }

        return text.ToString();
    }

    // The fields of a skeleton, or null when it has a symbol or a length the table does not have, or two fields of one kind. A
    // 12-hour field with no day period gets a, and a 24-hour field drops one (ICU's DateTimeMatcher::set, dtptngen.cpp).
    private static Dictionary<DateField, Field>? Parse(string skeleton)
    {
        var fields = new Dictionary<DateField, Field>();
        int i = 0;
        while (i < skeleton.Length)
        {
            char symbol = skeleton[i];
            int length = 1;
            while (i + length < skeleton.Length && skeleton[i + length] == symbol)
            {
                length++;
            }

            i += length;
            if (FormOf(symbol, length) is not { } form || !fields.TryAdd(form.Kind, new Field(symbol, length, form.Value)))
            {
                return null;
            }
        }

        if (fields.TryGetValue(DateField.Hour, out Field hour))
        {
            if (hour.Symbol is 'h' or 'K')
            {
                _ = fields.TryAdd(DateField.DayPeriod, new Field('a', 1, Short));
            }
            else
            {
                _ = fields.Remove(DateField.DayPeriod);
            }
        }

        return fields;
    }

    // The skeleton of a pattern's fields, in the order of the kinds (EEEE d MMMM, y is yMMMMEEEEd); null when a field is not in
    // the table of forms or two fields are of one kind.
    private static string? SkeletonOf(string pattern)
    {
        var fields = new SortedDictionary<DateField, string>();
        foreach ((char symbol, int length) in Fields(pattern))
        {
            if (FormOf(symbol, length) is not { } form || !fields.TryAdd(form.Kind, new string(symbol, length)))
            {
                return null;
            }
        }

        return string.Concat(fields.Values);
    }

    /// <summary>
    /// Returns the base skeleton of a skeleton's fields, as ICU's <c>DateTimeMatcher::set</c> makes its <c>baseOriginal</c>: each
    /// field's symbol as many times as the shortest length of its form, so that lengths of one form are one base (<c>M</c> and
    /// <c>MM</c> are <c>M</c>, <c>E</c> and <c>EEE</c> are <c>E</c>) and forms are not (<c>MMM</c>, <c>MMMM</c>, <c>LLLL</c>).
    /// </summary>
    /// <param name="fields">The fields, a 12-hour field with its <c>a</c>.</param>
    /// <returns>The base skeleton, in the order of the kinds.</returns>
    private static string Base(Dictionary<DateField, Field> fields) =>
        string.Concat(fields.OrderBy(pair => pair.Key).Select(pair => BaseField(pair.Value)));

    private static string BaseField(Field field) => new(field.Symbol, FormOf(field.Symbol, field.Length)!.MinimumLength);

    private static Form? FormOf(char symbol, int length)
    {
        foreach (Form form in Forms)
        {
            if (form.Symbol == symbol && length >= form.MinimumLength && length <= form.MaximumLength)
            {
                return form with { Value = form.Value > 0 ? form.Value + length : form.Value };
            }
        }

        return null;
    }

    /// <summary>
    /// One field of a skeleton: its symbol, its length, and its form's value.
    /// </summary>
    private readonly record struct Field(char Symbol, int Length, int Value);

    /// <summary>
    /// A format a skeleton may take: its skeleton, its pattern, the skeleton's fields and base, and whether the skeleton was given
    /// (an <c>availableFormats</c> item) or made from the pattern (a canonical item or a standard pattern).
    /// </summary>
    private sealed record Candidate(string Skeleton, string Pattern, Dictionary<DateField, Field> Fields, string Base, bool Specified);

    /// <summary>
    /// One form of a symbol: a range of lengths and the value distances are counted from. A numeric form's value adds the length.
    /// </summary>
    private sealed record Form(char Symbol, DateField Kind, int MinimumLength, int MaximumLength, int Value);
}
