using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// What an <see cref="AvailableSpace"/> holds.
/// </summary>
internal enum AvailableSpaceKind : byte
{
    /// <summary>A number of pixels.</summary>
    Definite,

    /// <summary>The node is sized under a min-content constraint.</summary>
    MinContent,

    /// <summary>The node is sized under a max-content constraint, or the space is not bounded.</summary>
    MaxContent,
}

/// <summary>
/// The space a parent gives a child's border box on one axis (design §3.2, Taffy's <c>AvailableSpace</c>).
/// </summary>
/// <param name="Kind">What the space holds.</param>
/// <param name="Value">The pixels of <see cref="AvailableSpaceKind.Definite"/>; 0 otherwise.</param>
internal readonly record struct AvailableSpace(AvailableSpaceKind Kind, float Value)
{
    /// <summary>
    /// Gets the min-content constraint.
    /// </summary>
    public static AvailableSpace MinContent => new(AvailableSpaceKind.MinContent, 0);

    /// <summary>
    /// Gets the max-content constraint.
    /// </summary>
    public static AvailableSpace MaxContent => new(AvailableSpaceKind.MaxContent, 0);

    /// <summary>
    /// Gets a value indicating whether the space is a number of pixels.
    /// </summary>
    public bool IsDefinite => Kind == AvailableSpaceKind.Definite;

    /// <summary>
    /// Returns a space of <paramref name="pixels"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AvailableSpace Definite(float pixels) => new(AvailableSpaceKind.Definite, pixels);

    /// <summary>
    /// Returns a definite space of <paramref name="pixels"/>, or this space when <paramref name="pixels"/> is NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AvailableSpace OrDefinite(float pixels) => float.IsNaN(pixels) ? this : Definite(pixels);

    /// <summary>
    /// Returns the pixels, or NaN when the space is a content constraint.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ToOption() => IsDefinite ? Value : float.NaN;

    /// <summary>
    /// Returns a definite space less <paramref name="pixels"/>; a content constraint stays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AvailableSpace Minus(float pixels) => IsDefinite ? Definite(Value - pixels) : this;

    /// <summary>
    /// Returns a definite space no smaller than <paramref name="pixels"/>; a content constraint stays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AvailableSpace AtLeast(float pixels) => IsDefinite ? Definite(Math.Max(Value, pixels)) : this;

    /// <summary>
    /// Returns a definite space clamped by a minimum and a maximum that may be NaN; a content constraint stays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AvailableSpace Clamp(float min, float max) => IsDefinite ? Definite(Maybe.Clamp(Value, min, max)) : this;
}

/// <summary>
/// The space a parent gives a child's border box on both axes.
/// </summary>
/// <param name="Width">The horizontal space.</param>
/// <param name="Height">The vertical space.</param>
internal readonly record struct AvailableSize(AvailableSpace Width, AvailableSpace Height)
{
    /// <summary>
    /// Gets the max-content constraint on both axes.
    /// </summary>
    public static AvailableSize MaxContent => new(AvailableSpace.MaxContent, AvailableSpace.MaxContent);

    /// <summary>
    /// Returns the space of the main axis of a flex container.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AvailableSpace Main(bool row) => row ? Width : Height;

    /// <summary>
    /// Returns the space of the cross axis of a flex container.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AvailableSpace Cross(bool row) => row ? Height : Width;

    /// <summary>
    /// Returns the space made of a main and a cross axis of a flex container.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AvailableSize FromAxes(bool row, AvailableSpace main, AvailableSpace cross) => row ? new(main, cross) : new(cross, main);
}
