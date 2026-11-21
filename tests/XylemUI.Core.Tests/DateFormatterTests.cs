namespace XylemUI.Tests;

// The date and time formats of LC2 (design §4.11, "Dates and times", "Added to the scope", and "Allocation and threads"): Flutter's 41
// skeletons in each locale, the 12-hour and the 24-hour forms of j, a date and a time joined by dateTimeFormats, the offset from GMT
// of the time zone skeletons, the value types a format takes, the digits of the locale, and no allocation. The expected values were
// read from the json of cldr-49.0.0-json-full.zip (ca-gregorian.json, timeZoneNames.json, numbers.json, timeData.json,
// dayPeriods.json), not from the generated code: the pattern of a skeleton that availableFormats has is its pattern there, and the
// others were worked out from the json with the rules of tools/CldrTableGen/SkeletonMatcher.cs (EEEE from E, LLL, LLLL, and MMMM
// from MMM, MMMMEEEEd from MMMMEd, QQQ, QQQQ, m, and s alone, jmz and jz from hmv and hv). In the expected text, _ is U+00A0, ^ is
// U+202F, | is U+200E, ! is U+200F, and @ is U+061C.
public sealed class DateFormatterTests
{
    // Tuesday the 3rd of February 2026, 13:05:09, nine hours ahead of GMT.
    private static readonly DateTimeOffset Moment = new(2026, 2, 3, 13, 5, 9, TimeSpan.FromHours(9));

    private static readonly Dictionary<string, LocaleData> ByTag = CldrLocales.All.ToDictionary(
        data => data.Locale.ToLanguageTag(),
        StringComparer.Ordinal
    );

    // Flutter 3.47.8's validDateFormats (packages/flutter_tools/lib/src/localizations/gen_l10n_types.dart, lines 31 to 73).
    private static readonly string[] Skeletons =
    [
        .. "d E EEEE LLL LLLL M Md MEd MMM MMMd MMMEd MMMM MMMMd MMMMEEEEd QQQ QQQQ y yM yMd yMEd yMMM yMMMd yMMMEd yMMMM yMMMMd".Split(' '),
        .. "yMMMMEEEEd yQQQ yQQQQ H Hm Hms j jm jms jmv jmz jv jz m ms s".Split(' '),
    ];

