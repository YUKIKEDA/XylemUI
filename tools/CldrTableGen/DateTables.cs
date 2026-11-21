using System.Globalization;
using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// The date tables of the entries (design §4.11, "Dates and times"): for each locale, the names of its Gregorian calendar, and the
/// patterns of Flutter's 41 skeletons with what joins and writes them. Locales whose bytes are the same share one table. The bytes are
/// read by <c>DateNames</c> and <c>DateFormatter</c> in XylemUI.Core.
/// </summary>
/// <remarks>
/// A name table is, for the format and then the stand-alone context, the months (abbreviated, wide, narrow; 12 each), the weekdays
/// (abbreviated, wide, narrow, short; Sunday first), the quarters (abbreviated, wide, narrow; 4 each), and the day periods
/// (abbreviated, wide, narrow; am and pm), then the eras (abbreviated, wide, narrow; before and in the common era): 170 strings. A
/// stand-alone name that is its format name is written empty. A pattern table is the digit zero of the locale's default numbering
/// system (2 bytes, little-endian); the patterns of the 41 skeletons in the order of <see cref="Skeletons"/>, a <c>j</c> skeleton's
/// with the hour of the locale's region (CLDR's <c>timeData</c>); the 24-hour patterns of the 7 <c>j</c> skeletons, empty when the
/// pattern is the one before; the patterns that join a date and a time (<c>dateTimeFormats</c>: full, long, medium, short); the
/// <c>Time-Day-Of-Week</c> append item; <c>gmtFormat</c> and <c>hourFormat</c>; and the flexible day periods that a <c>B</c> field
/// writes: a count byte (0 when no pattern has <c>B</c>) and, for each period in the order of its start, its start in minutes of the
/// day (2 bytes, little-endian) and its abbreviated, wide, and narrow names. A string is a length byte and that many bytes of UTF-8.
/// </remarks>
internal sealed class DateTables
{
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

    private DateTables(List<byte[]> names, List<byte[]> patterns, Dictionary<string, (int Names, int Patterns)> byTag)
    {
        Names = names;
        Patterns = patterns;
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
    /// Gets, for the data tag of each entry and of root, the index of its name and pattern tables.
    /// </summary>
    public IReadOnlyDictionary<string, (int Names, int Patterns)> ByTag { get; }

    /// <summary>
    /// Reads the tables of every entry and of root.
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <param name="locales">The set of locales.</param>
    /// <returns>The tables.</returns>
    public static DateTables Read(CldrZip cldr, LocaleSet locales)
    {
        JsonElement timeData = cldr.Supplemental("timeData").GetProperty("timeData");
        JsonElement dayPeriods = cldr.Supplemental("dayPeriods").GetProperty("dayPeriodRuleSet");
        var names = new List<byte[]>();
        var patterns = new List<byte[]>();
        var nameIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var patternIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var byTag = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
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
            byte[] patternTable = PatternTable(tag, gregorian, zones, hour, NumberTables.Zero(cldr, tag), dayPeriods);
            int nameAt = TableBytes.Index(NameTable(tag, gregorian), names, nameIndex);
            int patternAt = TableBytes.Index(patternTable, patterns, patternIndex);
            byTag.Add(tag, (nameAt, patternAt));
        }

        return new DateTables(names, patterns, byTag);
    }

    // The hour symbol that j stands for in the region (CLDR's timeData, _preferred), or in the world's (001) for a region timeData
    // does not list (UTS #35 part 4, "Time Data").
    private static char HourSymbol(string tag, string region, JsonElement timeData)
    {
        JsonElement data = timeData.TryGetProperty(region, out JsonElement own) ? own : timeData.GetProperty("001");
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

    private static byte[] PatternTable(string tag, JsonElement gregorian, JsonElement zones, char hour, char zero, JsonElement dayPeriods)
    {
        JsonElement dateTimeFormats = gregorian.GetProperty("dateTimeFormats");
        IEnumerable<KeyValuePair<string, string>> available = dateTimeFormats
            .GetProperty("availableFormats")
            .EnumerateObject()
            .Select(format => KeyValuePair.Create(format.Name, format.Value.GetString() ?? string.Empty));
        var matcher = new SkeletonMatcher(tag, available);
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
            TableBytes.Add(table, Placeholders(tag, TableBytes.Text(tag, dateTimeFormats, length), "{0}", "{1}"));
        }

        string timeAndWeekday = TableBytes.Text(tag, dateTimeFormats.GetProperty("appendItems"), "Time-Day-Of-Week");
        TableBytes.Add(table, Placeholders(tag, timeAndWeekday, "{0}", "{1}"));
        TableBytes.Add(table, Placeholders(tag, TableBytes.Text(tag, zones, "gmtFormat"), "{0}"));
        string hourFormat = TableBytes.Text(tag, zones, "hourFormat");
        CheckHourFormat(tag, hourFormat);
        TableBytes.Add(table, hourFormat);
        if (written.Any(pattern => HasField(pattern, 'B')))
        {
            AddDayPeriods(table, tag, gregorian.GetProperty("dayPeriods").GetProperty("format"), dayPeriods);
        }
        else
        {
            table.Add(0);
        }

        return [.. table];
    }

