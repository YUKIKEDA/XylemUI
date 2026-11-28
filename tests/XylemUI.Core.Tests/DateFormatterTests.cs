using System.Globalization;
using System.Text;

namespace XylemUI.Tests;

// The date and time formats of LC2 (design §4.11, "Dates and times", "Added to the scope", and "Allocation and threads"): Flutter's 41
// skeletons in each locale, the 12-hour and the 24-hour forms of j, a date and a time joined by the patterns that join them
// (dateTimeFormats-atTime, or dateTimeFormats for a locale without it), the offset from GMT
// of the time zone skeletons, the value types a format takes, the digits of the locale, and no allocation. The expected values were
// read from the json of cldr-49.0.0-json-full.zip (ca-gregorian.json, timeZoneNames.json, numbers.json, timeData.json), not from
// the generated code: the pattern of a skeleton that availableFormats has is its pattern there, and the others were worked out from
// the json with the rules of tools/CldrTableGen/SkeletonMatcher.cs (EEEE from E, LLL, LLLL, and MMMM from MMM, MMMMEEEEd from
// MMMMEd, yMMMMEEEEd from the full date pattern when no available format has its base, QQQ, QQQQ, m, and s alone, jmz and jz from
// hmv and hv). The ranges' expected values were worked out from the json's intervalFormats with the rules of
// tools/CldrTableGen/IntervalMatcher.cs, a port of ICU's DateIntervalFormat. In the expected text, _ is U+00A0, ^ is U+202F, | is
// U+200E, ! is U+200F, @ is U+061C, and ~ is U+2009.
public sealed class DateFormatterTests
{
    // The places in a date pattern table (PatternAt): jmz among the 41 patterns, and hourFormat after gmtFormat at the end.
    private const int JmzIndex = 35;
    private const int HourFormatIndex = 54;

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
    /// A locale's standard date patterns (dateFormats) are formats of the skeletons of their fields when no available format has the
    /// same base skeleton, as ICU's addICUPatterns adds them (design §4.11, "Nearness and lengths"; Issue #906): the full date
    /// pattern is yMMMMEEEEd where availableFormats has none, and the long one yMMMMd.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - pt-PT and ja, whose availableFormats have no yMMMMEEEEd and whose full date patterns are EEEE, d 'de' MMMM 'de' y and
    ///   y年M月d日EEEE; ha, ky, and bs, whose availableFormats have no yMMMMEEEEd; gsw, whose availableFormats have no yMMMMd.
    ///
    /// Steps:
    /// - Write the moment of the class with yMMMMEEEEd in pt-PT and in ja; read the yMMMMEEEEd patterns of ha, ky, and bs and the
    ///   yMMMMd pattern of gsw from the tables.
    ///
    /// Expected:
    /// - terça-feira, 3 de fevereiro de 2026; 2026年2月3日火曜日; EEEE d MMMM, y; y-'ж'., d-MMMM, EEEE; EEEE, d. MMMM y.;
    ///   d. MMMM y. These are ICU's patterns for the skeletons.
    /// </remarks>
    [Fact]
    public void FromSkeleton_NoFormatOfTheSkeleton_TakesTheStandardPattern()
    {
        Assert.Equal("terça-feira, 3 de fevereiro de 2026", Write(DateFormatter.FromSkeleton(ByTag["pt-PT"], "yMMMMEEEEd", false), Moment));
        Assert.Equal("2026年2月3日火曜日", Write(DateFormatter.FromSkeleton(CldrLocales.Ja, "yMMMMEEEEd", false), Moment));
        Assert.Equal("EEEE d MMMM, y", PatternAt(ByTag["ha"], 25));
        Assert.Equal("y-'ж'., d-MMMM, EEEE", PatternAt(ByTag["ky"], 25));
        Assert.Equal("EEEE, d. MMMM y.", PatternAt(ByTag["bs"], 25));
        Assert.Equal("d. MMMM y", PatternAt(ByTag["gsw"], 24));
    }

    /// <summary>
    /// A requested E of one letter counts as EEE when the pattern's field takes the requested length, as ICU's adjustFieldTypes does
    /// ("1-3 for E are equivalent to 3"; design §4.11, "Nearness and lengths"; Issue #906): a locale whose E is the short name
    /// cccccc writes the abbreviated one for E, and a pattern of E writes EEE, which is the same text.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - CLDR 49's availableFormats of nb, nn, and no have E as cccccc (the short name, sø.); en's has E as ccc and MEd as E, M/d.
    ///
    /// Steps:
    /// - Read the E patterns of nb, nn, and no and the MEd pattern of en from the tables; write the moment of the class with nb's E.
    ///
    /// Expected:
    /// - ccc for the three, EEE, M/d for en's MEd; tir. (the abbreviated name, as ICU writes it), not ti.
    /// </remarks>
    [Fact]
    public void FromSkeleton_WeekdayOfOneLetter_TakesTheAbbreviatedLength()
    {
        Assert.Equal("ccc", PatternAt(ByTag["nb"], 1));
        Assert.Equal("ccc", PatternAt(ByTag["nn"], 1));
        Assert.Equal("ccc", PatternAt(ByTag["no"], 1));
        Assert.Equal("EEE, M/d", PatternAt(CldrLocales.En, 7));
        Assert.Equal("tir.", Write(DateFormatter.FromSkeleton(ByTag["nb"], "E", false), Moment));
    }

