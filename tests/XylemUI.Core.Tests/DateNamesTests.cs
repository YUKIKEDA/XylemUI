namespace XylemUI.Tests;

// The names of the Gregorian calendar of LC2 (design §4.11, "Names and the week"): the months, the weekdays, the quarters, the eras,
// and the day periods, in each width and in the format and the stand-alone contexts, read once and kept. The expected values were
// read from the json of cldr-49.0.0-json-full.zip (ca-gregorian.json), not from the generated code.
public sealed class DateNamesTests
{
    /// <summary>
    /// Each name is CLDR's for its width and its context.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en and ja. en's narrow am and pm are a and p in the format context and AM and PM in the stand-alone one.
    ///
    /// Steps:
    /// - Read February, Tuesday, the first quarter, both eras, and pm in each width.
    ///
    /// Expected:
    /// - en: Feb, February, F; Tue, Tuesday, T, Tu; Q1, 1st quarter, 1; BC, AD, Before Christ, Anno Domini, B, A; PM, PM, p, and PM
    ///   alone. ja: 2月, 2月, 2; 火, 火曜日, 火, 火; Q1, 第1四半期, 1; 紀元前, 西暦, 紀元前, 西暦, BC, AD; 午後.
    /// </remarks>
    [Fact]
    public void Names_EnAndJa_AreTheJsons()
    {
        DateNames en = CldrLocales.En.Names;
        DateNames ja = CldrLocales.Ja.Names;

        Assert.Equal(["Feb", "February", "F"], Widths(width => en.Month(2, width, false)));
        Assert.Equal(["Tue", "Tuesday", "T", "Tu"], WeekdayWidths(width => en.Weekday(DayOfWeek.Tuesday, width, false)));
        Assert.Equal(["Q1", "1st quarter", "1"], Widths(width => en.Quarter(1, width, false)));
        Assert.Equal(["BC", "AD", "Before Christ", "Anno Domini", "B", "A"], Eras(en));
        Assert.Equal(["PM", "PM", "p"], Widths(width => en.DayPeriod(true, width, false)));
        Assert.Equal("PM", en.DayPeriod(true, DateNameWidth.Narrow, true));
        Assert.Equal("a", en.DayPeriod(false, DateNameWidth.Narrow, false));
        Assert.Equal(["2月", "2月", "2"], Widths(width => ja.Month(2, width, false)));
        Assert.Equal(["火", "火曜日", "火", "火"], WeekdayWidths(width => ja.Weekday(DayOfWeek.Tuesday, width, false)));
        Assert.Equal(["Q1", "第1四半期", "1"], Widths(width => ja.Quarter(1, width, false)));
        Assert.Equal(["紀元前", "西暦", "紀元前", "西暦", "BC", "AD"], Eras(ja));
        Assert.Equal("午後", ja.DayPeriod(true, DateNameWidth.Abbreviated, false));
    }

    /// <summary>
    /// The stand-alone names differ from the format ones where CLDR's do, and are the format ones elsewhere.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - ru, whose wide February is февраля in a date and февраль alone; de, whose abbreviated February is Feb. in a date and Feb
    ///   alone.
    ///
    /// Steps:
    /// - Read February in both contexts; read en's wide January in both.
    ///
    /// Expected:
    /// - The two forms of each; en's are both January, the same string.
    /// </remarks>
    [Fact]
    public void Names_StandAlone_AreTheJsonsOwn()
    {
        DateNames ru = CldrLocales.Ru.Names;
        DateNames de = CldrLocales.De.Names;
        DateNames en = CldrLocales.En.Names;

        Assert.Equal(("февраля", "февраль"), (ru.Month(2, DateNameWidth.Wide, false), ru.Month(2, DateNameWidth.Wide, true)));
        Assert.Equal(("Feb.", "Feb"), (de.Month(2, DateNameWidth.Abbreviated, false), de.Month(2, DateNameWidth.Abbreviated, true)));
        Assert.Equal("January", en.Month(1, DateNameWidth.Wide, true));
        Assert.Same(en.Month(1, DateNameWidth.Wide, false), en.Month(1, DateNameWidth.Wide, true));
    }

