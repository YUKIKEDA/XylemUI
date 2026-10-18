using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// A value for each side of a box: the padding, the border, or the margin of a style (design §3.2).
/// </summary>
/// <typeparam name="T">The kind of length the sides take.</typeparam>
/// <param name="Left">The left side.</param>
/// <param name="Top">The top side.</param>
/// <param name="Right">The right side.</param>
/// <param name="Bottom">The bottom side.</param>
internal readonly record struct EdgeInsets<T>(T Left, T Top, T Right, T Bottom)
    where T : struct
{
    /// <summary>
    /// Returns the same value on every side.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EdgeInsets<T> All(T value) => new(value, value, value, value);
}

/// <summary>
/// Creates the insets of a style in pixels.
/// </summary>
internal static class EdgeInsets
{
    /// <summary>
    /// Returns <paramref name="pixels"/> on every side.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EdgeInsets<LengthPercentage> All(float pixels) => EdgeInsets<LengthPercentage>.All(LengthPercentage.Px(pixels));

    /// <summary>
    /// Returns a margin of <paramref name="pixels"/> on every side.
    /// </summary>
    public static EdgeInsets<LengthPercentageAuto> Margin(float pixels) => EdgeInsets<LengthPercentageAuto>.All(LengthPercentageAuto.Px(pixels));

    /// <summary>
    /// Returns a margin of the given pixels on each side.
    /// </summary>
    public static EdgeInsets<LengthPercentageAuto> Margin(float left, float top, float right, float bottom) =>
        new(LengthPercentageAuto.Px(left), LengthPercentageAuto.Px(top), LengthPercentageAuto.Px(right), LengthPercentageAuto.Px(bottom));

    /// <summary>
    /// Returns the given pixels on each side.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EdgeInsets<LengthPercentage> Px(float left, float top, float right, float bottom) =>
        new(LengthPercentage.Px(left), LengthPercentage.Px(top), LengthPercentage.Px(right), LengthPercentage.Px(bottom));

    /// <summary>
    /// Returns the pixels of every side against <paramref name="basis"/> (CSS resolves a percentage of each side against the
    /// containing block's width).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Edges ResolveOrZero(in EdgeInsets<LengthPercentage> insets, float basis) =>
        new(insets.Left.ResolveOrZero(basis), insets.Top.ResolveOrZero(basis), insets.Right.ResolveOrZero(basis), insets.Bottom.ResolveOrZero(basis));

    /// <summary>
    /// Returns the pixels of every side of a margin against <paramref name="basis"/>; an automatic side is 0 until the free
    /// space is shared.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Edges ResolveOrZero(in EdgeInsets<LengthPercentageAuto> insets, float basis) =>
        new(insets.Left.ResolveOrZero(basis), insets.Top.ResolveOrZero(basis), insets.Right.ResolveOrZero(basis), insets.Bottom.ResolveOrZero(basis));

    /// <summary>
    /// Returns a value indicating whether a side is a percentage.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool UsesPercent(in EdgeInsets<LengthPercentageAuto> insets) =>
        insets.Left.IsPercent || insets.Top.IsPercent || insets.Right.IsPercent || insets.Bottom.IsPercent;

    /// <summary>
    /// Returns a value indicating whether a side is a percentage.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool UsesPercent(in EdgeInsets<LengthPercentage> insets) =>
        insets.Left.IsPercent || insets.Top.IsPercent || insets.Right.IsPercent || insets.Bottom.IsPercent;
}

/// <summary>
/// Insets resolved to pixels.
/// </summary>
/// <param name="Left">The left side.</param>
/// <param name="Top">The top side.</param>
/// <param name="Right">The right side.</param>
/// <param name="Bottom">The bottom side.</param>
internal readonly record struct Edges(float Left, float Top, float Right, float Bottom)
{
    /// <summary>
    /// Gets the left and right sides together.
    /// </summary>
    public float Horizontal => Left + Right;

    /// <summary>
    /// Gets the top and bottom sides together.
    /// </summary>
    public float Vertical => Top + Bottom;

    /// <summary>
    /// Returns the horizontal and the vertical sums.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Size Sum() => new(Horizontal, Vertical);

    /// <summary>
    /// Returns the sides of both insets added.
    /// </summary>
    public static Edges operator +(Edges a, Edges b) => new(a.Left + b.Left, a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom);

    /// <summary>
    /// Returns the sides of the main axis of a flex container together.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float MainSum(bool row) => row ? Horizontal : Vertical;

    /// <summary>
    /// Returns the sides of the cross axis of a flex container together.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float CrossSum(bool row) => row ? Vertical : Horizontal;

    /// <summary>
    /// Returns the start side of the main axis.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float MainStart(bool row) => row ? Left : Top;

    /// <summary>
    /// Returns the start side of the cross axis.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float CrossStart(bool row) => row ? Top : Left;
}