    /// <summary>
    /// Each of Flutter's 41 skeletons writes the locale's pattern, a skeleton with j in the hour of the locale's region or, asked for,
    /// in 24 hours (design §4.11, "Skeleton formats", "12 or 24 hours", and "Time zones").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en (the hour of the US is h), ja and de (H), ko (h), and ar (h, the Latin digits, and U+200F in its numeric dates).
    ///
    /// Steps:
    /// - Write Tuesday, 2026-02-03 13:05:09 at GMT+9 with each skeleton, and with each skeleton with j twice, without and with the
    ///   24-hour form.
    ///
    /// Expected:
    /// - The text of the row: en's jm is 1:05^PM and, in 24 hours, 13:05; ja's and de's are 13:05 both ways; the time zone skeletons
    ///   end with the offset, GMT+9 (ar: غرينتش+9).
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="skeleton">The skeleton.</param>
    /// <param name="use24HourFormat">Whether the 24-hour form is asked for.</param>
    /// <param name="expected">The text, with the marks of the class comment.</param>
    [Theory]
    [InlineData("en", "d", false, "3")]
    [InlineData("en", "E", false, "Tue")]
    [InlineData("en", "EEEE", false, "Tuesday")]
    [InlineData("en", "LLL", false, "Feb")]
    [InlineData("en", "LLLL", false, "February")]
    [InlineData("en", "M", false, "2")]
    [InlineData("en", "Md", false, "2/3")]
    [InlineData("en", "MEd", false, "Tue, 2/3")]
    [InlineData("en", "MMM", false, "Feb")]
    [InlineData("en", "MMMd", false, "Feb 3")]
    [InlineData("en", "MMMEd", false, "Tue, Feb 3")]
    [InlineData("en", "MMMM", false, "February")]
    [InlineData("en", "MMMMd", false, "February 3")]
    [InlineData("en", "MMMMEEEEd", false, "Tuesday, February 3")]
    [InlineData("en", "QQQ", false, "Q1")]
    [InlineData("en", "QQQQ", false, "1st quarter")]
    [InlineData("en", "y", false, "2026")]
    [InlineData("en", "yM", false, "2/2026")]
    [InlineData("en", "yMd", false, "2/3/2026")]
    [InlineData("en", "yMEd", false, "Tue, 2/3/2026")]
    [InlineData("en", "yMMM", false, "Feb 2026")]
    [InlineData("en", "yMMMd", false, "Feb 3, 2026")]
    [InlineData("en", "yMMMEd", false, "Tue, Feb 3, 2026")]
    [InlineData("en", "yMMMM", false, "February 2026")]
    [InlineData("en", "yMMMMd", false, "February 3, 2026")]
    [InlineData("en", "yMMMMEEEEd", false, "Tuesday, February 3, 2026")]
    [InlineData("en", "yQQQ", false, "Q1 2026")]
    [InlineData("en", "yQQQQ", false, "1st quarter 2026")]
    [InlineData("en", "H", false, "13")]
    [InlineData("en", "Hm", false, "13:05")]
    [InlineData("en", "Hms", false, "13:05:09")]
    [InlineData("en", "j", false, "1^PM")]
    [InlineData("en", "j", true, "13")]
    [InlineData("en", "jm", false, "1:05^PM")]
    [InlineData("en", "jm", true, "13:05")]
    [InlineData("en", "jms", false, "1:05:09^PM")]
    [InlineData("en", "jms", true, "13:05:09")]
    [InlineData("en", "jmv", false, "1:05^PM GMT+9")]
    [InlineData("en", "jmv", true, "13:05 GMT+9")]
    [InlineData("en", "jmz", false, "1:05^PM GMT+9")]
    [InlineData("en", "jmz", true, "13:05 GMT+9")]
    [InlineData("en", "jv", false, "1^PM GMT+9")]
    [InlineData("en", "jv", true, "13 GMT+9")]
    [InlineData("en", "jz", false, "1^PM GMT+9")]
    [InlineData("en", "jz", true, "13 GMT+9")]
    [InlineData("en", "m", false, "5")]
    [InlineData("en", "ms", false, "05:09")]
    [InlineData("en", "s", false, "9")]
    [InlineData("ja", "d", false, "3日")]
    [InlineData("ja", "E", false, "火")]
    [InlineData("ja", "EEEE", false, "火曜日")]
    [InlineData("ja", "LLL", false, "2月")]
    [InlineData("ja", "LLLL", false, "2月")]
    [InlineData("ja", "M", false, "2月")]
    [InlineData("ja", "Md", false, "2/3")]
    [InlineData("ja", "MEd", false, "2/3(火)")]
    [InlineData("ja", "MMM", false, "2月")]
    [InlineData("ja", "MMMd", false, "2月3日")]
    [InlineData("ja", "MMMEd", false, "2月3日(火)")]
    [InlineData("ja", "MMMM", false, "2月")]
    [InlineData("ja", "MMMMd", false, "2月3日")]
    [InlineData("ja", "MMMMEEEEd", false, "2月3日火曜日")]
    [InlineData("ja", "QQQ", false, "Q1")]
    [InlineData("ja", "QQQQ", false, "第1四半期")]
    [InlineData("ja", "y", false, "2026年")]
    [InlineData("ja", "yM", false, "2026/2")]
    [InlineData("ja", "yMd", false, "2026/2/3")]
    [InlineData("ja", "yMEd", false, "2026/2/3(火)")]
    [InlineData("ja", "yMMM", false, "2026年2月")]
    [InlineData("ja", "yMMMd", false, "2026年2月3日")]
    [InlineData("ja", "yMMMEd", false, "2026年2月3日(火)")]
    [InlineData("ja", "yMMMM", false, "2026年2月")]
    [InlineData("ja", "yMMMMd", false, "2026年2月3日")]
    [InlineData("ja", "yMMMMEEEEd", false, "2026年2月3日火曜日")]
    [InlineData("ja", "yQQQ", false, "2026/Q1")]
    [InlineData("ja", "yQQQQ", false, "2026年第1四半期")]
    [InlineData("ja", "H", false, "13時")]
    [InlineData("ja", "Hm", false, "13:05")]
    [InlineData("ja", "Hms", false, "13:05:09")]
    [InlineData("ja", "j", false, "13時")]
    [InlineData("ja", "j", true, "13時")]
    [InlineData("ja", "jm", false, "13:05")]
    [InlineData("ja", "jm", true, "13:05")]
    [InlineData("ja", "jms", false, "13:05:09")]
    [InlineData("ja", "jms", true, "13:05:09")]
    [InlineData("ja", "jmv", false, "13:05 GMT+9")]
    [InlineData("ja", "jmv", true, "13:05 GMT+9")]
    [InlineData("ja", "jmz", false, "13:05 GMT+9")]
    [InlineData("ja", "jmz", true, "13:05 GMT+9")]
    [InlineData("ja", "jv", false, "13時 GMT+9")]
    [InlineData("ja", "jv", true, "13時 GMT+9")]
    [InlineData("ja", "jz", false, "13時 GMT+9")]
    [InlineData("ja", "jz", true, "13時 GMT+9")]
    [InlineData("ja", "m", false, "5")]
    [InlineData("ja", "ms", false, "05:09")]
    [InlineData("ja", "s", false, "9")]
    [InlineData("ko", "d", false, "3일")]
    [InlineData("ko", "E", false, "화")]
    [InlineData("ko", "EEEE", false, "화요일")]
    [InlineData("ko", "LLL", false, "2월")]
    [InlineData("ko", "LLLL", false, "2월")]
    [InlineData("ko", "M", false, "2월")]
    [InlineData("ko", "Md", false, "2. 3.")]
    [InlineData("ko", "MEd", false, "2. 3. (화)")]
    [InlineData("ko", "MMM", false, "2월")]
    [InlineData("ko", "MMMd", false, "2월 3일")]
    [InlineData("ko", "MMMEd", false, "2월 3일 (화)")]
    [InlineData("ko", "MMMM", false, "2월")]
    [InlineData("ko", "MMMMd", false, "2월 3일")]
    [InlineData("ko", "MMMMEEEEd", false, "2월 3일 화요일")]
    [InlineData("ko", "QQQ", false, "1분기")]
    [InlineData("ko", "QQQQ", false, "제 1/4분기")]
    [InlineData("ko", "y", false, "2026년")]
    [InlineData("ko", "yM", false, "2026/2")]
    [InlineData("ko", "yMd", false, "2026/2/3")]
    [InlineData("ko", "yMEd", false, "2026/2/3 화")]
    [InlineData("ko", "yMMM", false, "2026년 2월")]
    [InlineData("ko", "yMMMd", false, "2026년 2월 3일")]
    [InlineData("ko", "yMMMEd", false, "2026년 2월 3일 (화)")]
    [InlineData("ko", "yMMMM", false, "2026년 2월")]
    [InlineData("ko", "yMMMMd", false, "2026년 2월 3일")]
    [InlineData("ko", "yMMMMEEEEd", false, "2026년 2월 3일 화요일")]
    [InlineData("ko", "yQQQ", false, "2026년 1분기")]
    [InlineData("ko", "yQQQQ", false, "2026년 제 1/4분기")]
    [InlineData("ko", "H", false, "13시")]
    [InlineData("ko", "Hm", false, "13:05")]
    [InlineData("ko", "Hms", false, "13:05:09")]
    [InlineData("ko", "j", false, "오후 1시")]
    [InlineData("ko", "j", true, "13시")]
    [InlineData("ko", "jm", false, "오후 1:05")]
    [InlineData("ko", "jm", true, "13:05")]
    [InlineData("ko", "jms", false, "오후 1:05:09")]
    [InlineData("ko", "jms", true, "13:05:09")]
    [InlineData("ko", "jmv", false, "오후 1:05 GMT+9")]
    [InlineData("ko", "jmv", true, "13:05 GMT+9")]
    [InlineData("ko", "jmz", false, "오후 1:05 GMT+9")]
    [InlineData("ko", "jmz", true, "13:05 GMT+9")]
    [InlineData("ko", "jv", false, "오후 1시 GMT+9")]
    [InlineData("ko", "jv", true, "13시 GMT+9")]
    [InlineData("ko", "jz", false, "오후 1시 GMT+9")]
    [InlineData("ko", "jz", true, "13시 GMT+9")]
    [InlineData("ko", "m", false, "5")]
    [InlineData("ko", "ms", false, "05:09")]
    [InlineData("ko", "s", false, "9")]
    [InlineData("de", "d", false, "3")]
    [InlineData("de", "E", false, "Di")]
    [InlineData("de", "EEEE", false, "Dienstag")]
    [InlineData("de", "LLL", false, "Feb")]
    [InlineData("de", "LLLL", false, "Februar")]
    [InlineData("de", "M", false, "2")]
    [InlineData("de", "Md", false, "3.2.")]
    [InlineData("de", "MEd", false, "Di., 3.2.")]
    [InlineData("de", "MMM", false, "Feb")]
    [InlineData("de", "MMMd", false, "3. Feb.")]
    [InlineData("de", "MMMEd", false, "Di., 3. Feb.")]
    [InlineData("de", "MMMM", false, "Februar")]
    [InlineData("de", "MMMMd", false, "3. Februar")]
    [InlineData("de", "MMMMEEEEd", false, "Dienstag, 3. Februar")]
    [InlineData("de", "QQQ", false, "Q1")]
    [InlineData("de", "QQQQ", false, "1. Quartal")]
    [InlineData("de", "y", false, "2026")]
    [InlineData("de", "yM", false, "2/2026")]
    [InlineData("de", "yMd", false, "3.2.2026")]
    [InlineData("de", "yMEd", false, "Di., 3.2.2026")]
    [InlineData("de", "yMMM", false, "Feb. 2026")]
    [InlineData("de", "yMMMd", false, "3. Feb. 2026")]
    [InlineData("de", "yMMMEd", false, "Di., 3. Feb. 2026")]
    [InlineData("de", "yMMMM", false, "Februar 2026")]
    [InlineData("de", "yMMMMd", false, "3. Februar 2026")]
    [InlineData("de", "yMMMMEEEEd", false, "Dienstag, 3. Februar 2026")]
    [InlineData("de", "yQQQ", false, "Q1 2026")]
    [InlineData("de", "yQQQQ", false, "1. Quartal 2026")]
    [InlineData("de", "H", false, "13 Uhr")]
    [InlineData("de", "Hm", false, "13:05")]
    [InlineData("de", "Hms", false, "13:05:09")]
    [InlineData("de", "j", false, "13 Uhr")]
    [InlineData("de", "j", true, "13 Uhr")]
    [InlineData("de", "jm", false, "13:05")]
    [InlineData("de", "jm", true, "13:05")]
    [InlineData("de", "jms", false, "13:05:09")]
    [InlineData("de", "jms", true, "13:05:09")]
    [InlineData("de", "jmv", false, "13:05 GMT+9")]
    [InlineData("de", "jmv", true, "13:05 GMT+9")]
    [InlineData("de", "jmz", false, "13:05 GMT+9")]
    [InlineData("de", "jmz", true, "13:05 GMT+9")]
    [InlineData("de", "jv", false, "13 Uhr GMT+9")]
    [InlineData("de", "jv", true, "13 Uhr GMT+9")]
    [InlineData("de", "jz", false, "13 Uhr GMT+9")]
    [InlineData("de", "jz", true, "13 Uhr GMT+9")]
    [InlineData("de", "m", false, "5")]
    [InlineData("de", "ms", false, "05:09")]
    [InlineData("de", "s", false, "9")]
    [InlineData("ar", "d", false, "3")]
    [InlineData("ar", "E", false, "الثلاثاء")]
    [InlineData("ar", "EEEE", false, "الثلاثاء")]
    [InlineData("ar", "LLL", false, "فبراير")]
    [InlineData("ar", "LLLL", false, "فبراير")]
    [InlineData("ar", "M", false, "2")]
    [InlineData("ar", "Md", false, "3!/2")]
    [InlineData("ar", "MEd", false, "الثلاثاء، 3!/2")]
    [InlineData("ar", "MMM", false, "فبراير")]
    [InlineData("ar", "MMMd", false, "3 فبراير")]
    [InlineData("ar", "MMMEd", false, "الثلاثاء، 3 فبراير")]
    [InlineData("ar", "MMMM", false, "فبراير")]
    [InlineData("ar", "MMMMd", false, "3 فبراير")]
    [InlineData("ar", "MMMMEEEEd", false, "الثلاثاء، 3 فبراير")]
    [InlineData("ar", "QQQ", false, "الربع الأول")]
    [InlineData("ar", "QQQQ", false, "الربع الأول")]
    [InlineData("ar", "y", false, "2026")]
    [InlineData("ar", "yM", false, "2!/2026")]
    [InlineData("ar", "yMd", false, "3!/2!/2026")]
    [InlineData("ar", "yMEd", false, "الثلاثاء، 3!/2!/2026")]
    [InlineData("ar", "yMMM", false, "فبراير 2026")]
    [InlineData("ar", "yMMMd", false, "3 فبراير 2026")]
    [InlineData("ar", "yMMMEd", false, "الثلاثاء، 3 فبراير 2026")]
    [InlineData("ar", "yMMMM", false, "فبراير 2026")]
    [InlineData("ar", "yMMMMd", false, "3 فبراير 2026")]
    [InlineData("ar", "yMMMMEEEEd", false, "الثلاثاء، 3 فبراير 2026")]
    [InlineData("ar", "yQQQ", false, "الربع الأول 2026")]
    [InlineData("ar", "yQQQQ", false, "الربع الأول 2026")]
    [InlineData("ar", "H", false, "13")]
    [InlineData("ar", "Hm", false, "13:05")]
    [InlineData("ar", "Hms", false, "13:05:09")]
    [InlineData("ar", "j", false, "1^م")]
    [InlineData("ar", "j", true, "13")]
    [InlineData("ar", "jm", false, "1:05 م")]
    [InlineData("ar", "jm", true, "13:05")]
    [InlineData("ar", "jms", false, "1:05:09 م")]
    [InlineData("ar", "jms", true, "13:05:09")]
    [InlineData("ar", "jmv", false, "1:05 م غرينتش+9")]
    [InlineData("ar", "jmv", true, "13:05 غرينتش+9")]
    [InlineData("ar", "jmz", false, "1:05 م غرينتش+9")]
    [InlineData("ar", "jmz", true, "13:05 غرينتش+9")]
    [InlineData("ar", "jv", false, "1^م غرينتش+9")]
    [InlineData("ar", "jv", true, "13 غرينتش+9")]
    [InlineData("ar", "jz", false, "1^م غرينتش+9")]
    [InlineData("ar", "jz", true, "13 غرينتش+9")]
    [InlineData("ar", "m", false, "5")]
    [InlineData("ar", "ms", false, "05:09")]
    [InlineData("ar", "s", false, "9")]
    public void FromSkeleton_FortyOneSkeletons_WriteTheLocalesPattern(string tag, string skeleton, bool use24HourFormat, string expected) =>
        Assert.Equal(Marked(expected), Write(DateFormatter.FromSkeleton(ByTag[tag], skeleton, use24HourFormat), Moment));

