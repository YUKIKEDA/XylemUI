namespace XylemUI;

/// <summary>
/// The fields of a text style that a container gives the text leaves below it, one bit each (design §3.2, "The text style a
/// container gives"): the four of the font, the size, and the line height. The color and the language are not among them.
/// </summary>
[Flags]
internal enum TextStyleFields : byte
{
    /// <summary>
    /// No field.
    /// </summary>
    None = 0,

    /// <summary>
    /// The font's family.
    /// </summary>
    Family = 1 << 0,

    /// <summary>
    /// The font's weight.
    /// </summary>
    Weight = 1 << 1,

    /// <summary>
    /// The font's slant.
    /// </summary>
    Slant = 1 << 2,

    /// <summary>
    /// The font's stretch.
    /// </summary>
    Stretch = 1 << 3,

    /// <summary>
    /// The size.
    /// </summary>
    Size = 1 << 4,

    /// <summary>
    /// The line height.
    /// </summary>
    LineHeight = 1 << 5,

    /// <summary>
    /// Every field.
    /// </summary>
    All = Family | Weight | Slant | Stretch | Size | LineHeight,
}
