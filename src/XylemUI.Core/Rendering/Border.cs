namespace XylemUI;

/// <summary>
/// The border of a rectangle: the width of each edge, drawn inside the rectangle, and its paint (design §5.0, "Shapes, brushes, and
/// shadows").
/// </summary>
/// <param name="Widths">The width of each edge, in logical pixels.</param>
/// <param name="Paint">The paint of the border.</param>
public readonly record struct Border(BorderWidths Widths, Paint Paint);
