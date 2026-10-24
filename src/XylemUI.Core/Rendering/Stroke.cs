namespace XylemUI;

/// <summary>
/// How a line, the outline of an ellipse, or an arc is drawn (design §5.0, "The API of lines, ellipses, arcs, and the paint of
/// text"). The line lies on the middle of the shape's outline, half its width on each side, as in Flutter, Avalonia, Skia, and SVG.
/// A style makes it once and keeps it, as it does a <see cref="Border"/>.
/// </summary>
/// <param name="Paint">The paint of the line.</param>
/// <param name="Width">
/// The width, in logical pixels. A width of 0 draws nothing; a line thinner than a pixel is drawn lighter, by the share of the
/// pixel it covers, not thinner.
/// </param>
/// <param name="Cap">The shape of the ends, and of each dash's ends.</param>
/// <param name="Dash">The dash pattern, or <see langword="null"/> for a solid line.</param>
public readonly record struct Stroke(Paint Paint, float Width, LineCap Cap = LineCap.Flat, Dash? Dash = null);
