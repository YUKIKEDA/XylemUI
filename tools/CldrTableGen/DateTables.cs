using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// The date tables of the entries (design §4.11, "Dates and times"): for each locale, the names of its Gregorian calendar, the
/// patterns of Flutter's 41 skeletons with what joins and writes them, and how the skeletons write a range of two dates. Locales whose
/// bytes are the same share one table. The bytes are read by <c>DateNames</c>, <c>DateFormatter</c>, and <c>DateRangeFormat</c> in
/// XylemUI.Core.
/// </summary>
/// <remarks>
/// A name table is, for the format and then the stand-alone context, the months (abbreviated, wide, narrow; 12 each), the weekdays
/// (abbreviated, wide, narrow, short; Sunday first), the quarters (abbreviated, wide, narrow; 4 each), and the day periods
/// (abbreviated, wide, narrow; am and pm), then the eras (abbreviated, wide, narrow; before and in the common era): 170 strings. A
/// stand-alone name that is its format name is written empty. A pattern table is the digit zero of the locale's default numbering
/// system (2 bytes, little-endian); the patterns of the 41 skeletons in the order of <see cref="Skeletons"/>, a <c>j</c> skeleton's
/// with the hour of the locale's language and region (CLDR's <c>timeData</c>); the 24-hour patterns of the 7 <c>j</c> skeletons,
/// empty when the pattern is the one before; the patterns that join a date and a time (<c>dateTimeFormats</c>: full, long, medium,
/// short) and the <c>Time-Day-Of-Week</c> append item, with their letters quoted; and <c>gmtFormat</c> and <c>hourFormat</c>. A
/// string is a length byte and that many bytes of UTF-8.
/// <para>
/// An interval table (design §4.11, "Interval formats") is <c>intervalFormatFallback</c>, then an entry for each of the 37 skeletons
/// without a time zone in the order of <see cref="Skeletons"/> (a <c>j</c> skeleton's in the hour of the locale), then one for the
/// 24-hour form of <c>j</c>, <c>jm</c>, and <c>jms</c>. An entry is a count byte, 255 and nothing more for a 24-hour entry that is
/// the skeleton's entry in the hour of the locale; then a byte for each field the dates can first differ in (the year, the month, the day, am or pm, the hour, the
/// minute, the second): 0 writes the earlier date alone with the skeleton's pattern, 1 writes both with it and joins them with the
/// fallback, and 2 + k takes the entry's record k; then the records, as many as the count, each a kind byte and its strings: 0 is a
/// pattern split into the part of the earlier date and the part of the later, 1 the same with the later date's part first (each a
/// string; the second may be empty), and 2 a pattern that writes both dates, joined by the fallback (one string). A record that two
/// fields take is written once.
/// </para>
/// </remarks>
internal sealed class DateTables
{
    // The kinds of record of an interval table, and the field bytes that take no record.
    private const byte SplitEarlierFirst = 0;
    private const byte SplitLaterFirst = 1;
    private const byte FallbackRecord = 2;
    private const byte SingleField = 0;
    private const byte OwnFallbackField = 1;
    private const byte FirstRecordField = 2;
    private const byte SameEntry = 255;

    /// <summary>
    /// Flutter's 41 skeletons, in the order of <c>validDateFormats</c> (Flutter 3.47.8,
    /// <c>packages/flutter_tools/lib/src/localizations/gen_l10n_types.dart</c>, lines 31 to 73).
    /// </summary>
    public static readonly string[] Skeletons =
    [
        "d",
        "E",
        "EEEE",
        "LLL",
        "LLLL",
        "M",
        "Md",
        "MEd",
        "MMM",
        "MMMd",
        "MMMEd",
        "MMMM",
        "MMMMd",
        "MMMMEEEEd",
        "QQQ",
        "QQQQ",
        "y",
        "yM",
        "yMd",
        "yMEd",
        "yMMM",
        "yMMMd",
        "yMMMEd",
        "yMMMM",
        "yMMMMd",
        "yMMMMEEEEd",
        "yQQQ",
        "yQQQQ",
        "H",
        "Hm",
        "Hms",
        "j",
        "jm",
        "jms",
        "jmv",
        "jmz",
        "jv",
        "jz",
        "m",
        "ms",
        "s",
    ];

