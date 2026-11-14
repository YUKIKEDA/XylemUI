namespace XylemUI;

/// <summary>
/// Specifies a plural category of CLDR (LDML part 3, "Language Plural Rules"; design §4.11, "Plurals"). A locale's rules use some of
/// them; <see cref="Other"/> is in every locale's.
/// </summary>
public enum PluralCategory : byte
{
    /// <summary>
    /// The category zero.
    /// </summary>
    Zero,

    /// <summary>
    /// The category one.
    /// </summary>
    One,

    /// <summary>
    /// The category two.
    /// </summary>
    Two,

    /// <summary>
    /// The category few.
    /// </summary>
    Few,

    /// <summary>
    /// The category many.
    /// </summary>
    Many,

    /// <summary>
    /// The category other, which a value no other rule holds for takes.
    /// </summary>
    Other,
}
