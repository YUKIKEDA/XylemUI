namespace XylemUI.Text;

/// <summary>
/// The weight of a font, from 1 (thinnest) to 999 (heaviest), as OpenType's <c>usWeightClass</c> and CSS's <c>font-weight</c>.
/// Its default is <see cref="Normal"/>.
/// </summary>
public readonly record struct FontWeight
{
    // The distance from 400, so that default(FontWeight) is the regular weight, not an invalid 0.
    private readonly short _fromNormal;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontWeight"/> struct.
    /// </summary>
    /// <param name="value">The weight, from 1 to 999.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is below 1 or above 999.</exception>
    public FontWeight(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 999);
        _fromNormal = (short)(value - 400);
    }

    /// <summary>
    /// Gets the regular weight, 400.
    /// </summary>
    public static FontWeight Normal => new(400);

    /// <summary>
    /// Gets the bold weight, 700.
    /// </summary>
    public static FontWeight Bold => new(700);

    /// <summary>
    /// Gets the weight, from 1 to 999.
    /// </summary>
    public int Value => 400 + _fromNormal;
}
