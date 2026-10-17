using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// What a <see cref="Length"/> or a <see cref="LengthPercentage"/> holds (design §3.2).
/// </summary>
internal enum LengthKind : byte
{
    /// <summary>The layout decides.</summary>
    Auto,

    /// <summary>A fixed number of logical pixels.</summary>
    Px,

    /// <summary>A fraction of the size the length resolves against (0.5 is 50 %).</summary>
    Percent,

    /// <summary>The content's smallest size: text breaks at every opportunity.</summary>
    MinContent,

    /// <summary>The content's largest size: text does not break.</summary>
    MaxContent,

    /// <summary>The available size, clamped between the content's smallest and largest sizes.</summary>
    FitContent,
}

/// <summary>
/// A size, a minimum or maximum size, or a flex basis in a style (design §3.2, CSS's <c>width</c>, <c>min-width</c>,
/// <c>max-width</c>, and <c>flex-basis</c>).
/// </summary>
/// <param name="Kind">What the length holds.</param>
/// <param name="Value">The pixels of <see cref="LengthKind.Px"/> or the fraction of <see cref="LengthKind.Percent"/>; 0 otherwise.</param>
internal readonly record struct Length(LengthKind Kind, float Value)
{
    /// <summary>
    /// Gets the length the layout decides.
    /// </summary>
    public static Length Auto => default;

    /// <summary>
    /// Gets the content's smallest size.
    /// </summary>
    public static Length MinContent => new(LengthKind.MinContent, 0);

    /// <summary>
    /// Gets the content's largest size.
    /// </summary>
    public static Length MaxContent => new(LengthKind.MaxContent, 0);

    /// <summary>
    /// Gets the available size clamped between the content's smallest and largest sizes.
    /// </summary>
    public static Length FitContent => new(LengthKind.FitContent, 0);

    /// <summary>
    /// Gets a value indicating whether the length is a fixed number of pixels.
    /// </summary>
    public bool IsPx => Kind == LengthKind.Px;

    /// <summary>
    /// Gets a value indicating whether the length is <see cref="LengthKind.Auto"/>.
    /// </summary>
    public bool IsAuto => Kind == LengthKind.Auto;

    /// <summary>
    /// Gets a value indicating whether the length is a percentage.
    /// </summary>
    public bool IsPercent => Kind == LengthKind.Percent;

    /// <summary>
    /// Returns a fixed length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Length Px(float pixels) => new(LengthKind.Px, pixels);

    /// <summary>
    /// Returns a percentage; <paramref name="fraction"/> 0.5 is 50 %.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Length Percent(float fraction) => new(LengthKind.Percent, fraction);

    /// <summary>
    /// Returns the pixels the length stands for against <paramref name="basis"/>, or NaN when it has none: an auto or content
    /// length, or a percentage of an unknown (NaN) basis.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Resolve(float basis) =>
        Kind switch
        {
            LengthKind.Px => Value,
            LengthKind.Percent => basis * Value,
            _ => float.NaN,
        };
}

/// <summary>
/// A padding, a border, or a margin in a style: a fixed number of pixels or a percentage (design §3.2).
/// </summary>
/// <param name="IsPercent">The value is a fraction of the size the length resolves against.</param>
/// <param name="Value">The pixels, or the fraction (0.5 is 50 %).</param>
internal readonly record struct LengthPercentage(bool IsPercent, float Value)
{
    /// <summary>
    /// Gets zero pixels.
    /// </summary>
    public static LengthPercentage Zero => default;

    /// <summary>
    /// Returns a fixed length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LengthPercentage Px(float pixels) => new(false, pixels);

    /// <summary>
    /// Returns a percentage; <paramref name="fraction"/> 0.5 is 50 %.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LengthPercentage Percent(float fraction) => new(true, fraction);

    /// <summary>
    /// Returns the pixels against <paramref name="basis"/>; a percentage of an unknown (NaN) basis is 0 (CSS).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ResolveOrZero(float basis) =>
        !IsPercent ? Value
        : float.IsNaN(basis) ? 0
        : basis * Value;
}

/// <summary>
/// A minimum or maximum size in a style: decided by the layout, a fixed number of pixels, or a percentage (design §3.2,
/// Taffy's <c>LengthPercentageAuto</c>).
/// </summary>
/// <param name="Kind">What the length holds: <see cref="LengthKind.Auto"/>, <see cref="LengthKind.Px"/>, or <see cref="LengthKind.Percent"/>.</param>
/// <param name="Value">The pixels, or the fraction (0.5 is 50 %); 0 for <see cref="LengthKind.Auto"/>.</param>
internal readonly record struct LengthPercentageAuto(LengthKind Kind, float Value)
{
    /// <summary>
    /// Gets the length the layout decides.
    /// </summary>
    public static LengthPercentageAuto Auto => default;

    /// <summary>
    /// Gets a value indicating whether the length is a percentage.
    /// </summary>
    public bool IsPercent => Kind == LengthKind.Percent;

    /// <summary>
    /// Returns a fixed length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LengthPercentageAuto Px(float pixels) => new(LengthKind.Px, pixels);

    /// <summary>
    /// Returns a percentage; <paramref name="fraction"/> 0.5 is 50 %.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LengthPercentageAuto Percent(float fraction) => new(LengthKind.Percent, fraction);

    /// <summary>
    /// Returns the pixels against <paramref name="basis"/>, or NaN for <see cref="LengthKind.Auto"/> and for a percentage of an
    /// unknown (NaN) basis.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Resolve(float basis) =>
        Kind switch
        {
            LengthKind.Px => Value,
            LengthKind.Percent => basis * Value,
            _ => float.NaN,
        };
}