    // The flexible day periods of the locale's rule set (cldr-core/supplemental/dayPeriods.json), found by its tag with its last
    // subtag dropped until one is there (zh-Hant takes zh's): each period with a start (_from), in the order of the starts, with its
    // names in the format context. The periods at one time (_at: midnight, noon) are not written.
    private static void AddDayPeriods(List<byte> table, string tag, JsonElement names, JsonElement dayPeriods)
    {
        string[] subtags = tag.Split('-');
        JsonElement? rules = null;
        for (int count = subtags.Length; count > 0 && rules is null; count--)
        {
            if (dayPeriods.TryGetProperty(string.Join('_', subtags[..count]), out JsonElement found))
            {
                rules = found;
            }
        }

        if (rules is not { } set)
        {
            throw new InvalidDataException(tag + " writes flexible day periods (B) and dayPeriods.json has no rules for it.");
        }

        var periods = new List<(int Start, int End, string Name)>();
        foreach (JsonProperty period in set.EnumerateObject())
        {
            if (period.Value.TryGetProperty("_from", out JsonElement from))
            {
                periods.Add((Minutes(tag, from), Minutes(tag, period.Value.GetProperty("_before")), period.Name));
            }
        }

        periods.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (int i = 0; i < periods.Count; i++)
        {
            // The periods cover the day once: each ends where the next starts, and the last where the first starts, past midnight.
            if (periods[i].End % (24 * 60) != periods[(i + 1) % periods.Count].Start)
            {
                throw new InvalidDataException("The day periods of " + tag + " do not cover the day once at " + periods[i].Name + ".");
            }
        }

        table.Add((byte)periods.Count);
        foreach ((int start, _, string name) in periods)
        {
            table.Add((byte)start);
            table.Add((byte)(start >> 8));
            foreach (string width in Widths)
            {
                TableBytes.Add(table, TableBytes.Text(tag, names.GetProperty(width), name));
            }
        }
    }

    // A time of dayPeriods.json, "05:00" or "24:00", in minutes of the day.
    private static int Minutes(string tag, JsonElement time)
    {
        string text = time.GetString() ?? string.Empty;
        if (
            text.Length == 5
            && text[2] == ':'
            && int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            && int.TryParse(text.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            && hours <= 24
            && minutes < 60
        )
        {
            return (hours * 60) + minutes;
        }

        throw new InvalidDataException("The day period time " + text + " of " + tag + " is not HH:mm.");
    }

    // A pattern with each placeholder once: {0} and {1} in a pattern that joins two (dateTimeFormats and the Time-Day-Of-Week append
    // item), {0} in gmtFormat.
    private static string Placeholders(string tag, string pattern, params ReadOnlySpan<string> placeholders)
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

    // The fields DateFormatter writes, with their lengths: the era, the year (not as yy) and the week's year, the quarter, the month
    // in both contexts, the day, the weekday in the format context and as text in the stand-alone one, the day period, the flexible
    // day period, the hours of 1 to 12 and of 0 to 23, the minute, the second, and the zone. Anything else stops the tool.
    private static void CheckPattern(string tag, string pattern)
    {
        foreach ((char symbol, int length) in SkeletonMatcher.Fields(pattern))
        {
            bool written = symbol switch
            {
                'G' or 'Q' or 'M' or 'L' or 'a' or 'B' => length <= 5,
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

    private static bool HasField(string pattern, char symbol) => SkeletonMatcher.Fields(pattern).Any(field => field.Symbol == symbol);
}