    /// <summary>
    /// Of two formats as near a skeleton, the one whose pattern writes the requested forms is taken: pt-PT's yMMMMEEEEd takes its
    /// yMMMMEd, with the wide month, and not its yMMMEEEEd, whose pattern has a numeric month (my reading of "Matching Skeletons", not
    /// the owner's decision; see tools/CldrTableGen/SkeletonMatcher.cs).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - pt-PT's yMMMEEEEd is EEEE, d/MM/y and its yMMMMEd E, d' de 'MMMM' de 'y; ja's are y年M月d日EEEE and y年M月d日(E), as
    ///   near each other, so the first in ordinal order, yMMMEEEEd, is taken.
    ///
    /// Steps:
    /// - Write the moment of the class with yMMMMEEEEd in pt-PT and in ja.
    ///
    /// Expected:
    /// - terça-feira, 3 de fevereiro de 2026; 2026年2月3日火曜日.
    /// </remarks>
    [Fact]
    public void FromSkeleton_FormatsAsNear_TakeThePatternOfTheRequestedForms()
    {
        Assert.Equal("terça-feira, 3 de fevereiro de 2026", Write(DateFormatter.FromSkeleton(ByTag["pt-PT"], "yMMMMEEEEd", false), Moment));
        Assert.Equal("2026年2月3日火曜日", Write(DateFormatter.FromSkeleton(CldrLocales.Ja, "yMMMMEEEEd", false), Moment));
    }