    /// <summary>
    /// The hour that j stands for is the one timeData gives the locale's language and region, then its region, then the world
    /// (design §4.11, "The region of a locale's values"; ICU's getAllowedHourFormats).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - timeData's en-001 is h, while 001 is H; fr-CA is H, while CA is h. en-001's hm is h:mm^a and fr-CA's Hm is HH 'h' mm.
    ///
    /// Steps:
    /// - Read the jm patterns of en-001 and fr-CA from the tables, and write the moment of the class with fr-CA's jm.
    ///
    /// Expected:
    /// - h:mm^a and HH 'h' mm; 13 h 05.
    /// </remarks>
    [Fact]
    public void FromSkeleton_HourOfTheLanguageAndRegion_IsTakenFirst()
    {
        Assert.Equal(Marked("h:mm^a"), PatternAt(ByTag["en-001"], 32));
        Assert.Equal("HH 'h' mm", PatternAt(ByTag["fr-CA"], 32));
        Assert.Equal("13 h 05", Write(DateFormatter.FromSkeleton(ByTag["fr-CA"], "jm", false), Moment));
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
    /// A date skeleton and a time skeleton are joined by the locale's patterns that join a date and a time, those of
    /// dateTimeFormats-atTime when the locale has them, as ICU's DateTimePatternGenerator reads them: full for a wide month and a
    /// weekday, long for a wide month, medium for an abbreviated month, and short otherwise; a weekday alone by the Time-Day-Of-Week
    /// append item (design §4.11, "The pattern that joins a date and a time"; PR #966's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - CLDR 49 gives no locale four different joining patterns. nn's are {1} {0} (full), {1} 'kl'. {0} (long), and {1}, {0}
    ///   (medium and short); fr's are {1}, {0} but the short {1} {0}; en's atTime ones are {1} 'at' {0} (full and long) and {1}, {0}
    ///   (medium and short); pt's short atTime one is {1}, {0}, where its dateTimeFormats has {1} {0}. ja's Time-Day-Of-Week is
    ///   {0} ({1}), ko's ({1}) {0}, en's {1}, {0}, and sl's {1} ob {0}, whose letters the tool quotes. nn's, fr's, pt's, and sl's
    ///   hour is H.
    /// - The date skeletons: a wide month with a weekday (MMMMEEEEd), a wide month (yMMMMd, LLLL), an abbreviated month (yMMMd,
    ///   LLL), no month name (yMd, QQQ), and a weekday alone (E, EEEE).
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
    [InlineData("nn", "MMMMEEEEd", "jm", "tysdag 3 februar kl. 13:05")]
    [InlineData("nn", "yMMMMd", "jm", "3. februar 2026 kl. 13:05")]
    [InlineData("nn", "yMMMd", "jm", "3. feb. 2026, 13:05")]
    [InlineData("nn", "LLLL", "jm", "februar kl. 13:05")]
    [InlineData("fr", "yMMMd", "jm", "3 févr. 2026, 13:05")]
    [InlineData("fr", "yMd", "jm", "03/02/2026 13:05")]
    [InlineData("fr", "LLL", "jm", "févr., 13:05")]
    [InlineData("fr", "QQQ", "jm", "T1 13:05")]
    [InlineData("sl", "EEEE", "jm", "torek ob 13:05")]
    [InlineData("ja", "EEEE", "jm", "13:05 (火曜日)")]
    [InlineData("ko", "E", "jm", "(화) 오후 1:05")]
    [InlineData("en", "E", "jm", "Tue, 1:05^PM")]
    [InlineData("en", "yMd", "jmz", "2/3/2026, 1:05^PM GMT+9")]
    [InlineData("en", "MMMMEEEEd", "jm", "Tuesday, February 3 at 1:05^PM")]
    [InlineData("en", "yMMMMd", "jm", "February 3, 2026 at 1:05^PM")]
    [InlineData("en", "yMMMd", "jm", "Feb 3, 2026, 1:05^PM")]
    [InlineData("pt", "yMd", "jm", "03/02/2026, 13:05")]
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
    /// In every locale, the text of each joining pattern is written as text: the joined format writes the date's text and the time's
    /// text in the places of {1} and {0}, and the pattern's other characters as they are, without their quotes (UTS #35 part 4, the
    /// joining patterns are simple formatters). sl's Time-Day-Of-Week, {1} ob {0}, made ob two fields before the tool quoted it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The 422 entries of CldrLocales and root; a date skeleton for each joining pattern: MMMMEEEEd (full), yMMMMd (long), yMMMd
    ///   (medium), yMd (short), and EEEE (Time-Day-Of-Week).
    ///
    /// Steps:
    /// - Write the moment of the class with jm, with each date skeleton, and with each date skeleton joined with jm.
    ///
    /// Expected:
    /// - Each joined text is the table's joining pattern with {1} as the date's text, {0} as the time's, a quote dropped, and two
    ///   quotes as one.
    /// </remarks>
    [Fact]
    public void EveryLocale_JoiningPatterns_WriteTheirTextAsText()
    {
        string[] dates = ["MMMMEEEEd", "yMMMMd", "yMMMd", "yMd", "EEEE"];
        foreach (LocaleData data in CldrLocales.All.Append(LocaleData.Root))
        {
            string time = Write(DateFormatter.FromSkeleton(data, "jm", false), Moment);
            for (int i = 0; i < dates.Length; i++)
            {
                string date = Write(DateFormatter.FromSkeleton(data, dates[i], false), Moment);
                string expected = Joined(PatternAt(data, 48 + i), date, time);

                Assert.Equal(expected, Write(DateFormatter.FromSkeletons(data, dates[i], "jm", false), Moment));
            }
        }
    }

    /// <summary>
    /// A pattern in the tables with a field the formats do not write fails the invariant when a format is made of it: the tool stops
    /// on such a pattern, so the tables never hold one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release ends the process). Tables that are en's but for the pattern of yMd, d o b, whose o and b are fields
    ///   the formats do not write.
    ///
    /// Steps:
    /// - Make a format of yMd, and one of jm, and write the moment of the class with jm.
    ///
    /// Expected:
    /// - <see cref="InvariantViolationException"/> for yMd; jm writes 1:05^PM.
    /// </remarks>
    [Fact]
    public void FromSkeleton_PatternWithAFieldTheFormatsDoNotWrite_FailsTheInvariant()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
        var data = new PatternLocaleData(WithPattern(CldrLocales.En.DatePatternTable, 18, "d o b"));

        Assert.Throws<InvariantViolationException>(() => DateFormatter.FromSkeleton(data, "yMd", false));
        Assert.Equal(Marked("1:05^PM"), Write(DateFormatter.FromSkeleton(data, "jm", false), Moment));
    }

