using System.Text;
using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// Finds how a skeleton writes a range of two dates for each field in which they can first differ (design §4.11, "Interval
/// formats"): a port of ICU's <c>DateIntervalFormat::initializePattern</c> and what it calls (ICU main, icu4c/source/i18n/dtitvfmt.cpp
/// and dtitvinf.cpp, read on 2026-11-27), for the skeletons of the 41 that have date fields only or time fields only and no time zone.
/// The ties of <c>getBestSkeleton</c>, which ICU breaks by the order of its hash table, are broken by the byte order of the skeletons.
/// An instance holds one locale's <c>intervalFormats</c>.
/// </summary>
/// <remarks>
/// The era is left out: a <see cref="DateTime"/> holds years of the common era only, so two values never differ in it. ICU's
/// <c>adjustFieldWidth</c> also rewrites <c>v</c>, <c>K</c>, <c>k</c>, and <c>b</c> for a skeleton with <c>z</c>, <c>K</c>, <c>k</c>,
/// or <c>b</c>, and drops the day period for one with <c>J</c>, which the port does not. The skeletons the tool matches have no
/// <c>z</c> (DateTables leaves out the time zone skeletons) and no <c>J</c>. <c>K</c>, <c>k</c>, and <c>b</c> could come only from the
/// locale's pattern of the hour, which <c>normalizeHourMetacharacters</c> reads. That pattern is the pattern table's <c>j</c> or
/// <c>H</c> pattern, on whose <c>K</c> and <c>k</c> <c>DateTables.CheckPattern</c> stops the tool (a locale whose pattern of the hour
/// were <c>ja</c>'s <c>aK時</c>), and its day period is the <c>a</c> that a 12-hour skeleton asks for: SkeletonMatcher writes the
/// requested day period, so <c>zh-Hant</c>'s <c>Bh時</c> is read as <c>ah時</c>.
/// </remarks>
internal sealed class IntervalMatcher
{
    /// <summary>
    /// The number of fields a range compares, in the order of <c>DateIntervalFormat::formatImpl</c> after the era: the year, the
    /// month, the day, am or pm, the hour, the minute, and the second.
    /// </summary>
    public const int FieldCount = 7;

    private const int Year = 0;
    private const int Month = 1;
    private const int Day = 2;
    private const int DayPeriod = 3;
    private const int Hour = 4;
    private const int Minute = 5;

    // getBestSkeleton's distances (dtitvinf.cpp): a letter only one skeleton has, and a numeric month against a text one.
    private const int DifferentField = 0x1000;
    private const int StringNumericDifference = 0x100;

    private const string LaterFirstPrefix = "latestFirst:";
    private const string EarlierFirstPrefix = "earliestFirst:";

    // The pattern letter of each field (fgCalendarFieldToPatternLetter, dtitvfmt.cpp), which prefixes a skeleton to look for the
    // patterns of a larger field; the second has no CLDR pattern.
    private static readonly char[] Letters = ['y', 'M', 'd', 'a', 'h', 'm', 's'];

    // The level of each field (SimpleDateFormat::fgCalendarFieldToLevel, ICU main's smpdtfmt.cpp, lines 1144 to 1154): a field is
    // smaller than every field of a pattern whose levels are all below its own.
    private static readonly int[] Levels = [10, 20, 30, 40, 50, 60, 70];

    private readonly string _tag;
    private readonly SkeletonMatcher _matcher;
    private readonly string _shortJoining;