    /// <summary>
    /// The 24-hour form changes a skeleton with j only: a skeleton with H and a date skeleton write the same text without it and with
    /// it (design §4.11, "12 or 24 hours").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en, whose hour is h.
    ///
    /// Steps:
    /// - Write the moment of the class with every skeleton without and with the 24-hour form.
    ///
    /// Expected:
    /// - The two texts are the same for the 34 skeletons without j, and differ for the 7 with j.
    /// </remarks>
    [Fact]
    public void FromSkeleton_Use24HourFormat_ChangesTheSkeletonsWithJOnly()
    {
        LocaleData en = CldrLocales.En;
        string[] changed =
        [
            .. Skeletons.Where(skeleton =>
                Write(DateFormatter.FromSkeleton(en, skeleton, false), Moment) != Write(DateFormatter.FromSkeleton(en, skeleton, true), Moment)
            ),
        ];

        Assert.Equal(["j", "jm", "jms", "jmv", "jmz", "jv", "jz"], changed);
    }

    /// <summary>
    /// A skeleton that is not one of the 41 throws <see cref="ArgumentException"/>, and a <see langword="null"/> one or a
    /// <see langword="null"/> locale <see cref="ArgumentNullException"/> (design §4.11, "Skeletons").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables.
    ///
    /// Steps:
    /// - Make a format of each skeleton of the rows; of a <see langword="null"/> skeleton; of yMd with a <see langword="null"/>
    ///   locale.
    ///
    /// Expected:
    /// - <see cref="ArgumentException"/> whose parameter is the skeleton for each row; <see cref="ArgumentNullException"/> for the
    ///   other two.
    /// </remarks>
    /// <param name="skeleton">A skeleton that is not one of the 41: one of CLDR's but not Flutter's (hm, Ed), two together
    /// (yMdjm), empty, or one in another order (dM).</param>
    [Theory]
    [InlineData("hm")]
    [InlineData("Ed")]
    [InlineData("yMdjm")]
    [InlineData("")]
    [InlineData("dM")]
    public void FromSkeleton_NotOneOfTheFortyOne_Throws(string skeleton)
    {
        ArgumentException thrown = Assert.Throws<ArgumentException>(() => DateFormatter.FromSkeleton(CldrLocales.En, skeleton, false));

        Assert.Equal("skeleton", thrown.ParamName);
        Assert.Throws<ArgumentNullException>(() => DateFormatter.FromSkeleton(CldrLocales.En, null!, false));
        Assert.Throws<ArgumentNullException>(() => DateFormatter.FromSkeleton(null!, "yMd", false));
    }

    /// <summary>
    /// A date skeleton and a time skeleton are joined by the locale's dateTimeFormats: full for a wide month and a weekday, long for a
    /// wide month, medium for an abbreviated month, and short otherwise; a weekday alone by the Time-Day-Of-Week append item
    /// (design §4.11, "The pattern that joins a date and a time").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - CLDR 49 gives no locale four different joining patterns. nn's are {1} {0} (full), {1} 'kl'. {0} (long), and {1}, {0}
    ///   (medium and short); fr's are {1}, {0} but the short {1} {0}; ja's Time-Day-Of-Week is {0} ({1}), ko's ({1}) {0}, and en's
    ///   {1}, {0}. nn's and fr's hour is H.
    ///
    /// Steps:
    /// - Join each row's skeletons, without the 24-hour form, and write the moment of the class.
    ///
    /// Expected:
    /// - The text of the row.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="dateSkeleton">The date skeleton.</param>
    /// <param name="timeSkeleton">The time skeleton.</param>
    /// <param name="expected">The text, with the marks of the class comment.</param>
    [Theory]
    [InlineData("nn", "MMMMEEEEd", "jm", "tysdag 3 februar 13:05")]
    [InlineData("nn", "yMMMMd", "jm", "3. februar 2026 kl. 13:05")]
    [InlineData("nn", "yMMMd", "jm", "3. feb. 2026, 13:05")]
    [InlineData("fr", "yMMMd", "jm", "3 févr. 2026, 13:05")]
    [InlineData("fr", "yMd", "jm", "03/02/2026 13:05")]
    [InlineData("ja", "EEEE", "jm", "13:05 (火曜日)")]
    [InlineData("ko", "E", "jm", "(화) 오후 1:05")]
    [InlineData("en", "E", "jm", "Tue, 1:05^PM")]
    [InlineData("en", "yMd", "jmz", "2/3/2026, 1:05^PM GMT+9")]
    public void FromSkeletons_DateSkeletons_TakeTheirJoiningPattern(string tag, string dateSkeleton, string timeSkeleton, string expected) =>
        Assert.Equal(Marked(expected), Write(DateFormatter.FromSkeletons(ByTag[tag], dateSkeleton, timeSkeleton, false), Moment));

