using System.Globalization;

namespace XylemUI.Tests;

// The number formats of LC2 (design §4.11, "Numbers", "Parsing", and "Allocation and threads"): decimal, percent, currency, and
// compact, with the symbols, the digits, and the grouping of each locale's default numbering system, rounding half to even, the
// options, the currency symbols and digits, parsing with full-width digits, and no allocation. The expected values were read from the
// json of cldr-49.0.0-json-full.zip (numbers.json, currencies.json, currencyData.json), not from the generated code, and written by
// hand with UTS #35 part 3. In the expected text, _ is U+00A0, ^ is U+202F, | is U+200E, ! is U+200F, and @ is U+061C.
public sealed class NumberFormatterTests
{
    private static readonly Dictionary<string, LocaleData> ByTag = CldrLocales.All.ToDictionary(
        data => data.Locale.ToLanguageTag(),
        StringComparer.Ordinal
    );

    /// <summary>
    /// A decimal format writes the locale's separators, digits, and minus sign, and groups as its pattern and its
    /// minimumGroupingDigits say (design §4.11, "Numbers").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: en, ja, de, fr (U+202F groups), tr; hi and mr (#,##,##0.###, mr in Devanagari digits; hi with three and four
    ///   groups, the secondary group of 2 repeated); es-419 and ar (a minus
    ///   with U+200E); ar-EG (Arabic-Indic digits, U+066B and U+066C, a minus with U+061C); es and pl (minimumGroupingDigits 2, pl's
    ///   group U+00A0); de-AT (group U+00A0) and fr-CH (group '); zero, trailing zeros, and decimal.MaxValue.
    ///
    /// Steps:
    /// - Write each value with the locale's decimal format (#,##0.### or #,##,##0.###: up to 3 fraction digits).
    ///
    /// Expected:
    /// - The text of each case.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="value">The number, in invariant text.</param>
    /// <param name="expected">The text, with the marks of the class comment.</param>
    [Theory]
    [InlineData("en", "1234567.891", "1,234,567.891")]
    [InlineData("ja", "1234567.891", "1,234,567.891")]
    [InlineData("de", "1234567.891", "1.234.567,891")]
    [InlineData("fr", "1234567.891", "1^234^567,891")]
    [InlineData("tr", "-1234.5", "-1.234,5")]
    [InlineData("hi", "1234567.891", "12,34,567.891")]
    [InlineData("hi", "12345678", "1,23,45,678")]
    [InlineData("hi", "123456789", "12,34,56,789")]
    [InlineData("mr", "1234567.5", "१२,३४,५६७.५")]
    [InlineData("es-419", "-1234.5", "-1,234.5")]
    [InlineData("ar", "-1234.5", "|-1,234.5")]
    [InlineData("ar-EG", "-1234.5", "@-١٬٢٣٤٫٥")]
    [InlineData("es", "1234", "1234")]
    [InlineData("es", "12345", "12.345")]
    [InlineData("pl", "1234", "1234")]
    [InlineData("pl", "12345", "12_345")]
    [InlineData("de-AT", "1234.5", "1_234,5")]
    [InlineData("fr-CH", "1234.5", "1'234,5")]
    [InlineData("en", "0", "0")]
    [InlineData("en", "1.500", "1.5")]
    [InlineData("en", "79228162514264337593543950335", "79,228,162,514,264,337,593,543,950,335")]
    public void Decimal_Locales_WriteTheirSymbolsDigitsAndGroups(string tag, string value, string expected)
    {
        Assert.Equal(Marked(expected), NumberFormatter.Decimal(ByTag[tag]).Format(Parse(value)));
    }

    /// <summary>
    /// A long and a double write as the decimal of the same value does, and the infinities and not a number take the locale's
    /// symbols with the pattern's affixes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal, percent, currency (USD), and compact formats; ar's decimal format.
    ///
    /// Steps:
    /// - Write -1234567 as a long and 1234.5 as a double; NaN and the infinities as doubles.
    ///
    /// Expected:
    /// - -1,234,567 and 1,234.5; NaN, ∞, -∞; NaN% and -∞%; -$∞ (no space: ∞ is not a digit); ∞ in the compact format; ar's NaN is
    ///   ليس_رقمًا.
    /// </remarks>
    [Fact]
    public void TryFormat_LongDoubleAndNonFinite_WriteTheLocalesText()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter number = NumberFormatter.Decimal(en);
        NumberFormatter percent = NumberFormatter.Percent(en);