    private static readonly string[] Widths = ["abbreviated", "wide", "narrow"];

    private static readonly string[] WeekdayWidths = ["abbreviated", "wide", "narrow", "short"];

    private static readonly string[] Weekdays = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    private static readonly string[] Lengths = ["full", "long", "medium", "short"];

    private DateTables(
        List<byte[]> names,
        List<byte[]> patterns,
        List<byte[]> intervals,
        Dictionary<string, (int Names, int Patterns, int Intervals)> byTag
    )
    {
        Names = names;
        Patterns = patterns;
        Intervals = intervals;
        ByTag = byTag;
    }

    /// <summary>
    /// Gets the distinct name tables, in the order of their first use (root first, then the entries in the order of their tags).
    /// </summary>
    public IReadOnlyList<byte[]> Names { get; }

    /// <summary>
    /// Gets the distinct pattern tables, in the same order.
    /// </summary>
    public IReadOnlyList<byte[]> Patterns { get; }

    /// <summary>
    /// Gets the distinct interval tables, in the same order.
    /// </summary>
    public IReadOnlyList<byte[]> Intervals { get; }

    /// <summary>
    /// Gets, for the data tag of each entry and of root, the index of its name, pattern, and interval tables.
    /// </summary>
    public IReadOnlyDictionary<string, (int Names, int Patterns, int Intervals)> ByTag { get; }

    /// <summary>
    /// Reads the tables of every entry and of root.
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <param name="locales">The set of locales.</param>
    /// <returns>The tables.</returns>
    public static DateTables Read(CldrZip cldr, LocaleSet locales)
    {
        JsonElement timeData = cldr.Supplemental("timeData").GetProperty("timeData");
        var names = new List<byte[]>();
        var patterns = new List<byte[]>();
        var intervals = new List<byte[]>();
        var nameIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var patternIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var intervalIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var byTag = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        foreach (LocaleEntry entry in locales.Entries.Prepend(locales.Root))
        {
            string tag = entry.DataTag;
            if (byTag.ContainsKey(tag))
            {
                continue;
            }

            JsonElement gregorian = cldr.Json("cldr-dates-full/main/" + tag + "/ca-gregorian.json")
                .GetProperty("main")
                .GetProperty(tag)
                .GetProperty("dates")
                .GetProperty("calendars")
                .GetProperty("gregorian");
            JsonElement zones = cldr.Json("cldr-dates-full/main/" + tag + "/timeZoneNames.json")
                .GetProperty("main")
                .GetProperty(tag)
                .GetProperty("dates")
                .GetProperty("timeZoneNames");
            char hour = HourSymbol(tag, entry.Region, timeData);
            SkeletonMatcher matcher = Matcher(tag, gregorian);
            (byte[] patternTable, List<string> written) = PatternTable(tag, gregorian, zones, matcher, hour, NumberTables.Zero(cldr, tag));
            byte[] intervalTable = IntervalTable(tag, gregorian, matcher, hour, written);
            int nameAt = TableBytes.Index(NameTable(tag, gregorian), names, nameIndex);
            int patternAt = TableBytes.Index(patternTable, patterns, patternIndex);
            int intervalAt = TableBytes.Index(intervalTable, intervals, intervalIndex);
            byTag.Add(tag, (nameAt, patternAt, intervalAt));
        }

        return new DateTables(names, patterns, intervals, byTag);
    }

