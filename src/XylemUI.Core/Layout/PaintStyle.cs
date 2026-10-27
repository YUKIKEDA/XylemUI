using System.Numerics;

namespace XylemUI;

/// <summary>
/// What a pseudo-state lays over a node's look (design §2.13): only values that do not change the layout. A value that is set
/// replaces the one below it; <see langword="null"/> keeps it (design §5.0, "Opacity, transforms, and pseudo-states").
/// </summary>
internal readonly record struct PaintStyle
{
    /// <summary>
    /// Gets the background; <see langword="null"/> keeps the one below.
    /// </summary>
    public Paint? Background { get; init; }

    /// <summary>
    /// Gets the color of the text, which the text leaves below take when they have none of their own; <see langword="null"/> keeps the
    /// one below.
    /// </summary>
    public Color? Foreground { get; init; }

    /// <summary>
    /// Gets the paint of the border; <see langword="null"/> keeps the one below.
    /// </summary>
    public Paint? BorderPaint { get; init; }

    /// <summary>
    /// Gets the opacity of the node and its subtree, from 0 to 1; <see langword="null"/> keeps the one below.
    /// </summary>
    public float? Opacity { get; init; }

    /// <summary>
    /// Gets the transform of the node and its subtree, about the node's center; <see langword="null"/> keeps the one below.
    /// </summary>
    public Matrix3x2? Transform { get; init; }
}

/// <summary>
/// The looks of a node's pseudo-states. Only a node given one has it (<see cref="NodeVisual.States"/>), so that a node with a
/// background alone does not hold two styles (PR #319's review).
/// </summary>
internal sealed class PseudoStyles
{
    /// <summary>
    /// Gets or sets what the hovered state lays over the node's look; <see langword="null"/> for nothing.
    /// </summary>
    public PaintStyle? Hovered { get; internal set; }

    /// <summary>
    /// Gets or sets what the pressed state lays over the hovered one; <see langword="null"/> for nothing.
    /// </summary>
    public PaintStyle? Pressed { get; internal set; }
}
