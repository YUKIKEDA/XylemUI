namespace XylemUI;

/// <summary>
/// The number symbols and patterns of a locale's default numbering system, read once from its CLDR table (design §4.11, "Numbers"
/// and "Default digits"). <see cref="LocaleData"/> keeps the instance.
/// </summary>
internal sealed class NumberSymbols
{
    /// <summary>
    /// Gets the digit zero; the digits one to nine follow it (CLDR's numeric numbering systems).
    /// </summary>
    public required char Zero { get; init; }

    /// <summary>
    /// Gets the fewest digits left of the first group separator (CLDR's <c>minimumGroupingDigits</c>).
    /// </summary>
    public required int MinimumGroupingDigits { get; init; }

    /// <summary>
    /// Gets the decimal separator.
    /// </summary>
    public required string Decimal { get; init; }

    /// <summary>
    /// Gets the group separator.
    /// </summary>
    public required string Group { get; init; }

    /// <summary>
    /// Gets the decimal separator of a currency format (CLDR's <c>currencyDecimal</c>, or the decimal separator).
    /// </summary>
    public required string CurrencyDecimal { get; init; }

    /// <summary>
    /// Gets the group separator of a currency format (CLDR's <c>currencyGroup</c>, or the group separator).
    /// </summary>
    public required string CurrencyGroup { get; init; }

    /// <summary>
    /// Gets the minus sign, with the bidi marks CLDR gives it.
    /// </summary>
    public required string Minus { get; init; }

    /// <summary>
    /// Gets the plus sign.
    /// </summary>
    public required string Plus { get; init; }

    /// <summary>
    /// Gets the percent sign.
    /// </summary>
    public required string Percent { get; init; }

    /// <summary>
    /// Gets the symbol of infinity.
    /// </summary>
    public required string Infinity { get; init; }

    /// <summary>
    /// Gets the symbol of a value that is not a number.
    /// </summary>
    public required string NaN { get; init; }

    /// <summary>
    /// Gets the standard decimal pattern (<c>#,##0.###</c>).
    /// </summary>
    public required string DecimalPattern { get; init; }

    /// <summary>
    /// Gets the standard percent pattern (<c>#,##0%</c>).
    /// </summary>
    public required string PercentPattern { get; init; }

    /// <summary>
    /// Gets the standard currency pattern (<c>¤#,##0.00</c>).
    /// </summary>
    public required string CurrencyPattern { get; init; }

    /// <summary>
    /// Gets the currency pattern for a symbol whose character next to the digits is a letter (CLDR's
    /// <c>standard-alphaNextToNumber</c>, <c>¤_#,##0.00</c>); empty when the locale has none, and then <c>currencySpacing</c> applies.
    /// </summary>
    public required string CurrencyAlphaPattern { get; init; }

    /// <summary>
    /// Reads the symbols from a locale's number table, in the order tools/CldrTableGen writes them: the digit zero, the minimum
    /// grouping digits, the nine symbols, and the four patterns.
    /// </summary>
    /// <param name="table">The table (<see cref="LocaleData.NumberTable"/>).</param>
    /// <returns>The symbols.</returns>
    public static NumberSymbols Read(ReadOnlySpan<byte> table)
    {
        var reader = new CldrTableReader(table);

        // An object initializer assigns in the order written, which is the order of the table.
        return new NumberSymbols
        {
            Zero = reader.ReadChar(),
            MinimumGroupingDigits = reader.ReadByte(),
            Decimal = reader.ReadString(),
            Group = reader.ReadString(),
            CurrencyDecimal = reader.ReadString(),
            CurrencyGroup = reader.ReadString(),
            Minus = reader.ReadString(),
            Plus = reader.ReadString(),
            Percent = reader.ReadString(),
            Infinity = reader.ReadString(),
            NaN = reader.ReadString(),
            DecimalPattern = reader.ReadString(),
            PercentPattern = reader.ReadString(),
            CurrencyPattern = reader.ReadString(),
            CurrencyAlphaPattern = reader.ReadString(),
        };
    }
}
