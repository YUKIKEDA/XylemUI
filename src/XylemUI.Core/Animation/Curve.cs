namespace XylemUI;

/// <summary>
/// How an animation's progress, 0 to 1 over its duration, maps to the part of the way its value has moved (design §2.14, "Curves"):
/// a cubic Bézier from (0, 0) to (1, 1), as CSS's <c>cubic-bezier</c>. Fluent 2's curves are all of this form.
/// </summary>
/// <remarks>
/// The curve's x is solved for the progress by Newton's method, with bisection when the slope is too flat, to within 1/100,000:
/// Flutter's <c>Cubic</c> bisects, and Avalonia's <c>KeySpline</c> uses Newton's method. A curve makes nothing per call.
/// </remarks>
public sealed class Curve
{
    private const float Precision = 1e-5f;
    private readonly float _x1;
    private readonly float _y1;
    private readonly float _x2;
    private readonly float _y2;
    private readonly bool _linear;

    private Curve(float x1, float y1, float x2, float y2)
    {
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
        _linear = x1 == y1 && x2 == y2;
    }

    /// <summary>
    /// Gets the straight line: the value moves at the same speed the whole way.
    /// </summary>
    public static Curve Linear { get; } = new(0, 0, 1, 1);

    /// <summary>
    /// Gets CSS's <c>ease</c>: cubic-bezier(0.25, 0.1, 0.25, 1).
    /// </summary>
    public static Curve Ease { get; } = new(0.25f, 0.1f, 0.25f, 1);

    /// <summary>
    /// Gets CSS's <c>ease-in</c>: cubic-bezier(0.42, 0, 1, 1), slow at the start.
    /// </summary>
    public static Curve EaseIn { get; } = new(0.42f, 0, 1, 1);

    /// <summary>
    /// Gets CSS's <c>ease-out</c>: cubic-bezier(0, 0, 0.58, 1), slow at the end.
    /// </summary>
    public static Curve EaseOut { get; } = new(0, 0, 0.58f, 1);

    /// <summary>
    /// Gets CSS's <c>ease-in-out</c>: cubic-bezier(0.42, 0, 0.58, 1), slow at both ends.
    /// </summary>
    public static Curve EaseInOut { get; } = new(0.42f, 0, 0.58f, 1);

    /// <summary>
    /// Gets a fast start that settles: cubic-bezier(0, 0, 0, 1), Fluent 2's <c>curveDecelerateMid</c> and WinUI's
    /// <c>ControlFastOutSlowInKeySpline</c>.
    /// </summary>
    public static Curve DecelerateMid { get; } = new(0, 0, 0, 1);

    /// <summary>
    /// Returns the cubic Bézier through (0, 0), (<paramref name="x1"/>, <paramref name="y1"/>), (<paramref name="x2"/>,
    /// <paramref name="y2"/>), and (1, 1).
    /// </summary>
    /// <param name="x1">The x of the first control point, 0 to 1.</param>
    /// <param name="y1">The y of the first control point; it may leave 0 to 1, for a curve that overshoots.</param>
    /// <param name="x2">The x of the second control point, 0 to 1.</param>
    /// <param name="y2">The y of the second control point.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An x is outside 0 to 1, or a value is not finite.</exception>
    public static Curve CubicBezier(float x1, float y1, float x2, float y2)
    {
        if (!(x1 is >= 0 and <= 1) || !(x2 is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(x1), "The control points' x is 0 to 1, so that the curve is a function of time.");
        }

        if (!float.IsFinite(y1) || !float.IsFinite(y2))
        {
            throw new ArgumentOutOfRangeException(nameof(y1), "The control points' y is finite.");
        }

        return new Curve(x1, y1, x2, y2);
    }

    /// <summary>
    /// Returns the part of the way at a progress: 0 at 0, 1 at 1.
    /// </summary>
    /// <param name="progress">The progress, clamped to 0 to 1.</param>
    /// <returns>The curve's y where its x is the progress.</returns>
    public float Transform(float progress)
    {
        if (progress <= 0)
        {
            return 0;
        }

        if (progress >= 1)
        {
            return 1;
        }

        if (_linear)
        {
            return progress;
        }

        float t = SolveX(progress);
        return Bezier(t, _y1, _y2);
    }

    // B(t) of a coordinate whose end points are 0 and 1: 3(1 − t)²t·p1 + 3(1 − t)t²·p2 + t³.
    private static float Bezier(float t, float p1, float p2)
    {
        float u = 1 - t;
        return (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t);
    }

    private static float Slope(float t, float p1, float p2)
    {
        float u = 1 - t;
        return (3 * u * u * p1) + (6 * u * t * (p2 - p1)) + (3 * t * t * (1 - p2));
    }

    // The t whose x is the progress: Newton's method from the progress itself, then bisection if it did not converge.
    private float SolveX(float x)
    {
        float t = x;
        for (int i = 0; i < 8; i++)
        {
            float error = Bezier(t, _x1, _x2) - x;
            if (MathF.Abs(error) < Precision)
            {
                return t;
            }

            float slope = Slope(t, _x1, _x2);
            if (MathF.Abs(slope) < 1e-6f)
            {
                break;
            }

            t -= error / slope;
        }

        float low = 0;
        float high = 1;
        t = x;
        for (int i = 0; i < 64; i++)
        {
            float value = Bezier(t, _x1, _x2);
            if (MathF.Abs(value - x) < Precision)
            {
                break;
            }

            if (value < x)
            {
                low = t;
            }
            else
            {
                high = t;
            }

            t = (low + high) / 2;
        }

        return t;
    }
}