    /// <summary>
    /// A date pattern's text in quotes is literal and two quotes are one quote character, inside quotes and outside them (UTS #35
    /// part 4, "Date Format Patterns").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The pattern h''mm 'o''clock' a.
    ///
    /// Steps:
    /// - Compile it.
    ///
    /// Expected:
    /// - The field h, the text ', the field mm, the text  o'clock  (with its spaces), and the field a.
    /// </remarks>
    [Fact]
    public void DatePattern_Quotes_AreLiteralAndTwoAreOne()
    {
        DatePatternItem[] expected =
        [
            new('h', 1, string.Empty),
            new('\0', 0, "'"),
            new('m', 2, string.Empty),
            new('\0', 0, " o'clock "),
            new('a', 1, string.Empty),
        ];

        Assert.Equal(expected, DatePattern.Compile("h''mm 'o''clock' a"));
    }

    /// <summary>
    /// A time zone skeleton writes the offset from GMT in the locale's gmtFormat and hourFormat, 0 with the positive pattern: the
    /// hours without padding and the minutes only when they are not 0 for z and v, and both padded for zzzz and vvvv (design §4.11,
    /// "Time zone skeletons"; Issue #906).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en (GMT{0}, +HH:mm;-HH:mm), sv (GMT{0}, a minus U+2212), fi (UTC{0}, +H.mm;-H.mm), ar (غرينتش{0}), el, whose Hmv and Hv
    ///   are HH:mm (vvvv) and HH (vvvv), and he, whose negative pattern ends with U+200E after the minutes and whose gmtFormat
    ///   ends with U+200E after {0}.
    ///
    /// Steps:
    /// - Write 13:05 with the row's skeleton in 24 hours at the row's offset.
    ///
    /// Expected:
    /// - The text of the row: 0 is GMT+0, UTC+0, and غرينتش+0 short and GMT+00:00 long, as ICU's formatOffsetLocalizedGMT writes
    ///   it; he's short -8 drops the hourFormat's text after the hours, its U+200E, as ICU does, and -5:30 keeps it.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="skeleton">The skeleton.</param>
    /// <param name="offsetMinutes">The offset from GMT, in minutes.</param>
    /// <param name="expected">The text, with the marks of the class comment.</param>
    [Theory]
    [InlineData("en", "jmz", 0, "13:05 GMT+0")]
    [InlineData("en", "jmz", 540, "13:05 GMT+9")]
    [InlineData("en", "jmz", -480, "13:05 GMT-8")]
    [InlineData("en", "jmz", 330, "13:05 GMT+5:30")]
    [InlineData("en", "jmv", -150, "13:05 GMT-2:30")]
    [InlineData("sv", "jmz", -480, "13:05 GMT−8")]
    [InlineData("sv", "jmv", 330, "13:05 GMT+5:30")]
    [InlineData("fi", "jmz", 0, "13.05 UTC+0")]
    [InlineData("fi", "jmz", 330, "13.05 UTC+5.30")]
    [InlineData("ar", "jmz", 0, "13:05 غرينتش+0")]
    [InlineData("ar", "jmz", -480, "13:05 غرينتش-8")]
    [InlineData("el", "jmz", 0, "13:05 (GMT+00:00)")]
    [InlineData("el", "jmz", 540, "13:05 (GMT+09:00)")]
    [InlineData("el", "jmv", -150, "13:05 (GMT-02:30)")]
    [InlineData("el", "jz", 330, "13 (GMT+05:30)")]
    [InlineData("he", "jmz", -480, "13:05 GMT-8|")]
    [InlineData("he", "jmz", -330, "13:05 GMT-5:30||")]
    public void TimeZone_Offsets_WriteTheLocalesGmtFormat(string tag, string skeleton, int offsetMinutes, string expected)
    {
        var value = new DateTimeOffset(2026, 2, 3, 13, 5, 9, TimeSpan.FromMinutes(offsetMinutes));

        Assert.Equal(Marked(expected), Write(DateFormatter.FromSkeleton(ByTag[tag], skeleton, true), value));
    }