    /// <summary>
    /// The joined format takes the 24-hour form for its time skeleton with j.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables.
    ///
    /// Steps:
    /// - Join yMd and jm without and with the 24-hour form, and write the moment of the class.
    ///
    /// Expected:
    /// - 2/3/2026, 1:05^PM and 2/3/2026, 13:05.
    /// </remarks>
    [Fact]
    public void FromSkeletons_Use24HourFormat_ChangesTheTime()
    {
        Assert.Equal(Marked("2/3/2026, 1:05^PM"), Write(DateFormatter.FromSkeletons(CldrLocales.En, "yMd", "jm", false), Moment));
        Assert.Equal("2/3/2026, 13:05", Write(DateFormatter.FromSkeletons(CldrLocales.En, "yMd", "jm", true), Moment));
    }

    /// <summary>
    /// A date skeleton that is not one of the 28 with date fields only, or a time skeleton that is not one of the 13 with time fields
    /// only, throws <see cref="ArgumentException"/> that names it; a <see langword="null"/> skeleton throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables.
    ///
    /// Steps:
    /// - Join jm and jm, yMd and yMd, yMd and hm; join a <see langword="null"/> date skeleton and a <see langword="null"/> time
    ///   skeleton.
    ///
    /// Expected:
    /// - The parameters dateSkeleton, timeSkeleton, and timeSkeleton; then <see cref="ArgumentNullException"/> twice.
    /// </remarks>
    [Fact]
    public void FromSkeletons_NotADateOrATimeSkeleton_Throws()
    {
        LocaleData en = CldrLocales.En;

        Assert.Equal("dateSkeleton", Assert.Throws<ArgumentException>(() => DateFormatter.FromSkeletons(en, "jm", "jm", false)).ParamName);
        Assert.Equal("timeSkeleton", Assert.Throws<ArgumentException>(() => DateFormatter.FromSkeletons(en, "yMd", "yMd", false)).ParamName);
        Assert.Equal("timeSkeleton", Assert.Throws<ArgumentException>(() => DateFormatter.FromSkeletons(en, "yMd", "hm", false)).ParamName);
        Assert.Throws<ArgumentNullException>(() => DateFormatter.FromSkeletons(en, null!, "jm", false));
        Assert.Throws<ArgumentNullException>(() => DateFormatter.FromSkeletons(en, "yMd", null!, false));
    }

    /// <summary>
    /// A time zone skeleton writes the offset from GMT in the locale's gmtFormat and hourFormat: no offset for GMT itself, the hours
    /// without padding and the minutes only when they are not 0 for z and v, and both padded for zzzz and vvvv (design §4.11,
    /// "Time zone skeletons").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en (GMT{0}, +HH:mm;-HH:mm), sv (GMT{0}, a minus U+2212), fi (UTC{0}, +H.mm;-H.mm), ar (غرينتش{0}), and el, whose Hmv and Hv
    ///   are HH:mm (vvvv) and HH (vvvv).
    ///
    /// Steps:
    /// - Write 13:05 with the row's skeleton in 24 hours at the row's offset.
    ///
    /// Expected:
    /// - The text of the row. CLDR 49 has no gmtZeroFormat: GMT itself is the gmtFormat without {0} (GMT, UTC, غرينتش).
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="skeleton">The skeleton.</param>
    /// <param name="offsetMinutes">The offset from GMT, in minutes.</param>
    /// <param name="expected">The text.</param>
    [Theory]
    [InlineData("en", "jmz", 0, "13:05 GMT")]
    [InlineData("en", "jmz", 540, "13:05 GMT+9")]
    [InlineData("en", "jmz", -480, "13:05 GMT-8")]
    [InlineData("en", "jmz", 330, "13:05 GMT+5:30")]
    [InlineData("en", "jmv", -150, "13:05 GMT-2:30")]
    [InlineData("sv", "jmz", -480, "13:05 GMT−8")]
    [InlineData("sv", "jmv", 330, "13:05 GMT+5:30")]
    [InlineData("fi", "jmz", 0, "13.05 UTC")]
    [InlineData("fi", "jmz", 330, "13.05 UTC+5.30")]
    [InlineData("ar", "jmz", 0, "13:05 غرينتش")]
    [InlineData("ar", "jmz", -480, "13:05 غرينتش-8")]
    [InlineData("el", "jmz", 0, "13:05 (GMT)")]
    [InlineData("el", "jmz", 540, "13:05 (GMT+09:00)")]
    [InlineData("el", "jmv", -150, "13:05 (GMT-02:30)")]
    [InlineData("el", "jz", 330, "13 (GMT+05:30)")]
    public void TimeZone_Offsets_WriteTheLocalesGmtFormat(string tag, string skeleton, int offsetMinutes, string expected)
    {
        var value = new DateTimeOffset(2026, 2, 3, 13, 5, 9, TimeSpan.FromMinutes(offsetMinutes));

        Assert.Equal(expected, Write(DateFormatter.FromSkeleton(ByTag[tag], skeleton, true), value));
    }

