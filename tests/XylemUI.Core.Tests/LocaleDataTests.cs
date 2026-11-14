using System.Globalization;

namespace XylemUI.Tests;

// The CLDR tables that tools/CldrTableGen generated from CLDR 49.0.0 (LC2, design §4.11, "Formats"): the set of locales and its
// entries, root, the week of each locale's region and the week number, the tables under another tag, and the plural rules made once.
// The expected values were read from the json of cldr-49.0.0-json-full.zip, not from the generated code.
public sealed class LocaleDataTests
{
    /// <summary>
    /// The tables are of the CLDR release that vendor/versions.lock pins.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The repository's vendor/versions.lock, found above the test's directory.
    ///
    /// Steps:
    /// - Read the version and the SHA-256 of its cldr-json line.
    ///
    /// Expected:
    /// - CldrData.Version and CldrData.Sha256 are those (49.0.0).
    /// </remarks>
    [Fact]
    public void CldrData_Release_IsTheLocked()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "XylemUI.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.NotNull(directory);
        string[] locked = File.ReadLines(Path.Combine(directory, "vendor", "versions.lock"))
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Single(fields => fields.Length >= 4 && fields[0] == "cldr-json");

        Assert.Equal(CldrData.Version, locked[1]);
        Assert.Equal(CldrData.Sha256, locked[2]);
        Assert.Equal("49.0.0", CldrData.Version);
    }

    /// <summary>
    /// The set has 421 tables and root, and CldrLocales has 422 entries: the modern locales without the three whose tag has a
    /// variant, the four languages of row LC4 that they lack, and the alias tl (design §4.11, "The set of locales").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The generated tables.
    ///
    /// Steps:
    /// - Read the counts the tool wrote, and the tags of CldrLocales.All.
    ///
    /// Expected:
    /// - 421 tables and 422 entries, and All has the 422.
    /// - No two entries have the same tag: be-tarask and el-polyton, read without their variant, would be be and el a second time.
    ///   ca-ES-valencia would be ca-ES, which no entry has: CLDR's modern set has ca, ca-AD, ca-FR, and ca-IT, and no ca-ES.
    /// - nb, bo, gsw, ug, and tl are entries; root (und) is not.
    /// - The tool counted 30 sets of cardinal rules, 25 of ordinal rules, and 52 pairs among the entries and root, and the methods
    ///   the tables select with are as many (counted from the json: the spike's README also counts 52 rule sets).
    /// </remarks>
    [Fact]
    public void CldrLocales_All_HoldsTheSetOnce()
    {
        string[] tags = [.. CldrLocales.All.Select(data => data.Locale.ToLanguageTag())];

        Assert.Equal(421, CldrData.TableCount);
        Assert.Equal(422, CldrData.EntryCount);
        Assert.Equal(422, tags.Length);
        Assert.Equal(tags.Length, tags.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("be", tags);
        Assert.Contains("ca", tags);
        Assert.DoesNotContain("ca-ES", tags);
        Assert.Contains("el", tags);
        foreach (string added in new[] { "nb", "bo", "gsw", "ug", "tl" })
        {
            Assert.Contains(added, tags);
        }

        Assert.DoesNotContain("und", tags);
        Assert.Equal(tags.Order(StringComparer.Ordinal), tags);

        LocaleData[] all = [.. CldrLocales.All, LocaleData.Root];
        Assert.Equal((30, 25, 52), (CldrData.CardinalRuleSets, CldrData.OrdinalRuleSets, CldrData.PluralRuleSetPairs));
        Assert.Equal(30, all.Select(data => data.Plurals.Cardinal).Distinct().Count());
        Assert.Equal(25, all.Select(data => data.Plurals.Ordinal).Distinct().Count());
        Assert.Equal(52, all.Select(data => (data.Plurals.Cardinal, data.Plurals.Ordinal)).Distinct().Count());
    }

    /// <summary>
    /// Each entry's locale is its CLDR tag read by Locale.Parse unchanged, and a property's name joins its tag's subtags.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tags of the entries, in the table of samples the tool wrote for the tests (root first).
    ///
    /// Steps:
    /// - Compare each entry of All with the tag in the same place; read some properties by name.
    ///
    /// Expected:
    /// - Each locale writes its CLDR tag back; En, EnGB, Es419, ZhHantHK, SrLatn, and Nb are en, en-GB, es-419, zh-Hant-HK, sr-Latn,
    ///   and nb.
    /// </remarks>
    [Fact]
    public void CldrLocales_Entries_HaveTheirTags()
    {
        string[] expected = [.. CldrPluralSamples.Entries.Skip(1).Select(entry => entry.Tag)];

        Assert.Equal("und", CldrPluralSamples.Entries[0].Tag);
        Assert.Equal(expected, CldrLocales.All.Select(data => data.Locale.ToLanguageTag()));
        Assert.Equal("en", CldrLocales.En.Locale.ToLanguageTag());
        Assert.Equal("en-GB", CldrLocales.EnGB.Locale.ToLanguageTag());
        Assert.Equal("es-419", CldrLocales.Es419.Locale.ToLanguageTag());
        Assert.Equal("zh-Hant-HK", CldrLocales.ZhHantHK.Locale.ToLanguageTag());
        Assert.Equal("sr-Latn", CldrLocales.SrLatn.Locale.ToLanguageTag());
        Assert.Equal("nb", CldrLocales.Nb.Locale.ToLanguageTag());
    }

    /// <summary>
    /// tl is fil's tables under the tag tl, which CLDR reads as fil (aliases.json) and the IANA registry does not replace.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - CldrLocales.Tl and CldrLocales.Fil.
    ///
    /// Steps:
    /// - Read their locales, weeks, and plural rules.
    ///
    /// Expected:
    /// - Tl's locale is tl and Fil's fil; the weeks are the same, and both select with the same rule methods.
    /// </remarks>
    [Fact]
    public void Tl_IsFilsTablesUnderTl()
    {
        LocaleData tl = CldrLocales.Tl;
        LocaleData fil = CldrLocales.Fil;

        Assert.Equal(Locale.Parse("tl"), tl.Locale);
        Assert.Equal("tl", tl.Locale.ToLanguageTag());
        Assert.Equal(Locale.Parse("fil"), fil.Locale);
        Assert.Equal(Week(fil), Week(tl));
        Assert.Equal(fil.Plurals.Cardinal, tl.Plurals.Cardinal);
        Assert.Equal(fil.Plurals.Ordinal, tl.Plurals.Ordinal);
    }

    /// <summary>
    /// Each locale holds the week of its region, or of its likely region when its tag has none; a region weekData does not list
    /// takes the world's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - CLDR 49's weekData: the world (001) Monday, 1, Saturday to Sunday; US and JP start on Sunday; GB and DE Monday with 4 days;
    ///   IN starts on Sunday with a weekend of Sunday alone; SA starts on Sunday with Friday to Saturday; AF starts on Saturday with
    ///   Thursday to Friday; 419 is not listed.
    /// - The likely regions: en US, ja JP, de DE, hi IN, ps AF.
    ///
    /// Steps:
    /// - Read the week of each locale.
    ///
    /// Expected:
    /// - The row's first day, minimal days, and weekend.
    /// </remarks>
    /// <param name="tag">The locale's tag.</param>
    /// <param name="first">The first day of the week.</param>
    /// <param name="minimalDays">The minimal days of the first week.</param>
    /// <param name="weekendStart">The first day of the weekend.</param>
    /// <param name="weekendEnd">The last day of the weekend.</param>
    [Theory]
    [InlineData("en", DayOfWeek.Sunday, 1, DayOfWeek.Saturday, DayOfWeek.Sunday)]
    [InlineData("en-GB", DayOfWeek.Monday, 4, DayOfWeek.Saturday, DayOfWeek.Sunday)]
    [InlineData("ja", DayOfWeek.Sunday, 1, DayOfWeek.Saturday, DayOfWeek.Sunday)]
    [InlineData("de", DayOfWeek.Monday, 4, DayOfWeek.Saturday, DayOfWeek.Sunday)]
    [InlineData("hi", DayOfWeek.Sunday, 1, DayOfWeek.Sunday, DayOfWeek.Sunday)]
    [InlineData("ar-SA", DayOfWeek.Sunday, 1, DayOfWeek.Friday, DayOfWeek.Saturday)]
    [InlineData("ps", DayOfWeek.Saturday, 1, DayOfWeek.Thursday, DayOfWeek.Friday)]
    [InlineData("es-419", DayOfWeek.Monday, 1, DayOfWeek.Saturday, DayOfWeek.Sunday)]
    public void Week_IsTheRegions(string tag, DayOfWeek first, int minimalDays, DayOfWeek weekendStart, DayOfWeek weekendEnd) =>
        Assert.Equal((first, minimalDays, weekendStart, weekendEnd), Week(Entry(tag)));

    /// <summary>
    /// Root is the undetermined locale with the world's week, and its plural rules have other alone.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - LocaleData.Root.
    ///
    /// Steps:
    /// - Read its locale and week; select 0, 1, 2, and 1.5.
    ///
    /// Expected:
    /// - The default locale (und); Monday, 1, Saturday to Sunday; other for every value, cardinal and ordinal.
    /// </remarks>
    [Fact]
    public void Root_IsUndeterminedWithTheWorldsWeek()
    {
        LocaleData root = LocaleData.Root;

        Assert.Equal(default, root.Locale);
        Assert.Equal((DayOfWeek.Monday, 1, DayOfWeek.Saturday, DayOfWeek.Sunday), Week(root));
        Assert.Same(root, LocaleData.Root);
        Assert.DoesNotContain(root, CldrLocales.All);
        foreach (long value in new long[] { 0, 1, 2 })
        {
            Assert.Equal(PluralCategory.Other, root.Plurals.Select(value));
            Assert.Equal(PluralCategory.Other, root.Plurals.SelectOrdinal(value));
        }

        Assert.Equal(PluralCategory.Other, root.Plurals.Select(1.5m));
    }

    /// <summary>
    /// With weeks from Monday and 4 days in the first, the week number is the ISO 8601 week, over every date DateOnly holds.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en-GB's tables (Monday, 4).
    ///
    /// Steps:
    /// - For every date from 0001-01-01 to 9999-12-31, take the week number and its year.
    ///
    /// Expected:
    /// - They are ISOWeek's, an independent count: the first days of January in the last week of the year before (2021-01-01 in
    ///   week 53 of 2020), the last days of December in week 1 of the next year (2024-12-30), and years of 52 and 53 weeks.
    /// </remarks>
    [Fact]
    public void WeekOfYear_MondayAndFourDays_IsTheIsoWeek()
    {
        LocaleData data = CldrLocales.EnGB;
        Assert.Equal((DayOfWeek.Monday, 4), (data.FirstDayOfWeek, data.MinimalDaysInFirstWeek));
        int mismatches = 0;
        string? first = null;
        for (int day = DateOnly.MinValue.DayNumber; day <= DateOnly.MaxValue.DayNumber; day++)
        {
            DateOnly date = DateOnly.FromDayNumber(day);
            DateTime time = date.ToDateTime(TimeOnly.MinValue);
            int week = data.WeekOfYear(date, out int year);
            if (week != ISOWeek.GetWeekOfYear(time) || year != ISOWeek.GetYear(time))
            {
                mismatches++;
                first ??= date.ToString("O", CultureInfo.InvariantCulture);
            }
        }

        Assert.True(mismatches == 0, mismatches + " dates differ from ISOWeek, the first " + first);
        Assert.Equal((53, 2020), (data.WeekOfYear(new DateOnly(2021, 1, 1), out int y1), y1));
        Assert.Equal((1, 2025), (data.WeekOfYear(new DateOnly(2024, 12, 30), out int y2), y2));
        Assert.Equal((53, 2026), (data.WeekOfYear(new DateOnly(2026, 12, 31), out int y3), y3));
        Assert.Equal((52, 9999), (data.WeekOfYear(DateOnly.MaxValue, out int y4), y4));
    }

    /// <summary>
    /// With weeks from Sunday and 1 day in the first, the first week is the one that holds the 1st of January, so the last days of
    /// December can be in week 1 of the next year, and a year whose 31st of December is a Saturday has 53 weeks.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables (Sunday, 1). Counted by hand: 2026-01-01 is a Thursday, 2026-12-27 a Sunday, 2027-01-01 a Friday,
    ///   2022-12-31 a Saturday, 0001-01-01 a Monday, and 9999-12-31 a Friday (10000-01-01 would be a Saturday).
    ///
    /// Steps:
    /// - Take the week of each date of the rows.
    ///
    /// Expected:
    /// - 2026-01-01 and 2026-01-03 in week 1 of 2026, 2026-01-04 in week 2; 2026-12-26 in week 52 of 2026, 2026-12-27 and
    ///   2026-12-31 in week 1 of 2027; 2022-12-31 in week 53 of 2022; 0001-01-01 in week 1 of 1; 9999-12-31 in week 1 of 10000.
    /// </remarks>
    /// <param name="date">The date, as yyyy-MM-dd.</param>
    /// <param name="week">The week's number.</param>
    /// <param name="year">The year the week belongs to.</param>
    [Theory]
    [InlineData("2026-01-01", 1, 2026)]
    [InlineData("2026-01-03", 1, 2026)]
    [InlineData("2026-01-04", 2, 2026)]
    [InlineData("2026-12-26", 52, 2026)]
    [InlineData("2026-12-27", 1, 2027)]
    [InlineData("2026-12-31", 1, 2027)]
    [InlineData("2022-12-31", 53, 2022)]
    [InlineData("0001-01-01", 1, 1)]
    [InlineData("9999-12-31", 1, 10000)]
    public void WeekOfYear_SundayAndOneDay_CountsFromTheWeekOfJanuaryFirst(string date, int week, int year)
    {
        LocaleData data = CldrLocales.En;
        Assert.Equal((DayOfWeek.Sunday, 1), (data.FirstDayOfWeek, data.MinimalDaysInFirstWeek));

        Assert.Equal((week, year), (data.WeekOfYear(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), out int actual), actual));
    }

    /// <summary>
    /// A date before the first week of the year 1 is in the last week of the year 0, which the proleptic Gregorian calendar makes a
    /// leap year that starts on a Saturday.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Tables whose weeks start on Wednesday with 4 days in the first (no CLDR region has them; the class is the test's). Counted
    ///   by hand: 0001-01-01 is a Monday, so the week from Wednesday 0000-12-27 holds 2 days of the year 1 and is the year 0's; the
    ///   year 0's first week starts on Wednesday -0001-12-29, with 4 days of it, so the week of 0001-01-01 is its 53rd.
    ///
    /// Steps:
    /// - Take the week of 0001-01-01 and of 0001-01-03 (the next Wednesday).
    ///
    /// Expected:
    /// - Week 53 of the year 0, and week 1 of the year 1.
    /// </remarks>
    [Fact]
    public void WeekOfYear_BeforeTheFirstWeekOfYearOne_IsInYearZero()
    {
        var data = new WeekLocaleData(DayOfWeek.Wednesday, 4);

        Assert.Equal((53, 0), (data.WeekOfYear(new DateOnly(1, 1, 1), out int y1), y1));
        Assert.Equal((1, 1), (data.WeekOfYear(new DateOnly(1, 1, 3), out int y2), y2));
    }

    /// <summary>
    /// The tables under another tag have that tag, and every value of the source tables: the week is not read again for the tag's
    /// region (design §4.11, "Locales that CLDR does not have").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables, whose week is the one of the US (Sunday, 1), and ru's, whose week starts on Monday.
    ///
    /// Steps:
    /// - Give en's tables the tag en-RU, then give those the tag en-001.
    ///
    /// Expected:
    /// - The locales are en-RU and en-001; both weeks are en's, not Russia's; both return en's plural rules, the same instance.
    /// </remarks>
    [Fact]
    public void WithLocale_KeepsTheTables_AndTakesTheTag()
    {
        LocaleData en = CldrLocales.En;

        LocaleData russia = en.WithLocale(Locale.Parse("en-RU"));
        LocaleData world = russia.WithLocale(Locale.Parse("en-001"));

        Assert.Equal(Locale.Parse("en-RU"), russia.Locale);
        Assert.Equal(Locale.Parse("en-001"), world.Locale);
        Assert.Equal(DayOfWeek.Monday, CldrLocales.Ru.FirstDayOfWeek);
        Assert.Equal(Week(en), Week(russia));
        Assert.Equal(Week(en), Week(world));
        Assert.Same(en.Plurals, russia.Plurals);
        Assert.Same(en.Plurals, world.Plurals);
        Assert.Equal(Locale.Parse("en"), en.Locale);
    }

    /// <summary>
    /// The plural rules are made when first read and kept: two threads that read them first at once both get the one stored.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Tables whose rules wait, when made, until two threads are making them.
    ///
    /// Steps:
    /// - Read the rules on two threads at once; read them again.
    ///
    /// Expected:
    /// - The rules were made twice, and the three reads return the same instance.
    /// </remarks>
    [Fact]
    public void Plurals_ReadFirstOnTwoThreads_KeepsOne()
    {
        var data = new WaitingLocaleData();
        var results = new PluralRules?[2];
        Thread[] threads = [new(() => results[0] = data.Plurals), new(() => results[1] = data.Plurals)];

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        // A watchdog, not a timing: each thread ends as soon as both have made the rules.
        foreach (Thread thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "A thread that reads the rules did not end.");
        }

        Assert.Equal(2, data.Made);
        Assert.NotNull(results[0]);
        Assert.Same(results[0], results[1]);
        Assert.Same(results[0], data.Plurals);
    }

    private static (DayOfWeek First, int MinimalDays, DayOfWeek WeekendStart, DayOfWeek WeekendEnd) Week(LocaleData data) =>
        (data.FirstDayOfWeek, data.MinimalDaysInFirstWeek, data.WeekendStart, data.WeekendEnd);

    private static LocaleData Entry(string tag) => CldrLocales.All.Single(data => data.Locale == Locale.Parse(tag));

    // Tables with a week of the test's choosing, and other alone for every value.
    private sealed class WeekLocaleData(DayOfWeek firstDay, int minimalDays)
        : LocaleData(Locale.Parse("en"), firstDay, minimalDays, DayOfWeek.Saturday, DayOfWeek.Sunday)
    {
        internal override PluralRules CreatePlurals() => new(Other, Other);

        private static PluralCategory Other(in PluralOperands operands) => PluralCategory.Other;
    }

    // Tables whose rules, when made, wait until a second thread is making them too, so that both reach the store. A thread that
    // waits in vain gives up after 30 seconds, and the count of 1 then fails the test.
    private sealed class WaitingLocaleData() : LocaleData(Locale.Parse("en"), DayOfWeek.Sunday, 1, DayOfWeek.Saturday, DayOfWeek.Sunday)
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _made;

        public int Made => Volatile.Read(ref _made);

        internal override PluralRules CreatePlurals()
        {
            if (Interlocked.Increment(ref _made) == 2)
            {
                _both.SetResult();
            }

            _ = _both.Task.Wait(TimeSpan.FromSeconds(30));
            return new PluralRules(Other, Other);
        }

        private static PluralCategory Other(in PluralOperands operands) => PluralCategory.Other;
    }
}