        Assert.Equal("-1,234,567", Write(number, -1_234_567L));
        Assert.Equal("1,234.5", Write(number, 1234.5));
        Assert.Equal("NaN", Write(number, double.NaN));
        Assert.Equal("∞", Write(number, double.PositiveInfinity));
        Assert.Equal("-∞", Write(number, double.NegativeInfinity));
        Assert.Equal("NaN%", Write(percent, double.NaN));
        Assert.Equal("-∞%", Write(percent, double.NegativeInfinity));
        Assert.Equal("-$∞", Write(NumberFormatter.Currency(en, "USD"), double.NegativeInfinity));
        Assert.Equal("∞", Write(NumberFormatter.Compact(en, CompactStyle.Short), double.PositiveInfinity));
        Assert.Equal(Marked("ليس_رقمًا"), Write(NumberFormatter.Decimal(ByTag["ar"]), double.NaN));
    }

    /// <summary>
    /// The ulong overloads of TryFormat, Of, and Format write a ulong as the decimal of the same value, and Format takes a long, a
    /// ulong, a decimal, and a double, so that a ulong and an int argument each have one overload (Issue #906, after PR #944's
    /// review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format.
    ///
    /// Steps:
    /// - Write ulong.MaxValue with TryFormat, with ToString of Of, and with Format; format -1234567 as a long, 7 as an int, and 1234.5
    ///   as a double.
    ///
    /// Expected:
    /// - 18,446,744,073,709,551,615 three times; -1,234,567, 7, and 1,234.5.
    /// </remarks>
    [Fact]
    public void Overloads_UlongLongAndDouble_WriteAsTheDecimalOfTheValue()
    {
        NumberFormatter format = CldrLocales.En.DecimalFormat;
        const string Largest = "18,446,744,073,709,551,615";

        Assert.Equal(Largest, Write(format, ulong.MaxValue));
        Assert.Equal(Largest, format.Of(ulong.MaxValue).ToString());
        Assert.Equal(Largest, format.Format(ulong.MaxValue));
        Assert.Equal("-1,234,567", format.Format(-1_234_567L));
        Assert.Equal("7", format.Format(7));
        Assert.Equal("1,234.5", format.Format(1234.5));
    }

    /// <summary>
    /// A value is rounded half to even at the most fraction digits, a decimal with decimal.Round and a double on its shortest
    /// round-trip digits, not with a format string (design §4.11, "How the digits are made").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format with at most 2 fraction digits, with at most 0, with at most 20, and with the pattern's 3.
    ///
    /// Steps:
    /// - Write 0.125 and 0.135 as decimals and doubles, -0.125, 2.5 and 3.5 with 0 digits, 1.0005 and 1.0015 with 3, 0.1 as a double
    ///   with 20; round 0.125 with 2 digits.
    ///
    /// Expected:
    /// - 0.12 and 0.14 both ways (the invariant "F2" writes 0.13 for 0.125), -0.12, 2 and 4, 1 and 1.002, 0.1 (the shortest digits
    ///   of the double, not its binary expansion); Round gives 0.12 with a scale of 2.
    /// </remarks>
    [Fact]
    public void TryFormat_Midpoint_RoundsHalfToEven()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter two = NumberFormatter.Decimal(en, new NumberOptions { MaximumFractionDigits = 2 });
        NumberFormatter none = NumberFormatter.Decimal(en, new NumberOptions { MaximumFractionDigits = 0 });
        NumberFormatter many = NumberFormatter.Decimal(en, new NumberOptions { MaximumFractionDigits = 20 });

        Assert.Equal("0.13", 0.125m.ToString("F2", CultureInfo.InvariantCulture));
        Assert.Equal("0.12", two.Format(0.125m));
        Assert.Equal("0.14", two.Format(0.135m));
        Assert.Equal("0.12", Write(two, 0.125));
        Assert.Equal("0.14", Write(two, 0.135));
        Assert.Equal("-0.12", two.Format(-0.125m));
        Assert.Equal("2", none.Format(2.5m));
        Assert.Equal("4", none.Format(3.5m));
        Assert.Equal("1", en.DecimalFormat.Format(1.0005m));
        Assert.Equal("1.002", en.DecimalFormat.Format(1.0015m));
        Assert.Equal("0.1", Write(many, 0.1));
        decimal rounded = two.Round(0.125m);
        Assert.Equal(0.12m, rounded);
        Assert.Equal(2, rounded.Scale);
    }

    /// <summary>
    /// A decimal is rounded at no more than the 28 fraction digits decimal.Round takes, counted before a percent moves the point by 2,
    /// so that more fraction digits than that write the decimal's own digits and do not throw.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format with at most 40 fraction digits, and en's percent format with at most 28 (28 + 2 digits before the point
    ///   moves).
    ///
    /// Steps:
    /// - Write and round 0.1234567890123456789012345678 (28 fraction digits) with each.
    ///
    /// Expected:
    /// - 0.1234567890123456789012345678 and 12.34567890123456789012345678%; Round gives the value and 12.34567890123456789012345678.
    /// </remarks>
    [Fact]
    public void TryFormat_FractionDigitsPastDecimalRound_RoundAtTwentyEight()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter number = NumberFormatter.Decimal(en, new NumberOptions { MaximumFractionDigits = 40 });
        NumberFormatter percent = NumberFormatter.Percent(en, new NumberOptions { MaximumFractionDigits = 28 });
        const decimal Value = 0.1234567890123456789012345678m;

        Assert.Equal("0.1234567890123456789012345678", number.Format(Value));
        Assert.Equal("12.34567890123456789012345678%", percent.Format(Value));
        Assert.Equal(Value, number.Round(Value));
        Assert.Equal(12.34567890123456789012345678m, percent.Round(Value));
    }

    /// <summary>
    /// A double is rounded on the digits of its shortest round-trip text at any number of fraction digits, with no border at 15 or
    /// 16 digits (design §4.11, "Rounding a double"; Issue #906, after PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format with at most 2, 15, 16, and 17 fraction digits.
    ///
    /// Steps:
    /// - Write 1.0000000000000002 (1 + 2^-52) with 15 and 16 digits, 0.1 + 0.2 (0.30000000000000004) with 16 and 17, and 1.015 and
    ///   2.675 with 2 and with 15 digits.
    ///
    /// Expected:
    /// - 1 and 1.0000000000000002; 0.3 and 0.30000000000000004; 1.02 and 2.68 with 2 digits (the binary values of both lie below
    ///   the midpoints, which a rounding of the binary value reads as 1.01 and 2.67), and 1.015 and 2.675 with 15.
    /// </remarks>
    [Fact]
    public void TryFormat_DoubleAtAnyFractionDigits_RoundsTheShortestDigits()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter two = NumberFormatter.Decimal(en, new NumberOptions { MaximumFractionDigits = 2 });
        NumberFormatter fifteen = NumberFormatter.Decimal(en, new NumberOptions { MaximumFractionDigits = 15 });
        NumberFormatter sixteen = NumberFormatter.Decimal(en, new NumberOptions { MaximumFractionDigits = 16 });
        NumberFormatter seventeen = NumberFormatter.Decimal(en, new NumberOptions { MaximumFractionDigits = 17 });
        double justAboveOne = 1 + Math.Pow(2, -52);
        double sum = 0.1 + 0.2;

        Assert.Equal("1", Write(fifteen, justAboveOne));
        Assert.Equal("1.0000000000000002", Write(sixteen, justAboveOne));
        Assert.Equal("0.3", Write(sixteen, sum));
        Assert.Equal("0.30000000000000004", Write(seventeen, sum));
        Assert.Equal(("1.02", "2.68"), (Write(two, 1.015), Write(two, 2.675)));
        Assert.Equal(("1.015", "2.675"), (Write(fifteen, 1.015), Write(fifteen, 2.675)));
    }

    /// <summary>
    /// A double writes as the decimal of the same literal does, in a decimal, a currency, and a percent format: the shortest digits
    /// are rounded as decimal digits, and a percent moves their point (Issue #906, after PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's USD format (2 fraction digits), en's percent format (0 fraction digits), and en's decimal format (3 fraction digits).
    ///
    /// Steps:
    /// - Write each literal as a double and as a decimal.
    ///
    /// Expected:
    /// - Both are the text of the case: USD 1.015 is $1.02, 4.525 is $4.52, 2.675 is $2.68; the percent of 4.525 is 452% (452.5 to
    ///   even), of 0.125 is 12%, and of 0.135 is 14%; 1.0015 is 1.002 and 1.0005 is 1.
    /// </remarks>
    /// <param name="kind">The format: usd, percent, or decimal.</param>
    /// <param name="literal">The number, in invariant text.</param>
    /// <param name="expected">The text both write.</param>
    [Theory]
    [InlineData("usd", "1.015", "$1.02")]
    [InlineData("usd", "4.525", "$4.52")]
    [InlineData("usd", "2.675", "$2.68")]
    [InlineData("percent", "4.525", "452%")]
    [InlineData("percent", "0.125", "12%")]
    [InlineData("percent", "0.135", "14%")]
    [InlineData("decimal", "1.0015", "1.002")]
    [InlineData("decimal", "1.0005", "1")]
    public void TryFormat_DoubleAndDecimalOfOneLiteral_WriteTheSameText(string kind, string literal, string expected)
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter format = kind switch
        {
            "usd" => NumberFormatter.Currency(en, "USD"),
            "percent" => en.PercentFormat,
            _ => en.DecimalFormat,
        };
        double number = double.Parse(literal, CultureInfo.InvariantCulture);

        Assert.Equal(expected, format.Format(Parse(literal)));
        Assert.Equal(expected, Write(format, number));
        Assert.Equal(expected, format.Format(number));
    }

    /// <summary>
    /// The options replace the pattern's digits and grouping; a fraction bound given alone moves the pattern's other bound past it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format with each option alone, and en's USD format with at most 1 fraction digit (the currency's 2 lowered).
    ///
    /// Steps:
    /// - Write 7 with 3 and with 5 integer digits, 1.5 with at least 2 fraction digits, 1.123456 with at least 5 (the pattern's
    ///   most of 3 raised), 1.25 in USD, and 1234567 without grouping; round 1.5 with at least 2 fraction digits.
    ///
    /// Expected:
    /// - 007, 00,007 (padding zeros are grouped), 1.50, 1.12346, $1.2, 1234567; Round gives 1.50 with a scale of 2.
    /// </remarks>
    [Fact]
    public void Options_DigitsAndGrouping_ReplaceThePatterns()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter twoFraction = NumberFormatter.Decimal(en, new NumberOptions { MinimumFractionDigits = 2 });

        Assert.Equal("007", NumberFormatter.Decimal(en, new NumberOptions { MinimumIntegerDigits = 3 }).Format(7));
        Assert.Equal("00,007", NumberFormatter.Decimal(en, new NumberOptions { MinimumIntegerDigits = 5 }).Format(7));
        Assert.Equal("1.50", twoFraction.Format(1.5m));
        Assert.Equal("1.12346", NumberFormatter.Decimal(en, new NumberOptions { MinimumFractionDigits = 5 }).Format(1.123456m));
        Assert.Equal("$1.2", NumberFormatter.Currency(en, "USD", new NumberOptions { MaximumFractionDigits = 1 }).Format(1.25m));
        Assert.Equal("1234567", NumberFormatter.Decimal(en, new NumberOptions { UseGrouping = false }).Format(1234567));
        Assert.Equal(2, twoFraction.Round(1.5m).Scale);
    }

    /// <summary>
    /// Options with a negative number of fraction digits, no integer digit, more than 999 integer or fraction digits, or more
    /// minimum than maximum fraction digits throw when a format is made (design §4.11, "Numbers"; Issue #906, after PR #944's
    /// review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables.
    ///
    /// Steps:
    /// - Make a decimal, a percent, and a currency format with MinimumIntegerDigits 0, MinimumFractionDigits -1,
    ///   MaximumFractionDigits -1, 3 and 2 minimum and maximum fraction digits, MinimumIntegerDigits 1000, MinimumFractionDigits
    ///   1000, and MaximumFractionDigits 1000 (each of the last three alone).
    ///
    /// Expected:
    /// - Each throws ArgumentOutOfRangeException for the parameter options; 2 and 2 do not throw.
    /// </remarks>
    [Fact]
    public void Options_OutOfRange_Throw()
    {
        LocaleData en = CldrLocales.En;
        NumberOptions[] invalid =
        [
            new() { MinimumIntegerDigits = 0 },
            new() { MinimumFractionDigits = -1 },
            new() { MaximumFractionDigits = -1 },
            new() { MinimumFractionDigits = 3, MaximumFractionDigits = 2 },
            new() { MinimumIntegerDigits = 1000 },
            new() { MinimumFractionDigits = 1000 },
            new() { MaximumFractionDigits = 1000 },
        ];

        foreach (NumberOptions options in invalid)
        {
            ArgumentOutOfRangeException number = Assert.Throws<ArgumentOutOfRangeException>(() => NumberFormatter.Decimal(en, options));
            ArgumentOutOfRangeException percent = Assert.Throws<ArgumentOutOfRangeException>(() => NumberFormatter.Percent(en, options));
            ArgumentOutOfRangeException currency = Assert.Throws<ArgumentOutOfRangeException>(() => NumberFormatter.Currency(en, "USD", options));
            Assert.Equal(("options", "options", "options"), (number.ParamName, percent.ParamName, currency.ParamName));
        }

        var same = new NumberOptions { MinimumFractionDigits = 2, MaximumFractionDigits = 2 };
        Assert.Equal("2.00", NumberFormatter.Decimal(en, same).Format(2));
    }

    /// <summary>
    /// The options take up to 999 integer and 999 fraction digits, ICU's bound, and a format with them writes every digit through
    /// TryFormat and ToString, as a decimal and as a double (Issue #906, after PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format with 999 minimum integer digits and 999 minimum and maximum fraction digits, without grouping and with
    ///   it; en's percent format with 999 maximum fraction digits.
    ///
    /// Steps:
    /// - Format 7 as a decimal and as a double; write the double with TryFormat into 1,999 characters and the decimal into 1,998;
    ///   format 7 with grouping; format 5e-324 (the smallest double) with the percent format.
    ///
    /// Expected:
    /// - 998 zeros, 7, a point, and 999 zeros (1,999 characters) each way; TryFormat is true for 1,999 characters and false with 0
    ///   written for 1,998; with grouping, 332 more characters (the 999 integer digits in 333 groups); the percent is 0, a point, 321
    ///   zeros, 5, and the sign (the 324 fraction digits of 5e-324 less the 2 the percent moves).
    /// </remarks>
    [Fact]
    public void Options_NineHundredNinetyNineDigits_WriteEveryDigit()
    {
        LocaleData en = CldrLocales.En;
        var most = new NumberOptions
        {
            MinimumIntegerDigits = 999,
            MinimumFractionDigits = 999,
            MaximumFractionDigits = 999,
            UseGrouping = false,
        };
        NumberFormatter number = NumberFormatter.Decimal(en, most);
        NumberFormatter grouped = NumberFormatter.Decimal(en, new NumberOptions { MinimumIntegerDigits = 999, MinimumFractionDigits = 999 });
        NumberFormatter percent = NumberFormatter.Percent(en, new NumberOptions { MaximumFractionDigits = 999 });
        string expected = new string('0', 998) + "7." + new string('0', 999);
        var buffer = new char[1999];

        Assert.Equal(expected, number.Format(7m));
        Assert.Equal(expected, number.Format(7.0));
        Assert.True(number.TryFormat(7.0, buffer, out int written));
        Assert.Equal(1999, written);
        Assert.False(number.TryFormat(7m, buffer.AsSpan(0, 1998), out int shortWritten));
        Assert.Equal(0, shortWritten);
        Assert.Equal(1999 + 332, grouped.Format(7m).Length);
        Assert.Equal("0." + new string('0', 321) + "5%", percent.Format(5e-324));
    }

    /// <summary>
    /// A percent format multiplies by 100, rounds half to even, and writes the locale's percent sign where its pattern puts it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: en; de and fr (#,##0_%); tr (%#,##0) and eu (%_#,##0, minus U+2212); ar (%, with U+200E on each side);
    ///   ar-EG (Arabic-Indic digits and ٪ with U+061C); hi (#,##,##0%).
    ///
    /// Steps:
    /// - Write each value with the locale's percent format (no fraction digit).
    ///
    /// Expected:
    /// - The text of each case: 0.125 is 12% and 0.135 is 14%; zero, and a value that rounds to zero, is one zero digit.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="value">The number, in invariant text.</param>
    /// <param name="expected">The text, with the marks of the class comment.</param>
    [Theory]
    [InlineData("en", "0.256", "26%")]
    [InlineData("en", "0.125", "12%")]
    [InlineData("en", "0.135", "14%")]
    [InlineData("en", "-0.25", "-25%")]
    [InlineData("en", "1", "100%")]
    [InlineData("en", "0", "0%")]
    [InlineData("en", "0.001", "0%")]
    [InlineData("en", "0.004", "0%")]
    [InlineData("de", "0", "0_%")]
    [InlineData("tr", "0", "%0")]
    [InlineData("de", "0.25", "25_%")]
    [InlineData("fr", "0.25", "25_%")]
    [InlineData("tr", "0.25", "%25")]
    [InlineData("tr", "-0.25", "-%25")]
    [InlineData("eu", "-0.25", "−%_25")]
    [InlineData("ar", "0.25", "25|%|")]
    [InlineData("ar-EG", "0.25", "٢٥٪@")]
    [InlineData("hi", "12345.67", "12,34,567%")]
    public void Percent_Locales_WriteTheirPercentSign(string tag, string value, string expected)
    {
        Assert.Equal(Marked(expected), ByTag[tag].PercentFormat.Format(Parse(value)));
    }

    /// <summary>
    /// Zero, and a value that rounds to zero, writes the same text through the decimal and the double paths of a percent format
    /// (PR #944's review: the decimal path wrote "00%").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The percent format of en.
    ///
    /// Steps:
    /// - Write 0, 0.001, and 0.004 as a decimal and as a double, with <c>Of</c>.
    ///
    /// Expected:
    /// - Each is "0%" on both paths.
    /// </remarks>
    [Fact]
    public void Percent_ZeroAndValuesThatRoundToZero_AreOneDigitOnBothPaths()
    {
        NumberFormatter percent = ByTag["en"].PercentFormat;
        string[] written =
        [
            percent.Of(0m).ToString(),
            percent.Of(0.0).ToString(),
            percent.Of(0.001m).ToString(),
            percent.Of(0.001).ToString(),
            percent.Of(0.004m).ToString(),
            percent.Of(0.004).ToString(),
        ];

        Assert.All(written, text => Assert.Equal("0%", text));
    }

    /// <summary>
    /// A currency format writes the currency's standard or narrow symbol in the locale, the currency's fraction digits, the
    /// locale's currency separators, and, for a symbol whose grapheme cluster next to the digits starts with a letter, the locale's
    /// alphaNextToNumber pattern (Issue #906; PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: en in USD, JPY (0 digits), CAD (CA$, narrow $), CHF (its code as the symbol), BHD (3 digits), and ZZZ (no symbol,
    ///   the default 2 digits); ja in JPY (￥); de in EUR; fr in USD ($US); es-419 in USD (its code); hi in INR; de-CH in CHF with its
    ///   negative subpattern; de-AT (currencyGroup .); fr-CH (currencyDecimal .); ar-EG in EGP (U+200F around the digits and in the
    ///   symbol); kn in RON (ಲೀ, whose last code point is the vowel sign U+0CC0, a mark, and whose last grapheme cluster starts with
    ///   the letter U+0CB2).
    ///
    /// Steps:
    /// - Write each value with the locale's currency format of the code, with the narrow symbol when the case says so.
    ///
    /// Expected:
    /// - The text of each case.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="code">The ISO 4217 code.</param>
    /// <param name="narrow">Whether the narrow symbol is shown.</param>
    /// <param name="value">The number, in invariant text.</param>
    /// <param name="expected">The text, with the marks of the class comment.</param>
    [Theory]
    [InlineData("en", "USD", false, "1234.5", "$1,234.50")]
    [InlineData("en", "USD", false, "-1234.5", "-$1,234.50")]
    [InlineData("en", "JPY", false, "1234.5", "¥1,234")]
    [InlineData("en", "JPY", false, "1235.5", "¥1,236")]
    [InlineData("en", "CAD", false, "1234.5", "CA$1,234.50")]
    [InlineData("en", "CAD", true, "1234.5", "$1,234.50")]
    [InlineData("en", "CHF", false, "1234.5", "CHF_1,234.50")]
    [InlineData("en", "BHD", false, "1.2345", "BHD_1.234")]
    [InlineData("en", "ZZZ", true, "1", "ZZZ_1.00")]
    [InlineData("ja", "JPY", false, "1234", "￥1,234")]
    [InlineData("de", "EUR", false, "1234.5", "1.234,50_€")]
    [InlineData("fr", "USD", false, "1234.5", "1^234,50_$US")]
    [InlineData("es-419", "USD", false, "1234.5", "USD_1,234.50")]
    [InlineData("hi", "INR", false, "1234567.5", "₹12,34,567.50")]
    [InlineData("de-CH", "CHF", false, "1234.5", "CHF_1'234.50")]
    [InlineData("de-CH", "CHF", false, "-1234.5", "CHF-1'234.50")]
    [InlineData("de-AT", "EUR", false, "1234.5", "€_1.234,50")]
    [InlineData("fr-CH", "CHF", false, "1234.5", "1'234.50_CHF")]
    [InlineData("ar-EG", "EGP", false, "1234.5", "!١٬٢٣٤٫٥٠_ج.م.!")]
    [InlineData("kn", "RON", true, "1234.5", "ಲೀ_1,234.50")]
    public void Currency_Locales_WriteTheSymbolAndTheCurrencysDigits(string tag, string code, bool narrow, string value, string expected)
    {
        NumberFormatter format = NumberFormatter.Currency(ByTag[tag], code, new NumberOptions { NarrowCurrencySymbol = narrow });

        Assert.Equal(Marked(expected), format.Format(Parse(value)));
    }

    /// <summary>
    /// Root's tables write a currency with root's pattern and symbols.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - LocaleData.Root, whose currency pattern is ¤_#,##0.00 and whose USD symbol is US$.
    ///
    /// Steps:
    /// - Write 1234.5 in USD, and 1234.5 with root's decimal format.
    ///
    /// Expected:
    /// - US$_1,234.50 and 1,234.5.
    /// </remarks>
    [Fact]
    public void Currency_Root_WritesRootsPattern()
    {
        Assert.Equal(Marked("US$_1,234.50"), NumberFormatter.Currency(LocaleData.Root, "USD").Format(1234.5m));
        Assert.Equal("1,234.5", LocaleData.Root.DecimalFormat.Format(1234.5m));
    }

    /// <summary>
    /// A currency code that is not three upper-case ASCII letters throws when the format is made (design §4.11, "Numbers").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables.
    ///
    /// Steps:
    /// - Make a currency format with usd, US, USDX, U1D, an empty code, and null.
    ///
    /// Expected:
    /// - ArgumentException for the parameter isoCode, and ArgumentNullException for null.
    /// </remarks>
    [Fact]
    public void Currency_CodeNotThreeUpperCaseLetters_Throws()
    {
        foreach (string code in new[] { "usd", "US", "USDX", "U1D", string.Empty })
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => NumberFormatter.Currency(CldrLocales.En, code));
            Assert.Equal("isoCode", exception.ParamName);
        }

        Assert.Throws<ArgumentNullException>(() => NumberFormatter.Currency(CldrLocales.En, null!));
    }

    /// <summary>
    /// A short compact format chooses the pattern of the number's power of ten, keeps two significant digits or the whole integer
    /// part, carries a rounding into the next power, writes in full what the locale does not compact, and groups only from 5
    /// integer digits.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: en (0K, 00K, 000K, 0M, ..., 000T); ja (0 for 1000, 0万, 0億); de (0 for 1000 to 100000, 0_Mio'.'); hi (00_लाख
    ///   for 1000000); fr (0_k).
    ///
    /// Steps:
    /// - Write each value with the locale's short compact format.
    ///
    /// Expected:
    /// - The text of each case: 1250 is 1.2K and 1350 is 1.4K (half to even), 999999 is 1M and 999.6 is 1K (the rounding carries),
    ///   1234567890123456 is 1235T (the largest pattern, not grouped), 0.0123 is 0.012, ja's 1234 is 1234 and de's 12345 is 12.345.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="value">The number, in invariant text.</param>
    /// <param name="expected">The text, with the marks of the class comment.</param>
    [Theory]
    [InlineData("en", "1234", "1.2K")]
    [InlineData("en", "12345", "12K")]
    [InlineData("en", "123456", "123K")]
    [InlineData("en", "1234567", "1.2M")]
    [InlineData("en", "999", "999")]
    [InlineData("en", "999999", "1M")]
    [InlineData("en", "999.6", "1K")]
    [InlineData("en", "1250", "1.2K")]
    [InlineData("en", "1350", "1.4K")]
    [InlineData("en", "-1234", "-1.2K")]
    [InlineData("en", "0", "0")]
    [InlineData("en", "1.234", "1.2")]
    [InlineData("en", "0.0123", "0.012")]
    [InlineData("en", "1234567890123456", "1235T")]
    [InlineData("ja", "1234", "1234")]
    [InlineData("ja", "12345", "1.2万")]
    [InlineData("ja", "123456789", "1.2億")]
    [InlineData("de", "12345", "12.345")]
    [InlineData("de", "1234567", "1,2_Mio.")]
    [InlineData("hi", "1234567", "12_लाख")]
    [InlineData("fr", "1500", "1,5_k")]
    public void Compact_Short_ChoosesThePatternAndTheDigits(string tag, string value, string expected)
    {
        NumberFormatter format = NumberFormatter.Compact(ByTag[tag], CompactStyle.Short);

        Assert.Equal(Marked(expected), format.Format(Parse(value)));
        Assert.Equal(Marked(expected), Write(format, (double)Parse(value)));
    }

    /// <summary>
    /// A long compact format chooses among the patterns of a power of ten by the plural category of the shown number, with the
    /// compact exponent c = 0, and takes the pattern of 1 alone when the shown number is the whole number 1, as ICU's
    /// CompactHandler::processQuantity does (design §4.11, "Choosing a compact pattern"; Issue #906, after PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: en; fr (1000-count-1 mille, one 0 millier, other 0 mille; one 0 million, other 0 millions); pl (one 0 tysiąc,
    ///   few 0 tysiące, many 0 tysięcy, other 0 tysiąca); ru (few 0 тысячи and 0 миллиона, other 0 тысячи); the entry tl, which
    ///   holds fil's tables (one 0 libo, other 0 na libo).
    ///
    /// Steps:
    /// - Write each value with the locale's long compact format.
    ///
    /// Expected:
    /// - The text of each case: fr's 1000 is mille (the shown 1 has a pattern of its own), 1500 is 1,5 millier (1.5 is one in
    ///   French), 1000000 is 1 million (one), and 2000000 is 2 millions; pl's 1000 is 1 tysiąc (one), 2000 is 2 tysiące (few), 5000
    ///   is 5 tysięcy (many), and 1500 is 1,5 tysiąca (other, a fraction digit); ru's 2000000 is 2 миллиона (few) and 1234 is
    ///   1,2 тысячи (other); tl's 1004 is 1 libo (shown as 1), 4000 is 4 na libo (4 is other in Filipino), and 5000 is 5 libo
    ///   (one).
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="value">The number, in invariant text.</param>
    /// <param name="expected">The text.</param>
    [Theory]
    [InlineData("en", "1234", "1.2 thousand")]
    [InlineData("en", "1000000", "1 million")]
    [InlineData("fr", "1000", "mille")]
    [InlineData("fr", "1500", "1,5 millier")]
    [InlineData("fr", "2000", "2 mille")]
    [InlineData("fr", "1000000", "1 million")]
    [InlineData("fr", "2000000", "2 millions")]
    [InlineData("pl", "1000", "1 tysiąc")]
    [InlineData("pl", "2000", "2 tysiące")]
    [InlineData("pl", "5000", "5 tysięcy")]
    [InlineData("pl", "1500", "1,5 tysiąca")]
    [InlineData("ru", "2000000", "2 миллиона")]
    [InlineData("ru", "1234", "1,2 тысячи")]
    [InlineData("tl", "1000", "1 libo")]
    [InlineData("tl", "1004", "1 libo")]
    [InlineData("tl", "4000", "4 na libo")]
    [InlineData("tl", "5000", "5 libo")]
    public void Compact_Long_ChoosesThePatternByTheShownNumber(string tag, string value, string expected)
    {
        Assert.Equal(expected, NumberFormatter.Compact(ByTag[tag], CompactStyle.Long).Format(Parse(value)));
    }

    /// <summary>
    /// The compact format selects the plural category with the compact exponent c = 0, as ICU does, which sets the exponent only
    /// after it selects (Issue #906, after PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Tables with pl's numbers and compact patterns (many 0 tysięcy, other 0 tysiąca) and plural rules that select many when c is
    ///   3 and other otherwise: no CLDR 49 locale has a compact many pattern and rules that read c.
    ///
    /// Steps:
    /// - Write 2000 and 2 with the long compact format.
    ///
    /// Expected:
    /// - 2 tysiąca (c = 0 is other; c = 3 would be many, 2 tysięcy), and 2 written in full.
    /// </remarks>
    [Fact]
    public void Compact_PluralRulesReadingC_GetZero()
    {
        NumberFormatter format = NumberFormatter.Compact(new ExponentLocaleData(), CompactStyle.Long);

        Assert.Equal("2 tysiąca", format.Format(2000));
        Assert.Equal("2", format.Format(2));
    }

    /// <summary>
    /// A currency symbol whose character next to the digits is not a letter takes the standard pattern even where currencySpacing
    /// would add a space; a locale with no alphaNextToNumber pattern applies currencySpacing (UTS #35 part 3, "Currency Formats";
    /// Issue #906).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's tables (¤#,##0.00, and ¤_#,##0.00 for a letter), and en's tables with the alphaNextToNumber pattern taken out of the
    ///   number table (no CLDR 49 locale lacks it and puts the sign next to the digits).
    ///
    /// Steps:
    /// - Write 1234.5 in XCG (symbol Cg., ending in a full stop) and in CHF (its code) with both.
    ///
    /// Expected:
    /// - en: Cg.1,234.50 and CHF_1,234.50 (the letter F takes the alphaNextToNumber pattern).
    /// - Without the pattern: Cg._1,234.50 (a full stop is neither a symbol nor a separator) and CHF_1,234.50, both by
    ///   currencySpacing.
    /// </remarks>
    [Fact]
    public void Currency_SymbolNextToTheDigits_TakesAlphaPatternOrCurrencySpacing()
    {
        var withoutAlpha = new NoAlphaLocaleData();

        Assert.Equal("Cg.1,234.50", NumberFormatter.Currency(CldrLocales.En, "XCG").Format(1234.5m));
        Assert.Equal(Marked("CHF_1,234.50"), NumberFormatter.Currency(CldrLocales.En, "CHF").Format(1234.5m));
        Assert.Equal(Marked("Cg._1,234.50"), NumberFormatter.Currency(withoutAlpha, "XCG").Format(1234.5m));
        Assert.Equal(Marked("CHF_1,234.50"), NumberFormatter.Currency(withoutAlpha, "CHF").Format(1234.5m));
    }

    /// <summary>
    /// A negative number that rounds to zero keeps the minus sign, and the double -0.0 is written as zero, as a decimal's zero is
    /// (Issue #906; the double, after PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format (3 fraction digits), percent format, and short compact format.
    ///
    /// Steps:
    /// - Write -0.0001 as a decimal and as a double; write -0.0 as a double with each format, and -0.0m as a decimal.
    ///
    /// Expected:
    /// - -0 for both -0.0001; 0, 0%, and 0 for the double -0.0, and 0 for the decimal -0.0m.
    /// </remarks>
    [Fact]
    public void TryFormat_NegativeZero_IsZeroAndANegativeThatRoundsToZeroKeepsTheMinus()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter compact = NumberFormatter.Compact(en, CompactStyle.Short);

        Assert.Equal("-0", en.DecimalFormat.Format(-0.0001m));
        Assert.Equal("-0", Write(en.DecimalFormat, -0.0001));
        Assert.Equal("0", Write(en.DecimalFormat, -0.0));
        Assert.Equal("0%", Write(en.PercentFormat, -0.0));
        Assert.Equal("0", Write(compact, -0.0));
        Assert.Equal("0", en.DecimalFormat.Format(-0.0m));
    }

    /// <summary>
    /// Round gives the number the format shows: a percent's number before the sign, a compact number's whole value, and the
    /// fraction digits shown as the scale, so that the plural rules select what is shown (design §4.11, "Passing the number").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's percent, USD, short compact, and decimal (at least 1 fraction digit) formats.
    ///
    /// Steps:
    /// - Round 0.125 as a percent, 1.5 in USD, 1234 and -1234 as compact numbers, and 1 with a fraction digit; select en's plural
    ///   category of the last; round decimal.MaxValue as a percent.
    ///
    /// Expected:
    /// - 12; 1.50 with a scale of 2; 1200 and -1200; 1.0, which is other in English where 1 is one; OverflowException.
    /// </remarks>
    [Fact]
    public void Round_GivesTheNumberShown()
    {
        LocaleData en = CldrLocales.En;
        decimal currency = NumberFormatter.Currency(en, "USD").Round(1.5m);
        NumberFormatter compact = NumberFormatter.Compact(en, CompactStyle.Short);
        decimal one = NumberFormatter.Decimal(en, new NumberOptions { MinimumFractionDigits = 1 }).Round(1m);

        Assert.Equal(12m, en.PercentFormat.Round(0.125m));
        Assert.Equal(1.50m, currency);
        Assert.Equal(2, currency.Scale);
        Assert.Equal(1200m, compact.Round(1234m));
        Assert.Equal(-1200m, compact.Round(-1234m));
        Assert.Equal(PluralCategory.Other, en.Plurals.Select(one));
        Assert.Equal(PluralCategory.One, en.Plurals.Select(1m));
        Assert.Throws<OverflowException>(() => en.PercentFormat.Round(decimal.MaxValue));
    }

    /// <summary>
    /// A compact number that rounds up past decimal.MaxValue still writes and Round of it throws, and a shown number past the range
    /// of decimal takes its plural category from a stand-in of 28 digits with the same last seven (design §4.11, "Passing the
    /// number").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's short compact format (000T is its largest pattern, at 10^14); pl's and fr's long compact formats, whose patterns at
    ///   10^14 differ by the plural category.
    ///
    /// Steps:
    /// - Write decimal.MaxValue with en's format, and round it.
    /// - Write 5 × 10^14 as a decimal with pl's and fr's formats (the shown 500: many in Polish, other in French), and 1.2345 ×
    ///   10^50 as a double: its shown number has 39 integer digits, past decimal, and its last seven are zeros. In Polish that is many
    ///   (i % 10 = 0; a stand-in ending in 2 would be few). In French it is many, which falls back to other (a stand-in of 0 would
    ///   be one).
    ///
    /// Expected:
    /// - 79,228,162,514,264,338T (the 17 digits of 7.9228162514264337 × 10^16 rounded up); OverflowException.
    /// - Each double's text ends with the word of the locale's text of 5 × 10^14, after a space.
    /// </remarks>
    [Fact]
    public void Compact_NumberPastDecimal_WritesAndRoundThrows()
    {
        NumberFormatter en = NumberFormatter.Compact(CldrLocales.En, CompactStyle.Short);

        Assert.Equal("79,228,162,514,264,338T", en.Format(decimal.MaxValue));
        Assert.Throws<OverflowException>(() => en.Round(decimal.MaxValue));
        foreach (LocaleData data in new[] { CldrLocales.Pl, CldrLocales.Fr })
        {
            NumberFormatter format = NumberFormatter.Compact(data, CompactStyle.Long);
            string power = format.Format(500_000_000_000_000m);
            string word = power[power.LastIndexOf(' ')..];

            Assert.EndsWith(word, format.Format(1.2345e50), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A destination too short for the text gives false and no characters; one of the text's length takes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format; 1234.5 is 7 characters.
    ///
    /// Steps:
    /// - Write 1234.5 into 6 and into 7 characters, as a decimal and as a double.
    ///
    /// Expected:
    /// - false and 0 written for 6; true and 7 written for 7.
    /// </remarks>
    [Fact]
    public void TryFormat_ShortDestination_IsFalse()
    {
        NumberFormatter format = CldrLocales.En.DecimalFormat;
        var buffer = new char[7];

        Assert.False(format.TryFormat(1234.5m, buffer.AsSpan(0, 6), out int shortDecimal));
        Assert.False(format.TryFormat(1234.5, buffer.AsSpan(0, 6), out int shortDouble));
        Assert.True(format.TryFormat(1234.5m, buffer, out int written));
        Assert.Equal((0, 0, 7), (shortDecimal, shortDouble, written));
        Assert.Equal("1,234.5", new string(buffer));
    }

    /// <summary>
    /// A locale's decimal and percent formats are made when first read and kept; the tables under another tag write as their source
    /// does (design §4.11, "Allocation and threads" and "Locales that CLDR does not have").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - de's tables, and en's under the tag en-RU.
    ///
    /// Steps:
    /// - Read de's formats twice; write 1234.5 and 0.25 with them, and 1234.5 with en-RU's decimal and USD formats.
    ///
    /// Expected:
    /// - The same instance each time; 1.234,5 and 25_%; en-RU writes 1,234.5 and $1,234.50, as en does.
    /// </remarks>
    [Fact]
    public void DecimalFormat_ReadTwice_IsOneInstance()
    {
        LocaleData de = CldrLocales.De;
        LocaleData russia = CldrLocales.En.WithLocale(Locale.Parse("en-RU"));

        Assert.Same(de.DecimalFormat, de.DecimalFormat);
        Assert.Same(de.PercentFormat, de.PercentFormat);
        Assert.Equal("1.234,5", de.DecimalFormat.Format(1234.5m));
        Assert.Equal(Marked("25_%"), de.PercentFormat.Format(0.25m));
        Assert.Equal("1,234.5", russia.DecimalFormat.Format(1234.5m));
        Assert.Equal("$1,234.50", NumberFormatter.Currency(russia, "USD").Format(1234.5m));
    }

    /// <summary>
    /// Each locale's decimal and percent formats read back what they write (design §4.11, "Tests": the number parsing).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Every entry of CldrLocales.All, and root.
    ///
    /// Steps:
    /// - Write -1234567.891, 0.5, 1234, 0, and 1000000 with the decimal format, and 0.12, -12.34, and 0.5 with the percent format;
    ///   read each text back as a decimal and as a double.
    ///
    /// Expected:
    /// - Every text is read, to the value written.
    /// </remarks>
    [Fact]
    public void TryParse_EveryLocale_ReadsWhatItsFormatsWrite()
    {
        decimal[] numbers = [-1234567.891m, 0.5m, 1234m, 0m, 1000000m];
        decimal[] percents = [0.12m, -12.34m, 0.5m];
        var failures = new List<string>();
        foreach (LocaleData data in CldrLocales.All.Append(LocaleData.Root))
        {
            foreach ((NumberFormatter format, decimal[] values) in new[] { (data.DecimalFormat, numbers), (data.PercentFormat, percents) })
            {
                foreach (decimal value in values)
                {
                    string text = format.Format(value);
                    if (!format.TryParse(text, out decimal read) || read != value)
                    {
                        failures.Add(data.Locale.ToLanguageTag() + " decimal " + text);
                    }

                    if (!format.TryParse(text, out double readDouble) || readDouble != (double)value)
                    {
                        failures.Add(data.Locale.ToLanguageTag() + " double " + text);
                    }
                }
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Parsing reads the locale's digits, ASCII digits, and full-width digits, skips group separators between integer digits
    /// without checking their place (and, where the separator is U+00A0 or U+202F, a space and the other of the two), reads a sign
    /// first (a hyphen-minus too where the minus is U+2212), a percent sign before or after the digits or none, and skips white space
    /// at the ends and the bidi marks anywhere (design §4.11, "Parsing"; Issue #906; PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: en without groups, with groups out of place, with spaces and a plus sign, with no integer digit, with no fraction
    ///   digit after the decimal separator; de's 3.14, whose . is its group separator (314: the place is not checked); hi with four
    ///   Indian groups; fr (separator U+202F) with a space, with U+202F, and with U+00A0; pl and ru (separator U+00A0) with a space,
    ///   with U+00A0, and with U+202F; ja with full-width digits, mixed digits, and a full-width
    ///   number with an ASCII group; ar-EG with its own digits and minus, with ASCII digits, and a percent without the U+061C of its
    ///   sign; ar with a minus without its U+200E, and with U+200E between digits; sv, whose minus is U+2212, with a hyphen-minus and
    ///   with its own; en, tr, and de percents.
    ///
    /// Steps:
    /// - Read each text with the locale's decimal or percent format, as a decimal and as a double.
    ///
    /// Expected:
    /// - The number of each case; the percent format divides by 100.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="percent">Whether the percent format reads it.</param>
    /// <param name="text">The text, with the marks of the class comment.</param>
    /// <param name="expected">The number, in invariant text.</param>
    [Theory]
    [InlineData("en", false, "1234.5", "1234.5")]
    [InlineData("en", false, "1,2,3", "123")]
    [InlineData("en", false, "  +42  ", "42")]
    [InlineData("en", false, "-.5", "-0.5")]
    [InlineData("en", false, "5.", "5")]
    [InlineData("de", false, "3.14", "314")]
    [InlineData("hi", false, "1,23,45,678", "12345678")]
    [InlineData("fr", false, "1 234,5", "1234.5")]
    [InlineData("fr", false, "1^234,5", "1234.5")]
    [InlineData("fr", false, "1_234_567,5", "1234567.5")]
    [InlineData("pl", false, "1 234,5", "1234.5")]
    [InlineData("pl", false, "1_234,5", "1234.5")]
    [InlineData("pl", false, "1^234^567,5", "1234567.5")]
    [InlineData("ru", false, "1 234,5", "1234.5")]
    [InlineData("ru", false, "1_234,5", "1234.5")]
    [InlineData("ru", false, "1^234^567,5", "1234567.5")]
    [InlineData("ja", false, "１２３４.５", "1234.5")]
    [InlineData("ja", false, "1２3４", "1234")]
    [InlineData("ja", false, "１,２３４", "1234")]
    [InlineData("ar-EG", false, "@-١٬٢٣٤٫٥", "-1234.5")]
    [InlineData("ar-EG", false, "1234٫5", "1234.5")]
    [InlineData("ar-EG", true, "٢٥٪", "0.25")]
    [InlineData("ar", false, "-1,234.5", "-1234.5")]
    [InlineData("ar", false, "1|2|3", "123")]
    [InlineData("sv", false, "-5", "-5")]
    [InlineData("sv", false, "−5", "-5")]
    [InlineData("en", true, "25%", "0.25")]
    [InlineData("en", true, "25", "0.25")]
    [InlineData("en", true, "25 %", "0.25")]
    [InlineData("tr", true, "%25", "0.25")]
    [InlineData("tr", true, "-%25", "-0.25")]
    [InlineData("de", true, "25_%", "0.25")]
    public void TryParse_AcceptedForms_AreRead(string tag, bool percent, string text, string expected)
    {
        LocaleData data = ByTag[tag];
        NumberFormatter format = percent ? data.PercentFormat : data.DecimalFormat;

        Assert.True(format.TryParse(Marked(text), out decimal value));
        Assert.True(format.TryParse(Marked(text), out double number));
        Assert.Equal(Parse(expected), value);
        Assert.Equal((double)Parse(expected), number);
    }

    /// <summary>
    /// Text with a character the format does not read, with no digit, with a group separator not between digits, or with a number
    /// outside the range of decimal is not read (design §4.11, "Parsing").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: en with a letter, the empty text, white space alone, a sign alone, two group separators in a row, a separator
    ///   first or last (after a sign, before a decimal separator), two decimal separators, a percent sign in the decimal format, NaN,
    ///   ∞, an exponent, a number past decimal.MaxValue, two percent signs, U+2212 (en's minus is a hyphen-minus), and U+00A0
    ///   between digits; tr's percent with the sign after the percent sign (%-25); de with a space, with U+00A0, and with U+202F
    ///   (its separator is .).
    ///
    /// Steps:
    /// - Read each text as a decimal, with the marks of the class comment.
    ///
    /// Expected:
    /// - false and 0 for each.
    /// </remarks>
    /// <param name="tag">The locale.</param>
    /// <param name="percent">Whether the percent format reads it.</param>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("en", false, "12a")]
    [InlineData("en", false, "")]
    [InlineData("en", false, "   ")]
    [InlineData("en", false, "-")]
    [InlineData("en", false, "1,,234")]
    [InlineData("en", false, ",123")]
    [InlineData("en", false, "1,")]
    [InlineData("en", false, "1,.5")]
    [InlineData("en", false, "-,5")]
    [InlineData("en", false, "1.2.3")]
    [InlineData("en", false, "25%")]
    [InlineData("en", false, "NaN")]
    [InlineData("en", false, "∞")]
    [InlineData("en", false, "1e5")]
    [InlineData("en", false, "−5")]
    [InlineData("en", false, "79228162514264337593543950336")]
    [InlineData("en", true, "%25%")]
    [InlineData("en", false, "1_234")]
    [InlineData("tr", true, "%-25")]
    [InlineData("de", false, "1 234")]
    [InlineData("de", false, "1_234")]
    [InlineData("de", false, "1^234")]
    public void TryParse_OtherText_IsFalse(string tag, bool percent, string text)
    {
        LocaleData data = ByTag[tag];
        NumberFormatter format = percent ? data.PercentFormat : data.DecimalFormat;

        Assert.False(format.TryParse(Marked(text), out decimal value));
        Assert.Equal(0m, value);
    }

    /// <summary>
    /// Text of 256 characters or more is read too (in a rented buffer), and a currency or a compact format does not parse.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal, USD, and compact formats; 299 zeros and a 7.
    ///
    /// Steps:
    /// - Read the long text; read 42 with the currency and the compact formats, as a decimal and as a double.
    ///
    /// Expected:
    /// - 7; InvalidOperationException for each of the four.
    /// </remarks>
    [Fact]
    public void TryParse_LongTextAndOtherFormats_ReadOrThrow()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter currency = NumberFormatter.Currency(en, "USD");
        NumberFormatter compact = NumberFormatter.Compact(en, CompactStyle.Short);

        Assert.True(en.DecimalFormat.TryParse(new string('0', 299) + "7", out decimal value));
        Assert.Equal(7m, value);
        Assert.Throws<InvalidOperationException>(() => currency.TryParse("42", out decimal _));
        Assert.Throws<InvalidOperationException>(() => currency.TryParse("42", out double _));
        Assert.Throws<InvalidOperationException>(() => compact.TryParse("42", out decimal _));
        Assert.Throws<InvalidOperationException>(() => compact.TryParse("42", out double _));
    }

    /// <summary>
    /// Digits past the range of double are not read as a double, as the infinities are not (PR #944's review: double.TryParse gives an
    /// infinity for them); the largest double's digits are.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal and percent formats; a 1 and 400 zeros (past 1.8 × 10^308, read in a rented buffer); a 1 and 308 zeros.
    ///
    /// Steps:
    /// - Read the first as a double with both formats, and with a minus sign; read the second as a double.
    ///
    /// Expected:
    /// - false and 0 for each of the first three; true and 10^308 for the second.
    /// </remarks>
    [Fact]
    public void TryParse_DigitsPastDouble_IsFalse()
    {
        LocaleData en = CldrLocales.En;
        string past = "1" + new string('0', 400);

        Assert.False(en.DecimalFormat.TryParse(past, out double number));
        Assert.False(en.PercentFormat.TryParse(past, out double percent));
        Assert.False(en.DecimalFormat.TryParse("-" + past, out double negative));
        Assert.Equal((0.0, 0.0, 0.0), (number, percent, negative));
        Assert.True(en.DecimalFormat.TryParse("1" + new string('0', 308), out double largest));
        Assert.Equal(1e308, largest);
    }

    /// <summary>
    /// A formatted number in an interpolation of a NodeTextWriter writes the format's text; the default value writes nothing
    /// (design §4.11, "Allocation and threads").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal, percent, and short compact formats; a writer over a buffer.
    ///
    /// Steps:
    /// - Write "{1234.5m} / {0.25} / {1234L}{default} files" with Of, and read the writer's text; ToString of Of(1234.5).
    ///
    /// Expected:
    /// - "1,234.5 / 25% / 1.2K files"; "1,234.5".
    /// </remarks>
    [Fact]
    public void Of_InAnInterpolation_WritesTheFormattedNumber()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter compact = NumberFormatter.Compact(en, CompactStyle.Short);
        var writer = new NodeTextWriter(new char[64], new TextRangeWriter());

        writer.Write($"{en.DecimalFormat.Of(1234.5m)} / {en.PercentFormat.Of(0.25)} / {compact.Of(1234L)}{default(FormattedNumber)} files");

        Assert.Equal("1,234.5 / 25% / 1.2K files", new string(writer.Buffer, 0, writer.Length));
        Assert.Equal("1,234.5", en.DecimalFormat.Of(1234.5).ToString());
    }

    /// <summary>
    /// ToString of a formatted number longer than its 64 characters on the stack writes it whole, through a buffer of 256 characters
    /// and through one grown past that.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's decimal format with at least 100 and with at least 300 integer digits, without grouping.
    ///
    /// Steps:
    /// - Call ToString of Of(7m) with each, and Format(7m) with the second.
    ///
    /// Expected:
    /// - 99 zeros and a 7; 299 zeros and a 7, both ways.
    /// </remarks>
    [Fact]
    public void ToString_TextPastSixtyFourCharacters_IsWhole()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter hundred = NumberFormatter.Decimal(en, new NumberOptions { MinimumIntegerDigits = 100, UseGrouping = false });
        NumberFormatter threeHundred = NumberFormatter.Decimal(en, new NumberOptions { MinimumIntegerDigits = 300, UseGrouping = false });

        Assert.Equal(new string('0', 99) + "7", hundred.Of(7m).ToString());
        Assert.Equal(new string('0', 299) + "7", threeHundred.Of(7m).ToString());
        Assert.Equal(new string('0', 299) + "7", threeHundred.Format(7m));
    }

    /// <summary>
    /// Writing, rounding, and parsing allocate nothing, and neither does writing Of(value) into a NodeTextWriter (design §4.11,
    /// "Allocation and threads"; §11.3's Zero-Alloc check).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - ar-EG's decimal format (Arabic-Indic digits), en's percent, USD, and short compact formats, pl's long compact format, a
    ///   destination buffer, a writer over a buffer, and the texts to parse, all made before the measured runs.
    ///
    /// Steps:
    /// - 1,000 times, in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: write the index as a long and its magnitude as a
    ///   ulong, and the index plus 0.5 as a decimal and as a double (the shortest digits, rounded, and moved by a percent), with each
    ///   format; round it; parse a decimal and a percent text as a decimal and a double; write four Of values (a long, a decimal, a
    ///   double, and a ulong) into a new writer over the same buffer.
    ///
    /// Expected:
    /// - No byte is allocated, and every run wrote and parsed.
    /// </remarks>
    [Fact]
    public void Formats_AllocateNothing()
    {
        LocaleData en = CldrLocales.En;
        NumberFormatter[] formats =
        [
            NumberFormatter.Decimal(ByTag["ar-EG"]),
            en.PercentFormat,
            NumberFormatter.Currency(en, "USD"),
            NumberFormatter.Compact(en, CompactStyle.Short),
            NumberFormatter.Compact(CldrLocales.Pl, CompactStyle.Long),
        ];
        NumberFormatter number = en.DecimalFormat;
        NumberFormatter percent = en.PercentFormat;
        char[] destination = new char[64];
        char[] text = new char[64];
        var ranges = new TextRangeWriter();
        string numberText = Marked("１,234.5");
        string percentText = "-12 %";
        int done = 0;

        int runs = PerformanceAssert.DoesNotAllocate(
            1_000,
            i =>
            {
                bool all = true;
                foreach (NumberFormatter format in formats)
                {
                    all &= format.TryFormat(i, destination, out _);
                    all &= format.TryFormat((ulong)Math.Abs(i), destination, out _);
                    all &= format.TryFormat(i + 0.5m, destination, out _);
                    all &= format.TryFormat(i + 0.5, destination, out _);
                    _ = format.Round(i + 0.5m);
                }

                all &= number.TryParse(numberText, out decimal parsed) && parsed == 1234.5m;
                all &= number.TryParse(numberText, out double parsedDouble) && parsedDouble == 1234.5;
                all &= percent.TryParse(percentText, out decimal parsedPercent) && parsedPercent == -0.12m;
                var writer = new NodeTextWriter(text, ranges);
                writer.Write($"{number.Of(i)} {percent.Of(i + 0.5m)} {formats[3].Of(i + 0.5)} {number.Of((ulong)Math.Abs(i))}");
                if (all && writer.Length > 0)
                {
                    done++;
                }
            }
        );

        Assert.Equal(runs, done);
    }

    /// <summary>
    /// The tool's counts of distinct number tables, and values of the json the tables hold: ja's decimal separator, ar's and
    /// ar-EG's digits, and es's minimum grouping digits (design §4.11, "Tests").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The generated tables.
    ///
    /// Steps:
    /// - Read the counts the tool wrote, and the symbols of ja, ar, ar-EG, and es.
    ///
    /// Expected:
    /// - 74 number tables (with the alphaNextToNumber pattern), 110 compact tables, and 245 currency tables (counted from the json: locales whose bytes are the same
    ///   share one); ja's decimal separator is .; ar's zero is 0 (CLDR 49 gives ar the Latin digits; ar-EG keeps the Arabic-Indic
    ///   ones, whose zero is U+0660); es groups from 2 digits.
    /// </remarks>
    [Fact]
    public void CldrData_NumberTables_HoldTheJsonsValues()
    {
        Assert.Equal(74, CldrData.NumberTableCount);
        Assert.Equal(110, CldrData.CompactTableCount);
        Assert.Equal(245, CldrData.CurrencyTableCount);
        Assert.Equal(".", CldrLocales.Ja.Numbers.Decimal);
        Assert.Equal('0', CldrLocales.Ar.Numbers.Zero);
        Assert.Equal((char)0x0660, ByTag["ar-EG"].Numbers.Zero);
        Assert.Equal(2, CldrLocales.Es.Numbers.MinimumGroupingDigits);
    }

    private static decimal Parse(string value) => decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Write(NumberFormatter format, long value)
    {
        var buffer = new char[64];
        Assert.True(format.TryFormat(value, buffer, out int written));
        return new string(buffer, 0, written);
    }

    private static string Write(NumberFormatter format, ulong value)
    {
        var buffer = new char[64];
        Assert.True(format.TryFormat(value, buffer, out int written));
        return new string(buffer, 0, written);
    }

    private static string Write(NumberFormatter format, double value)
    {
        var buffer = new char[64];
        Assert.True(format.TryFormat(value, buffer, out int written));
        return new string(buffer, 0, written);
    }

    // A number table with its last string, the alphaNextToNumber pattern, emptied: the digit zero and the minimum grouping digits
    // (3 bytes), then 12 strings of a length byte and their bytes.
    private static byte[] WithoutAlphaPattern(ReadOnlySpan<byte> table)
    {
        int at = 3;
        for (int i = 0; i < 12; i++)
        {
            at += 1 + table[at];
        }

        return [.. table[..at], 0];
    }

    // The text with the marks of the class comment replaced by the characters they stand for.
    private static string Marked(string text) =>
        text.Replace('_', (char)0x00A0).Replace('^', (char)0x202F).Replace('|', (char)0x200E).Replace('!', (char)0x200F).Replace('@', (char)0x061C);

    // en's tables without the alphaNextToNumber pattern.
    private sealed class NoAlphaLocaleData() : LocaleData(Locale.Parse("en"), DayOfWeek.Sunday, 1, DayOfWeek.Saturday, DayOfWeek.Sunday)
    {
        private static readonly byte[] Table = WithoutAlphaPattern(CldrLocales.En.NumberTable);

        internal override ReadOnlySpan<byte> NumberTable => Table;

        internal override ReadOnlySpan<byte> CompactTable => CldrLocales.En.CompactTable;

        internal override ReadOnlySpan<byte> CurrencyTable => CldrLocales.En.CurrencyTable;

        internal override ReadOnlySpan<byte> DateNameTable => CldrLocales.En.DateNameTable;

        internal override ReadOnlySpan<byte> DatePatternTable => CldrLocales.En.DatePatternTable;

        internal override PluralRules CreatePlurals() => CldrLocales.En.Plurals;
    }

    // pl's number tables with plural rules that select many when the compact exponent is 3, and other otherwise.
    private sealed class ExponentLocaleData() : LocaleData(Locale.Parse("pl"), DayOfWeek.Monday, 4, DayOfWeek.Saturday, DayOfWeek.Sunday)
    {
        internal override ReadOnlySpan<byte> NumberTable => CldrLocales.Pl.NumberTable;

        internal override ReadOnlySpan<byte> CompactTable => CldrLocales.Pl.CompactTable;

        internal override ReadOnlySpan<byte> CurrencyTable => CldrLocales.Pl.CurrencyTable;

        internal override ReadOnlySpan<byte> DateNameTable => CldrLocales.Pl.DateNameTable;

        internal override ReadOnlySpan<byte> DatePatternTable => CldrLocales.Pl.DatePatternTable;

        internal override PluralRules CreatePlurals() => new(ManyAtThree, ManyAtThree);

        private static PluralCategory ManyAtThree(in PluralOperands operands) => operands.E == 3 ? PluralCategory.Many : PluralCategory.Other;
    }
}
