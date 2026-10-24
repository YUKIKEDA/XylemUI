using System.Numerics;

namespace XylemUI;

/// <summary>
/// The shadow of a rounded rectangle, read from a <see cref="DisplayList"/> (design §5.0, "Shapes, brushes, and shadows"). The
/// shape's edges are on whole physical pixels; the radii, offset, spread, and sigma are physical pixels, not rounded.
/// </summary>
/// <param name="X">The left edge of the shape that casts the shadow.</param>
/// <param name="Y">The top edge of the shape.</param>
/// <param name="Width">The width of the shape.</param>
/// <param name="Height">The height of the shape.</param>
/// <param name="Radii">The radius of each corner of the shape.</param>
/// <param name="Color">The color of the shadow.</param>
/// <param name="Offset">How far the shadow is moved from the shape.</param>
/// <param name="Sigma">The standard deviation of the blur: half of CSS's blur radius.</param>
/// <param name="Spread">How much the shadow grows before it is blurred.</param>
/// <param name="Inset">Whether the shadow is drawn inside the shape.</param>
public readonly record struct ShadowCommand(
    float X,
    float Y,
    float Width,
    float Height,
    CornerRadii Radii,
    Color Color,
    Vector2 Offset,
    float Sigma,
    float Spread,
    bool Inset
);
