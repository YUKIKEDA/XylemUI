namespace XylemUI;

/// <summary>
/// A color of a gradient and where it is, from 0 at the start to 1 at the end (design §5.0, "Shapes, brushes, and shadows").
/// </summary>
/// <param name="Offset">Where the color is, from 0 to 1.</param>
/// <param name="Color">The color.</param>
public readonly record struct GradientStop(float Offset, Color Color);
