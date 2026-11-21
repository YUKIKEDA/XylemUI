using System.Globalization;
using System.Text;

namespace XylemUI;

/// <summary>
/// A CLDR number pattern read into its parts (UTS #35 part 3, "Number Patterns"; design §4.11, "Numbers"): the affixes of positive and
/// negative numbers, the grouping sizes, and the digits the pattern shows. tools/CldrTableGen checks that every pattern it writes has
/// this shape: a prefix, digits of <c>#</c>, <c>0</c>, <c>,</c>, and <c>.</c>, a suffix, and an optional negative subpattern after
/// <c>;</c>, whose digits are not read.
/// </summary>
internal sealed class NumberPattern
{
    /// <summary>
    /// Gets the affixes of a positive number or zero.
    /// </summary>
    public required NumberAffixes Positive { get; init; }

    /// <summary>
    /// Gets the affixes of a negative number: the negative subpattern's, or the minus sign before the positive prefix when the
    /// pattern has none.
    /// </summary>
    public required NumberAffixes Negative { get; init; }

    /// <summary>
    /// Gets the digits of the group nearest the decimal separator; 0 when the pattern does not group.
    /// </summary>
    public required int PrimaryGrouping { get; init; }

    /// <summary>
    /// Gets the digits of each group left of the primary one (2 in the Indian <c>#,##,##0</c>).
    /// </summary>
    public required int SecondaryGrouping { get; init; }

    /// <summary>
    /// Gets the zeros of the integer part: the fewest integer digits, or, in a compact pattern (<c>00K</c>), the digits shown.
    /// </summary>
    public required int IntegerZeros { get; init; }

    /// <summary>
    /// Gets the fewest fraction digits (the zeros after the decimal separator).
    /// </summary>
    public required int MinimumFractionDigits { get; init; }

    /// <summary>
    /// Gets the most fraction digits (the zeros and number signs after the decimal separator).
    /// </summary>
    public required int MaximumFractionDigits { get; init; }

    /// <summary>
    /// Gets a value indicating whether the pattern shows the number: the compact pattern <c>mille</c> of French has no digits.
    /// </summary>
    public required bool HasNumber { get; init; }

    /// <summary>
    /// Reads a pattern, with the locale's symbols in place of <c>%</c>, <c>-</c>, and <c>+</c>, and a currency's symbol in place of
    /// <c>¤</c>.
    /// </summary>
    /// <param name="pattern">The pattern, as CLDR writes it.</param>
    /// <param name="symbols">The locale's symbols.</param>
    /// <param name="currency">The currency's symbol; <see langword="null"/> for a pattern with no <c>¤</c>.</param>
    /// <param name="currencySpacing">Whether CLDR's <c>currencySpacing</c> applies: only for a locale with no
    /// <c>standard-alphaNextToNumber</c> pattern (Issue #906).</param>
    /// <returns>The parts.</returns>
    public static NumberPattern Parse(string pattern, NumberSymbols symbols, string? currency, bool currencySpacing)
    {
        string? spaced = currencySpacing ? currency : null;
        int index = 0;
        string prefix = Affix(pattern, ref index, symbols, currency, out _, out bool prefixEndsWithCurrency);
        int start = index;
        while (index < pattern.Length && IsNumberCharacter(pattern[index]))
        {
            index++;
        }

        ReadOnlySpan<char> number = pattern.AsSpan(start, index - start);
        string suffix = Affix(pattern, ref index, symbols, currency, out bool suffixStartsWithCurrency, out _);
        var positive = new NumberAffixes(
            prefix,
            suffix,
            prefixEndsWithCurrency && IsSpaced(spaced, last: true),
            suffixStartsWithCurrency && IsSpaced(spaced, last: false)
        );
        NumberAffixes negative = positive with { Prefix = symbols.Minus + prefix };
        if (index < pattern.Length)
        {
            // The negative subpattern after ';' gives its affixes; its digits are the positive pattern's.
            index++;
            string negativePrefix = Affix(pattern, ref index, symbols, currency, out _, out bool negativeEndsWithCurrency);
            while (index < pattern.Length && IsNumberCharacter(pattern[index]))
            {
                index++;
            }

            string negativeSuffix = Affix(pattern, ref index, symbols, currency, out bool negativeStartsWithCurrency, out _);
            negative = new NumberAffixes(
                negativePrefix,
                negativeSuffix,
                negativeEndsWithCurrency && IsSpaced(spaced, last: true),
                negativeStartsWithCurrency && IsSpaced(spaced, last: false)
            );
        }

        return Digits(number, positive, negative);
    }

