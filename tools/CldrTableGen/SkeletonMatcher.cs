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
    private readonly List<Candidate> _candidates = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SkeletonMatcher"/> class with a locale's formats, its standard patterns whose
    /// base skeleton no format has, and a canonical item for each symbol of <see cref="CanonicalItems"/> whose skeleton has no format.
    /// </summary>
    /// <param name="tag">The locale, for the messages.</param>
    /// <param name="availableFormats">The locale's <c>availableFormats</c>: each skeleton and its pattern. A skeleton with an
    /// alternative or a count (<c>hm-alt-ascii</c>, <c>yw-count-one</c>) and a skeleton with a symbol or a length the table of forms
    /// does not have (<c>MMMddd</c>) are not candidates.</param>
    /// <param name="standardPatterns">The locale's <c>dateFormats</c> and <c>timeFormats</c> (full, long, medium, short). Each is a
    /// candidate under the skeleton of its fields, as ICU's <c>DateTimePatternGenerator::addICUPatterns</c> adds them, unless a
    /// format or an earlier standard pattern has the same base skeleton (<see cref="Base"/>): ICU's <c>availableFormats</c> override a
    /// pattern of the same base. A pattern with a field the table of forms does not have, or with two fields of one kind, is not a
    /// candidate.</param>
    public SkeletonMatcher(string tag, IEnumerable<KeyValuePair<string, string>> availableFormats, IEnumerable<string> standardPatterns)
    {
        _tag = tag;
        foreach ((string skeleton, string pattern) in availableFormats)
        {
            if (!skeleton.Contains('-', StringComparison.Ordinal) && Parse(skeleton) is { } fields)
            {
                _candidates.Add(new Candidate(skeleton, pattern, fields));
            }
        }

        var bases = new HashSet<string>(_candidates.Select(candidate => Base(candidate.Fields)), StringComparer.Ordinal);
        foreach (string pattern in standardPatterns)
        {
            if (SkeletonOf(pattern) is { } skeleton && Parse(skeleton) is { } fields && bases.Add(Base(fields)))
            {
                _candidates.Add(new Candidate(skeleton, pattern, fields));
            }
        }

        foreach (char symbol in CanonicalItems)
        {
            string item = symbol.ToString();
            if (Parse(item) is { } own && !_candidates.Any(candidate => Distance(own, candidate.Fields) == 0))
            {
                _candidates.Add(new Candidate(item, item, own));
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
        Candidate closest = _candidates.MinBy(candidate => Distance(requested, candidate.Fields))!;
        int bestDistance = Distance(requested, closest.Fields);
        if (bestDistance >= MissingField)
        {
            throw new InvalidDataException(
                "No format of " + _tag + " has the fields of the skeleton " + skeleton + " and no other: the closest is " + closest.Skeleton + "."
            );
        }

        // Among the formats at the best distance, the one whose adjusted pattern is nearest the request wins, then the first in
        // ordinal order of its skeleton (design §4.11, "The same distance"; Issue #906). The ties are between MMMEEEEd and MMMMEd: a
        // yMMMEEEEd of EEEE, d/MM/y keeps its numeric month, and a yMMMMEd writes the wide one.
        string? best = null;
        string? bestSkeleton = null;
        int bestPatternDistance = int.MaxValue;
        foreach (Candidate candidate in _candidates)
        {
            if (Distance(requested, candidate.Fields) != bestDistance)
            {
                continue;
            }

            string pattern = Adjust(candidate.Pattern, requested, candidate.Fields);
            int patternDistance = PatternDistance(requested, pattern);
            if (
                patternDistance < bestPatternDistance
                || (patternDistance == bestPatternDistance && string.CompareOrdinal(candidate.Skeleton, bestSkeleton) < 0)
            )
            {
                best = pattern;
                bestSkeleton = candidate.Skeleton;
                bestPatternDistance = patternDistance;
            }
        }

        return best!;
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

    // The distance of a pattern's fields from the requested ones, by the forms the pattern writes; a field of a kind not requested
    // (the a of h:mm a) is not counted.
    private static int PatternDistance(Dictionary<DateField, Field> requested, string pattern)
    {
        int distance = 0;
        foreach ((char symbol, int length) in Fields(pattern))
        {
            if (FormOf(symbol, length) is { } form && requested.TryGetValue(form.Kind, out Field wanted))
            {
                distance += Math.Abs(wanted.Value - form.Value);
            }
        }

        return distance;
    }

    // The pattern with each field of a requested kind made as long as the request's (ICU's adjustFieldTypes, dtptngen.cpp). An
    // hour, a minute, or a second keeps its length; so does a field whose format already had the requested length, or whose pattern
    // is numeric where its format's skeleton is text or the other way round (ja's MMM is M月, so LLLL stays M月). A year, a month, a
    // weekday, and an hour keep the pattern's symbol (gd's Y, L, c, and the locale's hour); the day period and the zone take the
    // request's (a for zh-Hant's B, z for v). Text in quotes and the other characters are kept as they are.
    private static string Adjust(string pattern, Dictionary<DateField, Field> requested, Dictionary<DateField, Field> matched)
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
                Field own = matched[form.Kind];
                bool keep = own.Length == wanted.Length || (form.Value > 0) != (own.Value > 0);
                adjusted = keep ? length : wanted.Length;
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
    /// A format a skeleton may take: its skeleton, its pattern, and the skeleton's fields.
    /// </summary>
    private sealed record Candidate(string Skeleton, string Pattern, Dictionary<DateField, Field> Fields);

    /// <summary>
    /// One form of a symbol: a range of lengths and the value distances are counted from. A numeric form's value adds the length.
    /// </summary>
    private sealed record Form(char Symbol, DateField Kind, int MinimumLength, int MaximumLength, int Value);
}