    // The intervalFormatItems: each skeleton, in byte order, and its pattern for each field (null when it has none).
    private readonly SortedDictionary<string, string?[]> _items = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="IntervalMatcher"/> class with a locale's <c>intervalFormats</c>.
    /// </summary>
    /// <param name="tag">The locale, for the messages.</param>
    /// <param name="intervalFormats">The locale's <c>intervalFormats</c>: <c>intervalFormatFallback</c> and the items. An item's
    /// letters are read as ICU's <c>DateIntervalSink</c> reads them: <c>a</c> and <c>B</c> are am or pm, <c>h</c> and <c>H</c> the hour,
    /// the first in byte order winning; <c>G</c> and any other letter are not read.</param>
    /// <param name="matcher">The locale's skeleton formats, for the patterns of the hour and of a date with a time.</param>
    /// <param name="shortJoining">The locale's short pattern that joins a date and a time (<c>dateTimeFormats-atTime</c>'s when the
    /// locale has it, else <c>dateTimeFormats</c>'), with its letters quoted, which ICU's <c>getBestPattern</c> takes for <c>yMd</c>
    /// and a time.</param>
    /// <exception cref="InvalidDataException">The fallback does not have <c>{0}</c> and <c>{1}</c> once each.</exception>
    public IntervalMatcher(string tag, JsonElement intervalFormats, SkeletonMatcher matcher, string shortJoining)
    {
        _tag = tag;
        _matcher = matcher;
        _shortJoining = shortJoining;
        Fallback = DateTables.Placeholders(tag, TableBytes.Text(tag, intervalFormats, "intervalFormatFallback"), "{0}", "{1}");

        // DateIntervalInfo::setFallbackIntervalPattern: the later date comes first when {1} is before {0}.
        LaterFirst = Fallback.IndexOf("{1}", StringComparison.Ordinal) < Fallback.IndexOf("{0}", StringComparison.Ordinal);
        foreach (JsonProperty item in intervalFormats.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string?[] patterns = new string?[FieldCount];
            foreach (JsonProperty letter in item.Value.EnumerateObject().OrderBy(letter => letter.Name, StringComparer.Ordinal))
            {
                int field = letter.Name switch
                {
                    "y" => Year,
                    "M" => Month,
                    "d" => Day,
                    "a" or "B" => DayPeriod,
                    "h" or "H" => Hour,
                    "m" => Minute,
                    _ => -1,
                };
                if (field >= 0 && patterns[field] is null)
                {
                    patterns[field] = TableBytes.Text(tag, item.Value, letter.Name);
                }
            }

            _items.Add(item.Name, patterns);
        }
    }

    /// <summary>
    /// Gets <c>intervalFormatFallback</c>, which joins two dates written whole: <c>{0}</c> is the earlier and <c>{1}</c> the later.
    /// </summary>
    public string Fallback { get; }

    /// <summary>
    /// Gets a value indicating whether the later date comes first by default: <see cref="Fallback"/> has <c>{1}</c> before
    /// <c>{0}</c>.
    /// </summary>
    public bool LaterFirst { get; }

    /// <summary>
    /// Returns how a skeleton writes a range for each field the two dates can first differ in.
    /// </summary>
    /// <param name="skeleton">The skeleton, with <c>j</c> already replaced by an hour symbol; date fields only or time fields only,
    /// and no time zone, as DateTables passes the 37 of Flutter's 41 skeletons without a time zone, none of which has both.</param>
    /// <param name="pattern">The skeleton's own pattern, which writes one date.</param>
    /// <returns>For the year, the month, the day, am or pm, the hour, the minute, and the second, how the range is written.</returns>
    /// <exception cref="InvalidDataException">An interval pattern of the data is a prefix alone, a skeleton of the data has a character
    /// that is not a letter.</exception>
    public IntervalSlot[] Match(string skeleton, string pattern)
    {
        var parts = new Part[FieldCount];
        for (int i = 0; i < FieldCount; i++)
        {
            parts[i] = new Part(null, null, LaterFirst);
        }

        string converted = NormalizeHourMetacharacters(skeleton);
        Split(converted, out string dateSkeleton, out string normalizedDate, out string timeSkeleton, out string normalizedTime);
        SetSeparateDateTimePattern(parts, normalizedDate, normalizedTime);
        if (timeSkeleton.Length > 0)
        {
            // initializePattern's path for a time skeleton, found or not: a range of other days writes both dates whole with yMd and
            // the time, the pattern ICU's getBestPattern joins with the short joining pattern (the month of yMd is numeric): pt's
            // {1}, {0} of atTime, so that ICU 78.3 writes pt's Hm from 2026-03-04 09:05 to 2026-03-05 11:30 as
            // 04/03/2026, 09:05 – 05/03/2026, 11:30.
            string withDate = Join(_shortJoining, _matcher.Match("yMd"), _matcher.Match(timeSkeleton));
            foreach (int field in new[] { Day, Month, Year })
            {
                parts[field] = parts[field] with { Second = withDate };
            }
        }

        var slots = new IntervalSlot[FieldCount];
        for (int i = 0; i < FieldCount; i++)
        {
            Part part = parts[i];
            if (string.IsNullOrEmpty(part.First) && string.IsNullOrEmpty(part.Second))
            {
                // formatImpl: one date when the field is smaller than every field of the pattern, else the fallback with the
                // pattern.
                slots[i] = IsFieldUnitIgnored(pattern, Levels[i]) ? IntervalSlot.Single : IntervalSlot.OwnFallback;
            }
            else if (string.IsNullOrEmpty(part.First))
            {
                slots[i] = new IntervalSlot(IntervalKind.Fallback, part.Second!, string.Empty, false);
            }
            else
            {
                slots[i] = new IntervalSlot(IntervalKind.Split, part.First, part.Second ?? string.Empty, part.LaterFirst);
            }
        }

        return slots;
    }