    /// <summary>
    /// Returns a value indicating whether the currency sign of a pattern's positive part comes before its digits, so that the
    /// symbol's end is next to the number; otherwise its start is.
    /// </summary>
    /// <param name="pattern">The pattern, as CLDR writes it.</param>
    /// <returns><see langword="true"/> when an unquoted <c>¤</c> comes before the digits.</returns>
    public static bool CurrencyBeforeNumber(string pattern)
    {
        bool quoted = false;
        foreach (char character in pattern)
        {
            if (character == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && character == '¤')
            {
                return true;
            }
            else if (!quoted && (character == ';' || IsNumberCharacter(character)))
            {
                return false;
            }
        }

        return false;
    }

    private static NumberPattern Digits(ReadOnlySpan<char> number, NumberAffixes positive, NumberAffixes negative)
    {
        int integerDigits = 0;
        int integerZeros = 0;
        int fractionDigits = 0;
        int fractionZeros = 0;
        int lastSeparator = -1;
        int separatorBefore = -1;
        bool fraction = false;
        foreach (char character in number)
        {
            switch (character)
            {
                case '.':
                    fraction = true;
                    break;
                case ',':
                    separatorBefore = lastSeparator;
                    lastSeparator = integerDigits;
                    break;
                case '0' when fraction:
                    fractionDigits++;
                    fractionZeros++;
                    break;
                case '0':
                    integerDigits++;
                    integerZeros++;
                    break;
                default:
                    if (fraction)
                    {
                        fractionDigits++;
                    }
                    else
                    {
                        integerDigits++;
                    }

                    break;
            }
        }

        int primary = lastSeparator < 0 ? 0 : integerDigits - lastSeparator;
        return new NumberPattern
        {
            Positive = positive,
            Negative = negative,
            PrimaryGrouping = primary,
            SecondaryGrouping = separatorBefore < 0 ? primary : lastSeparator - separatorBefore,
            IntegerZeros = integerZeros,
            MinimumFractionDigits = fractionZeros,
            MaximumFractionDigits = fractionDigits,
            HasNumber = !number.IsEmpty,
        };
    }

    private static bool IsNumberCharacter(char character) => character is '#' or '0' or ',' or '.';

    // The text up to the digits or to ';', with quoted text as it is ('' is one quote) and the special characters replaced. It says
    // whether the first and the last of its parts is the currency sign.
    private static string Affix(
        string pattern,
        ref int index,
        NumberSymbols symbols,
        string? currency,
        out bool startsWithCurrency,
        out bool endsWithCurrency
    )
    {
        var text = new StringBuilder();
        bool quoted = false;
        bool first = true;
        startsWithCurrency = false;
        endsWithCurrency = false;
        for (; index < pattern.Length; index++)
        {
            char character = pattern[index];
            if (character == '\'')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '\'')
                {
                    text.Append('\'');
                    index++;
                    endsWithCurrency = false;
                    first = false;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (!quoted && (character == ';' || IsNumberCharacter(character)))
            {
                break;
            }

            bool isCurrency = !quoted && character == '¤';
            if (quoted)
            {
                text.Append(character);
            }
            else if (isCurrency)
            {
                text.Append(currency ?? "¤");
            }
            else if (character == '%')
            {
                text.Append(symbols.Percent);
            }
            else if (character == '-')
            {
                text.Append(symbols.Minus);
            }
            else if (character == '+')
            {
                text.Append(symbols.Plus);
            }
            else
            {
                text.Append(character);
            }

            startsWithCurrency |= first && isCurrency;
            endsWithCurrency = isCurrency;
            first = false;
        }

        return text.ToString();
    }

    // CLDR's currencySpacing, for a locale with no alphaNextToNumber pattern, the same in every locale of the set
    // (tools/CldrTableGen checks it): a no-break space goes between the
    // digits and a currency symbol whose character next to them is neither a symbol nor a separator ([[:^S:]&[:^Z:]]).
    private static bool IsSpaced(string? currency, bool last)
    {
        if (string.IsNullOrEmpty(currency))
        {
            return false;
        }

        Rune rune;
        if (last)
        {
            _ = Rune.DecodeLastFromUtf16(currency, out rune, out _);
        }
        else
        {
            _ = Rune.DecodeFromUtf16(currency, out rune, out _);
        }

        return Rune.GetUnicodeCategory(rune)
            is not (
                UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol
                or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
            );
    }
}
