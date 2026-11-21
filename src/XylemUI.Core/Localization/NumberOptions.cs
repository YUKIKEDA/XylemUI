namespace XylemUI;

/// <summary>
/// The digits and the grouping a decimal, percent, or currency format shows, in place of those of the locale's CLDR pattern (design
/// §4.11, "Numbers"). <see cref="NumberFormatter"/> checks them when it makes a format.
/// </summary>
public sealed class NumberOptions
{
    /// <summary>
    /// Gets the fewest integer digits shown, padded with zeros (<c>007</c> for 3); 1 or more. The default is 1.
    /// </summary>
    public int MinimumIntegerDigits { get; init; } = 1;

    /// <summary>
    /// Gets the fewest fraction digits shown, padded with zeros; 0 or more. <see langword="null"/> takes the pattern's, or the
    /// currency's for a currency format, raised to <see cref="MaximumFractionDigits"/> when that is given and smaller.
    /// </summary>
    public int? MinimumFractionDigits { get; init; }

    /// <summary>
    /// Gets the most fraction digits shown; the value is rounded half to even at the last one. 0 or more, and not less than
    /// <see cref="MinimumFractionDigits"/> when both are given. <see langword="null"/> takes the pattern's, or the currency's for a
    /// currency format, raised to <see cref="MinimumFractionDigits"/> when that is given and larger.
    /// </summary>
    public int? MaximumFractionDigits { get; init; }

    /// <summary>
    /// Gets a value indicating whether the integer digits are grouped as the locale's pattern groups them. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool UseGrouping { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a currency format shows the currency's narrow symbol (<c>$</c> in place of <c>US$</c>). Only
    /// <see cref="NumberFormatter.Currency"/> reads it; a currency with no narrow symbol shows its standard one.
    /// </summary>
    public bool NarrowCurrencySymbol { get; init; }
}