    /// <summary>
    /// A time zone skeleton takes a <see cref="DateTimeOffset"/> only: a <see cref="DateTime"/>, a <see cref="DateOnly"/>, or a
    /// <see cref="TimeOnly"/> throws <see cref="ArgumentException"/>, from TryFormat, Of, and Format alike (design §4.11, "Time zone
    /// skeletons").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's jmz, jmv, jz, and jv formats, and yMd joined with jmz.
    ///
    /// Steps:
    /// - Write a DateTime, a DateOnly, and a TimeOnly with each, by TryFormat and by Of; Format a DateTime.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentException"/> whose parameter is value.
    /// </remarks>
    [Fact]
    public void TimeZone_ValueWithoutAnOffset_Throws()
    {
        char[] buffer = new char[64];
        string[] zones = ["jmz", "jmv", "jz", "jv"];
        DateFormatter[] formats =
        [
            .. zones.Select(skeleton => DateFormatter.FromSkeleton(CldrLocales.En, skeleton, false)),
            DateFormatter.FromSkeletons(CldrLocales.En, "yMd", "jmz", false),
        ];
        foreach (DateFormatter format in formats)
        {
            Action[] calls =
            [
                () => format.TryFormat(Moment.DateTime, buffer, out _),
                () => format.TryFormat(DateOnly.FromDateTime(Moment.DateTime), buffer, out _),
                () => format.TryFormat(TimeOnly.FromDateTime(Moment.DateTime), buffer, out _),
                () => format.Of(Moment.DateTime),
                () => format.Of(DateOnly.FromDateTime(Moment.DateTime)),
                () => format.Of(TimeOnly.FromDateTime(Moment.DateTime)),
                () => format.Format(Moment.DateTime),
            ];
            foreach (Action call in calls)
            {
                Assert.Equal("value", Assert.Throws<ArgumentException>(call).ParamName);
            }
        }
    }

    /// <summary>
    /// A <see cref="DateTime"/> is written with its fields as they are, whatever its kind, and a <see cref="DateTimeOffset"/> with its
    /// clock's fields, not converted to another zone (design §4.11, "Value types").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMd joined with Hms, and en's jmz in 24 hours.
    ///
    /// Steps:
    /// - Write 2026-02-03 13:05:09 as a DateTime of each kind, and as a DateTimeOffset at GMT+9 and at GMT-5.
    ///
    /// Expected:
    /// - 2/3/2026, 13:05:09 each time; jmz writes 13:05 GMT+9 and 13:05 GMT-5.
    /// </remarks>
    [Fact]
    public void ValueTypes_KindAndOffset_AreNotConverted()
    {
        DateFormatter format = DateFormatter.FromSkeletons(CldrLocales.En, "yMd", "Hms", false);
        DateFormatter zone = DateFormatter.FromSkeleton(CldrLocales.En, "jmz", true);
        var clock = new DateTime(2026, 2, 3, 13, 5, 9);

        foreach (DateTimeKind kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Utc, DateTimeKind.Local })
        {
            Assert.Equal("2/3/2026, 13:05:09", format.Format(DateTime.SpecifyKind(clock, kind)));
        }

