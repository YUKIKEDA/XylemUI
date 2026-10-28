using System.Numerics;

namespace XylemUI;

/// <summary>
/// What a pseudo-state lays over a node's look (design §2.13): only values that do not change the layout. A value that is set
/// replaces the one below it; <see langword="null"/> keeps it (design §5.0, "Opacity, transforms, and pseudo-states").
/// </summary>
public readonly record struct PaintStyle
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

    /// <summary>
    /// Gets or sets the looks of the focus states, made the first time one is set: a node with a hovered look alone does not hold
    /// three more styles.
    /// </summary>
    public FocusStyles? Focus { get; internal set; }

    /// <summary>
    /// Gets or sets what the disabled state lays over every other look, the last and the strongest (design §2.13);
    /// <see langword="null"/> for nothing.
    /// </summary>
    public PaintStyle? Disabled { get; internal set; }

    /// <summary>
    /// Returns whether one state has a look. It tests each style where it is, without copying it out.
    /// </summary>
    /// <param name="state">One state.</param>
    /// <returns>Whether the state has a look.</returns>
    public bool Has(PseudoState state) =>
        state switch
        {
            PseudoState.Hovered => Hovered is not null,
            PseudoState.Pressed => Pressed is not null,
            PseudoState.Focused => Focus?.Focused is not null,
            PseudoState.FocusVisible => Focus?.FocusVisible is not null,
            PseudoState.FocusWithin => Focus?.FocusWithin is not null,
            PseudoState.Disabled => Disabled is not null,
            _ => false,
        };
}

/// <summary>
/// The looks of a node's focus states (design §2.13), laid over the pressed one in this order: focus within, focused, focus visible.
/// </summary>
internal sealed class FocusStyles
{
    /// <summary>
    /// Gets or sets what the focus-within state lays over the pressed one; <see langword="null"/> for nothing.
    /// </summary>
    public PaintStyle? FocusWithin { get; internal set; }

    /// <summary>
    /// Gets or sets what the focused state lays over the focus-within one; <see langword="null"/> for nothing.
    /// </summary>
    public PaintStyle? Focused { get; internal set; }

    /// <summary>
    /// Gets or sets what the focus-visible state lays over the focused one; <see langword="null"/> for nothing.
    /// </summary>
    public PaintStyle? FocusVisible { get; internal set; }
}
