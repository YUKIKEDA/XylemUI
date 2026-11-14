namespace XylemUI;

/// <summary>
/// Selects the plural category of a number with a locale's CLDR rules, cardinal (<c>1 file</c>, <c>2 files</c>) and ordinal
/// (<c>1st</c>, <c>2nd</c>) (design §4.11, "Plurals"). An instance does not change after it is made, so any thread may call it, and
/// a selection allocates nothing. A locale's instance is <see cref="LocaleData.Plurals"/>.
/// </summary>
public sealed class PluralRules
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluralRules"/> class.
    /// </summary>
    /// <param name="cardinal">The cardinal rules.</param>
    /// <param name="ordinal">The ordinal rules.</param>
    internal PluralRules(PluralRule cardinal, PluralRule ordinal)
    {
        Cardinal = cardinal;
        Ordinal = ordinal;
    }

    /// <summary>
    /// Gets the method of the cardinal rules, which locales with the same rules share.
    /// </summary>
    internal PluralRule Cardinal { get; }

    /// <summary>
    /// Gets the method of the ordinal rules, which locales with the same rules share.
    /// </summary>
    internal PluralRule Ordinal { get; }

    /// <summary>
    /// Returns the cardinal category of a whole number.
    /// </summary>
    /// <param name="value">The number; its sign is not read.</param>
    /// <returns>The category, one of those the locale's cardinal rules have.</returns>
    public PluralCategory Select(long value) => Cardinal(PluralOperands.Of(value));

    /// <summary>
    /// Returns the cardinal category of a decimal number, whose scale is the number of its visible fraction digits: in English
    /// <c>1m</c> is one and <c>1.0m</c> is other. A number a format rounds is passed through that format's rounding first.
    /// </summary>
    /// <param name="value">The number; its sign is not read.</param>
    /// <returns>The category, one of those the locale's cardinal rules have.</returns>
    public PluralCategory Select(decimal value) => Cardinal(PluralOperands.Of(value, 0));

    /// <summary>
    /// Returns the ordinal category of a whole number.
    /// </summary>
    /// <param name="value">The number; its sign is not read.</param>
    /// <returns>The category, one of those the locale's ordinal rules have.</returns>
    public PluralCategory SelectOrdinal(long value) => Ordinal(PluralOperands.Of(value));

    /// <summary>
    /// Returns the cardinal category of a compact decimal (<c>1.2M</c>), which the compact format selects with (design §4.11,
    /// "Passing the number").
    /// </summary>
    /// <param name="value">The whole value the compact decimal stands for (1200000 for 1.2M), with the scale of its visible
    /// fraction digits.</param>
    /// <param name="exponent">The exponent of the power of 10 the compact decimal shows (6 for 1.2M).</param>
    /// <returns>The category.</returns>
    internal PluralCategory Select(decimal value, int exponent) => Cardinal(PluralOperands.Of(value, exponent));
}
