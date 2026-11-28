using System.Buffers;

namespace XylemUI;

/// <summary>
/// Formats dates and times with a locale's CLDR tables (design §4.11, "Dates and times"), in the Gregorian calendar: a format is one
/// of Flutter's 41 skeletons (<c>yMd</c>, <c>jm</c>), whose pattern tools/CldrTableGen found in the locale's
/// <c>availableFormats</c> by UTS #35's "Matching Skeletons", or a date skeleton and a time skeleton joined by the locale's pattern
/// that joins a date and a time. The digits are those of the locale's default numbering system. An instance does not change after it is
/// made, so any thread may call it.
/// </summary>
/// <remarks>
/// A skeleton with <c>j</c> takes the 24-hour pattern when <c>use24HourFormat</c> is <see langword="true"/>, and the pattern of the
/// hour of the locale's language and region (CLDR's <c>timeData</c>) otherwise, which is 24-hour for <c>ja</c> and <c>de</c>; a
/// skeleton with <c>H</c> ignores it. A time zone field (<c>jmv</c>, <c>jmz</c>, <c>jv</c>, <c>jz</c>) writes the offset from GMT
/// in the locale's <c>gmtFormat</c> and <c>hourFormat</c>, short (<c>GMT+9</c>, <c>GMT+5:30</c>, <c>GMT+0</c>) for <c>z</c> and
/// <c>v</c> and long (<c>GMT+09:00</c>, <c>GMT+00:00</c>) for <c>zzzz</c> and <c>vvvv</c>; time zone names are not in the tables.
/// Making a format reads the tables and allocates; <see cref="TryFormat(DateTime, Span{char}, out int)"/>, its overloads, and the
/// <c>Of</c> methods allocate nothing, and so do the <c>TryParse</c> methods for text of up to 256 characters. The format of
/// <c>yMd</c> parses a date, and that of <c>yMd</c> joined with <c>jm</c> a date and a time; no other format parses. The first range a format writes or returns reads its interval patterns and allocates;
/// <c>TryFormatRange</c> and <c>OfRange</c> allocate nothing after that. <see cref="Format"/> makes a string.
/// </remarks>
public sealed class DateFormatter
{
    private const int DateSkeletons = 28;
    private const int FirstHourSkeleton = 31;
    private const int HourSkeletons = 7;

    // The strings of a pattern table after the 41 patterns and the 7 24-hour ones: the four joining patterns, the Time-Day-Of-Week
    // append item, gmtFormat, and hourFormat.
    private const int JoiningPatterns = 48;
    private const int TimeAndWeekday = JoiningPatterns + 4;
    private const int GmtFormat = TimeAndWeekday + 1;
    private const int HourFormat = GmtFormat + 1;

    // The entries of an interval table (tools/CldrTableGen/DateTables.cs): the 37 skeletons without the 4 time zone ones that start at
    // FirstZoneSkeleton, then the 24-hour entries of j, jm, and jms.
    private const int FirstZoneSkeleton = 34;
    private const int ZoneSkeletons = 4;
    private const int FirstHourRangeEntry = 41 - ZoneSkeletons;

    // Text this long or shorter is parsed in a buffer on the stack; longer text in a rented array.
    private const int ParseCapacity = 256;

    // The most digits a field of parsed text takes, so that its value fits an int; the range is checked after it is read.
    private const int MaxFieldDigits = 9;

    private const char NoBreakSpace = (char)0x00A0;
    private const char NarrowNoBreakSpace = (char)0x202F;
    private const char IdeographicSpace = (char)0x3000;

    // Full-width punctuation (U+FF01 to U+FF5E), which parsing compares as the ASCII character 0xFEE0 below it (U+0021 to U+007E).
    private const char FirstFullWidth = (char)0xFF01;
    private const char LastFullWidth = (char)0xFF5E;
    private const int FullWidthOffset = 0xFEE0;

    // The skeletons of Flutter's validDateFormats, in the order of the pattern tables (tools/CldrTableGen/DateTables.cs): the first
    // 28 have date fields only, the rest time fields only, and the 7 that start at FirstHourSkeleton have j.
    private static readonly string[] Skeletons =
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

    private readonly LocaleData _locale;
    private readonly DateNames _names;
    private readonly DatePatternItem[] _items;
    private readonly char _zero;
    private readonly bool _hasDate;
    private readonly bool _hasTime;
    private readonly bool _hasZone;
    private readonly string _gmtPrefix;
    private readonly string _gmtSuffix;
    private readonly OffsetPattern _positive;
    private readonly OffsetPattern _negative;

    // What the format parses (design §4.11, "Parsing short dates"), the pattern's pieces as parsing reads them, and the am and pm of
    // the pattern's day period field without their bidi marks; empty for a format that does not parse them.
    private readonly ParseKind _parse;
    private readonly DatePatternItem[] _parseItems;
    private readonly string _am;
    private readonly string _pm;

    // The entry of the interval table that a range reads, -1 for a joined format and a time zone skeleton, and the entry a 24-hour
    // entry stands for when the table points it there; and the range's patterns, read when a range is first formatted, so that a
    // binary that formats no range keeps no interval table.
    private readonly int _rangeEntry;
    private readonly int _ownRangeEntry;
    private DateRangeFormat? _ranges;

