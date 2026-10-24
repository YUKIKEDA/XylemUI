using System.Numerics;

namespace XylemUI;

/// <summary>
/// A gradient, made once and not changed (design §5.0, "Shapes, brushes, and shadows"). Its positions are fractions of the box of
/// the shape it fills, so one brush fits shapes of every size, as CSS's gradients and Avalonia's relative points do. A style makes
/// it once and keeps it, so a frame that draws with it allocates nothing.
/// </summary>
/// <remarks>
/// The colors are interpolated premultiplied by their alpha, as CSS does, so a fade to a transparent color does not turn gray.
/// Before the first stop and after the last, the shape takes the color at that end; two stops at the same offset make a hard edge.
/// </remarks>
public sealed class GradientBrush
{
    private readonly GradientStop[] _stops;

    private GradientBrush(
        GradientKind kind,
        float angle,
        Vector2 center,
        float radius,
        ReadOnlySpan<GradientStop> stops,
        GradientColorSpace colorSpace
    )
    {
        if (stops.Length < 2)
        {
            throw new ArgumentException("A gradient has two stops or more.", nameof(stops));
        }

        for (int i = 0; i < stops.Length; i++)
        {
            if (!float.IsFinite(stops[i].Offset) || (i > 0 && stops[i].Offset < stops[i - 1].Offset))
            {
                throw new ArgumentException($"The offset of stop {i} is not a number at or after the stop before it.", nameof(stops));
            }
        }

        if (!float.IsFinite(angle) || !float.IsFinite(center.X) || !float.IsFinite(center.Y) || !(radius > 0) || !float.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "The angle and the center are numbers and the radius is a positive number.");
        }

        Kind = kind;
        Angle = angle;
        Center = center;
        Radius = radius;
        _stops = stops.ToArray();
        ColorSpace = colorSpace;
    }

    /// <summary>
    /// Gets the shape of the gradient.
    /// </summary>
    public GradientKind Kind { get; }

    /// <summary>
    /// Gets the angle, in degrees, clockwise from up: the direction of a linear gradient, as CSS's; where a conic gradient starts.
    /// </summary>
    public float Angle { get; }

    /// <summary>
    /// Gets the center of a radial or conic gradient, as a fraction of the shape's box (0.5, 0.5 is its middle).
    /// </summary>
    public Vector2 Center { get; }

    /// <summary>
    /// Gets the radius of a radial gradient, as a fraction of the larger of the box's width and height.
    /// </summary>
    public float Radius { get; }

    /// <summary>
    /// Gets the stops, two or more, in the order of their offsets.
    /// </summary>
    public ReadOnlySpan<GradientStop> Stops => _stops;

    /// <summary>
    /// Gets the color space the colors are interpolated in.
    /// </summary>
    public GradientColorSpace ColorSpace { get; }

    /// <summary>
    /// Gets or sets the last display list frame that wrote this brush, and <see cref="ListIndex"/> its place in that frame's
    /// brushes, so that a brush used many times in a frame is sent to the GPU once without a lookup.
    /// </summary>
    internal long ListFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the place of this brush among the brushes of <see cref="ListFrame"/>.
    /// </summary>
    internal int ListIndex { get; set; }

    /// <summary>
    /// Returns a linear gradient along a line at an angle, whose ends meet the corners of the shape's box, as CSS's
    /// <c>linear-gradient</c>.
    /// </summary>
    /// <param name="angle">The direction, in degrees clockwise from up: 0 runs from the bottom to the top, 90 from left to right.</param>
    /// <param name="stops">The stops, two or more, in the order of their offsets.</param>
    /// <param name="colorSpace">The color space to interpolate in.</param>
    /// <returns>The brush.</returns>
    /// <exception cref="ArgumentException">There are fewer than two stops, or an offset is not a number or goes back.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The angle is not a number.</exception>
    public static GradientBrush Linear(float angle, ReadOnlySpan<GradientStop> stops, GradientColorSpace colorSpace = GradientColorSpace.Srgb) =>
        new(GradientKind.Linear, angle, new Vector2(0.5f), 1, stops, colorSpace);

    /// <summary>
    /// Returns a radial gradient: circles out from a center.
    /// </summary>
    /// <param name="center">The center, as a fraction of the shape's box.</param>
    /// <param name="radius">The radius where the last stop is, as a fraction of the larger of the box's width and height.</param>
    /// <param name="stops">The stops, two or more, in the order of their offsets.</param>
    /// <param name="colorSpace">The color space to interpolate in.</param>
    /// <returns>The brush.</returns>
    /// <exception cref="ArgumentException">There are fewer than two stops, or an offset is not a number or goes back.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The center is not a number, or the radius is not a positive number.</exception>
    public static GradientBrush Radial(
        Vector2 center,
        float radius,
        ReadOnlySpan<GradientStop> stops,
        GradientColorSpace colorSpace = GradientColorSpace.Srgb
    ) => new(GradientKind.Radial, 0, center, radius, stops, colorSpace);

    /// <summary>
    /// Returns a conic gradient: around a center, clockwise from an angle, as CSS's <c>conic-gradient</c>.
    /// </summary>
    /// <param name="center">The center, as a fraction of the shape's box.</param>
    /// <param name="startAngle">Where the first stop is, in degrees clockwise from up.</param>
    /// <param name="stops">The stops, two or more, in the order of their offsets; offset 1 is a full turn.</param>
    /// <param name="colorSpace">The color space to interpolate in.</param>
    /// <returns>The brush.</returns>
    /// <exception cref="ArgumentException">There are fewer than two stops, or an offset is not a number or goes back.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The center or the angle is not a number.</exception>
    public static GradientBrush Conic(
        Vector2 center,
        float startAngle,
        ReadOnlySpan<GradientStop> stops,
        GradientColorSpace colorSpace = GradientColorSpace.Srgb
    ) => new(GradientKind.Conic, startAngle, center, 1, stops, colorSpace);
}
