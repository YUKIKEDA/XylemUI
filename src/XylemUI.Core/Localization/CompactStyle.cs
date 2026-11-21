namespace XylemUI;

/// <summary>
/// The width of a compact number (design §4.11, "Numbers"), CLDR's short and long compact decimal formats.
/// </summary>
public enum CompactStyle : byte
{
    /// <summary>
    /// The short form: <c>1.2K</c> in English, <c>1.2万</c> in Japanese.
    /// </summary>
    Short,

    /// <summary>
    /// The long form: <c>1.2 thousand</c> in English.
    /// </summary>
    Long,
}