    /// <summary>
    /// Returns where an interval pattern splits into the part of the first date and the part of the second: before the first letter
    /// that repeats a field already seen, quoted text skipped (ICU's <c>splitPatternInto2Part</c>). The letters are compared as they
    /// are, so <c>M</c> and <c>L</c> differ.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The length of the first part; the pattern's length when no field repeats.</returns>
    internal static int SplitPoint(string pattern)
    {
        bool inQuote = false;
        char previous = '\0';
        int count = 0;
        var seen = new bool[58];
        bool foundRepetition = false;
        int i;
        for (i = 0; i < pattern.Length; i++)
        {
            char character = pattern[i];
            if (character != previous && count > 0)
            {
                if (!seen[previous - 'A'])
                {
                    seen[previous - 'A'] = true;
                }
                else
                {
                    foundRepetition = true;
                    break;
                }

                count = 0;
            }

            if (character == '\'')
            {
                // Two quotes in a row are a quote character, inside quotes or out.
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    i++;
                }
                else
                {
                    inQuote = !inQuote;
                }
            }
            else if (!inQuote && char.IsAsciiLetter(character))
            {
                previous = character;
                count++;
            }
        }

        // The last field: "dd MM" repeats nothing and "d-d" repeats its last field.
        if (count > 0 && !foundRepetition && !seen[previous - 'A'])
        {
            count = 0;
        }