        Assert.Equal("2/3/2026, 13:05:09", Write(format, new DateTimeOffset(clock, TimeSpan.FromHours(9))));
        Assert.Equal("2/3/2026, 13:05:09", Write(format, new DateTimeOffset(clock, TimeSpan.FromHours(-5))));
        Assert.Equal("13:05 GMT+9", Write(zone, new DateTimeOffset(clock, TimeSpan.FromHours(9))));
        Assert.Equal("13:05 GMT-5", Write(zone, new DateTimeOffset(clock, TimeSpan.FromHours(-5))));
    }

    /// <summary>
    /// A <see cref="DateOnly"/> is written by a format of date fields and a <see cref="TimeOnly"/> by a format of time fields; a
    /// date for a format with a time field, and a time for a format with a date field, throw <see cref="ArgumentException"/> (design
    /// §4.11, "Value types").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMd, jm, and yMd joined with jm.
    ///
    /// Steps:
    /// - Write the date and the time of the moment of the class with each format, by TryFormat and by Of.
    ///
    /// Expected:
    /// - yMMMd writes Feb 3, 2026 for the date and throws for the time; jm writes 1:05^PM for the time and throws for the date; the
    ///   joined format throws for both.
    /// </remarks>
    [Fact]
    public void ValueTypes_DateAndTime_TakeTheFormatsOfTheirFields()
    {
        var date = DateOnly.FromDateTime(Moment.DateTime);
        var time = TimeOnly.FromDateTime(Moment.DateTime);
        char[] buffer = new char[64];
        DateFormatter dateFormat = DateFormatter.FromSkeleton(CldrLocales.En, "yMMMd", false);
        DateFormatter timeFormat = DateFormatter.FromSkeleton(CldrLocales.En, "jm", false);
        DateFormatter joined = DateFormatter.FromSkeletons(CldrLocales.En, "yMd", "jm", false);

        Assert.True(dateFormat.TryFormat(date, buffer, out int written));
        Assert.Equal("Feb 3, 2026", new string(buffer, 0, written));
        Assert.Equal("Feb 3, 2026", dateFormat.Of(date).ToString());
        Assert.True(timeFormat.TryFormat(time, buffer, out written));
        Assert.Equal(Marked("1:05^PM"), new string(buffer, 0, written));
        Assert.Equal(Marked("1:05^PM"), timeFormat.Of(time).ToString());
        Assert.Throws<ArgumentException>(() => dateFormat.TryFormat(time, buffer, out _));
        Assert.Throws<ArgumentException>(() => dateFormat.Of(time));
        Assert.Throws<ArgumentException>(() => timeFormat.TryFormat(date, buffer, out _));
        Assert.Throws<ArgumentException>(() => timeFormat.Of(date));
        Assert.Throws<ArgumentException>(() => joined.TryFormat(date, buffer, out _));
        Assert.Throws<ArgumentException>(() => joined.TryFormat(time, buffer, out _));
    }

    /// <summary>
    /// A pattern with a flexible day period (zh-Hant's Bh:mm) writes the period of CLDR's rules for zh at the time: the period that
    /// starts last at or before it (design §4.11, "Names and the week"; my reading, which leaves out the period at midnight).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - zh-Hant, whose hour is h (TW) and whose hm is Bh:mm; dayPeriods.json's zh: night1 from 00:00, morning1 from 05:00, morning2
    ///   from 08:00, afternoon1 from 12:00, afternoon2 from 13:00, evening1 from 19:00, and midnight at 00:00.
    ///
    /// Steps:
    /// - Write the time of each row with jm, without and with the 24-hour form.
    ///
    /// Expected:
    /// - The row's text; in 24 hours, the hours and the minutes alone (HH:mm).
    /// </remarks>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="expected">The text.</param>
    /// <param name="expected24">The text in 24 hours.</param>
    [Theory]
    [InlineData(0, 30, "凌晨12:30", "00:30")]
    [InlineData(5, 0, "清晨5:00", "05:00")]
    [InlineData(8, 0, "上午8:00", "08:00")]
    [InlineData(12, 0, "中午12:00", "12:00")]
    [InlineData(13, 5, "下午1:05", "13:05")]
    [InlineData(19, 0, "晚上7:00", "19:00")]
    [InlineData(23, 59, "晚上11:59", "23:59")]
    public void FlexibleDayPeriods_ZhHant_FollowTheRulesOfZh(int hour, int minute, string expected, string expected24)
    {
        var time = new TimeOnly(hour, minute);

        Assert.Equal(expected, DateFormatter.FromSkeleton(CldrLocales.ZhHant, "jm", false).Of(time).ToString());
        Assert.Equal(expected24, DateFormatter.FromSkeleton(CldrLocales.ZhHant, "jm", true).Of(time).ToString());
    }

    /// <summary>
    /// A date is written in the digits of the locale's default numbering system (design §4.11, "Default digits").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - ar-EG, whose default numbering system is arab (U+0660 to U+0669) and whose yMd is d!/M!/y.
    ///
    /// Steps:
    /// - Write the moment of the class with yMd.
    ///
    /// Expected:
    /// - ٣!/٢!/٢٠٢٦.
    /// </remarks>
    [Fact]
    public void Digits_ArEG_AreArabicIndic() =>
        Assert.Equal(Marked("٣!/٢!/٢٠٢٦"), Write(DateFormatter.FromSkeleton(ByTag["ar-EG"], "yMd", false), Moment));

    /// <summary>
    /// The fields that the 41 skeletons of a few locales write: the year of the week (gd's yMMM is LLL Y), the era (th's yQQQQ is
    /// QQQQ G y), and root's patterns.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - gd, whose region GB starts its weeks on Monday with 4 days in the first, so that Friday, 2027-01-01, is in the last week of
    ///   2026; th; root, whose hour is the world's H.
    ///
    /// Steps:
    /// - Write 2027-01-01 and 2027-01-04 with gd's yMMM; the moment of the class with th's yQQQQ and with root's yMd, jm,
    ///   yMMMMEEEEd, and EEEE.
    ///
    /// Expected:
    /// - Faoi 2026 and Faoi 2027; ไตรมาส 1 ค.ศ. 2026; 2026-02-03, 13:05, 2026 M02 3, Tue, and Tue.
    /// </remarks>
    [Fact]
    public void Fields_WeekYearEraAndRoot_AreWritten()
    {
        DateFormatter gd = DateFormatter.FromSkeleton(ByTag["gd"], "yMMM", false);
        DateFormatter root = DateFormatter.FromSkeleton(LocaleData.Root, "yMd", false);

        Assert.Equal("Faoi 2026", gd.Of(new DateOnly(2027, 1, 1)).ToString());
        Assert.Equal("Faoi 2027", gd.Of(new DateOnly(2027, 1, 4)).ToString());
        Assert.Equal("ไตรมาส 1 ค.ศ. 2026", Write(DateFormatter.FromSkeleton(ByTag["th"], "yQQQQ", false), Moment));
        Assert.Equal("2026-02-03", Write(root, Moment));
        Assert.Equal("13:05", Write(DateFormatter.FromSkeleton(LocaleData.Root, "jm", false), Moment));
        Assert.Equal("2026 M02 3, Tue", Write(DateFormatter.FromSkeleton(LocaleData.Root, "yMMMMEEEEd", false), Moment));
        Assert.Equal("Tue", Write(DateFormatter.FromSkeleton(LocaleData.Root, "EEEE", false), Moment));
    }

    /// <summary>
    /// The tables under another tag write the source tables' patterns, and tl writes fil's (design §4.11, "Locales that CLDR does
    /// not have" and "The set of locales").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables under the tag en-RU; tl and fil.
    ///
    /// Steps:
    /// - Write the moment of the class with yMMMd and jm.
    ///
    /// Expected:
    /// - en-RU writes en's Feb 3, 2026 and 1:05^PM; tl writes what fil writes.
    /// </remarks>
    [Fact]
    public void WithLocaleAndTl_WriteTheSourceTables()
    {
        LocaleData russia = CldrLocales.En.WithLocale(Locale.Parse("en-RU"));

        Assert.Equal("Feb 3, 2026", Write(DateFormatter.FromSkeleton(russia, "yMMMd", false), Moment));
        Assert.Equal(Marked("1:05^PM"), Write(DateFormatter.FromSkeleton(russia, "jm", false), Moment));
        foreach (string skeleton in new[] { "yMMMd", "jm" })
        {
            Assert.Equal(
                Write(DateFormatter.FromSkeleton(CldrLocales.Fil, skeleton, false), Moment),
                Write(DateFormatter.FromSkeleton(CldrLocales.Tl, skeleton, false), Moment)
            );
        }
    }

    /// <summary>
    /// Every entry and root writes each of the 41 skeletons, in both forms of the hour, and a skeleton with j in 24 hours writes the
    /// hour 13 in the locale's digits.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The 422 entries of CldrLocales and root.
    ///
    /// Steps:
    /// - Write the moment of the class with each skeleton, without and with the 24-hour form.
    ///
    /// Expected:
    /// - No call throws and every text has a character; jm in 24 hours has the digits one and three of the locale.
    /// </remarks>
    [Fact]
    public void EveryLocale_FortyOneSkeletons_Write()
    {
        foreach (LocaleData data in CldrLocales.All.Append(LocaleData.Root))
        {
            foreach (string skeleton in Skeletons)
            {
                Assert.NotEmpty(Write(DateFormatter.FromSkeleton(data, skeleton, false), Moment));
                Assert.NotEmpty(Write(DateFormatter.FromSkeleton(data, skeleton, true), Moment));
            }

            string thirteen = new(new[] { (char)(data.Numbers.Zero + 1), (char)(data.Numbers.Zero + 3) });
            Assert.Contains(thirteen, Write(DateFormatter.FromSkeleton(data, "jm", true), Moment), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A destination too short for the text writes nothing and returns <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMMEEEEd (Tuesday, February 3, 2026: 25 characters).
    ///
    /// Steps:
    /// - Write the moment of the class into 24 and 25 characters.
    ///
    /// Expected:
    /// - false and 0, then true and 25.
    /// </remarks>
    [Fact]
    public void TryFormat_ShortDestination_IsFalse()
    {
        DateFormatter format = DateFormatter.FromSkeleton(CldrLocales.En, "yMMMMEEEEd", false);

        Assert.False(format.TryFormat(Moment, new char[24], out int none));
        Assert.Equal(0, none);
        Assert.True(format.TryFormat(Moment, new char[25], out int all));
        Assert.Equal(25, all);
    }

    /// <summary>
    /// A formatted date in an interpolation of a NodeTextWriter writes the format's text; the default value writes nothing (design
    /// §4.11, "Allocation and threads").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMd, jm, and jmz formats; a writer over a buffer.
    ///
    /// Steps:
    /// - Write "{date}, {time}{default} at {zone}" with Of: the date of the moment of the class as a DateOnly, its clock as a
    ///   DateTime, and the moment as a DateTimeOffset; read the writer's text.
    ///
    /// Expected:
    /// - "Feb 3, 2026, 1:05^PM at 1:05^PM GMT+9".
    /// </remarks>
    [Fact]
    public void Of_InAnInterpolation_WritesTheFormattedDate()
    {
        DateFormatter date = DateFormatter.FromSkeleton(CldrLocales.En, "yMMMd", false);
        DateFormatter time = DateFormatter.FromSkeleton(CldrLocales.En, "jm", false);
        DateFormatter zone = DateFormatter.FromSkeleton(CldrLocales.En, "jmz", false);
        var writer = new NodeTextWriter(new char[64], new TextRangeWriter());

        writer.Write($"{date.Of(DateOnly.FromDateTime(Moment.DateTime))}, {time.Of(Moment.DateTime)}{default(FormattedDate)} at {zone.Of(Moment)}");

        Assert.Equal(Marked("Feb 3, 2026, 1:05^PM at 1:05^PM GMT+9"), new string(writer.Buffer, 0, writer.Length));
    }

    /// <summary>
    /// Writing a date allocates nothing, and neither does writing Of(value) into a NodeTextWriter (design §4.11, "Allocation and
    /// threads"; §11.3's Zero-Alloc check).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMMEEEEd, jmz, and yMd joined with jm; zh-Hant's jm (a flexible day period); ar-EG's yMd (Arabic-Indic digits); a
    ///   destination buffer and a writer over a buffer, all made before the measured runs.
    ///
    /// Steps:
    /// - 1,000 times, in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: write the moment of the class plus the index in
    ///   minutes as a DateTimeOffset with each format that takes one, as a DateTime, a DateOnly, and a TimeOnly with the ones that
    ///   take them, and write three Of values into a new writer over the same buffer.
    ///
    /// Expected:
    /// - No byte is allocated, and every run wrote.
    /// </remarks>
    [Fact]
    public void Formats_AllocateNothing()
    {
        DateFormatter full = DateFormatter.FromSkeleton(CldrLocales.En, "yMMMMEEEEd", false);
        DateFormatter zone = DateFormatter.FromSkeleton(CldrLocales.En, "jmz", false);
        DateFormatter joined = DateFormatter.FromSkeletons(CldrLocales.En, "yMd", "jm", false);
        DateFormatter periods = DateFormatter.FromSkeleton(CldrLocales.ZhHant, "jm", false);
        DateFormatter digits = DateFormatter.FromSkeleton(ByTag["ar-EG"], "yMd", false);
        char[] destination = new char[64];
        char[] text = new char[64];
        var ranges = new TextRangeWriter();
        int done = 0;

        int runs = PerformanceAssert.DoesNotAllocate(
            1_000,
            i =>
            {
                DateTimeOffset value = Moment.AddMinutes(i);
                bool all = full.TryFormat(value, destination, out _);
                all &= full.TryFormat(DateOnly.FromDateTime(value.DateTime), destination, out _);
                all &= zone.TryFormat(value, destination, out _);
                all &= joined.TryFormat(value.DateTime, destination, out _);
                all &= periods.TryFormat(TimeOnly.FromDateTime(value.DateTime), destination, out _);
                all &= digits.TryFormat(value, destination, out _);
                var writer = new NodeTextWriter(text, ranges);
                writer.Write($"{full.Of(value)} {zone.Of(value)} {periods.Of(TimeOnly.FromDateTime(value.DateTime))}");
                if (all && writer.Length > 0)
                {
                    done++;
                }
            }
        );

        Assert.Equal(runs, done);
    }

    /// <summary>
    /// The tool's counts of distinct date tables, and that a date's digits are the number table's (design §4.11, "Tests").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The generated tables.
    ///
    /// Steps:
    /// - Read the counts the tool wrote; write the day of the moment of the class with d for every entry and root.
    ///
    /// Expected:
    /// - 149 name tables and 166 pattern tables (counted from the json: locales whose bytes are the same share one); each day 3 is the
    ///   digit three of the locale's number table.
    /// </remarks>
    [Fact]
    public void CldrData_DateTables_HoldTheJsonsValues()
    {
        Assert.Equal(149, CldrData.DateNameTableCount);
        Assert.Equal(166, CldrData.DatePatternTableCount);
        foreach (LocaleData data in CldrLocales.All.Append(LocaleData.Root))
        {
            string day = Write(DateFormatter.FromSkeleton(data, "d", false), Moment);
            Assert.Contains((char)(data.Numbers.Zero + 3), day);
        }
    }

    private static string Write(DateFormatter format, DateTimeOffset value)
    {
        char[] buffer = new char[128];
        Assert.True(format.TryFormat(value, buffer, out int written));
        return new string(buffer, 0, written);
    }

    // The text with the marks of the class comment replaced by the characters they stand for.
    private static string Marked(string text) =>
        text.Replace('_', (char)0x00A0).Replace('^', (char)0x202F).Replace('|', (char)0x200E).Replace('!', (char)0x200F).Replace('@', (char)0x061C);
}