    /// <summary>
    /// A value out of range throws <see cref="ArgumentOutOfRangeException"/> that names it: a month of 0 or 13, a quarter of 0 or 5,
    /// the short width of anything but a weekday, a weekday or a width that is no member of its type (design §4.11, "Names and the
    /// week").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's names.
    ///
    /// Steps:
    /// - Read each name of the rows.
    ///
    /// Expected:
    /// - <see cref="ArgumentOutOfRangeException"/> with the parameter of the row.
    /// </remarks>
    [Fact]
    public void Names_OutOfRange_Throw()
    {
        DateNames en = CldrLocales.En.Names;
        (string Parameter, Func<string> Read)[] cases =
        [
            ("month", () => en.Month(0, DateNameWidth.Wide, false)),
            ("month", () => en.Month(13, DateNameWidth.Wide, false)),
            ("width", () => en.Month(1, DateNameWidth.Short, false)),
            ("width", () => en.Month(1, (DateNameWidth)9, false)),
            ("quarter", () => en.Quarter(0, DateNameWidth.Wide, false)),
            ("quarter", () => en.Quarter(5, DateNameWidth.Wide, false)),
            ("width", () => en.Quarter(1, DateNameWidth.Short, false)),
            ("width", () => en.Era(true, DateNameWidth.Short)),
            ("width", () => en.DayPeriod(true, DateNameWidth.Short, false)),
            ("day", () => en.Weekday((DayOfWeek)7, DateNameWidth.Wide, false)),
            ("width", () => en.Weekday(DayOfWeek.Sunday, (DateNameWidth)4, false)),
        ];

        foreach ((string parameter, Func<string> read) in cases)
        {
            Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(read).ParamName);
        }
    }

    /// <summary>
    /// The names are read once and kept: the tables give the same instance each time, and a name the same string, with no allocation
    /// (design §4.11, "Names and the week": "made a string when first read and kept by LocaleData").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables, their names read once before the measured runs.
    ///
    /// Steps:
    /// - Read the names twice; then, 1,000 times in the windows of <c>PerformanceAssert.DoesNotAllocate</c>, read a month, a weekday,
    ///   a quarter, an era, and a day period.
    ///
    /// Expected:
    /// - The same instance and the same strings; no byte is allocated.
    /// </remarks>
    [Fact]
    public void Names_ReadAgain_AreKeptAndAllocateNothing()
    {
        LocaleData en = CldrLocales.En;
        DateNames names = en.Names;
        int found = 0;

        Assert.Same(names, en.Names);
        Assert.Same(names.Month(5, DateNameWidth.Wide, false), names.Month(5, DateNameWidth.Wide, false));
        int runs = PerformanceAssert.DoesNotAllocate(
            1_000,
            index =>
            {
                // The warm-up runs count up from a negative index.
                int i = index & int.MaxValue;
                bool standAlone = i % 2 == 0;
                int length = names.Month((i % 12) + 1, DateNameWidth.Wide, standAlone).Length;
                length += names.Weekday((DayOfWeek)(i % 7), DateNameWidth.Short, standAlone).Length;
                length += names.Quarter((i % 4) + 1, DateNameWidth.Abbreviated, standAlone).Length;
                length += names.Era(true, DateNameWidth.Narrow).Length;
                length += names.DayPeriod(!standAlone, DateNameWidth.Wide, standAlone).Length;
                if (length > 0)
                {
                    found++;
                }
            }
        );

        Assert.Equal(runs, found);
    }

    private static string[] Widths(Func<DateNameWidth, string> read) =>
        [read(DateNameWidth.Abbreviated), read(DateNameWidth.Wide), read(DateNameWidth.Narrow)];

    private static string[] WeekdayWidths(Func<DateNameWidth, string> read) =>
        [read(DateNameWidth.Abbreviated), read(DateNameWidth.Wide), read(DateNameWidth.Narrow), read(DateNameWidth.Short)];

    private static string[] Eras(DateNames names) =>
        [
            names.Era(false, DateNameWidth.Abbreviated),
            names.Era(true, DateNameWidth.Abbreviated),
            names.Era(false, DateNameWidth.Wide),
            names.Era(true, DateNameWidth.Wide),
            names.Era(false, DateNameWidth.Narrow),
            names.Era(true, DateNameWidth.Narrow),
        ];
}