        return i - count;
    }

    /// <summary>
    /// Returns whether a field is smaller than every field of a pattern (ICU's <c>SimpleDateFormat::isFieldUnitIgnored</c>), so that
    /// two dates that first differ in it write one date.
    /// </summary>
    /// <param name="pattern">The pattern, or a skeleton read as one.</param>
    /// <param name="fieldLevel">The field's level: the year 10, the month 20, the day 30, am or pm 40, the hour 50, the minute 60, and
    /// the second 70.</param>
    /// <returns><see langword="true"/> when no field of the pattern has a level of at least the field's.</returns>
    internal static bool IsFieldUnitIgnored(string pattern, int fieldLevel)
    {
        bool inQuote = false;
        char previous = '\0';
        int count = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            char character = pattern[i];
            if (character != previous && count > 0)
            {
                if (fieldLevel <= Level(previous))
                {
                    return false;
                }

                count = 0;
            }

            if (character == '\'')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    i++;
                }
                else
                {
                    inQuote = !inQuote;
                }
            }
            else if (!inQuote && char.IsAsciiLetter(character))
            {
                previous = character;
                count++;
            }
        }

        return count == 0 || fieldLevel > Level(previous);
    }

    // The level of a pattern letter (SimpleDateFormat::getLevelFromChar, smpdtfmt.cpp, lines 1156 to 1184); -1 for a letter that is
    // not a calendar field.
    private static int Level(char letter) =>
        letter switch
        {
            'G' or 'O' or 'V' or 'X' or 'Z' or 'g' or 'l' or 'v' or 'x' or 'z' => 0,
            'U' or 'Y' or 'r' or 'u' or 'y' => 10,
            'D' or 'L' or 'M' or 'Q' or 'q' or 'w' => 20,
            'E' or 'F' or 'W' or 'c' or 'd' or 'e' => 30,
            'A' or 'a' => 40,
            'H' or 'K' or 'h' or 'k' => 50,
            'm' => 60,
            's' => 70,
            'S' => 80,
            _ => -1,
        };

    // The width of each letter of a skeleton (DateIntervalInfo::parseSkeleton), indexed from A.
    private static int[] Widths(string skeleton)
    {
        int[] widths = new int[58];
        foreach (char letter in skeleton)
        {
            if (!char.IsAsciiLetter(letter))
            {
                throw new InvalidDataException("The interval skeleton " + skeleton + " has a character that is not a letter.");
            }

            widths[letter - 'A']++;
        }

        return widths;
    }

    // DateIntervalInfo::stringNumeric: a numeric month (M or MM) against a text one (MMM or longer).
    private static bool StringNumeric(int width, int otherWidth, char letter) => letter == 'M' && (width <= 2) != (otherWidth <= 2);

    // A joining pattern with {1} replaced by a date pattern and {0} by a time pattern (SimpleFormatter::format in getBestPattern).
    // The joining pattern has each placeholder once (DateTables.Joining checks it).
    private static string Join(string joining, string date, string time)
    {
        int dateAt = joining.IndexOf("{1}", StringComparison.Ordinal);
        int timeAt = joining.IndexOf("{0}", StringComparison.Ordinal);
        (int first, string firstPattern, int second, string secondPattern) =
            dateAt < timeAt ? (dateAt, date, timeAt, time) : (timeAt, time, dateAt, date);
        return string.Concat(joining[..first], firstPattern, joining[(first + 3)..second], secondPattern, joining[(second + 3)..]);
    }

    // DateIntervalFormat::getDateTimeSkeleton: the date and the time letters of a skeleton, and their normalized forms: y, M (one
    // when numeric), E (one up to three), and d once each, the other date letters as they come first; the hour once, then m, z,
    // and v, with a, s, and the other time letters as they come first.
    private static void Split(string skeleton, out string date, out string normalizedDate, out string time, out string normalizedTime)
    {
        var dateText = new StringBuilder();
        var normalizedDateText = new StringBuilder();
        var timeText = new StringBuilder();
        var normalizedTimeText = new StringBuilder();
        int eCount = 0;
        int dCount = 0;
        int monthCount = 0;
        int yearCount = 0;
        int minuteCount = 0;
        int vCount = 0;
        int zCount = 0;
        char hour = '\0';
        foreach (char letter in skeleton)
        {
            switch (letter)
            {
                case 'E':
                    dateText.Append(letter);
                    eCount++;
                    break;
                case 'd':
                    dateText.Append(letter);
                    dCount++;
                    break;
                case 'M':
                    dateText.Append(letter);
                    monthCount++;
                    break;
                case 'y':
                    dateText.Append(letter);
                    yearCount++;
                    break;
                case 'G' or 'Y' or 'u' or 'Q' or 'q' or 'L' or 'l' or 'W' or 'w' or 'D' or 'F' or 'g' or 'e' or 'c' or 'U' or 'r':
                    normalizedDateText.Append(letter);
                    dateText.Append(letter);
                    break;
                case 'h' or 'H' or 'k' or 'K':
                    timeText.Append(letter);
                    if (hour == '\0')
                    {
                        hour = letter;
                    }

                    break;
                case 'm':
                    timeText.Append(letter);
                    minuteCount++;
                    break;
                case 'z':
                    zCount++;
                    timeText.Append(letter);
                    break;
                case 'v':
                    vCount++;
                    timeText.Append(letter);
                    break;
                case 'a' or 'V' or 'Z' or 'j' or 's' or 'S' or 'A' or 'b' or 'B':
                    timeText.Append(letter);
                    normalizedTimeText.Append(letter);
                    break;
                default:
                    break;
            }
        }

        normalizedDateText.Append('y', yearCount);
        if (monthCount > 0)
        {
            // MAX_M_COUNT and MAX_E_COUNT (dtitvfmt.h) are above the 4 letters a skeleton of the 41 has.
            normalizedDateText.Append('M', monthCount < 3 ? 1 : monthCount);
        }

        if (eCount > 0)
        {
            normalizedDateText.Append('E', eCount <= 3 ? 1 : eCount);
        }

        if (dCount > 0)
        {
            normalizedDateText.Append('d');
        }

        if (hour != '\0')
        {
            normalizedTimeText.Append(hour);
        }

        if (minuteCount > 0)
        {
            normalizedTimeText.Append('m');
        }

        if (zCount > 0)
        {
            normalizedTimeText.Append('z');
        }

        if (vCount > 0)
        {
            normalizedTimeText.Append('v');
        }

        date = dateText.ToString();
        normalizedDate = normalizedDateText.ToString();
        time = timeText.ToString();
        normalizedTime = normalizedTimeText.ToString();
    }

    // DateIntervalFormat::normalizeHourMetacharacters: the hour field becomes the hour letter of the locale's pattern of that hour
    // alone (h or H) and, for h, a day period of the skeleton's letter or a, 1, 3, or 5 long; the skeleton's own day period is
    // removed. Quoted text of the pattern is not read (de's 'Uhr'). ICU also takes K, k, b, and B from the pattern, and then
    // adjustFieldWidth rewrites the interval patterns' letters, which the port does not do. None of them reaches here: the pattern
    // is the pattern table's j or H pattern, on whose K or k DateTables.CheckPattern stops the tool, and its day period is the a
    // that SkeletonMatcher writes for a 12-hour skeleton (the remarks of the class).
    private string NormalizeHourMetacharacters(string skeleton)
    {
        char hourLetter = '\0';
        char dayPeriodLetter = '\0';
        int hourStart = 0;
        int hourLength = 0;
        int dayPeriodStart = 0;
        int dayPeriodLength = 0;
        for (int i = 0; i < skeleton.Length; i++)
        {
            char letter = skeleton[i];
            if (letter is 'j' or 'J' or 'C' or 'h' or 'H' or 'k' or 'K')
            {
                if (hourLetter == '\0')
                {
                    hourLetter = letter;
                    hourStart = i;
                }

                hourLength++;
            }
            else if (letter is 'a' or 'b' or 'B')
            {
                if (dayPeriodLetter == '\0')
                {
                    dayPeriodLetter = letter;
                    dayPeriodStart = i;
                }

                dayPeriodLength++;
            }
            else if (hourLetter != '\0' && dayPeriodLetter != '\0')
            {
                break;
            }
        }

        if (hourLetter == '\0')
        {
            return skeleton;
        }

        var pattern = new StringBuilder(_matcher.Match(hourLetter.ToString()));
        for (int quote = IndexOf(pattern, '\'', 0); quote >= 0; quote = IndexOf(pattern, '\'', 0))
        {
            int end = IndexOf(pattern, '\'', quote + 1);
            pattern.Remove(quote, (end < 0 ? quote : end) - quote + 1);
        }

        string text = pattern.ToString();
        char hour = text.Contains('h', StringComparison.Ordinal) ? 'h' : 'H';
        if (dayPeriodLetter == '\0')
        {
            dayPeriodLetter = 'a';
        }

        var hourAndDayPeriod = new StringBuilder().Append(hour);
        if (hour == 'h')
        {
            int length =
                dayPeriodLength >= 5 || hourLength >= 5 ? 5
                : dayPeriodLength >= 3 || hourLength >= 3 ? 3
                : 1;
            hourAndDayPeriod.Append(dayPeriodLetter, length);
        }

        var result = new StringBuilder(skeleton);
        result.Remove(hourStart, hourLength).Insert(hourStart, hourAndDayPeriod.ToString());
        if (dayPeriodStart > hourStart)
        {
            dayPeriodStart += hourAndDayPeriod.Length - hourLength;
        }

        result.Remove(dayPeriodStart, dayPeriodLength);
        return result.ToString();
    }

    private static int IndexOf(StringBuilder text, char character, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == character)
            {
                return i;
            }
        }

        return -1;
    }

    // DateIntervalFormat::setSeparateDateTimePtn: the patterns of the closest item for a skeleton of date fields only (the day, the
    // month, then the year, from the skeleton the month's search extended) or of time fields only (the minute, the hour, am or pm);
    // nothing when no item is near (a field that only one has).
    private void SetSeparateDateTimePattern(Part[] parts, string normalizedDate, string normalizedTime)
    {
        string skeleton = normalizedTime.Length != 0 ? normalizedTime : normalizedDate;
        string? best = BestSkeleton(skeleton, out int difference);
        if (best is null || difference == -1)
        {
            return;
        }

        if (normalizedTime.Length == 0)
        {
            string extendedSkeleton = string.Empty;
            string extendedBest = string.Empty;
            SetIntervalPattern(parts, Day, skeleton, best, difference, true, ref extendedSkeleton, ref extendedBest);
            if (SetIntervalPattern(parts, Month, skeleton, best, difference, true, ref extendedSkeleton, ref extendedBest))
            {
                skeleton = extendedSkeleton;
                best = extendedBest;
            }

            SetIntervalPattern(parts, Year, skeleton, best, difference, true, ref extendedSkeleton, ref extendedBest);
        }
        else
        {
            string none = string.Empty;
            string noneBest = string.Empty;
            SetIntervalPattern(parts, Minute, skeleton, best, difference, false, ref none, ref noneBest);
            SetIntervalPattern(parts, Hour, skeleton, best, difference, false, ref none, ref noneBest);
            SetIntervalPattern(parts, DayPeriod, skeleton, best, difference, false, ref none, ref noneBest);
        }
    }

    // DateIntervalInfo::getBestSkeleton: the item whose letters' widths are nearest the skeleton's, z read as v, k as H, K as h, and a
    // and b left out. The difference is 0 for the same widths, 1 for other widths of the same letters, 2 when letters were read as
    // others or left out and every letter is in both, and -1 when a letter is in one only. At the same distance the first in byte
    // order wins (design §4.11, "Choosing the skeleton").
    private string? BestSkeleton(string skeleton, out int difference)
    {
        bool replaced = skeleton.IndexOfAny(['z', 'k', 'K', 'a', 'b']) >= 0;
        string input = replaced
            ? skeleton
                .Replace('z', 'v')
                .Replace('k', 'H')
                .Replace('K', 'h')
                .Replace("a", string.Empty, StringComparison.Ordinal)
                .Replace("b", string.Empty, StringComparison.Ordinal)
            : skeleton;
        int[] inputWidths = Widths(input);
        int bestDistance = int.MaxValue;
        string? best = null;
        difference = 0;
        foreach (string candidate in _items.Keys)
        {
            int[] widths = Widths(candidate);
            int distance = 0;
            int fieldDifference = 1;
            for (int i = 0; i < widths.Length; i++)
            {
                int inputWidth = inputWidths[i];
                int width = widths[i];
                if (inputWidth == width)
                {
                    continue;
                }

                if (inputWidth == 0 || width == 0)
                {
                    fieldDifference = -1;
                    distance += DifferentField;
                }
                else if (StringNumeric(inputWidth, width, (char)('A' + i)))
                {
                    distance += StringNumericDifference;
                }
                else
                {
                    distance += Math.Abs(inputWidth - width);
                }
            }

            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
                difference = fieldDifference;
            }

            if (distance == 0)
            {
                difference = 0;
                break;
            }
        }

        if (replaced && difference != -1)
        {
            difference = 2;
        }

        return best;
    }

    // DateIntervalFormat::setIntervalPattern (the overload with the best skeleton): the item's pattern of the field, with its
    // widths adjusted to the skeleton's. With none, nothing when the field is smaller than every letter of the best skeleton; am or
    // pm takes the hour's pattern; a date field looks in the item of the skeleton with the field's letter in front (yMMM for MMM),
    // and, when that is not an item and the best skeleton was the skeleton itself, in the item nearest it. Returns whether the
    // pattern came from the extended skeleton, which then stays in extendedSkeleton and extendedBest.
    private bool SetIntervalPattern(
        Part[] parts,
        int field,
        string skeleton,
        string best,
        int difference,
        bool canExtend,
        ref string extendedSkeleton,
        ref string extendedBest
    )
    {
        string? pattern = Item(best, field);
        if (pattern is null)
        {
            if (IsFieldUnitIgnored(best, Levels[field]))
            {
                return false;
            }

            // In 24 hours an item may have no pattern for am or pm, which then is the hour's.
            if (field == DayPeriod)
            {
                if (Item(best, Hour) is { } hourPattern)
                {
                    SetIntervalPattern(parts, field, AdjustFieldWidth(skeleton, best, hourPattern), LaterFirst);
                }

                return false;
            }

            if (canExtend)
            {
                char letter = Letters[field];
                extendedSkeleton = letter + skeleton;
                extendedBest = letter + best;
                pattern = Item(extendedBest, field);
                if (pattern is null && difference == 0)
                {
                    // ICU passes its copy of the difference to getBestSkeleton, which sets it whatever it finds.
                    string? nearest = BestSkeleton(extendedBest, out difference);
                    if (nearest is not null && difference != -1)
                    {
                        pattern = Item(nearest, field);
                        best = nearest;
                    }
                }
            }
        }

        if (pattern is null)
        {
            return false;
        }

        SetIntervalPattern(parts, field, difference != 0 ? AdjustFieldWidth(skeleton, best, pattern) : pattern, LaterFirst);
        return canExtend && extendedSkeleton.Length > 0;
    }

    // DateIntervalFormat::setIntervalPattern (the overload with the order): latestFirst: and earliestFirst: set the order, and the
    // pattern is split into the parts of the two dates.
    private void SetIntervalPattern(Part[] parts, int field, string intervalPattern, bool laterFirst)
    {
        string pattern = intervalPattern;
        if (intervalPattern.StartsWith(LaterFirstPrefix, StringComparison.Ordinal))
        {
            laterFirst = true;
            pattern = intervalPattern[LaterFirstPrefix.Length..];
        }
        else if (intervalPattern.StartsWith(EarlierFirstPrefix, StringComparison.Ordinal))
        {
            laterFirst = false;
            pattern = intervalPattern[EarlierFirstPrefix.Length..];
        }

        // A pattern whose fields do not repeat splits at its end: ICU's splitPatternInto2Part makes it all the first part, and
        // formatImpl writes one date with it. Only an empty pattern would leave the first part empty, and CLDR's texts are not empty,
        // so only a prefix with nothing after it is.
        pattern = RequestedDayPeriod(pattern);
        if (pattern.Length == 0)
        {
            throw new InvalidDataException("The interval pattern " + intervalPattern + " of " + _tag + " has nothing after its prefix.");
        }

        int split = SplitPoint(pattern);
        parts[field] = new Part(pattern[..split], pattern[split..], laterFirst);
    }

    // The pattern with each flexible day period (B) outside quotes written as the requested a of the same length, as the date
    // formats write it (design §4.11, "The letter of the day period"; Issue #906): CLDR 49's zh-Hant hm item is Bh:mm至h:mm, and
    // ICU 78.3 with CLDR 48 writes 上午9:05至11:30. ICU's interval code would keep the B.
    private static string RequestedDayPeriod(string pattern)
    {
        if (!pattern.Contains('B', StringComparison.Ordinal))
        {
            return pattern;
        }

        var text = new StringBuilder(pattern.Length);
        bool quoted = false;
        foreach (char character in pattern)
        {
            quoted ^= character == '\'';
            text.Append(!quoted && character == 'B' ? 'a' : character);
        }

        return text.ToString();
    }

    // DateIntervalFormat::adjustFieldWidth: each field of the pattern as long as the best skeleton's is made as long as the
    // skeleton's when that is longer (MMM d – d, y for yMMMMd from yMMMd's MMM becomes MMMM); L counts as M. A pattern with a and a
    // best skeleton without it count a as one letter long.
    private static string AdjustFieldWidth(string skeleton, string best, string pattern)
    {
        int[] inputWidths = Widths(skeleton);
        int[] bestWidths = Widths(best);
        if (pattern.Contains('a', StringComparison.Ordinal) && bestWidths['a' - 'A'] == 0)
        {
            bestWidths['a' - 'A'] = 1;
        }

        var adjusted = new StringBuilder(pattern);
        bool inQuote = false;
        char previous = '\0';
        int count = 0;
        for (int i = 0; i < adjusted.Length; i++)
        {
            char character = adjusted[i];
            if (character != previous && count > 0)
            {
                int added = Widen(previous, count, inputWidths, bestWidths);
                adjusted.Insert(i, new string(previous, added));
                i += added;
                count = 0;
            }

            if (character == '\'')
            {
                if (i + 1 < adjusted.Length && adjusted[i + 1] == '\'')
                {
                    i++;
                }
                else
                {
                    inQuote = !inQuote;
                }
            }
            else if (!inQuote && char.IsAsciiLetter(character))
            {
                previous = character;
                count++;
            }
        }

        if (count > 0)
        {
            adjusted.Append(previous, Widen(previous, count, inputWidths, bestWidths));
        }

        return adjusted.ToString();
    }

    // The letters a run of a field gains: the skeleton's width less the best skeleton's, when the run is as long as the best
    // skeleton's and the skeleton's is longer.
    private static int Widen(char letter, int count, int[] inputWidths, int[] bestWidths)
    {
        int index = (letter == 'L' ? 'M' : letter) - 'A';
        int width = bestWidths[index];
        int inputWidth = inputWidths[index];
        return width == count && inputWidth > width ? inputWidth - width : 0;
    }

    private string? Item(string skeleton, int field) => _items.TryGetValue(skeleton, out string?[]? patterns) ? patterns[field] : null;

    /// <summary>
    /// ICU's <c>PatternInfo</c>: the part of the first date, the part of the second, and whether the later date is the first. A fallback
    /// has no first part and the whole pattern as its second.
    /// </summary>
    private readonly record struct Part(string? First, string? Second, bool LaterFirst);
}
