using System.Numerics;

namespace XylemUI;

/// <summary>
/// A line, an ellipse, an arc, or a sector, read from a <see cref="DisplayList"/> (design §5.0, "The API of lines, ellipses, arcs,
/// and the paint of text"). Every length is in physical pixels and is not rounded; angles are radians, clockwise from up.
/// </summary>
/// <param name="A">A <see cref="CommandTag.Line"/>'s first end's x; otherwise the center's x.</param>
/// <param name="B">A line's first end's y; otherwise the center's y.</param>
/// <param name="C">A line's second end's x; an ellipse's radius along x; an arc's or a sector's radius.</param>
/// <param name="D">A line's second end's y; an ellipse's radius along y; 0 for an arc or a sector.</param>
/// <param name="Color">The color, when <paramref name="Brush"/> is −1.</param>
/// <param name="Brush">The gradient, as an index into <see cref="DisplayListReader.Brushes"/>, or −1.</param>
/// <param name="Box">The box the gradient's fractions are of: left, top, width, and height.</param>
/// <param name="Width">The width of the line; 0 for a filled ellipse or a sector.</param>
/// <param name="Cap">The shape of the ends.</param>
/// <param name="Dash">The dash pattern, its lengths in physical pixels, or <see langword="null"/>.</param>
/// <param name="StartAngle">Where an arc or a sector starts.</param>
/// <param name="SweepAngle">How far an arc or a sector runs clockwise from <paramref name="StartAngle"/>, more than 0 and at most 2π (2π for a circle).</param>
public readonly record struct CurveCommand(
    float A,
    float B,
    float C,
    float D,
    Color Color,
    int Brush,
    Vector4 Box,
    float Width,
    LineCap Cap,
    Dash? Dash,
    float StartAngle,
    float SweepAngle
);
