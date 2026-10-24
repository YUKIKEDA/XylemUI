using System.Numerics;

namespace XylemUI;

/// <summary>
/// The shadow of a rounded rectangle, with CSS's <c>box-shadow</c> values (design §5.0, "Shapes, brushes, and shadows"). The
/// offset, blur, and spread are logical pixels, multiplied by the frame's scale and not rounded.
/// </summary>
/// <param name="Color">The color of the shadow.</param>
/// <param name="Offset">How far the shadow is moved from the shape.</param>
/// <param name="BlurRadius">The blur, CSS's: twice the standard deviation of the Gaussian (σ = <paramref name="BlurRadius"/> / 2).</param>
/// <param name="Spread">How much the shadow grows (or, when negative, shrinks) before it is blurred.</param>
/// <param name="Inset">Whether the shadow is drawn inside the shape, as from its edges, rather than outside it.</param>
public readonly record struct BoxShadow(Color Color, Vector2 Offset, float BlurRadius, float Spread = 0, bool Inset = false);
