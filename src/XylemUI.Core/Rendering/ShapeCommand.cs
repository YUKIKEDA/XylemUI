namespace XylemUI;

/// <summary>
/// A rounded rectangle with a border, read from a <see cref="DisplayList"/> (design §5.0, "Shapes, brushes, and shadows"). The
/// rectangle's edges are on whole physical pixels; the radii and widths are physical pixels, not rounded.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Fill">The color of the fill, when <paramref name="FillBrush"/> is −1.</param>
/// <param name="FillBrush">The fill's gradient, as an index into <see cref="DisplayListReader.Brushes"/>, or −1.</param>
/// <param name="Radii">The radius of each corner.</param>
/// <param name="BorderWidths">The width of each edge of the border; zero for no border.</param>
/// <param name="BorderColor">The color of the border, when <paramref name="BorderBrush"/> is −1.</param>
/// <param name="BorderBrush">The border's gradient, as an index into <see cref="DisplayListReader.Brushes"/>, or −1.</param>
public readonly record struct ShapeCommand(
    float X,
    float Y,
    float Width,
    float Height,
    Color Fill,
    int FillBrush,
    CornerRadii Radii,
    BorderWidths BorderWidths,
    Color BorderColor,
    int BorderBrush
);
