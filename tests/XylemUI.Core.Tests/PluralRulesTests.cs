using System.Globalization;

namespace XylemUI.Tests;

// The plural rules of CLDR compiled to methods by tools/CldrTableGen (LC2, design §4.11, "Plurals" and "Plural rules"): every sample
// CLDR writes beside a rule selects that rule's category, the operands are UTS #35's, a range holds for whole values only, a locale
// takes the rules of its truncated tag, and a selection allocates nothing. The samples come from plurals.json and ordinals.json of
// cldr-49.0.0-json-full.zip, through the table the tool wrote for the tests (CldrPluralSamples.g.cs).
public sealed class PluralRulesTests
{
    /// <summary>
    /// For every entry and root, every sample of every rule of its cardinal and ordinal keys selects that rule's category.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - CldrPluralSamples: each entry's tag with the keys whose rules it takes, and each key's samples by category.
    /// - The tables of each entry (CldrLocales.All) and of root.
    ///
    /// Steps:
    /// - Expand each sample list: values, ranges (0~15, 0.0~1.5, stepped by the last digit of their ends), and compact decimals
    ///   (1.1c6, the value 1100000 shown with the exponent 6); the ellipsis is skipped.
    /// - Select each value: a whole @integer value with Select(long), a @decimal value with Select(decimal) at its own scale, a
    ///   compact decimal with its exponent, and an ordinal value with SelectOrdinal.
    ///
    /// Expected:
    /// - Every value selects its sample's category; the run selected 39,458 values (counted from the json with the same expansion).
    /// </remarks>
    [Fact]
    public void Select_EverySampleOfEveryEntry_GivesItsCategory()
    {
        var byTag = CldrLocales.All.ToDictionary(data => data.Locale.ToLanguageTag(), StringComparer.Ordinal);
        byTag.Add("und", LocaleData.Root);
        var failures = new List<string>();
        int selected = 0;
        foreach ((string tag, string cardinalKey, string ordinalKey) in CldrPluralSamples.Entries)
        {
            PluralRules rules = byTag[tag].Plurals;
            foreach ((string key, string category, string samples) in CldrPluralSamples.Cardinal.Where(sample => sample.Key == cardinalKey))
            {
                foreach ((decimal value, int exponent, bool isInteger) in Expand(samples))
                {
                    PluralCategory actual =
                        exponent != 0 ? rules.Select(value, exponent)
                        : isInteger ? rules.Select(decimal.ToInt64(value))
                        : rules.Select(value);
                    selected++;
                    if (actual != Category(category))
                    {
                        failures.Add(tag + " (" + key + ") cardinal " + Text(value, exponent) + ": " + actual + ", not " + category);
                    }
                }
            }

            foreach ((string key, string category, string samples) in CldrPluralSamples.Ordinal.Where(sample => sample.Key == ordinalKey))
            {
                foreach ((decimal value, int exponent, bool isInteger) in Expand(samples))
                {
                    Assert.True(isInteger && exponent == 0, "An ordinal sample is a whole number: " + samples);
                    PluralCategory actual = rules.SelectOrdinal(decimal.ToInt64(value));
                    selected++;
                    if (actual != Category(category))
                    {
                        failures.Add(tag + " (" + key + ") ordinal " + Text(value, exponent) + ": " + actual + ", not " + category);
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, failures.Count + " samples failed, the first: " + string.Join("\n", failures.Take(20)));
        Assert.Equal(39_458, selected);
    }

    /// <summary>
    /// The visible fraction digits of a decimal decide its category, and the sign does not: in English 1 is one, 1.0 other.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - en's rules: one is i = 1 and v = 0. Its ordinal rules: one is n % 10 = 1 and n % 100 != 11, two n % 10 = 2 and
    ///   n % 100 != 12, few n % 10 = 3 and n % 100 != 13.
    ///
    /// Steps:
    /// - Select 1, -1, 1m, 1.0m, 2, and long.MinValue; select the ordinals 1, 2, 3, 4, 11, 21, 22, and 112.
    ///
    /// Expected:
    /// - One, one, one, other, other, other; one, two, few, other, other, one, two, and other for 112 (112 % 100 is 12).
    /// </remarks>
    [Fact]
    public void Select_English_ReadsTheVisibleFractionDigits()
    {
        PluralRules en = CldrLocales.En.Plurals;

        Assert.Equal(PluralCategory.One, en.Select(1));
        Assert.Equal(PluralCategory.One, en.Select(-1));
        Assert.Equal(PluralCategory.One, en.Select(1m));
        Assert.Equal(PluralCategory.Other, en.Select(1.0m));
        Assert.Equal(PluralCategory.Other, en.Select(2));
        Assert.Equal(PluralCategory.Other, en.Select(long.MinValue));
        PluralCategory[] ordinals = [.. new long[] { 1, 2, 3, 4, 11, 21, 22, 112 }.Select(en.SelectOrdinal)];
        Assert.Equal(
            [
                PluralCategory.One,
                PluralCategory.Two,
                PluralCategory.Few,
                PluralCategory.Other,
                PluralCategory.Other,
                PluralCategory.One,
                PluralCategory.Two,
                PluralCategory.Other,
            ],
            ordinals
        );
    }

    /// <summary>
    /// A range of a rule holds for whole values only, the value modulo a number included: 3 is few in Arabic and 3.5 is not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - ar's rules: few is n % 100 = 3..10, many n % 100 = 11..99. lt's: few is n % 10 = 2..9 and n % 100 != 11..19, many f != 0.
    ///
    /// Steps:
    /// - Select 3, 3.5m, 103.5m, and 11 in Arabic; 2 and 2.5m in Lithuanian.
    ///
    /// Expected:
    /// - Few, other, other, many; few, many.
    /// </remarks>
    [Fact]
    public void Select_RangeOfARule_HoldsForWholeValuesOnly()
    {
        PluralRules ar = CldrLocales.Ar.Plurals;
        PluralRules lt = CldrLocales.Lt.Plurals;

        Assert.Equal(PluralCategory.Few, ar.Select(3));
        Assert.Equal(PluralCategory.Other, ar.Select(3.5m));
        Assert.Equal(PluralCategory.Other, ar.Select(103.5m));
        Assert.Equal(PluralCategory.Many, ar.Select(11));
        Assert.Equal(PluralCategory.Few, lt.Select(2));
        Assert.Equal(PluralCategory.Many, lt.Select(2.5m));
    }

    /// <summary>
    /// A locale takes the rules of its tag truncated subtag by subtag: the plurals component lists no parent locales, so pt-AO
    /// takes pt's rules, not those of pt-PT, its parent in the main inheritance.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - pt's rules: one is i = 0..1. pt-PT's: one is i = 1 and v = 0.
    ///
    /// Steps:
    /// - Select 0 in pt, pt-AO, and pt-PT.
    ///
    /// Expected:
    /// - One, one, other; pt-AO selects with pt's method.
    /// </remarks>
    [Fact]
    public void Plurals_RegionalLocale_TakesTheRulesOfItsTruncatedTag()
    {
        Assert.Equal(PluralCategory.One, CldrLocales.Pt.Plurals.Select(0));
        Assert.Equal(PluralCategory.One, CldrLocales.PtAO.Plurals.Select(0));
        Assert.Equal(PluralCategory.Other, CldrLocales.PtPT.Plurals.Select(0));
        Assert.Equal(CldrLocales.Pt.Plurals.Cardinal, CldrLocales.PtAO.Plurals.Cardinal);
    }

    /// <summary>
    /// The operands of a decimal are those UTS #35 part 3 defines ("Plural Operand Meanings").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 1, 1.0, 1.00, 1.3, 1.30, 1.03, 1.230, 1200.50, -1.5, and a decimal of 28 fraction digits.
    ///
    /// Steps:
    /// - Make the operands of each.
    ///
    /// Expected:
    /// - n, i, v, w, f, t of each row, with e 0; the sign dropped.
    /// </remarks>
    /// <param name="text">The number.</param>
    /// <param name="n">The absolute value.</param>
    /// <param name="i">The integer digits.</param>
    /// <param name="v">The visible fraction digits with trailing zeros.</param>
    /// <param name="w">The visible fraction digits without trailing zeros.</param>
    /// <param name="f">The visible fraction digits as a number, with trailing zeros.</param>
    /// <param name="t">The visible fraction digits as a number, without trailing zeros.</param>
    [Theory]
    [InlineData("1", "1", "1", 0, 0, "0", "0")]
    [InlineData("1.0", "1", "1", 1, 0, "0", "0")]
    [InlineData("1.00", "1", "1", 2, 0, "0", "0")]
    [InlineData("1.3", "1.3", "1", 1, 1, "3", "3")]
    [InlineData("1.30", "1.3", "1", 2, 1, "30", "3")]
    [InlineData("1.03", "1.03", "1", 2, 2, "3", "3")]
    [InlineData("1.230", "1.23", "1", 3, 2, "230", "23")]
    [InlineData("1200.50", "1200.5", "1200", 2, 1, "50", "5")]
    [InlineData("-1.5", "1.5", "1", 1, 1, "5", "5")]
    [InlineData("0.0000000000000000000000000001", "0.0000000000000000000000000001", "0", 28, 28, "1", "1")]
    public void Operands_OfADecimal_AreTheTables(string text, string n, string i, int v, int w, string f, string t)
    {
        PluralOperands operands = PluralOperands.Of(decimal.Parse(text, CultureInfo.InvariantCulture), 0);

        Assert.Equal(
            (Parse(n), Parse(i), v, w, Parse(f), Parse(t), 0),
            (operands.N, operands.I, operands.V, operands.W, operands.F, operands.T, operands.E)
        );
    }

    /// <summary>
    /// The operands of a whole number have no fraction digits, and the absolute value of long.MinValue does not overflow.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - long.MinValue and -7.
    ///
    /// Steps:
    /// - Make their operands.
    ///
    /// Expected:
    /// - n and i are 9223372036854775808 and 7; v, w, f, t, and e are 0.
    /// </remarks>
    [Fact]
    public void Operands_OfAWholeNumber_HaveNoFraction()
    {
        PluralOperands min = PluralOperands.Of(long.MinValue);
        PluralOperands seven = PluralOperands.Of(-7);

        Assert.Equal((9223372036854775808m, 9223372036854775808m, 0, 0, 0m, 0m, 0), (min.N, min.I, min.V, min.W, min.F, min.T, min.E));
        Assert.Equal((7m, 7m, 0, 0, 0m, 0m, 0), (seven.N, seven.I, seven.V, seven.W, seven.F, seven.T, seven.E));
    }

    /// <summary>
    /// Selecting a category allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - fr's rules, whose many reads the compact exponent, made before the measured runs.
    ///
    /// Steps:
    /// - 1,000 times, in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: select the index as a long, as a decimal with
    ///   one fraction digit, as a compact decimal, and as an ordinal.
    ///
    /// Expected:
    /// - No byte is allocated, and every run selected many for the compact decimal.
    /// </remarks>
    [Fact]
    public void Select_AllocatesNothing()
    {
        PluralRules fr = CldrLocales.Fr.Plurals;
        int many = 0;

        int runs = PerformanceAssert.DoesNotAllocate(
            1_000,
            i =>
            {
                _ = fr.Select(i);
                _ = fr.Select(i + 0.5m);
                _ = fr.SelectOrdinal(i);
                if (fr.Select(1_100_000m, 6) == PluralCategory.Many)
                {
                    many++;
                }
            }
        );

        Assert.Equal(runs, many);
    }

    // The values of a sample list ("@integer 0, 2~16, 1c3, … @decimal 0.0~1.5, 1.1c3, …"): each with its compact exponent (0 for
    // none), and whether it was listed under @integer.
    private static IEnumerable<(decimal Value, int Exponent, bool IsInteger)> Expand(string samples)
    {
        foreach (string part in samples.Split('@', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = part.Trim();
            bool isInteger = trimmed.StartsWith("integer", StringComparison.Ordinal);
            if (!isInteger && !trimmed.StartsWith("decimal", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A sample list starts with " + trimmed + ".");
            }

            foreach (string raw in trimmed[7..].Split(','))
            {
                string token = raw.Trim();
                if (token.Length == 0 || token == "…")
                {
                    continue;
                }

                int tilde = token.IndexOf('~', StringComparison.Ordinal);
                if (tilde < 0)
                {
                    yield return Compact(token, isInteger);
                    continue;
                }

                decimal low = Parse(token[..tilde]);
                decimal high = Parse(token[(tilde + 1)..]);
                decimal step = 1m;
                for (int k = 0; k < low.Scale; k++)
                {
                    step /= 10;
                }

                for (decimal value = low; value <= high; value += step)
                {
                    yield return (value, 0, isInteger);
                }
            }
        }
    }

    // A value with a compact exponent ("1.1c6"): the point moved right by the exponent, keeping the digits left after it visible.
    private static (decimal Value, int Exponent, bool IsInteger) Compact(string token, bool isInteger)
    {
        int c = token.IndexOfAny(['c', 'e']);
        if (c < 0)
        {
            return (Parse(token), 0, isInteger);
        }

        int exponent = int.Parse(token[(c + 1)..], CultureInfo.InvariantCulture);
        string mantissa = token[..c];
        int point = mantissa.IndexOf('.', StringComparison.Ordinal);
        string whole = point < 0 ? mantissa : mantissa[..point];
        string fraction = point < 0 ? string.Empty : mantissa[(point + 1)..];
        string digits =
            fraction.Length <= exponent
                ? whole + fraction + new string('0', exponent - fraction.Length)
                : whole + fraction[..exponent] + "." + fraction[exponent..];
        return (Parse(digits), exponent, isInteger);
    }

    private static decimal Parse(string text) => decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static PluralCategory Category(string name) =>
        name switch
        {
            "zero" => PluralCategory.Zero,
            "one" => PluralCategory.One,
            "two" => PluralCategory.Two,
            "few" => PluralCategory.Few,
            "many" => PluralCategory.Many,
            _ => PluralCategory.Other,
        };

    private static string Text(decimal value, int exponent) =>
        value.ToString(CultureInfo.InvariantCulture) + (exponent == 0 ? string.Empty : " (c" + exponent + ")");
}
