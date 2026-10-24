namespace XylemUI;

/// <summary>
/// What a shape is filled with: a solid color, or a gradient (design §5.0, "Shapes, brushes, and shadows").
/// </summary>
/// <param name="Color">The color, used when <paramref name="Gradient"/> is <see langword="null"/>.</param>
/// <param name="Gradient">The gradient, or <see langword="null"/> for the solid color.</param>
public readonly record struct Paint(Color Color, GradientBrush? Gradient = null)
{
    /// <summary>
    /// Returns the paint of a solid color.
    /// </summary>
    /// <param name="color">The color.</param>
    public static implicit operator Paint(Color color) => new(color);

    /// <summary>
    /// Returns the paint of a gradient.
    /// </summary>
    /// <param name="gradient">The gradient.</param>
    public static implicit operator Paint(GradientBrush gradient) => new(default, gradient);
}
