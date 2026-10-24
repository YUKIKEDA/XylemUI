namespace XylemUI;

/// <summary>
/// A dash pattern (design §5.0, "The API of lines, ellipses, arcs, and the paint of text"): <paramref name="On"/> is drawn, then
/// <paramref name="Off"/> is left, again and again from the start of the line. The lengths are logical pixels, as SVG's
/// <c>stroke-dasharray</c>, not multiples of the width. Each dash takes the stroke's caps, so a dash of length 0 with
/// <see cref="LineCap.Round"/> is a dot.
/// </summary>
/// <param name="On">The length drawn.</param>
/// <param name="Off">The length left between two dashes.</param>
/// <param name="Offset">How far into the pattern the line starts, as SVG's <c>stroke-dashoffset</c>.</param>
public readonly record struct Dash(float On, float Off, float Offset = 0);
