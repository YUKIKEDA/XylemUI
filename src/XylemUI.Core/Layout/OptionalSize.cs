using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// A width and a height, each of which may be unknown: NaN stands for "no value" (Taffy's <c>Size&lt;Option&lt;f32&gt;&gt;</c>).
/// </summary>
/// <param name="Width">The width, or NaN.</param>
/// <param name="Height">The height, or NaN.</param>
internal readonly record struct OptionalSize(float Width, float Height)
{
    /// <summary>
    /// Gets a size with neither value.
    /// </summary>
    public static OptionalSize None => new(float.NaN, float.NaN);

    /// <summary>
    /// Gets a value indicating whether both values are known.
    /// </summary>
    public bool IsComplete => !float.IsNaN(Width) && !float.IsNaN(Height);

    /// <summary>
    /// Returns the value of the main axis of a flex container.
    /// </summary>
    /// <param name="row"><see langword="true"/> when the main axis is horizontal.</param>
    /// <returns>The width when <paramref name="row"/> is <see langword="true"/>; otherwise the height.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Main(bool row) => row ? Width : Height;

    /// <summary>
    /// Returns the value of the cross axis of a flex container.
    /// </summary>
    /// <param name="row"><see langword="true"/> when the main axis is horizontal.</param>
    /// <returns>The height when <paramref name="row"/> is <see langword="true"/>; otherwise the width.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Cross(bool row) => row ? Height : Width;

    /// <summary>
    /// Returns this size with the main axis of a flex container replaced.
    /// </summary>
    /// <param name="row"><see langword="true"/> when the main axis is horizontal.</param>
    /// <param name="value">The new value of the main axis, or NaN.</param>
    /// <returns>The size with the main axis set to <paramref name="value"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize WithMain(bool row, float value) => row ? this with { Width = value } : this with { Height = value };

    /// <summary>
    /// Returns this size with the cross axis of a flex container replaced.
    /// </summary>
    /// <param name="row"><see langword="true"/> when the main axis is horizontal.</param>
    /// <param name="value">The new value of the cross axis, or NaN.</param>
    /// <returns>The size with the cross axis set to <paramref name="value"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize WithCross(bool row, float value) => row ? this with { Height = value } : this with { Width = value };

    /// <summary>
    /// Returns each value of this size, or of <paramref name="other"/> where this one has none.
    /// </summary>
    /// <param name="other">The size whose values fill the unknown values of this one.</param>
    /// <returns>The size with each unknown value taken from <paramref name="other"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize Or(OptionalSize other) => new(Maybe.Or(Width, other.Width), Maybe.Or(Height, other.Height));

    /// <summary>
    /// Returns each value clamped by the minimum and maximum of the same axis (each of which may be NaN).
    /// </summary>
    /// <param name="min">The minimum of each axis, or NaN.</param>
    /// <param name="max">The maximum of each axis, or NaN.</param>
    /// <returns>The clamped size.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize Clamp(OptionalSize min, OptionalSize max) =>
        new(Maybe.Clamp(Width, min.Width, max.Width), Maybe.Clamp(Height, min.Height, max.Height));

    /// <summary>
    /// Returns each known value no smaller than the matching value of <paramref name="floor"/>.
    /// </summary>
    /// <param name="floor">The smallest value of each axis.</param>
    /// <returns>The size with each known value raised to <paramref name="floor"/>; unknown values stay unknown.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize AtLeast(Size floor) => new(Maybe.Max(Width, floor.Width), Maybe.Max(Height, floor.Height));

    /// <summary>
    /// Returns each known value less the matching value of <paramref name="amount"/>.
    /// </summary>
    /// <param name="amount">The amount to take from each axis.</param>
    /// <returns>The size less <paramref name="amount"/>; unknown values stay unknown.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize Minus(Size amount) => new(Width - amount.Width, Height - amount.Height);
}

/// <summary>
/// The arithmetic of values that may be unknown, NaN standing for "no value" (Taffy's <c>MaybeMath</c>). An unknown bound or
/// operand leaves the value as it is; an unknown value stays unknown.
/// </summary>
internal static class Maybe
{
    /// <summary>
    /// Returns a value indicating whether two values are equal, NaN equal to NaN.
    /// </summary>
    /// <param name="a">The first value, or NaN.</param>
    /// <param name="b">The second value, or NaN.</param>
    /// <returns><see langword="true"/> when the values are equal or both NaN; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Same(float a, float b) => a == b || (float.IsNaN(a) && float.IsNaN(b));

    /// <summary>
    /// Returns <paramref name="value"/>, or <paramref name="fallback"/> when it is NaN.
    /// </summary>
    /// <param name="value">The value, or NaN.</param>
    /// <param name="fallback">The value to return when <paramref name="value"/> is NaN.</param>
    /// <returns><paramref name="value"/>, or <paramref name="fallback"/> when it is NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Or(float value, float fallback) => float.IsNaN(value) ? fallback : value;

    /// <summary>
    /// Returns the larger of the value and a bound that may be NaN.
    /// </summary>
    /// <param name="value">The value, or NaN.</param>
    /// <param name="bound">The lower bound, or NaN.</param>
    /// <returns>The larger of the two, or <paramref name="value"/> when either is NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max(float value, float bound) => float.IsNaN(value) || float.IsNaN(bound) || value >= bound ? value : bound;

    /// <summary>
    /// Returns the smaller of the value and a bound that may be NaN.
    /// </summary>
    /// <param name="value">The value, or NaN.</param>
    /// <param name="bound">The upper bound, or NaN.</param>
    /// <returns>The smaller of the two, or <paramref name="value"/> when either is NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min(float value, float bound) => float.IsNaN(value) || float.IsNaN(bound) || value <= bound ? value : bound;

    /// <summary>
    /// Returns the value clamped by bounds that may be NaN; the minimum wins over the maximum (CSS).
    /// </summary>
    /// <param name="value">The value, or NaN.</param>
    /// <param name="min">The minimum, or NaN.</param>
    /// <param name="max">The maximum, or NaN.</param>
    /// <returns>The clamped value; NaN when <paramref name="value"/> is NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float value, float min, float max) => Max(Min(value, max), min);

    /// <summary>
    /// Returns the value plus an amount that may be NaN.
    /// </summary>
    /// <param name="value">The value, or NaN.</param>
    /// <param name="amount">The amount to add, or NaN.</param>
    /// <returns>The sum, or <paramref name="value"/> when <paramref name="amount"/> is NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Add(float value, float amount) => float.IsNaN(amount) ? value : value + amount;

    /// <summary>
    /// Returns the value less an amount that may be NaN.
    /// </summary>
    /// <param name="value">The value, or NaN.</param>
    /// <param name="amount">The amount to take away, or NaN.</param>
    /// <returns>The difference, or <paramref name="value"/> when <paramref name="amount"/> is NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sub(float value, float amount) => float.IsNaN(amount) ? value : value - amount;
}
