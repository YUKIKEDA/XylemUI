namespace XylemUI;

/// <summary>
/// The names of a locale's Gregorian calendar (design §4.11, "Names and the week"): its months, weekdays, quarters, eras, and day
/// periods, in each width, in the form used inside a date (CLDR's format context) and in the form shown alone (the stand-alone
/// context). <see cref="LocaleData.Names"/> reads them once from the locale's table and keeps them; an instance does not change, so
/// any thread may read it.
/// </summary>
/// <remarks>
/// The methods return the strings the instance holds and allocate nothing.
/// </remarks>
public sealed class DateNames
{
    private const int MonthCount = 12;
    private const int WeekdayCount = 7;
    private const int QuarterCount = 4;
    private const int TextWidths = 3;
    private const int WeekdayWidths = 4;

    private readonly string[] _months;
    private readonly string[] _weekdays;
    private readonly string[] _quarters;
    private readonly string[] _dayPeriods;
    private readonly string[] _eras;

    private DateNames(string[] months, string[] weekdays, string[] quarters, string[] dayPeriods, string[] eras)
    {
        _months = months;
        _weekdays = weekdays;
        _quarters = quarters;
        _dayPeriods = dayPeriods;
        _eras = eras;
    }

    /// <summary>
    /// Returns the name of a month.
    /// </summary>
    /// <param name="month">The month, from 1 (January) to 12.</param>
    /// <param name="width">The width: abbreviated, wide, or narrow.</param>
    /// <param name="standAlone"><see langword="true"/> for the form shown alone (<c>LLLL</c>), <see langword="false"/> for the form
    /// inside a date (<c>MMMM</c>).</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="month"/> is not from 1 to 12, or <paramref name="width"/> is
    /// <see cref="DateNameWidth.Short"/> or not a member of <see cref="DateNameWidth"/>.</exception>
    public string Month(int month, DateNameWidth width, bool standAlone)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, MonthCount);
        return _months[(((Context(standAlone) * TextWidths) + TextWidth(width)) * MonthCount) + month - 1];
    }

    /// <summary>
    /// Returns the name of a weekday.
    /// </summary>
    /// <param name="day">The weekday.</param>
    /// <param name="width">The width: abbreviated, wide, narrow, or short.</param>
    /// <param name="standAlone"><see langword="true"/> for the form shown alone (<c>cccc</c>), <see langword="false"/> for the form
    /// inside a date (<c>EEEE</c>).</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="day"/> or <paramref name="width"/> is not a member of its
    /// type.</exception>
    public string Weekday(DayOfWeek day, DateNameWidth width, bool standAlone)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)day, (uint)DayOfWeek.Saturday, nameof(day));
        ArgumentOutOfRangeException.ThrowIfGreaterThan((byte)width, (byte)DateNameWidth.Short, nameof(width));
        return _weekdays[(((Context(standAlone) * WeekdayWidths) + (int)width) * WeekdayCount) + (int)day];
    }

    /// <summary>
    /// Returns the name of a quarter.
    /// </summary>
    /// <param name="quarter">The quarter, from 1 to 4.</param>
    /// <param name="width">The width: abbreviated, wide, or narrow.</param>
    /// <param name="standAlone"><see langword="true"/> for the form shown alone (<c>qqqq</c>), <see langword="false"/> for the form
    /// inside a date (<c>QQQQ</c>).</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quarter"/> is not from 1 to 4, or <paramref name="width"/> is
    /// <see cref="DateNameWidth.Short"/> or not a member of <see cref="DateNameWidth"/>.</exception>
    public string Quarter(int quarter, DateNameWidth width, bool standAlone)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, QuarterCount);
        return _quarters[(((Context(standAlone) * TextWidths) + TextWidth(width)) * QuarterCount) + quarter - 1];
    }

    /// <summary>
    /// Returns the name of an era of the Gregorian calendar.
    /// </summary>
    /// <param name="commonEra"><see langword="true"/> for the common era (<c>AD</c>), <see langword="false"/> for the era before it
    /// (<c>BC</c>).</param>
    /// <param name="width">The width: abbreviated, wide, or narrow.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is <see cref="DateNameWidth.Short"/> or not a member of
    /// <see cref="DateNameWidth"/>.</exception>
    public string Era(bool commonEra, DateNameWidth width) => _eras[(TextWidth(width) * 2) + (commonEra ? 1 : 0)];

    /// <summary>
    /// Returns the name of the morning or the afternoon (CLDR's <c>am</c> and <c>pm</c>).
    /// </summary>
    /// <param name="pm"><see langword="true"/> for the afternoon (<c>PM</c>), <see langword="false"/> for the morning
    /// (<c>AM</c>).</param>
    /// <param name="width">The width: abbreviated, wide, or narrow.</param>
    /// <param name="standAlone"><see langword="true"/> for the form shown alone, <see langword="false"/> for the form inside a
    /// time.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is <see cref="DateNameWidth.Short"/> or not a member of
    /// <see cref="DateNameWidth"/>.</exception>
    public string DayPeriod(bool pm, DateNameWidth width, bool standAlone) =>
        _dayPeriods[(((Context(standAlone) * TextWidths) + TextWidth(width)) * 2) + (pm ? 1 : 0)];

    /// <summary>
    /// Reads the names from a locale's name table, in the order tools/CldrTableGen writes them: for the format and then the
    /// stand-alone context, the months, the weekdays, the quarters, and the day periods, each in its widths; then the eras. A
    /// stand-alone name written empty is the format name in the same place.
    /// </summary>
    /// <param name="table">The table (<see cref="LocaleData.DateNameTable"/>).</param>
    /// <returns>The names.</returns>
    internal static DateNames Read(ReadOnlySpan<byte> table)
    {
        var reader = new CldrTableReader(table);
        string[] months = new string[2 * TextWidths * MonthCount];
        string[] weekdays = new string[2 * WeekdayWidths * WeekdayCount];
        string[] quarters = new string[2 * TextWidths * QuarterCount];
        string[] dayPeriods = new string[2 * TextWidths * 2];
        for (int context = 0; context < 2; context++)
        {
            ReadNames(ref reader, months, context);
            ReadNames(ref reader, weekdays, context);
            ReadNames(ref reader, quarters, context);
            ReadNames(ref reader, dayPeriods, context);
        }

        string[] eras = new string[TextWidths * 2];
        for (int i = 0; i < eras.Length; i++)
        {
            eras[i] = reader.ReadString();
        }

        return new DateNames(months, weekdays, quarters, dayPeriods, eras);
    }

    // The names of one kind in one context: the first half of the array is the format context, the second the stand-alone one.
    private static void ReadNames(ref CldrTableReader reader, string[] names, int context)
    {
        int half = names.Length / 2;
        for (int i = 0; i < half; i++)
        {
            string name = reader.ReadString();
            names[(context * half) + i] = context == 1 && name.Length == 0 ? names[i] : name;
        }
    }

    private static int Context(bool standAlone) => standAlone ? 1 : 0;

    // The index of a width that months, quarters, eras, and day periods have.
    private static int TextWidth(DateNameWidth width)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((byte)width, (byte)DateNameWidth.Narrow, nameof(width));
        return (int)width;
    }
}
