namespace XylemUI.Text;

/// <summary>
/// The width of a font's design relative to its normal width, as OpenType's <c>usWidthClass</c> and CSS's <c>font-stretch</c>.
/// </summary>
public enum FontStretch
{
    /// <summary>50 % of the normal width.</summary>
    UltraCondensed = 1,

    /// <summary>62.5 %.</summary>
    ExtraCondensed = 2,

    /// <summary>75 %.</summary>
    Condensed = 3,

    /// <summary>87.5 %.</summary>
    SemiCondensed = 4,

    /// <summary>The normal width.</summary>
    Normal = 5,

    /// <summary>112.5 %.</summary>
    SemiExpanded = 6,

    /// <summary>125 %.</summary>
    Expanded = 7,

    /// <summary>150 %.</summary>
    ExtraExpanded = 8,

    /// <summary>200 %.</summary>
    UltraExpanded = 9,
}