    /// <summary>
    /// The long offset (zzzz) writes the hours in two digits whether the hourFormat has H or HH, as ICU's formatOffsetLocalizedGMT
    /// does (design §4.11, "Time zone skeletons"; Issue #906). No skeleton of the 41 reaches zzzz in cs or fi, so the test puts it in
    /// their tables.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - cs's hourFormat is +H:mm;-H:mm and fi's +H.mm;-H.mm. Tables that are en's but for the pattern of jmz, the row's pattern
    ///   with zzzz, and the gmtFormat and hourFormat, the row locale's.
    ///
    /// Steps:
    /// - Write 13:05 at the row's offset with the format of jmz.
    ///
    /// Expected:
    /// - The text of the row: GMT+09:00, GMT-08:00, UTC+09.00, and GMT+00:00, not GMT+9:00.
    /// </remarks>
    /// <param name="tag">The locale whose gmtFormat and hourFormat the table takes.</param>
    /// <param name="pattern">The pattern of jmz.</param>
    /// <param name="offsetMinutes">The offset from GMT, in minutes.</param>
    /// <param name="expected">The text.</param>
    [Theory]
    [InlineData("cs", "H:mm zzzz", 540, "13:05 GMT+09:00")]
    [InlineData("cs", "H:mm zzzz", -480, "13:05 GMT-08:00")]
    [InlineData("cs", "H:mm zzzz", 0, "13:05 GMT+00:00")]
    [InlineData("fi", "H.mm zzzz", 540, "13.05 UTC+09.00")]
    public void TimeZone_LongOffsetOfAnHourFormatWithOneH_WritesTwoDigits(string tag, string pattern, int offsetMinutes, string expected)
    {
        Assert.Equal("+H:mm;-H:mm", PatternAt(ByTag["cs"], HourFormatIndex));
        Assert.Equal("+H.mm;-H.mm", PatternAt(ByTag["fi"], HourFormatIndex));
        byte[] table = WithPattern(CldrLocales.En.DatePatternTable, JmzIndex, pattern);
        table = WithPattern(table, HourFormatIndex - 1, PatternAt(ByTag[tag], HourFormatIndex - 1));
        table = WithPattern(table, HourFormatIndex, PatternAt(ByTag[tag], HourFormatIndex));
        var value = new DateTimeOffset(2026, 2, 3, 13, 5, 9, TimeSpan.FromMinutes(offsetMinutes));

        Assert.Equal(expected, Write(DateFormatter.FromSkeleton(new PatternLocaleData(table), "jmz", false), value));
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
    /// The day period of a j skeleton is am or pm, the a that j asks for, also where the locale's format has a flexible day period
    /// (zh-Hant's Bh:mm), as ICU's adjustFieldTypes writes the requested letter (design §4.11, "The letter of the day period";
    /// Issue #906).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - zh-Hant, whose hour is h (TW), whose hm is Bh:mm, and whose abbreviated am and pm are 上午 and 下午.
    ///
    /// Steps:
    /// - Write the time of each row with jm, without and with the 24-hour form.
    ///
    /// Expected:
    /// - The row's text: 上午 before 12:00 and 下午 from it, midnight and noon included; in 24 hours, HH:mm.
    /// </remarks>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="expected">The text.</param>
    /// <param name="expected24">The text in 24 hours.</param>
    [Theory]
    [InlineData(0, 30, "上午12:30", "00:30")]
    [InlineData(7, 3, "上午7:03", "07:03")]
    [InlineData(11, 59, "上午11:59", "11:59")]
    [InlineData(12, 0, "下午12:00", "12:00")]
    [InlineData(19, 0, "下午7:00", "19:00")]
    public void DayPeriod_ZhHant_IsAmOrPm(int hour, int minute, string expected, string expected24)
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
    /// A range writes CLDR's interval pattern of the first field the two values differ in, split between them; a field smaller than
    /// every field of the format writes the start alone; a skeleton with no near interval item, and a time skeleton whose values are
    /// on other days, write both values whole joined by intervalFormatFallback (design §4.11, "Interval formats").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en (fallback {0}~–~{1}, its hour h), ja (fallback {0}～{1}, its hour H), and pt (its hour H, its short atTime joining
    ///   pattern {1}, {0}); CLDR 49's intervalFormats.
    ///
    /// Steps:
    /// - Write the row's range of DateTime values (written yyyy-MM-ddTHH:mm:ss, the time 0 when left out) with the row's skeleton,
    ///   with the 24-hour form when the row asks for it. The rows cover a day, a month, and a year that differ (yMMMd); a skeleton
    ///   with no item of its own (yMMMMd takes yMMMd's, widened); a field the item lacks (d's month from Md); an hour, a minute, and
    ///   am or pm (jm; 24-hour jm takes Hm's hour pattern for am or pm), am and pm at 11:59 and 12:00, and two times from 12:00, both
    ///   pm; a time skeleton with another day (jm, the 24-hour j and jms, whose entries are the two around 24-hour jm's, and pt's
    ///   Hm, joined by the atTime pattern as ICU 78.3 writes it); skeletons with no near item (LLL, E, QQQ, and Hms for a second);
    ///   and fields smaller than the format's (Hm and a second, yMd and an hour, y and a month).
    ///
    /// Expected:
    /// - The text of the row.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="skeleton">The skeleton.</param>
    /// <param name="use24HourFormat">Whether the 24-hour form is asked for.</param>
    /// <param name="start">The start.</param>
    /// <param name="end">The end.</param>
    /// <param name="expected">The text, with the marks of the class comment.</param>
    [Theory]
    [InlineData("en", "yMMMd", false, "2026-01-03", "2026-01-09", "Jan 3~–~9, 2026")]
    [InlineData("en", "yMMMd", false, "2026-01-03", "2026-02-09", "Jan 3~–~Feb 9, 2026")]
    [InlineData("en", "yMMMd", false, "2025-12-29", "2026-01-04", "Dec 29, 2025~–~Jan 4, 2026")]
    [InlineData("en", "yMMMMd", false, "2026-01-03", "2026-01-09", "January 3~–~9, 2026")]
    [InlineData("en", "d", false, "2026-01-03", "2026-02-09", "1/3~–~2/9")]
    [InlineData("en", "jm", false, "2026-01-03T09:05:00", "2026-01-03T11:30:00", "9:05~–~11:30^AM")]
    [InlineData("en", "jm", false, "2026-01-03T09:05:00", "2026-01-03T09:30:00", "9:05~–~9:30^AM")]
    [InlineData("en", "jm", false, "2026-01-03T09:05:00", "2026-01-03T13:30:00", "9:05^AM~–~1:30^PM")]
    [InlineData("en", "jm", true, "2026-01-03T09:05:00", "2026-01-03T21:30:00", "09:05~–~21:30")]
    [InlineData("en", "jm", false, "2026-01-03T11:59:00", "2026-01-03T12:00:00", "11:59^AM~–~12:00^PM")]
    [InlineData("en", "jm", false, "2026-01-03T12:00:00", "2026-01-03T12:30:00", "12:00~–~12:30^PM")]
    [InlineData("en", "jm", false, "2026-01-03T09:05:00", "2026-01-04T10:00:00", "1/3/2026, 9:05^AM~–~1/4/2026, 10:00^AM")]
    [InlineData("en", "j", true, "2026-01-03T09:05:00", "2026-01-04T10:00:00", "1/3/2026, 09~–~1/4/2026, 10")]
    [InlineData("en", "jms", true, "2026-01-03T09:05:00", "2026-01-04T10:00:00", "1/3/2026, 09:05:00~–~1/4/2026, 10:00:00")]
    [InlineData("pt", "Hm", false, "2026-03-04T09:05:00", "2026-03-05T11:30:00", "04/03/2026, 09:05~–~05/03/2026, 11:30")]
    [InlineData("en", "LLL", false, "2026-01-03", "2026-03-09", "Jan~–~Mar")]
    [InlineData("en", "E", false, "2026-01-03", "2026-01-09", "Sat~–~Fri")]
    [InlineData("en", "QQQ", false, "2026-01-03", "2026-04-09", "Q1~–~Q2")]
    [InlineData("en", "Hms", false, "2026-01-03T13:05:09", "2026-01-03T13:05:30", "13:05:09~–~13:05:30")]
    [InlineData("en", "Hm", false, "2026-01-03T13:05:09", "2026-01-03T13:05:30", "13:05")]
    [InlineData("en", "yMd", false, "2026-01-03T09:00:00", "2026-01-03T17:00:00", "1/3/2026")]
    [InlineData("en", "y", false, "2026-01-03", "2026-03-09", "2026")]
    [InlineData("ja", "yMMMd", false, "2026-01-03", "2026-01-09", "2026年1月3日～9日")]
    [InlineData("ja", "yMMMd", false, "2026-01-03", "2026-02-09", "2026年1月3日～2月9日")]
    [InlineData("ja", "yMMMd", false, "2025-12-29", "2026-01-04", "2025年12月29日～2026年1月4日")]
    [InlineData("ja", "yMMMMd", false, "2026-01-03", "2026-01-09", "2026年1月3日～9日")]
    [InlineData("ja", "jm", false, "2026-01-03T09:05:00", "2026-01-03T11:30:00", "9時05分～11時30分")]
    [InlineData("ja", "jm", false, "2026-01-03T09:05:00", "2026-01-03T09:30:00", "9時05分～9時30分")]
    [InlineData("ja", "jm", false, "2026-01-03T09:05:00", "2026-01-04T10:00:00", "2026/1/3 9:05～2026/1/4 10:00")]
    [InlineData("ja", "LLL", false, "2026-01-03", "2026-03-09", "1月～3月")]
    [InlineData("ja", "yMd", false, "2026-01-03T09:00:00", "2026-01-03T17:00:00", "2026/1/3")]
    public void TryFormatRange_IntervalFormats_WriteTheFirstFieldThatDiffers(
        string tag,
        string skeleton,
        bool use24HourFormat,
        string start,
        string end,
        string expected
    )
    {
        DateFormatter format = DateFormatter.FromSkeleton(ByTag[tag], skeleton, use24HourFormat);
        char[] buffer = new char[128];

        Assert.True(format.TryFormatRange(ParseClock(start), ParseClock(end), buffer, out int written));

        Assert.Equal(Marked(expected), new string(buffer, 0, written));
    }

    /// <summary>
    /// A range of two DateOnly values writes what the same days at midnight write; OfRange writes it in an interpolation of a
    /// NodeTextWriter, and its default value writes nothing; a range of one day writes the day alone.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMd; a writer over a buffer.
    ///
    /// Steps:
    /// - Write 2026-01-03 to 2026-01-09 as DateOnly values with TryFormatRange; write "Week of {range}{default}" with OfRange; write
    ///   2026-01-03 to 2026-01-03.
    ///
    /// Expected:
    /// - Jan 3~–~9, 2026; Week of Jan 3~–~9, 2026; Jan 3, 2026.
    /// </remarks>
    [Fact]
    public void OfRange_DateOnly_WritesTheRange()
    {
        DateFormatter format = DateFormatter.FromSkeleton(CldrLocales.En, "yMMMd", false);
        var start = new DateOnly(2026, 1, 3);
        var end = new DateOnly(2026, 1, 9);
        char[] buffer = new char[64];
        var writer = new NodeTextWriter(new char[64], new TextRangeWriter());

        Assert.True(format.TryFormatRange(start, end, buffer, out int written));
        writer.Write($"Week of {format.OfRange(start, end)}{default(FormattedDateRange)}");
        Assert.True(format.TryFormatRange(start, start, buffer.AsSpan(32), out int sameDay));

        Assert.Equal(Marked("Jan 3~–~9, 2026"), new string(buffer, 0, written));
        Assert.Equal(Marked("Week of Jan 3~–~9, 2026"), new string(writer.Buffer, 0, writer.Length));
        Assert.Equal("Jan 3, 2026", new string(buffer, 32, sameDay));
    }

    /// <summary>
    /// A 24-hour entry that the table points to the skeleton's entry in the locale's hour (the byte 255) writes what that entry
    /// writes: a locale whose hour is H writes the same range with the 24-hour form and without it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - ja, whose hour is H, so that its interval table has the byte 255 for the 24-hour entries of j, jm, and jms, which follow
    ///   one another (jms's is read after skipping two such entries).
    ///
    /// Steps:
    /// - Write 2026-01-03 09:05 to 11:30, and 2026-01-03 09:05 to 2026-01-04 10:00, with j, jm, and jms, without and with the
    ///   24-hour form.
    ///
    /// Expected:
    /// - Each skeleton writes the same text both ways; jm's first range is 9時05分～11時30分.
    /// </remarks>
    [Fact]
    public void TryFormatRange_TwentyFourHourEntryOfTheLocalesHour_WritesTheOwnEntry()
    {
        var start = new DateTime(2026, 1, 3, 9, 5, 0);
        DateTime[] ends = [new(2026, 1, 3, 11, 30, 0), new(2026, 1, 4, 10, 0, 0)];
        char[] buffer = new char[128];

        foreach (string skeleton in new[] { "j", "jm", "jms" })
        {
            foreach (DateTime end in ends)
            {
                Assert.True(DateFormatter.FromSkeleton(CldrLocales.Ja, skeleton, false).TryFormatRange(start, end, buffer, out int written));
                string own = new(buffer, 0, written);
                Assert.True(DateFormatter.FromSkeleton(CldrLocales.Ja, skeleton, true).TryFormatRange(start, end, buffer, out written));
                Assert.Equal(own, new string(buffer, 0, written));
            }
        }

        Assert.True(DateFormatter.FromSkeleton(CldrLocales.Ja, "jm", true).TryFormatRange(start, ends[0], buffer, out int text));
        Assert.Equal("9時05分～11時30分", new string(buffer, 0, text));
    }

    /// <summary>
    /// A destination too short for a range writes nothing and returns <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMd (Jan 3~–~9, 2026: 15 characters).
    ///
    /// Steps:
    /// - Write 2026-01-03 to 2026-01-09 into 14 and 15 characters.
    ///
    /// Expected:
    /// - false and 0, then true and 15.
    /// </remarks>
    [Fact]
    public void TryFormatRange_ShortDestination_IsFalse()
    {
        DateFormatter format = DateFormatter.FromSkeleton(CldrLocales.En, "yMMMd", false);
        var start = new DateOnly(2026, 1, 3);
        var end = new DateOnly(2026, 1, 9);

        Assert.False(format.TryFormatRange(start, end, new char[14], out int none));
        Assert.Equal(0, none);
        Assert.True(format.TryFormatRange(start, end, new char[15], out int all));
        Assert.Equal(15, all);
    }

    /// <summary>
    /// The later date is written first where the table says so: by the fallback when it has {1} before {0}, and by a split record
    /// of the kind that writes its first part with the later date, a record whose second part is empty included (design §4.11,
    /// "Split in two"). CLDR 49 has neither, so the tables are made by the test.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Tables that are en's but for the interval table, which the test makes: each of its 40 entries is the same, with the row's
    ///   fallback, the row's field bytes for the 7 fields, and the row's records. The format is en's yMMMd (MMM d, y).
    ///
    /// Steps:
    /// - Write 2026-01-03 to 2026-01-09, and 2025-12-29 to 2026-01-04, with each table.
    ///
    /// Expected:
    /// - The fallback &lt;{1} - {0}&gt; with the format's own pattern: &lt;Jan 9, 2026 - Jan 3, 2026&gt;.
    /// - The fallback {1} - {0} with the record M/d/y for both dates: 1/9/2026 - 1/3/2026.
    /// - The split MMM d -  | d, y with the later date first: Jan 9 - 3, 2026; with the earlier first: Jan 3 - 9, 2026.
    /// - The split y | (empty) with the later date first writes 2026 for the second range, and with the earlier first 2025.
    /// </remarks>
    [Fact]
    public void TryFormatRange_LaterDateFirst_WritesTheEndFirst()
    {
        var start = new DateTime(2026, 1, 3);
        var end = new DateTime(2026, 1, 9);
        var newYearStart = new DateTime(2025, 12, 29);
        var newYearEnd = new DateTime(2026, 1, 4);
        int[] own = [1, 1, 1, 1, 1, 1, 1];
        int[] record = [2, 2, 2, 2, 2, 2, 2];

        Assert.Equal("<Jan 9, 2026 - Jan 3, 2026>", Range(IntervalTableOf("<{1} - {0}>", own), start, end));
        Assert.Equal("1/9/2026 - 1/3/2026", Range(IntervalTableOf("{1} - {0}", record, (2, "M/d/y", string.Empty)), start, end));
        Assert.Equal("Jan 9 - 3, 2026", Range(IntervalTableOf("{0} - {1}", record, (1, "MMM d - ", "d, y")), start, end));
        Assert.Equal("Jan 3 - 9, 2026", Range(IntervalTableOf("{0} - {1}", record, (0, "MMM d - ", "d, y")), start, end));
        Assert.Equal("2026", Range(IntervalTableOf("{0} - {1}", record, (1, "y", string.Empty)), newYearStart, newYearEnd));
        Assert.Equal("2025", Range(IntervalTableOf("{0} - {1}", record, (0, "y", string.Empty)), newYearStart, newYearEnd));

        static string Range(byte[] intervals, DateTime from, DateTime to)
        {
            var data = new PatternLocaleData(CldrLocales.En.DatePatternTable.ToArray(), intervals);
            char[] buffer = new char[64];
            Assert.True(DateFormatter.FromSkeleton(data, "yMMMd", false).TryFormatRange(from, to, buffer, out int written));
            return new string(buffer, 0, written);
        }
    }

    /// <summary>
    /// A range whose start is after its end, a value type the format does not take, and a time zone skeleton, which needs an offset,
    /// throw <see cref="ArgumentException"/>; a date skeleton joined with a time skeleton throws
    /// <see cref="InvalidOperationException"/>, as intervalFormats have no patterns for it (design §4.11, "The table").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMd, jm, and jmz, and yMd joined with jm.
    ///
    /// Steps:
    /// - Write and make ranges of 2026-01-09 to 2026-01-03 with yMMMd, as DateOnly and DateTime values; ranges of DateOnly values
    ///   with jm; of DateTime values with jmz; of DateTime values with the joined format.
    ///
    /// Expected:
    /// - ArgumentException naming start for each: the reversed ranges say the start is after the end, jm's DateOnly values that
    ///   they have no time, and jmz's DateTime values that a range takes no offset; InvalidOperationException for the joined format;
    ///   by TryFormatRange and by OfRange.
    /// </remarks>
    [Fact]
    public void TryFormatRange_ValuesTheFormatDoesNotTake_Throw()
    {
        const string Reversed = "The range's start is after its end.";
        const string NoTime = "A range of DateOnly values has no time, and the format writes a time field.";
        const string NoOffset = "The format writes a time zone, and a range takes no offset.";
        DateFormatter date = DateFormatter.FromSkeleton(CldrLocales.En, "yMMMd", false);
        DateFormatter time = DateFormatter.FromSkeleton(CldrLocales.En, "jm", false);
        DateFormatter zone = DateFormatter.FromSkeleton(CldrLocales.En, "jmz", false);
        DateFormatter joined = DateFormatter.FromSkeletons(CldrLocales.En, "yMd", "jm", false);
        var early = new DateOnly(2026, 1, 3);
        var late = new DateOnly(2026, 1, 9);
        DateTime earlyClock = early.ToDateTime(TimeOnly.MinValue);
        DateTime lateClock = late.ToDateTime(TimeOnly.MinValue);
        char[] buffer = new char[64];

        Check(Reversed, () => date.TryFormatRange(late, early, buffer, out _));
        Check(Reversed, () => date.TryFormatRange(lateClock, earlyClock, buffer, out _));
        Check(Reversed, () => date.OfRange(late, early));
        Check(Reversed, () => date.OfRange(lateClock, earlyClock));
        Check(NoTime, () => time.TryFormatRange(early, late, buffer, out _));
        Check(NoTime, () => time.OfRange(early, late));
        Check(NoOffset, () => zone.TryFormatRange(earlyClock, lateClock, buffer, out _));
        Check(NoOffset, () => zone.OfRange(earlyClock, lateClock));
        Assert.Throws<InvalidOperationException>(() => joined.TryFormatRange(earlyClock, lateClock, buffer, out _));
        Assert.Throws<InvalidOperationException>(() => joined.OfRange(earlyClock, lateClock));

        static void Check(string message, Action call)
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(call);
            Assert.Equal("start", thrown.ParamName);
            Assert.StartsWith(message, thrown.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Writing a date allocates nothing, and neither does writing Of(value) into a NodeTextWriter (design §4.11, "Allocation and
    /// threads"; §11.3's Zero-Alloc check).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMMEEEEd, jmz, and yMd joined with jm; zh-Hant's jm (am and pm before the hour); ar-EG's yMd (Arabic-Indic digits); a
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
    /// Writing a range allocates nothing once the format has written one, and neither does writing OfRange into a NodeTextWriter
    /// (design §4.11, "Allocation and threads"; §11.3's Zero-Alloc check).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's yMMMd (split patterns), jm (split, and the fallback with yMd and the time for other days), and LLL (the fallback with
    ///   its own pattern); one range written with each before the measured runs, which reads their interval patterns; a destination
    ///   buffer and a writer over a buffer.
    ///
    /// Steps:
    /// - 1,000 times, in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: with the index shifted by the 100 runs of the
    ///   unmeasured window so that it is 0 or more, write the range from 2026-01-03 09:05 to that many hours later with jm, and
    ///   to that many days later with yMMMd and LLL (as DateOnly values), and write the three OfRange values into a new writer over
    ///   the same buffer.
    ///
    /// Expected:
    /// - No byte is allocated, and every run wrote.
    /// </remarks>
    [Fact]
    public void Ranges_AllocateNothing()
    {
        DateFormatter days = DateFormatter.FromSkeleton(CldrLocales.En, "yMMMd", false);
        DateFormatter times = DateFormatter.FromSkeleton(CldrLocales.En, "jm", false);
        DateFormatter months = DateFormatter.FromSkeleton(CldrLocales.En, "LLL", false);
        var start = new DateTime(2026, 1, 3, 9, 5, 0);
        var startDay = DateOnly.FromDateTime(start);
        char[] destination = new char[64];
        char[] text = new char[128];
        var ranges = new TextRangeWriter();
        Assert.True(days.TryFormatRange(startDay, startDay, destination, out _));
        Assert.True(times.TryFormatRange(start, start, destination, out _));
        Assert.True(months.TryFormatRange(startDay, startDay, destination, out _));
        int done = 0;

        int runs = PerformanceAssert.DoesNotAllocate(
            1_000,
            i =>
            {
                int steps = i + 100;
                DateTime end = start.AddHours(steps);
                DateOnly endDay = startDay.AddDays(steps);
                bool all = times.TryFormatRange(start, end, destination, out _);
                all &= days.TryFormatRange(startDay, endDay, destination, out _);
                all &= months.TryFormatRange(startDay, endDay, destination, out _);
                var writer = new NodeTextWriter(text, ranges);
                writer.Write($"{times.OfRange(start, end)} {days.OfRange(startDay, endDay)} {months.OfRange(startDay, endDay)}");
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
    /// - 149 name tables, 168 pattern tables, and 164 interval tables (counted from the json: locales whose bytes are the same share
    ///   one); each day 3 is the digit three of the locale's number table.
    /// </remarks>
    [Fact]
    public void CldrData_DateTables_HoldTheJsonsValues()
    {
        Assert.Equal(149, CldrData.DateNameTableCount);
        Assert.Equal(168, CldrData.DatePatternTableCount);
        Assert.Equal(164, CldrData.DateIntervalTableCount);
        foreach (LocaleData data in CldrLocales.All.Append(LocaleData.Root))
        {
            string day = Write(DateFormatter.FromSkeleton(data, "d", false), Moment);
            Assert.Contains((char)(data.Numbers.Zero + 3), day);
        }
    }

    // A clock written yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss.
    private static DateTime ParseClock(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static string Write(DateFormatter format, DateTimeOffset value)
    {
        char[] buffer = new char[128];
        Assert.True(format.TryFormat(value, buffer, out int written));
        return new string(buffer, 0, written);
    }

    // The string at an index of a locale's date pattern table: the 41 patterns in the order of Skeletons, the 7 24-hour ones, the
    // four joining patterns, the Time-Day-Of-Week append item, gmtFormat, and hourFormat.
    private static string PatternAt(LocaleData data, int index)
    {
        var reader = new CldrTableReader(data.DatePatternTable);
        _ = reader.ReadChar();
        for (int i = 0; i < index; i++)
        {
            reader.SkipString();
        }

        return reader.ReadString();
    }

    // A joining pattern with {1} replaced by a date's text and {0} by a time's, a single quote dropped, and two quotes as one.
    private static string Joined(string joining, string date, string time)
    {
        var text = new StringBuilder();
        for (int i = 0; i < joining.Length; i++)
        {
            if (joining[i] == '{' && i + 2 < joining.Length && joining[i + 2] == '}')
            {
                text.Append(joining[i + 1] == '1' ? date : time);
                i += 2;
            }
            else if (joining[i] != '\'')
            {
                text.Append(joining[i]);
            }
            else if (i + 1 < joining.Length && joining[i + 1] == '\'')
            {
                text.Append('\'');
                i++;
            }
        }

        return text.ToString();
    }

    // A pattern table with the string at an index replaced by a pattern: the digit zero, then each string as a length byte and its
    // UTF-8.
    private static byte[] WithPattern(ReadOnlySpan<byte> table, int index, string pattern)
    {
        var reader = new CldrTableReader(table);
        var bytes = new List<byte>(table[..2].ToArray());
        _ = reader.ReadChar();
        for (int i = 0; !reader.End; i++)
        {
            string text = reader.ReadString();
            AddString(bytes, i == index ? pattern : text);
        }

        return [.. bytes];
    }

    // An interval table (tools/CldrTableGen/DateTables.cs) whose 40 entries are the same: the fallback, then for each entry the count
    // of records, the field bytes, and the records, each a kind byte and its strings (one for kind 2, the fallback with a pattern).
    private static byte[] IntervalTableOf(string fallback, int[] fields, params (int Kind, string First, string Second)[] records)
    {
        var bytes = new List<byte>();
        AddString(bytes, fallback);
        for (int entry = 0; entry < 40; entry++)
        {
            bytes.Add((byte)records.Length);
            foreach (int field in fields)
            {
                bytes.Add((byte)field);
            }

            foreach ((int kind, string first, string second) in records)
            {
                bytes.Add((byte)kind);
                AddString(bytes, first);
                if (kind != 2)
                {
                    AddString(bytes, second);
                }
            }
        }

        return [.. bytes];
    }

    // A string of a table: a length byte and its UTF-8.
    private static void AddString(List<byte> bytes, string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        bytes.Add((byte)utf8.Length);
        bytes.AddRange(utf8);
    }

    // The text with the marks of the class comment replaced by the characters they stand for.
    private static string Marked(string text) =>
        text.Replace('_', (char)0x00A0)
            .Replace('^', (char)0x202F)
            .Replace('|', (char)0x200E)
            .Replace('!', (char)0x200F)
            .Replace('@', (char)0x061C)
            .Replace('~', (char)0x2009);

    // Tables that are en's but for the date pattern table, which the test gives, and the interval table, when the test gives one.
    private sealed class PatternLocaleData(byte[] patterns, byte[]? intervals = null)
        : LocaleData(Locale.Parse("en"), DayOfWeek.Sunday, 1, DayOfWeek.Saturday, DayOfWeek.Sunday)
    {
        internal override ReadOnlySpan<byte> NumberTable => CldrLocales.En.NumberTable;

        internal override ReadOnlySpan<byte> CompactTable => CldrLocales.En.CompactTable;

        internal override ReadOnlySpan<byte> CurrencyTable => CldrLocales.En.CurrencyTable;

        internal override ReadOnlySpan<byte> DateNameTable => CldrLocales.En.DateNameTable;

        internal override ReadOnlySpan<byte> DatePatternTable => patterns;

        internal override ReadOnlySpan<byte> DateIntervalTable => intervals is null ? CldrLocales.En.DateIntervalTable : intervals;

        internal override PluralRules CreatePlurals() => new(Other, Other);

        private static PluralCategory Other(in PluralOperands operands) => PluralCategory.Other;
    }
}
