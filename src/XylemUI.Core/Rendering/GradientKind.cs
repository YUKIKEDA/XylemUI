namespace XylemUI;

/// <summary>
/// The shape of a gradient (design §5.0, "Shapes, brushes, and shadows").
/// </summary>
public enum GradientKind : byte
{
    /// <summary>Along a line at an angle, as CSS's <c>linear-gradient</c>.</summary>
    Linear,

    /// <summary>Out from a center in circles, as CSS's <c>radial-gradient</c> with a circle.</summary>
    Radial,

    /// <summary>Around a center, as CSS's <c>conic-gradient</c>.</summary>
    Conic,
}
