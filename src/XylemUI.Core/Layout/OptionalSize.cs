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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Main(bool row) => row ? Width : Height;

    /// <summary>
    /// Returns the value of the cross axis of a flex container.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Cross(bool row) => row ? Height : Width;

    /// <summary>
    /// Returns this size with the main axis of a flex container replaced.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize WithMain(bool row, float value) => row ? this with { Width = value } : this with { Height = value };

    /// <summary>
    /// Returns this size with the cross axis of a flex container replaced.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize WithCross(bool row, float value) => row ? this with { Height = value } : this with { Width = value };

    /// <summary>
    /// Returns each value of this size, or of <paramref name="other"/> where this one has none.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize Or(OptionalSize other) => new(Maybe.Or(Width, other.Width), Maybe.Or(Height, other.Height));

    /// <summary>
    /// Returns each value clamped by the minimum and maximum of the same axis (each of which may be NaN).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize Clamp(OptionalSize min, OptionalSize max) =>
        new(Maybe.Clamp(Width, min.Width, max.Width), Maybe.Clamp(Height, min.Height, max.Height));

    /// <summary>
    /// Returns each known value no smaller than the matching value of <paramref name="floor"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionalSize AtLeast(Size floor) => new(Maybe.Max(Width, floor.Width), Maybe.Max(Height, floor.Height));

    /// <summary>
    /// Returns each known value less the matching value of <paramref name="amount"/>.
    /// </summary>
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Same(float a, float b) => a == b || (float.IsNaN(a) && float.IsNaN(b));

    /// <summary>
    /// Returns <paramref name="value"/>, or <paramref name="fallback"/> when it is NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Or(float value, float fallback) => float.IsNaN(value) ? fallback : value;

    /// <summary>
    /// Returns the larger of the value and a bound that may be NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max(float value, float bound) => float.IsNaN(value) || float.IsNaN(bound) || value >= bound ? value : bound;

    /// <summary>
    /// Returns the smaller of the value and a bound that may be NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min(float value, float bound) => float.IsNaN(value) || float.IsNaN(bound) || value <= bound ? value : bound;

    /// <summary>
    /// Returns the value clamped by bounds that may be NaN; the minimum wins over the maximum (CSS).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float value, float min, float max) => Max(Min(value, max), min);

    /// <summary>
    /// Returns the value plus an amount that may be NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Add(float value, float amount) => float.IsNaN(amount) ? value : value + amount;

    /// <summary>
    /// Returns the value less an amount that may be NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sub(float value, float amount) => float.IsNaN(amount) ? value : value - amount;
}
