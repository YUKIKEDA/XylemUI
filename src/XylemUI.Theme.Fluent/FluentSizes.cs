namespace XylemUI.Theme.Fluent;

/// <summary>
/// The sizes of a <see cref="FluentTheme"/> (design §13.2, "テーマの値"): the stroke widths of Fluent's tokens and, from row B1, the
/// heights of parts, which change with the density. Fluent's token package has no heights (design §13.2, "密度"), so this type is
/// written by hand around the generated <see cref="FluentStrokeWidths"/>, and row B1 adds the heights here.
/// </summary>
public sealed record FluentSizes
{
    /// <summary>
    /// Gets the stroke widths (Fluent's <c>strokeWidth*</c> tokens).
    /// </summary>
    public required FluentStrokeWidths StrokeWidths { get; init; }

    /// <summary>
    /// Gets the sizes of <see cref="Density.Standard"/>. Until row B1 sets the sizes of <see cref="Density.Compact"/>, a compact
    /// theme has these too.
    /// </summary>
    internal static FluentSizes Standard { get; } = new() { StrokeWidths = FluentStrokeWidths.Standard };
}