    private DateFormatter(LocaleData locale, string pattern, int rangeEntry, int ownRangeEntry, ParseKind parse)
    {
        ReadOnlySpan<byte> table = locale.DatePatternTable;
        _locale = locale;
        _rangeEntry = rangeEntry;
        _ownRangeEntry = ownRangeEntry;
        _names = locale.Names;
        _items = DatePattern.Compile(pattern);
        _parse = parse;
        _parseItems = parse == ParseKind.None ? [] : ParseItems(_items, pattern);
        _am = string.Empty;
        _pm = string.Empty;
        foreach (DatePatternItem item in _parseItems)
        {
            if (item.Symbol == 'a')
            {
                _am = ParseText.WithoutBidiMarks(_names.DayPeriod(false, Width(item.Length), false));
                _pm = ParseText.WithoutBidiMarks(_names.DayPeriod(true, Width(item.Length), false));
            }
        }

        foreach (DatePatternItem item in _items)
        {
            switch (item.Symbol)
            {
                case '\0':
                    break;
                case 'G' or 'y' or 'Y' or 'Q' or 'M' or 'L' or 'd' or 'E' or 'c':
                    _hasDate = true;
                    break;
                case 'a' or 'h' or 'H' or 'm' or 's':
                    _hasTime = true;
                    break;
                case 'z' or 'v':
                    _hasTime = true;
                    _hasZone = true;
                    break;
                default:
                    // tools/CldrTableGen stops on a field TryWrite does not write, and quotes the letters of the joining patterns.
                    Invariant.Fail("The date pattern " + pattern + " has the field " + item.Symbol + ", which the formats do not write.");
                    break;
            }
        }

        var reader = new CldrTableReader(table);
        _zero = reader.ReadChar();
        string gmt = StringAt(table, GmtFormat);
        int placeholder = gmt.IndexOf("{0}", StringComparison.Ordinal);
        _gmtPrefix = gmt[..placeholder];
        _gmtSuffix = gmt[(placeholder + 3)..];
        string hourFormat = StringAt(table, HourFormat);
        int separator = hourFormat.IndexOf(';', StringComparison.Ordinal);
        _positive = OffsetPattern.Parse(hourFormat[..separator]);
        _negative = OffsetPattern.Parse(hourFormat[(separator + 1)..]);
    }

    // The types of value a format writes that can lack a field of the format; a DateTimeOffset has every field.
    private enum ValueKind : byte
    {
        DateTime,
        DateOnly,
        TimeOnly,
    }

    // What a format parses: nothing, a date (yMd), or a date and a time (yMd joined with jm).
    private enum ParseKind : byte
    {
        None,
        Date,
        DateTime,
    }

    /// <summary>
    /// Makes a format of one of Flutter's 41 skeletons.
    /// </summary>
    /// <param name="locale">The locale's tables.</param>
    /// <param name="skeleton">The skeleton: <c>d</c>, <c>E</c>, <c>EEEE</c>, <c>LLL</c>, <c>LLLL</c>, <c>M</c>, <c>Md</c>,
    /// <c>MEd</c>, <c>MMM</c>, <c>MMMd</c>, <c>MMMEd</c>, <c>MMMM</c>, <c>MMMMd</c>, <c>MMMMEEEEd</c>, <c>QQQ</c>, <c>QQQQ</c>,
    /// <c>y</c>, <c>yM</c>, <c>yMd</c>, <c>yMEd</c>, <c>yMMM</c>, <c>yMMMd</c>, <c>yMMMEd</c>, <c>yMMMM</c>, <c>yMMMMd</c>,
    /// <c>yMMMMEEEEd</c>, <c>yQQQ</c>, <c>yQQQQ</c>, <c>H</c>, <c>Hm</c>, <c>Hms</c>, <c>j</c>, <c>jm</c>, <c>jms</c>, <c>jmv</c>,
    /// <c>jmz</c>, <c>jv</c>, <c>jz</c>, <c>m</c>, <c>ms</c>, or <c>s</c>.</param>
    /// <param name="use24HourFormat">Whether a skeleton with <c>j</c> writes the 24-hour form (<see cref="Localization.Use24HourFormat"/>);
    /// <see langword="false"/> writes the form of the locale's region. The other skeletons do not read it.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> or <paramref name="skeleton"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="skeleton"/> is not one of the 41.</exception>
    public static DateFormatter FromSkeleton(LocaleData locale, string skeleton, bool use24HourFormat)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(skeleton);
        int index = Array.IndexOf(Skeletons, skeleton);
        if (index < 0)
        {
            ThrowHelper.ThrowNotASkeleton(skeleton, "one of Flutter's 41 date skeletons", nameof(skeleton));
        }

        int ownRangeEntry = RangeEntryOf(index);
        int rangeEntry = ownRangeEntry;
        if (use24HourFormat && index is >= FirstHourSkeleton and < FirstZoneSkeleton)
        {
            rangeEntry = FirstHourRangeEntry + index - FirstHourSkeleton;
        }

