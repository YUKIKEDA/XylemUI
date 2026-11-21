namespace XylemUI;

/// <summary>
/// One pattern of a locale's compact decimal format (UTS #35 part 3, "Compact Number Formats"; design §4.11, "Numbers"): the power
/// of ten it applies from, the plural category or the explicit value 1 it is for, and the pattern (<c>00K</c>).
/// </summary>
internal sealed class CompactPattern
{
    /// <summary>
    /// The category byte of a pattern for the shown number 1 alone (CLDR's <c>1000-count-1</c>, French <c>mille</c>); bytes 0 to 5
    /// are the members of <see cref="PluralCategory"/>.
    /// </summary>
    public const int ExplicitOne = 6;

    /// <summary>
    /// Gets the exponent of the power of ten the pattern applies from (3 for 1000).
    /// </summary>
    public required int Exponent { get; init; }

    /// <summary>
    /// Gets the plural category the pattern is for, as a byte, or <see cref="ExplicitOne"/>.
    /// </summary>
    public required int Category { get; init; }

    /// <summary>
    /// Gets the pattern read into its parts; its <see cref="NumberPattern.IntegerZeros"/> are the digits it shows.
    /// </summary>
    public required NumberPattern Pattern { get; init; }

    /// <summary>
    /// Gets a value indicating whether the pattern is <c>0</c>, which CLDR writes for a power of ten the locale does not compact
    /// (Japanese 1000): the number is written in full.
    /// </summary>
    public required bool Uncompacted { get; init; }

    /// <summary>
    /// Reads the patterns of one width from a locale's compact table, in the order tools/CldrTableGen writes them: for the short
    /// and then the long width, a count, and for each pattern its exponent, its category byte, and its text, ordered by exponent and
    /// then by category.
    /// </summary>
    /// <param name="table">The table (<see cref="LocaleData.CompactTable"/>).</param>
    /// <param name="style">The width.</param>
    /// <param name="symbols">The locale's symbols, for a minus sign in a pattern.</param>
    /// <returns>The patterns, ordered by exponent.</returns>
    public static CompactPattern[] Read(ReadOnlySpan<byte> table, CompactStyle style, NumberSymbols symbols)
    {
        var reader = new CldrTableReader(table);
        if (style == CompactStyle.Long)
        {
            int skipped = reader.ReadByte();
            for (int i = 0; i < skipped; i++)
            {
                _ = reader.ReadByte();
                _ = reader.ReadByte();
                reader.SkipString();
            }
        }

        var patterns = new CompactPattern[reader.ReadByte()];
        for (int i = 0; i < patterns.Length; i++)
        {
            int exponent = reader.ReadByte();
            int category = reader.ReadByte();
            string text = reader.ReadString();
            patterns[i] = new CompactPattern
            {
                Exponent = exponent,
                Category = category,
                Pattern = NumberPattern.Parse(text, symbols, null, false),
                Uncompacted = text == "0",
            };
        }

        return patterns;
    }
}
