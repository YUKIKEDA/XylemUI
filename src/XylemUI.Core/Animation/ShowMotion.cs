using System.Numerics;

namespace XylemUI;

/// <summary>
/// How a <c>Show</c>'s part enters and leaves (design §2.14, "The entry and exit of Show"): its opacity from 0 to 1, and an offset
/// from where it slides in, over a duration along a curve. <c>Show</c> moves them on the part's root node, apart from the values the
/// node sets itself. Fluent React's presence components and Flutter's <c>AnimatedSwitcher</c> work the same way.
/// </summary>
public sealed class ShowMotion
{
    private ShowMotion(TimeSpan duration, Curve curve, Vector2 offset)
    {
        Duration = duration;
        Curve = curve;
        Offset = offset;
    }

    /// <summary>
    /// Gets the duration of an entry or an exit.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the curve of an entry; an exit runs it backward.
    /// </summary>
    public Curve Curve { get; }

    /// <summary>
    /// Gets where the part is, in logical pixels from its place, when it is wholly out: it slides from there as it enters.
    /// </summary>
    public Vector2 Offset { get; }

    /// <summary>
    /// Returns a fade: the part's opacity goes from 0 to 1 as it enters, and back as it leaves.
    /// </summary>
    /// <param name="duration">The duration; above 0.</param>
    /// <param name="curve">The curve; <see cref="Curve.EaseOut"/> when <see langword="null"/>.</param>
    /// <returns>The motion.</returns>
    public static ShowMotion Fade(TimeSpan duration, Curve? curve = null) => new(Check(duration), curve ?? Curve.EaseOut, Vector2.Zero);

    /// <summary>
    /// Returns a fade with a slide: the part comes in from an offset as it fades in, and goes back there as it fades out.
    /// </summary>
    /// <param name="dx">The offset's x, in logical pixels.</param>
    /// <param name="dy">The offset's y, in logical pixels.</param>
    /// <param name="duration">The duration; above 0.</param>
    /// <param name="curve">The curve; <see cref="Curve.EaseOut"/> when <see langword="null"/>.</param>
    /// <returns>The motion.</returns>
    public static ShowMotion FadeSlide(float dx, float dy, TimeSpan duration, Curve? curve = null)
    {
        if (!float.IsFinite(dx) || !float.IsFinite(dy))
        {
            throw new ArgumentOutOfRangeException(nameof(dx), "The offset is finite.");
        }

        return new(Check(duration), curve ?? Curve.EaseOut, new Vector2(dx, dy));
    }

    private static TimeSpan Check(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        return duration;
    }
}