        string pattern = PatternOf(locale.DatePatternTable, index, use24HourFormat);
        return new DateFormatter(locale, pattern, rangeEntry, ownRangeEntry, skeleton == "yMd" ? ParseKind.Date : ParseKind.None);
    }

    /// <summary>
    /// Makes a format of a date skeleton and a time skeleton of the 41, joined by the locale's patterns that join a date and a time
    /// (CLDR's <c>dateTimeFormats-atTime</c>, or <c>dateTimeFormats</c> for a locale without them, as ICU's
    /// <c>DateTimePatternGenerator</c> reads them; UTS #35 part 4, "Matching Skeletons"): the full pattern when the date skeleton has a wide month and a weekday (<c>MMMMEEEEd</c>), the long one
    /// for a wide month alone (<c>yMMMMd</c>), the medium one for an abbreviated month (<c>yMMMd</c>), and the short one otherwise
    /// (<c>yMd</c>). A date skeleton of a weekday alone (<c>E</c>, <c>EEEE</c>) is joined by the <c>Time-Day-Of-Week</c> append item
    /// instead.
    /// </summary>
    /// <param name="locale">The locale's tables.</param>
    /// <param name="dateSkeleton">The date skeleton: one of the 41 from <c>d</c> to <c>yQQQQ</c>, which have no time field.</param>
    /// <param name="timeSkeleton">The time skeleton: one of the 41 from <c>H</c> to <c>s</c>, which have no date field.</param>
    /// <param name="use24HourFormat">Whether a time skeleton with <c>j</c> writes the 24-hour form; <see langword="false"/> writes the
    /// form of the locale's region.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/>, <paramref name="dateSkeleton"/>, or
    /// <paramref name="timeSkeleton"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="dateSkeleton"/> is not a date skeleton of the 41, or
    /// <paramref name="timeSkeleton"/> is not a time skeleton of the 41.</exception>
    public static DateFormatter FromSkeletons(LocaleData locale, string dateSkeleton, string timeSkeleton, bool use24HourFormat)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(dateSkeleton);
        ArgumentNullException.ThrowIfNull(timeSkeleton);
        int date = Array.IndexOf(Skeletons, dateSkeleton);
        if (date is < 0 or >= DateSkeletons)
        {
            ThrowHelper.ThrowNotASkeleton(dateSkeleton, "one of the 28 date skeletons of Flutter's 41", nameof(dateSkeleton));
        }

        int time = Array.IndexOf(Skeletons, timeSkeleton);
        if (time < DateSkeletons)
        {
            ThrowHelper.ThrowNotASkeleton(timeSkeleton, "one of the 13 time skeletons of Flutter's 41", nameof(timeSkeleton));
        }

        ReadOnlySpan<byte> table = locale.DatePatternTable;
        string joining = StringAt(table, JoiningOf(dateSkeleton));
        string pattern = Join(joining, PatternOf(table, date, use24HourFormat), PatternOf(table, time, use24HourFormat));
        bool parses = dateSkeleton == "yMd" && timeSkeleton == "jm";
        return new DateFormatter(locale, pattern, -1, -1, parses ? ParseKind.DateTime : ParseKind.None);
    }

    /// <summary>
    /// Writes a date and a time: the clock's fields as they are, whatever its <see cref="DateTime.Kind"/>. Allocates nothing.
    /// </summary>
    /// <param name="value">The date and time.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    /// <exception cref="ArgumentException">The format has a time zone field (<c>jmv</c>, <c>jmz</c>, <c>jv</c>, <c>jz</c>), which
    /// needs an offset.</exception>
    public bool TryFormat(DateTime value, Span<char> destination, out int charsWritten)
    {
        Check(ValueKind.DateTime);
        return TryWrite(value, 0, destination, out charsWritten);
    }

    /// <summary>
    /// Writes a date and a time with an offset: the clock's fields as they are, not converted to another zone, and the offset in a
    /// time zone field. Allocates nothing.
    /// </summary>
    /// <param name="value">The date, the time, and the offset.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    public bool TryFormat(DateTimeOffset value, Span<char> destination, out int charsWritten) =>
        TryWrite(value.DateTime, OffsetMinutes(value), destination, out charsWritten);

    /// <summary>
    /// Writes a date. Allocates nothing.
    /// </summary>
    /// <param name="value">The date.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    /// <exception cref="ArgumentException">The format has a time field.</exception>
    public bool TryFormat(DateOnly value, Span<char> destination, out int charsWritten)
    {
        Check(ValueKind.DateOnly);
        return TryWrite(value.ToDateTime(TimeOnly.MinValue), 0, destination, out charsWritten);
    }

    /// <summary>
    /// Writes a time. Allocates nothing.
    /// </summary>
    /// <param name="value">The time.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    /// <exception cref="ArgumentException">The format has a date field, or a time zone field, which needs an offset.</exception>
    public bool TryFormat(TimeOnly value, Span<char> destination, out int charsWritten)
    {
        Check(ValueKind.TimeOnly);
        return TryWrite(DateTime.MinValue.Add(value.ToTimeSpan()), 0, destination, out charsWritten);
    }

    /// <summary>
    /// Returns a date and a time with this format, for an interpolation of a <see cref="NodeTextWriter"/>. Allocates nothing.
    /// </summary>
    /// <param name="value">The date and time.</param>
    /// <returns>The value and the format.</returns>
    /// <exception cref="ArgumentException">The format has a time zone field.</exception>
    public FormattedDate Of(DateTime value)
    {
        Check(ValueKind.DateTime);
        return new FormattedDate(this, value, 0);
    }

    /// <summary>
    /// Returns a date and a time with an offset with this format, for an interpolation of a <see cref="NodeTextWriter"/>. Allocates
    /// nothing.
    /// </summary>
    /// <param name="value">The date, the time, and the offset.</param>
    /// <returns>The value and the format.</returns>
    public FormattedDate Of(DateTimeOffset value) => new(this, value.DateTime, OffsetMinutes(value));

    /// <summary>
    /// Returns a date with this format, for an interpolation of a <see cref="NodeTextWriter"/>. Allocates nothing.
    /// </summary>
    /// <param name="value">The date.</param>
    /// <returns>The value and the format.</returns>
    /// <exception cref="ArgumentException">The format has a time field.</exception>
    public FormattedDate Of(DateOnly value)
    {
        Check(ValueKind.DateOnly);
        return new FormattedDate(this, value.ToDateTime(TimeOnly.MinValue), 0);
    }

    /// <summary>
    /// Returns a time with this format, for an interpolation of a <see cref="NodeTextWriter"/>. Allocates nothing.
    /// </summary>
    /// <param name="value">The time.</param>
    /// <returns>The value and the format.</returns>
    /// <exception cref="ArgumentException">The format has a date field or a time zone field.</exception>
    public FormattedDate Of(TimeOnly value)
    {
        Check(ValueKind.TimeOnly);
        return new FormattedDate(this, DateTime.MinValue.Add(value.ToTimeSpan()), 0);
    }

    /// <summary>
    /// Writes a range of two dates (CLDR's <c>intervalFormats</c>, design §4.11, "Interval formats"): a field the two have the same is
    /// written once where the locale's interval pattern allows (<c>Jan 3 – 9, 2026</c>), two dates that differ only in a field
    /// smaller than the format's are written as the start alone, and the others are both written whole and joined by the locale's
    /// <c>intervalFormatFallback</c>. Allocates nothing once the format has written a range.
    /// </summary>
    /// <param name="start">The first day of the range.</param>
    /// <param name="end">The last day of the range.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    /// <exception cref="ArgumentException">The format has a time field, or <paramref name="start"/> is after
    /// <paramref name="end"/>.</exception>
    /// <exception cref="InvalidOperationException">The format joins a date skeleton and a time skeleton
    /// (<see cref="FromSkeletons"/>).</exception>
    public bool TryFormatRange(DateOnly start, DateOnly end, Span<char> destination, out int charsWritten)
    {
        CheckRange(ValueKind.DateOnly, start > end);
        return TryWriteRange(start.ToDateTime(TimeOnly.MinValue), end.ToDateTime(TimeOnly.MinValue), destination, out charsWritten);
    }

    /// <summary>
    /// Writes a range of two dates and times, as <see cref="TryFormatRange(DateOnly, DateOnly, Span{char}, out int)"/> writes two
    /// dates: the clocks' fields as they are, whatever their <see cref="DateTime.Kind"/>, compared from the year down to the second.
    /// Allocates nothing once the format has written a range.
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    /// <exception cref="ArgumentException">The format has a time zone field (<c>jmv</c>, <c>jmz</c>, <c>jv</c>, <c>jz</c>), which
    /// needs an offset, or <paramref name="start"/> is after <paramref name="end"/>.</exception>
    /// <exception cref="InvalidOperationException">The format joins a date skeleton and a time skeleton
    /// (<see cref="FromSkeletons"/>).</exception>
    public bool TryFormatRange(DateTime start, DateTime end, Span<char> destination, out int charsWritten)
    {
        CheckRange(ValueKind.DateTime, start > end);
        return TryWriteRange(start, end, destination, out charsWritten);
    }

    /// <summary>
    /// Returns a range of two dates with this format, for an interpolation of a <see cref="NodeTextWriter"/>; it writes what
    /// <see cref="TryFormatRange(DateOnly, DateOnly, Span{char}, out int)"/> writes. Allocates nothing once the format has written
    /// or returned a range.
    /// </summary>
    /// <param name="start">The first day of the range.</param>
    /// <param name="end">The last day of the range.</param>
    /// <returns>The range and the format.</returns>
    /// <exception cref="ArgumentException">The format has a time field, or <paramref name="start"/> is after
    /// <paramref name="end"/>.</exception>
    /// <exception cref="InvalidOperationException">The format joins a date skeleton and a time skeleton
    /// (<see cref="FromSkeletons"/>).</exception>
    public FormattedDateRange OfRange(DateOnly start, DateOnly end)
    {
        CheckRange(ValueKind.DateOnly, start > end);
        return new FormattedDateRange(this, start.ToDateTime(TimeOnly.MinValue), end.ToDateTime(TimeOnly.MinValue));
    }

    /// <summary>
    /// Returns a range of two dates and times with this format, for an interpolation of a <see cref="NodeTextWriter"/>; it writes
    /// what <see cref="TryFormatRange(DateTime, DateTime, Span{char}, out int)"/> writes. Allocates nothing once the format has
    /// written or returned a range.
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <returns>The range and the format.</returns>
    /// <exception cref="ArgumentException">The format has a time zone field, or <paramref name="start"/> is after
    /// <paramref name="end"/>.</exception>
    /// <exception cref="InvalidOperationException">The format joins a date skeleton and a time skeleton
    /// (<see cref="FromSkeletons"/>).</exception>
    public FormattedDateRange OfRange(DateTime start, DateTime end)
    {
        CheckRange(ValueKind.DateTime, start > end);
        return new FormattedDateRange(this, start, end);
    }

    /// <summary>
    /// Returns a date and a time formatted as a new string; not for the frame path.
    /// </summary>
    /// <param name="value">The date and time.</param>
    /// <returns>The formatted date and time.</returns>
    /// <exception cref="ArgumentException">The format has a time zone field.</exception>
    public string Format(DateTime value) => Of(value).ToString();

    /// <summary>
    /// Reads a date the format of <c>yMd</c> writes, strictly in the order of its pattern (design §4.11, "Parsing short dates"): each
    /// number as many digits as it has, of the locale's digits, ASCII digits, or full-width digits (U+FF10 to U+FF19), and the year
    /// as written (<c>26</c> is the year 26); each separator as the pattern writes it, but that U+0020, U+00A0, U+202F, and U+3000
    /// each stand for any of the four, and full-width punctuation (U+FF01 to U+FF5E) for its ASCII character. White space at either
    /// end and the bidi marks U+200E, U+200F, and U+061C anywhere are skipped. Allocates nothing for text of up to 256 characters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The date; <see langword="default"/> when the text is not read.</param>
    /// <returns><see langword="false"/> when the text does not follow the pattern, or has a year outside 1 to 9999, a month outside 1
    /// to 12, or a day the month does not have.</returns>
    /// <exception cref="InvalidOperationException">The format is not of the <c>yMd</c> skeleton alone
    /// (<see cref="FromSkeleton"/>).</exception>
    public bool TryParse(ReadOnlySpan<char> text, out DateOnly value)
    {
        if (_parse != ParseKind.Date)
        {
            ThrowHelper.ThrowDateParseNotSupported("Only a format of the yMd skeleton (FromSkeleton) parses a date.");
        }

        if (TryRead(text, out DateTime clock))
        {
            value = DateOnly.FromDateTime(clock);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads a date and a time the format of <c>yMd</c> joined with <c>jm</c> writes, as
    /// <see cref="TryParse(ReadOnlySpan{char}, out DateOnly)"/> reads a date: an hour of <c>h</c> is 1 to 12 and takes the pattern's
    /// am or pm, compared ordinally and ignoring case (12 am is the hour 0), an hour of <c>H</c> is 0 to 23, and a minute 0 to 59.
    /// The value's <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/> and its seconds are 0; a time that a
    /// change to daylight saving time skips is not checked. Allocates nothing for text of up to 256 characters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The date and time; <see langword="default"/> when the text is not read.</param>
    /// <returns><see langword="false"/> when the text does not follow the pattern, or has a field outside its range.</returns>
    /// <exception cref="InvalidOperationException">The format is not of <c>yMd</c> joined with <c>jm</c>
    /// (<see cref="FromSkeletons"/>).</exception>
    public bool TryParse(ReadOnlySpan<char> text, out DateTime value)
    {
        if (_parse != ParseKind.DateTime)
        {
            ThrowHelper.ThrowDateParseNotSupported(
                "Only a format of the yMd skeleton joined with the jm skeleton (FromSkeletons) parses a date and a time."
            );
        }

        return TryRead(text, out value);
    }

    /// <summary>
    /// Writes the fields of a clock and an offset. The caller has checked that the value has every field the format writes.
    /// </summary>
    /// <param name="clock">The date and time.</param>
    /// <param name="offsetMinutes">The offset from GMT in minutes, for a time zone field.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    internal bool TryWrite(DateTime clock, int offsetMinutes, Span<char> destination, out int charsWritten)
    {
        var writer = new CharWriter(destination);
        Write(ref writer, _items, clock, offsetMinutes);
        return writer.TryFinish(out charsWritten);
    }

    /// <summary>
    /// Writes a range of two clocks with the patterns of the format's interval table entry (design §4.11, "Interval formats"). The
    /// caller has checked the values' type and order, which also read the patterns.
    /// </summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range, not before <paramref name="start"/>.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    internal bool TryWriteRange(DateTime start, DateTime end, Span<char> destination, out int charsWritten)
    {
        DateRangeFormat ranges = Ranges();
        var writer = new CharWriter(destination);
        int field = DateRangeFormat.GreatestDifference(start, end);
        DateRangeFormat.Slot slot = field < 0 ? default : ranges.SlotOf(field);
        switch (slot.Kind)
        {
            case DateRangeFormat.SlotKind.Single:
                Write(ref writer, _items, start, 0);
                break;
            case DateRangeFormat.SlotKind.Split:
                Write(ref writer, slot.First!, slot.LaterFirst ? end : start, 0);
                Write(ref writer, slot.Second!, slot.LaterFirst ? start : end, 0);
                break;
            default:
                DatePatternItem[] items = slot.First ?? _items;
                writer.Append(ranges.Before);
                Write(ref writer, items, ranges.LaterFirst ? end : start, 0);
                writer.Append(ranges.Between);
                Write(ref writer, items, ranges.LaterFirst ? start : end, 0);
                writer.Append(ranges.After);
                break;
        }

        return writer.TryFinish(out charsWritten);
    }

    // Writes the fields of a pattern with a clock and an offset.
    private void Write(ref CharWriter writer, DatePatternItem[] items, DateTime clock, int offsetMinutes)
    {
        foreach (DatePatternItem item in items)
        {
            int length = item.Length;
            switch (item.Symbol)
            {
                case '\0':
                    writer.Append(item.Literal);
                    break;
                case 'G':
                    // DateTime holds years 1 to 9999, all of the common era.
                    writer.Append(_names.Era(true, Width(length)));
                    break;
                case 'y':
                    WriteNumber(ref writer, clock.Year, length);
                    break;
                case 'Y':
                    _ = _locale.WeekOfYear(DateOnly.FromDateTime(clock), out int weekYear);
                    WriteNumber(ref writer, weekYear, length);
                    break;
                case 'Q':
                    int quarter = ((clock.Month - 1) / 3) + 1;
                    WriteNumberOrName(ref writer, quarter, length, length > 2 ? _names.Quarter(quarter, Width(length), false) : null);
                    break;
                case 'M' or 'L':
                    string? month = length > 2 ? _names.Month(clock.Month, Width(length), item.Symbol == 'L') : null;
                    WriteNumberOrName(ref writer, clock.Month, length, month);
                    break;
                case 'd':
                    WriteNumber(ref writer, clock.Day, length);
                    break;
                case 'E' or 'c':
                    writer.Append(_names.Weekday(clock.DayOfWeek, WeekdayWidth(length), item.Symbol == 'c'));
                    break;
                case 'a':
                    writer.Append(_names.DayPeriod(clock.Hour >= 12, Width(length), false));
                    break;
                case 'h':
                    WriteNumber(ref writer, clock.Hour % 12 == 0 ? 12 : clock.Hour % 12, length);
                    break;
                case 'H':
                    WriteNumber(ref writer, clock.Hour, length);
                    break;
                case 'm':
                    WriteNumber(ref writer, clock.Minute, length);
                    break;
                case 's':
                    WriteNumber(ref writer, clock.Second, length);
                    break;
                default:
                    // z and v: the constructor lets no other field through, and tools/CldrTableGen checks the fields of the range
                    // patterns, which have no zone.
                    WriteOffset(ref writer, offsetMinutes, length == 4);
                    break;
            }
        }
    }

    private static int OffsetMinutes(DateTimeOffset value) => (int)(value.Offset.Ticks / TimeSpan.TicksPerMinute);

    // The entry of a skeleton in the locale's hour in an interval table, which has none for the time zone skeletons (-1).
    private static int RangeEntryOf(int index)
    {
        if (index < FirstZoneSkeleton)
        {
            return index;
        }

        return index >= FirstZoneSkeleton + ZoneSkeletons ? index - ZoneSkeletons : -1;
    }

    // The width of a text field: 1 to 3 letters abbreviated, 4 wide, 5 narrow.
    private static DateNameWidth Width(int length) =>
        length switch
        {
            <= 3 => DateNameWidth.Abbreviated,
            4 => DateNameWidth.Wide,
            _ => DateNameWidth.Narrow,
        };

    // The width of a weekday: 6 letters are the short form.
    private static DateNameWidth WeekdayWidth(int length) => length == 6 ? DateNameWidth.Short : Width(length);

    // The joining pattern of a date skeleton (UTS #35 part 4, "Matching Skeletons", the combining of a date and a time).
    private static int JoiningOf(string dateSkeleton)
    {
        if (dateSkeleton is "E" or "EEEE")
        {
            return TimeAndWeekday;
        }

        if (dateSkeleton.Contains("MMMM", StringComparison.Ordinal) || dateSkeleton.Contains("LLLL", StringComparison.Ordinal))
        {
            return JoiningPatterns + (dateSkeleton.Contains('E', StringComparison.Ordinal) ? 0 : 1);
        }

        bool abbreviatedMonth = dateSkeleton.Contains("MMM", StringComparison.Ordinal) || dateSkeleton.Contains("LLL", StringComparison.Ordinal);
        return JoiningPatterns + (abbreviatedMonth ? 2 : 3);
    }

    // A joining pattern with {1} replaced by the date pattern and {0} by the time pattern; the rest of it is pattern text.
    private static string Join(string joining, string date, string time)
    {
        int first = joining.IndexOf('{', StringComparison.Ordinal);
        int second = joining.IndexOf('{', first + 1);
        string firstPattern = joining[first + 1] == '1' ? date : time;
        string secondPattern = joining[first + 1] == '1' ? time : date;
        return string.Concat(joining[..first], firstPattern, joining[(first + 3)..second], secondPattern, joining[(second + 3)..]);
    }

    // The pattern of a skeleton: its 24-hour pattern for a j skeleton when asked and the table has one, else its own.
    private static string PatternOf(ReadOnlySpan<byte> table, int index, bool use24HourFormat)
    {
        if (use24HourFormat && index is >= FirstHourSkeleton and < FirstHourSkeleton + HourSkeletons)
        {
            string pattern = StringAt(table, Skeletons.Length + index - FirstHourSkeleton);
            if (pattern.Length > 0)
            {
                return pattern;
            }
        }

        return StringAt(table, index);
    }

    // The string at an index of a pattern table: the strings start after the digit zero.
    private static string StringAt(ReadOnlySpan<byte> table, int index)
    {
        var reader = new CldrTableReader(table);
        _ = reader.ReadChar();
        for (int i = 0; i < index; i++)
        {
            reader.SkipString();
        }

        return reader.ReadString();
    }

    // The pieces of a pattern as parsing reads them: the literals without their bidi marks, the first without the white space at its
    // start and the last without the white space at its end, as parsing trims the text; a literal left empty is dropped.
    private static DatePatternItem[] ParseItems(DatePatternItem[] items, string pattern)
    {
        var parsed = new List<DatePatternItem>(items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            DatePatternItem item = items[i];
            if (item.Symbol != '\0')
            {
                bool read = item.Symbol is 'y' or 'h' or 'H' or 'm' or 'a' || (item.Symbol is 'M' or 'd' && item.Length <= 2);
                if (!read)
                {
                    // tools/CldrTableGen stops on a yMd or jm pattern with another field (design §4.11, "The generator's stop").
                    Invariant.Fail("The date pattern " + pattern + " has the field " + item.Symbol + ", which parsing does not read.");
                }

                parsed.Add(item);
                continue;
            }

            string literal = ParseText.WithoutBidiMarks(item.Literal);
            literal = i == 0 ? literal.TrimStart() : literal;
            literal = i == items.Length - 1 ? literal.TrimEnd() : literal;
            if (literal.Length > 0)
            {
                parsed.Add(item with { Literal = literal });
            }
        }

        return [.. parsed];
    }

    // Reads a literal of a pattern at a position of the text, a character for each of its characters, as Fold compares them.
    private static bool TryReadLiteral(ReadOnlySpan<char> text, ref int at, string literal)
    {
        foreach (char expected in literal)
        {
            if (at >= text.Length || Fold(text[at]) != Fold(expected))
            {
                return false;
            }

            at++;
        }

        return true;
    }

    // A character as parsing compares a separator (design §4.11, "Separators, am and pm, full-width symbols"): U+0020, U+00A0,
    // U+202F, and the full-width space U+3000 as a space, and full-width punctuation as its ASCII character.
    private static char Fold(char character) =>
        character switch
        {
            ' ' or NoBreakSpace or NarrowNoBreakSpace or IdeographicSpace => ' ',
            >= FirstFullWidth and <= LastFullWidth => (char)(character - FullWidthOffset),
            _ => character,
        };

    // Throws ArgumentException when the format does not take a value of a type: a date for a format with time fields, a time for a
    // format with date fields, and a value with no offset for a format with a time zone field (design §4.11, "Value types").
    private void Check(ValueKind kind)
    {
        if (kind == ValueKind.DateOnly && _hasTime)
        {
            ThrowHelper.ThrowDateValueNotTaken("A DateOnly has no time, and the format writes a time field.", "value");
        }

        if (kind == ValueKind.TimeOnly && _hasDate)
        {
            ThrowHelper.ThrowDateValueNotTaken("A TimeOnly has no date, and the format writes a date field.", "value");
        }

        if (_hasZone)
        {
            ThrowHelper.ThrowDateValueNotTaken("The format writes a time zone, which only a DateTimeOffset has.", "value");
        }
    }

    // Throws ArgumentException, naming start, when the format does not take a range of a type: a range of dates for a format with
    // time fields, and any range for a format with a time zone field, as a range takes no offset (design §4.11, "The table");
    // InvalidOperationException for a joined format, which has no interval patterns; and ArgumentException for a range whose start
    // is after its end. Then reads the range's patterns when no range has read them.
    private void CheckRange(ValueKind kind, bool reversed)
    {
        if (kind == ValueKind.DateOnly && _hasTime)
        {
            ThrowHelper.ThrowDateValueNotTaken("A range of DateOnly values has no time, and the format writes a time field.", "start");
        }

        if (_hasZone)
        {
            ThrowHelper.ThrowDateValueNotTaken("The format writes a time zone, and a range takes no offset.", "start");
        }

        // A time zone skeleton has no entry either, but it has thrown above.
        if (_rangeEntry < 0)
        {
            ThrowHelper.ThrowJoinedFormatRange();
        }

        if (reversed)
        {
            ThrowHelper.ThrowRangeStartAfterEnd();
        }

        _ = Ranges();
    }

    // The range's patterns, read from the interval table when first asked for. Two threads that ask first at once may both read
    // them; the first stored is the one both use.
    private DateRangeFormat Ranges()
    {
        DateRangeFormat? ranges = Volatile.Read(ref _ranges);
        if (ranges is null)
        {
            ranges = DateRangeFormat.Read(_locale.DateIntervalTable, _rangeEntry, _ownRangeEntry);
            ranges = Interlocked.CompareExchange(ref _ranges, ranges, null) ?? ranges;
        }

        return ranges;
    }

    private void WriteNumberOrName(ref CharWriter writer, int value, int length, string? name)
    {
        if (name is null)
        {
            WriteNumber(ref writer, value, length);
        }
        else
        {
            writer.Append(name);
        }
    }

    // Writes a number of at least a count of digits, in the locale's digits.
    private void WriteNumber(ref CharWriter writer, int value, int minimumDigits)
    {
        Span<char> digits = stackalloc char[10];
        digits[0] = (char)(_zero + (value % 10));
        int count = 1;
        for (value /= 10; value > 0; value /= 10)
        {
            digits[count++] = (char)(_zero + (value % 10));
        }

        for (int i = count; i < minimumDigits; i++)
        {
            writer.Append(_zero);
        }

        for (int i = count - 1; i >= 0; i--)
        {
            writer.Append(digits[i]);
        }
    }

    // Writes an offset from GMT in the locale's gmtFormat (UTS #35 part 4, "Time Zone Format Terminology", the localized GMT
    // format), 0 with the positive pattern (GMT+0, GMT+00:00; Issue #906): the short form with the hours unpadded and, when the
    // minutes are 0, without the hourFormat's text from the separator on, as ICU's TimeZoneFormat::truncateOffsetPattern cuts it
    // after the hours (he's negative pattern, -HH:mm and U+200E, writes -8 without the mark); the long form with the hours in two
    // digits and the minutes, whether the hourFormat has H or HH, as TimeZoneFormat::formatOffsetLocalizedGMT writes them (cs's
    // +H:mm writes GMT+09:00; Issue #906).
    private void WriteOffset(ref CharWriter writer, int offsetMinutes, bool isLong)
    {
        OffsetPattern sign = offsetMinutes >= 0 ? _positive : _negative;
        int magnitude = Math.Abs(offsetMinutes);
        writer.Append(_gmtPrefix);
        writer.Append(sign.Prefix);
        WriteNumber(ref writer, magnitude / 60, isLong ? 2 : 1);
        if (isLong || magnitude % 60 != 0)
        {
            writer.Append(sign.Separator);
            WriteNumber(ref writer, magnitude % 60, 2);
            writer.Append(sign.Suffix);
        }

        writer.Append(_gmtSuffix);
    }

    // Reads the text without its bidi marks and the white space at its ends.
    private bool TryRead(ReadOnlySpan<char> text, out DateTime value)
    {
        char[]? rented = null;
        Span<char> buffer = text.Length <= ParseCapacity ? stackalloc char[ParseCapacity] : (rented = ArrayPool<char>.Shared.Rent(text.Length));
        try
        {
            int length = ParseText.WithoutBidiMarks(text, buffer);
            return TryReadFields(buffer[..length].Trim(), out value);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    // Reads the pieces of the pattern in their order, then checks the fields' ranges (design §4.11, "Parsing short dates" and "The
    // hour fields and the result").
    private bool TryReadFields(ReadOnlySpan<char> text, out DateTime value)
    {
        value = default;
        int year = 0;
        int month = 0;
        int day = 0;
        int hour = 0;
        int minute = 0;
        bool twelveHour = false;
        bool pm = false;
        int at = 0;
        foreach (DatePatternItem item in _parseItems)
        {
            if (item.Symbol == '\0')
            {
                if (!TryReadLiteral(text, ref at, item.Literal))
                {
                    return false;
                }

                continue;
            }

            if (item.Symbol == 'a')
            {
                if (!TryReadDayPeriod(text, ref at, out pm))
                {
                    return false;
                }

                continue;
            }

            int number = ReadNumber(text, ref at);
            if (number < 0)
            {
                return false;
            }

            switch (item.Symbol)
            {
                case 'y':
                    year = number;
                    break;
                case 'M':
                    month = number;
                    break;
                case 'd':
                    day = number;
                    break;
                case 'h':
                    hour = number;
                    twelveHour = true;
                    break;
                case 'H':
                    hour = number;
                    break;
                default:
                    // m: ParseItems lets no other field through.
                    minute = number;
                    break;
            }
        }

        bool date = at == text.Length && year is >= 1 and <= 9999 && month is >= 1 and <= 12;
        if (!date || day < 1 || day > DateTime.DaysInMonth(year, month) || minute > 59)
        {
            return false;
        }

        // tools/CldrTableGen lets h into a jm pattern only with a, so a read h has its am or pm.
        if (twelveHour ? (hour is < 1 or > 12) : hour > 23)
        {
            return false;
        }

        hour = twelveHour ? (hour % 12) + (pm ? 12 : 0) : hour;
        value = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return true;
    }

    // Reads a number at a position: every digit there, but -1 for none or for more than MaxFieldDigits.
    private int ReadNumber(ReadOnlySpan<char> text, ref int at)
    {
        int value = 0;
        int count = 0;
        while (at < text.Length)
        {
            int digit = ParseText.DigitValue(text[at], _zero);
            if (digit < 0)
            {
                break;
            }

            if (count == MaxFieldDigits)
            {
                return -1;
            }

            value = (value * 10) + digit;
            count++;
            at++;
        }

        return count == 0 ? -1 : value;
    }

    // Reads the pattern's am or pm at a position, ordinally and ignoring case, as ICU's matchString and .NET's TryParseExact compare
    // them; neither depends on culture data, so it reads them under InvariantGlobalization too.
    private bool TryReadDayPeriod(ReadOnlySpan<char> text, ref int at, out bool pm)
    {
        ReadOnlySpan<char> rest = text[at..];
        pm = rest.StartsWith(_pm, StringComparison.OrdinalIgnoreCase);
        if (!pm && !rest.StartsWith(_am, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        at += pm ? _pm.Length : _am.Length;
        return true;
    }

    /// <summary>
    /// One side of an <c>hourFormat</c> (<c>+HH:mm</c>): the text before the hours, the text between the hours and the minutes, and
    /// the text after the minutes.
    /// </summary>
    /// <param name="Prefix">The text before the hours, with the sign.</param>
    /// <param name="Separator">The text between the hours and the minutes.</param>
    /// <param name="Suffix">The text after the minutes.</param>
    private readonly record struct OffsetPattern(string Prefix, string Separator, string Suffix)
    {
        // tools/CldrTableGen checks the form: text, H or HH, text, mm, text.
        public static OffsetPattern Parse(string pattern)
        {
            int hour = pattern.IndexOf('H', StringComparison.Ordinal);
            int hoursEnd = pattern.LastIndexOf('H') + 1;
            int minute = pattern.IndexOf("mm", StringComparison.Ordinal);
            return new OffsetPattern(pattern[..hour], pattern[hoursEnd..minute], pattern[(minute + 2)..]);
        }
    }
}
