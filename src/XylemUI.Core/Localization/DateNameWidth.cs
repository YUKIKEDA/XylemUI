namespace XylemUI;

/// <summary>
/// The width of a date name (design §4.11, "Names and the week"), CLDR's abbreviated, wide, narrow, and short forms.
/// </summary>
public enum DateNameWidth : byte
{
    /// <summary>
    /// The abbreviated form: <c>Feb</c> and <c>Tue</c> in English.
    /// </summary>
    Abbreviated,

    /// <summary>
    /// The wide form: <c>February</c> and <c>Tuesday</c> in English.
    /// </summary>
    Wide,

    /// <summary>
    /// The narrow form: <c>F</c> and <c>T</c> in English.
    /// </summary>
    Narrow,

    /// <summary>
    /// The short form of a weekday: <c>Tu</c> in English. Months, quarters, eras, and day periods have none.
    /// </summary>
    Short,
}
