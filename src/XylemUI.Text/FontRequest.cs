namespace XylemUI.Text;

/// <summary>
/// The font a text asks for (design §4.5): its family, weight, slant, and stretch.
/// </summary>
/// <param name="Family">The family name; <see langword="null"/> for the OS's UI font (design §4.5, the default font).</param>
/// <param name="Weight">The weight.</param>
/// <param name="Slant">The slant.</param>
/// <param name="Stretch">The stretch.</param>
public readonly record struct FontRequest(string? Family, FontWeight Weight, FontSlant Slant, FontStretch Stretch)
{
    /// <summary>
    /// Gets the OS's UI font at its normal weight, slant, and stretch.
    /// </summary>
    public static FontRequest Default => new(null, FontWeight.Normal, FontSlant.Normal, FontStretch.Normal);
}
