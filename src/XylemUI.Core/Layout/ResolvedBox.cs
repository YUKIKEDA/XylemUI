namespace XylemUI;

/// <summary>
/// The lengths of a style without a percentage, in pixels: the same for every parent, so resolved once when the style is set
/// (row L4). An automatic or content-sized length is NaN, as resolving it gives.
/// </summary>
/// <param name="Size">The width and the height.</param>
/// <param name="Min">The minimum width and height.</param>
/// <param name="Max">The maximum width and height.</param>
/// <param name="Margin">The margins, an automatic side 0.</param>
/// <param name="PaddingBorder">The padding and the border, added together.</param>
internal readonly record struct ResolvedBox(OptionalSize Size, OptionalSize Min, OptionalSize Max, Edges Margin, Edges PaddingBorder);
