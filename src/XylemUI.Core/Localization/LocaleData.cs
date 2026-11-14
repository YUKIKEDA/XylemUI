namespace XylemUI;

/// <summary>
/// The CLDR tables of one locale (design §4.11, "Formats"): its plural rules and its week. An app takes them from
/// <see cref="CldrLocales"/>, and only the tables it references stay in a NativeAOT binary.
/// </summary>
/// <remarks>
/// Apps cannot derive from the class. An instance does not change, so any thread may read it; the values made when first read are
/// made once (design §4.11, "Allocation and threads").
/// </remarks>
public abstract class LocaleData
{
    private PluralRules? _plurals;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocaleData"/> class.
    /// </summary>
    /// <param name="locale">The locale the tables are for.</param>
    /// <param name="firstDayOfWeek">The first day of the week.</param>
    /// <param name="minimalDaysInFirstWeek">The days of the new year that the first week of a year holds at least (1 to 7).</param>
    /// <param name="weekendStart">The first day of the weekend.</param>
    /// <param name="weekendEnd">The last day of the weekend.</param>
    internal LocaleData(Locale locale, DayOfWeek firstDayOfWeek, int minimalDaysInFirstWeek, DayOfWeek weekendStart, DayOfWeek weekendEnd)
    {
        Locale = locale;
        FirstDayOfWeek = firstDayOfWeek;
        MinimalDaysInFirstWeek = minimalDaysInFirstWeek;
        WeekendStart = weekendStart;
        WeekendEnd = weekendEnd;
    }

    /// <summary>
    /// Gets the tables of CLDR's root locale (design §4.11, "When there is no table"). Its locale is the undetermined one, and its
    /// week is the one CLDR gives the world (region 001).
    /// </summary>
    public static LocaleData Root => CldrRoot.Instance;

    /// <summary>
    /// Gets the locale the tables are for.
    /// </summary>
    public Locale Locale { get; }

    /// <summary>
    /// Gets the plural rules, made when first read.
    /// </summary>
    public PluralRules Plurals
    {
        get
        {
            PluralRules? plurals = _plurals;
            if (plurals is null)
            {
                // Two threads that read it first at once may both make one; the first stored is the one both return.
                plurals = CreatePlurals();
                plurals = Interlocked.CompareExchange(ref _plurals, plurals, null) ?? plurals;
            }

            return plurals;
        }
    }

    /// <summary>
    /// Gets the first day of the week, CLDR's value for the locale's region, or for its likely region when the tag has none
    /// (<c>ja</c> takes Japan's).
    /// </summary>
    public DayOfWeek FirstDayOfWeek { get; }

    /// <summary>
    /// Gets the days of the new year that the first week of a year holds at least, from 1 to 7 (CLDR's <c>minDays</c> of the same
    /// region).
    /// </summary>
    public int MinimalDaysInFirstWeek { get; }

    /// <summary>
    /// Gets the first day of the weekend (CLDR's <c>weekendStart</c> of the same region).
    /// </summary>
    public DayOfWeek WeekendStart { get; }

    /// <summary>
    /// Gets the last day of the weekend (CLDR's <c>weekendEnd</c> of the same region); the same day as <see cref="WeekendStart"/>
    /// when the weekend is one day.
    /// </summary>
    public DayOfWeek WeekendEnd { get; }

    /// <summary>
    /// Returns the week of the year a date is in, as UTS #35 part 4 ("Week Data") counts it: the weeks start on
    /// <see cref="FirstDayOfWeek"/>, and the first week of a year is the first that holds at least
    /// <see cref="MinimalDaysInFirstWeek"/> days of it. Days before it are in the last week of the year before, and days of a week
    /// that is the first of the next year are in that year. A year has 52 or 53 weeks.
    /// </summary>
    /// <param name="date">The date.</param>
    /// <param name="year">The year the week belongs to: the date's year, the year before, or the year after. It is 0 for a date in
    /// the last week of the year before 1, and 10000 for one in the first week of the year after 9999.</param>
    /// <returns>The week's number, from 1 to 53.</returns>
    public int WeekOfYear(DateOnly date, out int year)
    {
        int day = date.DayNumber;
        year = date.Year;
        int start = FirstWeekStart(year);
        if (day < start)
        {
            year--;
            start = FirstWeekStart(year);
        }
        else
        {
            int next = FirstWeekStart(year + 1);
            if (day >= next)
            {
                year++;
                start = next;
            }
        }

        return ((day - start) / 7) + 1;
    }

    /// <summary>
    /// Returns these tables under another tag, for a locale that CLDR does not have (<c>en-RU</c>; design §4.11, "Locales that CLDR
    /// does not have"): their <see cref="Locale"/> is <paramref name="locale"/>, and every value, the week included, is that of
    /// these tables, not read again for the tag's region.
    /// </summary>
    /// <param name="locale">The tag the tables are given.</param>
    /// <returns>The tables under the tag.</returns>
    public LocaleData WithLocale(Locale locale) => new TaggedLocaleData(locale, this);

    /// <summary>
    /// Makes the plural rules of the tables, which <see cref="Plurals"/> keeps.
    /// </summary>
    /// <returns>The rules.</returns>
    internal abstract PluralRules CreatePlurals();

    // The day number (DateOnly.DayNumber) of the first day of the first week of a year. Years 0 and 10000 are outside DateOnly, so
    // the day number of a year's first day is computed in the proleptic Gregorian calendar, with floor division.
    private int FirstWeekStart(int year)
    {
        int before = year - 1;
        int january1 = (365 * before) + FloorDivide(before, 4) - FloorDivide(before, 100) + FloorDivide(before, 400);

        // Day number 0, the 1st of January of the year 1, is a Monday; DayOfWeek counts from Sunday.
        int dayOfWeek = Modulo(january1 + 1, 7);
        int offset = Modulo(dayOfWeek - (int)FirstDayOfWeek, 7);
        int weekStart = january1 - offset;
        return 7 - offset >= MinimalDaysInFirstWeek ? weekStart : weekStart + 7;
    }

    private static int FloorDivide(int value, int divisor) => (value >= 0 ? value : value - divisor + 1) / divisor;

    private static int Modulo(int value, int divisor) => ((value % divisor) + divisor) % divisor;

    // The tables of another LocaleData under another tag: its week is copied, and its plural rules are the same instance.
    private sealed class TaggedLocaleData : LocaleData
    {
        private readonly LocaleData _tables;

        public TaggedLocaleData(Locale locale, LocaleData tables)
            : base(locale, tables.FirstDayOfWeek, tables.MinimalDaysInFirstWeek, tables.WeekendStart, tables.WeekendEnd)
        {
            _tables = tables;
        }

        internal override PluralRules CreatePlurals() => _tables.Plurals;
    }
}