    /// <summary>
    /// Returns a pattern with each placeholder once: <c>{0}</c> and <c>{1}</c> in a pattern that joins two (<c>dateTimeFormats</c>,
    /// the <c>Time-Day-Of-Week</c> append item, and <c>intervalFormatFallback</c>), <c>{0}</c> in <c>gmtFormat</c>.
    /// </summary>
    /// <param name="tag">The locale, for the message.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="placeholders">The placeholders it must have once each.</param>
    /// <returns>The pattern.</returns>
    /// <exception cref="InvalidDataException">A placeholder is not there once.</exception>
    internal static string Placeholders(string tag, string pattern, params ReadOnlySpan<string> placeholders)
    {
        foreach (string placeholder in placeholders)
        {
            int at = pattern.IndexOf(placeholder, StringComparison.Ordinal);
            if (at < 0 || pattern.IndexOf(placeholder, at + 1, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException("The pattern " + pattern + " of " + tag + " does not have " + placeholder + " once.");
            }
        }

        return pattern;
    }

    // The skeleton formats of a locale: its availableFormats, and its time and date patterns, which ICU's DateTimePatterns hold the
    // 4 time patterns before the 4 date patterns of, and addICUPatterns adds in that order.
    private static SkeletonMatcher Matcher(string tag, JsonElement gregorian)
    {
        IEnumerable<KeyValuePair<string, string>> available = gregorian
            .GetProperty("dateTimeFormats")
            .GetProperty("availableFormats")
            .EnumerateObject()
            .Select(format => KeyValuePair.Create(format.Name, format.Value.GetString() ?? string.Empty));
        IEnumerable<string> standard = Lengths
            .Select(length => TableBytes.Text(tag, gregorian.GetProperty("timeFormats"), length))
            .Concat(Lengths.Select(length => TableBytes.Text(tag, gregorian.GetProperty("dateFormats"), length)));
        return new SkeletonMatcher(tag, available, standard);
    }

    // The interval table of a locale (the remarks of the class): the entries of the 37 skeletons without a time zone, then the
    // 24-hour ones of j, jm, and jms; written holds the 41 patterns and the 7 24-hour ones of the pattern table.
    private static byte[] IntervalTable(string tag, JsonElement gregorian, SkeletonMatcher matcher, char hour, List<string> written)
    {
        JsonElement dateTimeFormats = gregorian.GetProperty("dateTimeFormats");
        string shortJoining = Joining(tag, TableBytes.Text(tag, dateTimeFormats, "short"));
        var intervals = new IntervalMatcher(tag, dateTimeFormats.GetProperty("intervalFormats"), matcher, shortJoining);
        var table = new List<byte>();
        TableBytes.Add(table, intervals.Fallback);
        var own = new Dictionary<string, IntervalSlot[]>(StringComparer.Ordinal);
        for (int i = 0; i < Skeletons.Length; i++)
        {
            string skeleton = Skeletons[i];
            if (!HasZone(skeleton))
            {
                IntervalSlot[] slots = intervals.Match(skeleton.Replace('j', hour), written[i]);
                own.Add(skeleton, slots);
                IntervalEntry(table, tag, slots);
            }
        }

        string[] hourSkeletons = [.. Skeletons.Where(skeleton => skeleton.Contains('j', StringComparison.Ordinal))];
        for (int i = 0; i < hourSkeletons.Length; i++)
        {
            string skeleton = hourSkeletons[i];
            if (!HasZone(skeleton))
            {
                IntervalSlot[] slots = intervals.Match(skeleton.Replace('j', 'H'), written[Skeletons.Length + i]);
                if (slots.SequenceEqual(own[skeleton]))
                {
                    table.Add(SameEntry);
                }
                else
                {
                    IntervalEntry(table, tag, slots);
                }
            }
        }

        return [.. table];
    }

    // A range takes no offset, so the skeletons with a time zone have no entry (design §4.11, "The table").
    private static bool HasZone(string skeleton) =>
        skeleton.Contains('v', StringComparison.Ordinal) || skeleton.Contains('z', StringComparison.Ordinal);

    // One entry of an interval table: the count of records, a byte for each field, and the records.
    private static void IntervalEntry(List<byte> table, string tag, IntervalSlot[] slots)
    {
        var records = new List<IntervalSlot>();
        var fields = new byte[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            IntervalSlot slot = slots[i];
            if (slot.Kind == IntervalKind.Single)
            {
                fields[i] = SingleField;
            }
            else if (slot.Kind == IntervalKind.OwnFallback)
            {
                fields[i] = OwnFallbackField;
            }
            else
            {
                int at = records.IndexOf(slot);
                if (at < 0)
                {
                    at = records.Count;
                    records.Add(slot);
                }

                fields[i] = (byte)(FirstRecordField + at);
            }
        }

        table.Add((byte)records.Count);
        table.AddRange(fields);
        foreach (IntervalSlot record in records)
        {
            if (record.Kind == IntervalKind.Split)
            {
                CheckPattern(tag, record.First);
                CheckPattern(tag, record.Second);
                table.Add(record.LaterFirst ? SplitLaterFirst : SplitEarlierFirst);
                TableBytes.Add(table, record.First);
                TableBytes.Add(table, record.Second);
            }
            else
            {
                CheckPattern(tag, record.First);
                table.Add(FallbackRecord);
                TableBytes.Add(table, record.First);
            }
        }
    }

    // The hour symbol that j stands for (CLDR's timeData, _preferred; UTS #35 part 4, "Time Data"): the entry of the language and
    // the region (en-001, fr-CA; CLDR 49's JSON writes them with a hyphen) first, then the region's, then the world's (001), in the order of ICU's
    // DateTimePatternGenerator::getAllowedHourFormats (ICU main, icu4c/source/i18n/dtptngen.cpp).
    private static char HourSymbol(string tag, string region, JsonElement timeData)
    {
        if (!timeData.TryGetProperty(LocaleSet.Language(tag) + "-" + region, out JsonElement data) && !timeData.TryGetProperty(region, out data))
        {
            data = timeData.GetProperty("001");
        }

        return data.GetProperty("_preferred").GetString() switch
        {
            "h" => 'h',
            "H" => 'H',
            string other => throw new InvalidDataException("The preferred hour " + other + " of " + tag + " (" + region + ") is not h or H."),
            null => throw new InvalidDataException("timeData has no preferred hour for " + region + "."),
        };
    }

    private static byte[] NameTable(string tag, JsonElement gregorian)
    {
        var table = new List<byte>();
        foreach (string context in new[] { "format", "stand-alone" })
        {
            bool standAlone = context == "stand-alone";
            NameSet(table, tag, gregorian.GetProperty("months"), context, standAlone, Widths, 12, Ordinal);
            NameSet(table, tag, gregorian.GetProperty("days"), context, standAlone, WeekdayWidths, 7, index => Weekdays[index]);
            NameSet(table, tag, gregorian.GetProperty("quarters"), context, standAlone, Widths, 4, Ordinal);
            NameSet(table, tag, gregorian.GetProperty("dayPeriods"), context, standAlone, Widths, 2, index => index == 0 ? "am" : "pm");
        }

        JsonElement eras = gregorian.GetProperty("eras");
        foreach (string width in new[] { "eraAbbr", "eraNames", "eraNarrow" })
        {
            TableBytes.Add(table, TableBytes.Text(tag, eras.GetProperty(width), "0"));
            TableBytes.Add(table, TableBytes.Text(tag, eras.GetProperty(width), "1"));
        }

        return [.. table];
    }

    // The key of the month or the quarter at an index: "1" for the first.
    private static string Ordinal(int index) => (index + 1).ToString(CultureInfo.InvariantCulture);

    // The names of one kind in one context; a stand-alone name that is the format name in the same place is written empty.
    private static void NameSet(
        List<byte> table,
        string tag,
        JsonElement kind,
        string context,
        bool standAlone,
        string[] widths,
        int count,
        Func<int, string> key
    )
    {
        foreach (string width in widths)
        {
            JsonElement names = kind.GetProperty(context).GetProperty(width);
            JsonElement formatNames = kind.GetProperty("format").GetProperty(width);
            for (int i = 0; i < count; i++)
            {
                string name = TableBytes.Text(tag, names, key(i));
                TableBytes.Add(table, standAlone && name == TableBytes.Text(tag, formatNames, key(i)) ? string.Empty : name);
            }
        }
    }

    // The pattern table of a locale (the remarks of the class), and the 41 patterns and the 7 24-hour ones it writes, each 24-hour
    // one also when it is the pattern before.
    private static (byte[] Table, List<string> Written) PatternTable(
        string tag,
        JsonElement gregorian,
        JsonElement zones,
        SkeletonMatcher matcher,
        char hour,
        char zero
    )
    {
        JsonElement dateTimeFormats = gregorian.GetProperty("dateTimeFormats");
        var table = new List<byte> { (byte)zero, (byte)(zero >> 8) };
        var written = new List<string>();
        foreach (string skeleton in Skeletons)
        {
            string pattern = matcher.Match(skeleton.Replace('j', hour));
            written.Add(pattern);
            TableBytes.Add(table, pattern);
        }

        // The 24-hour patterns of the j skeletons; a locale whose hour is H already has them.
        foreach (string skeleton in Skeletons.Where(skeleton => skeleton.Contains('j', StringComparison.Ordinal)))
        {
            string pattern = matcher.Match(skeleton.Replace('j', 'H'));
            bool same = pattern == written[Array.IndexOf(Skeletons, skeleton)];
            written.Add(pattern);
            TableBytes.Add(table, same ? string.Empty : pattern);
        }

        foreach (string pattern in written)
        {
            CheckPattern(tag, pattern);
        }

        foreach (string length in Lengths)
        {
            TableBytes.Add(table, Joining(tag, TableBytes.Text(tag, dateTimeFormats, length)));
        }

        TableBytes.Add(table, Joining(tag, TableBytes.Text(tag, dateTimeFormats.GetProperty("appendItems"), "Time-Day-Of-Week")));
        TableBytes.Add(table, Placeholders(tag, TableBytes.Text(tag, zones, "gmtFormat"), "{0}"));
        string hourFormat = TableBytes.Text(tag, zones, "hourFormat");
        CheckHourFormat(tag, hourFormat);
        TableBytes.Add(table, hourFormat);
        return ([.. table], written);
    }

    // A pattern that joins two (dateTimeFormats and the Time-Day-Of-Week append item), with {0} and {1} once each and each run of
    // ASCII letters outside them and outside quotes put in quotes: UTS #35 part 4 makes these simple formatters whose text is
    // literal, and DateFormatter compiles the joined text as one date pattern, where a letter would be a field (sl's {1} ob {0} is
    // written {1} 'ob' {0}). A run next to a quote stops the tool: quoting it would make two quotes in a row, a quote character.
    private static string Joining(string tag, string pattern)
    {
        _ = Placeholders(tag, pattern, "{0}", "{1}");
        var text = new StringBuilder();
        bool quoted = false;
        int i = 0;
        while (i < pattern.Length)
        {
            char character = pattern[i];
            if (character == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && char.IsAsciiLetter(character))
            {
                int end = i + 1;
                while (end < pattern.Length && char.IsAsciiLetter(pattern[end]))
                {
                    end++;
                }

                if ((i > 0 && pattern[i - 1] == '\'') || (end < pattern.Length && pattern[end] == '\''))
                {
                    throw new InvalidDataException("The joining pattern " + pattern + " of " + tag + " has letters next to a quote.");
                }

                text.Append('\'').Append(pattern, i, end - i).Append('\'');
                i = end;
                continue;
            }

            text.Append(character);
            i++;
        }

        return text.ToString();
    }

    // The fields DateFormatter writes, with their lengths: the era, the year (not as yy) and the week's year, the quarter, the month
    // in both contexts, the day, the weekday in the format context and as text in the stand-alone one, am or pm, the hours of 1 to
    // 12 and of 0 to 23, the minute, the second, and the zone. Anything else stops the tool.
    private static void CheckPattern(string tag, string pattern)
    {
        foreach ((char symbol, int length) in SkeletonMatcher.Fields(pattern))
        {
            bool written = symbol switch
            {
                'G' or 'Q' or 'M' or 'L' or 'a' => length <= 5,
                'y' or 'Y' => length != 2,
                'E' => length <= 6,
                'c' => length is >= 3 and <= 6,
                'd' or 'h' or 'H' or 'm' or 's' => length <= 2,
                'z' => length <= 4,
                'v' => length is 1 or 4,
                _ => false,
            };
            if (!written)
            {
                throw new InvalidDataException("The pattern " + pattern + " of " + tag + " has a field the formats do not write.");
            }
        }
    }

    // An hourFormat is a positive and a negative pattern, each of text, H or HH, text, mm, and text (+HH:mm;-HH:mm).
    private static void CheckHourFormat(string tag, string hourFormat)
    {
        string[] parts = hourFormat.Split(';');
        foreach (string part in parts)
        {
            int hour = part.IndexOf('H', StringComparison.Ordinal);
            int hourEnd = part.LastIndexOf('H');
            int minute = part.IndexOf("mm", StringComparison.Ordinal);
            bool valid =
                parts.Length == 2
                && hour >= 0
                && hourEnd - hour <= 1
                && minute > hourEnd
                && part.LastIndexOf('m') == minute + 1
                && !part.Contains('\'', StringComparison.Ordinal);
            if (!valid)
            {
                throw new InvalidDataException("The hourFormat " + hourFormat + " of " + tag + " is not the form the formats write.");
            }
        }
    }
}
