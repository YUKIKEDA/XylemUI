using System.Numerics;

namespace XylemUI;

/// <summary>
/// What a drop does with the value it carries (design §7, "Drag and drop"): the drag source allows a set, the drop target accepts a
/// set, and one of both is chosen.
/// </summary>
[Flags]
public enum DragEffects : byte
{
    /// <summary>
    /// Nothing: the drag was cancelled or dropped where nothing took it.
    /// </summary>
    None = 0,

    /// <summary>
    /// The target copies the value; the source keeps it.
    /// </summary>
    Copy = 1,

    /// <summary>
    /// The target takes the value; the source removes it.
    /// </summary>
    Move = 2,

    /// <summary>
    /// The target links to the value.
    /// </summary>
    Link = 4,
}

/// <summary>
/// How a drag source starts and ends its drags (design §7, "Drag and drop").
/// </summary>
public sealed record DragSourceOptions
{
    /// <summary>
    /// Gets what is drawn under the pointer while the value is dragged, in front of every popup and hit by nothing; with
    /// <see langword="null"/>, the default, the cursor alone shows the drag. It is made when a drag starts, in the source's scope.
    /// </summary>
    public Func<Element>? Preview { get; init; }

    /// <summary>
    /// Gets the effects a drop may choose; <see cref="DragEffects.Move"/> by default.
    /// </summary>
    public DragEffects Allowed { get; init; } = DragEffects.Move;

    /// <summary>
    /// Gets what the value is outside the app, for a drag that leaves the window (design §7, "Drag and drop", the second PR of row
    /// DD1); with <see langword="null"/>, the default, a drag stays in the app when the pointer leaves the window.
    /// </summary>
    public Func<ExternalDragData?>? External { get; init; }

    /// <summary>
    /// Gets what runs once when a drag ends, with the effect the drop chose, or <see cref="DragEffects.None"/> when it was cancelled
    /// or dropped where nothing took it. It runs even when the source's node left the tree during the drag, so it does not read the
    /// source's nodes.
    /// </summary>
    public Action<DragEffects>? Ended { get; init; }
}

/// <summary>
/// What a drop target does with the drags of values of type <typeparamref name="T"/> (design §7, "Drag and drop").
/// </summary>
/// <typeparam name="T">The type of the values it takes; a value of a type assignable to it matches.</typeparam>
public sealed record DropTargetOptions<T>
    where T : notnull
{
    /// <summary>
    /// Gets whether the target takes a value now; with <see langword="null"/>, the default, it takes every value of the type.
    /// </summary>
    public Func<T, bool>? CanDrop { get; init; }

    /// <summary>
    /// Gets the effects the target accepts; all three by default.
    /// </summary>
    public DragEffects Accepts { get; init; } = DragEffects.Copy | DragEffects.Move | DragEffects.Link;

    /// <summary>
    /// Gets what runs when a drag comes over the target.
    /// </summary>
    public DragHandler<T>? Enter { get; init; }

    /// <summary>
    /// Gets what runs when a drag over the target moves, or its effect changes with the modifier keys.
    /// </summary>
    public DragHandler<T>? Over { get; init; }

    /// <summary>
    /// Gets what runs when a drag leaves the target, is cancelled over it, or moves to another target.
    /// </summary>
    public DragHandler<T>? Leave { get; init; }

    /// <summary>
    /// Gets what runs when the value is dropped on the target.
    /// </summary>
    public required DragHandler<T> Drop { get; init; }
}

/// <summary>
/// A handler of a drag over a drop target.
/// </summary>
/// <typeparam name="T">The type the target takes.</typeparam>
/// <param name="e">The drag.</param>
public delegate void DragHandler<T>(in DragEvent<T> e)
    where T : notnull;

/// <summary>
/// A drag over a drop target, as its handler receives it (design §7, "Drag and drop").
/// </summary>
/// <typeparam name="T">The type the target takes.</typeparam>
public readonly struct DragEvent<T>
    where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DragEvent{T}"/> struct.
    /// </summary>
    /// <param name="value">The value dragged.</param>
    /// <param name="position">Where the pointer is, in logical pixels from the target's top left.</param>
    /// <param name="effect">The effect a drop chooses now.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    internal DragEvent(T value, Vector2 position, DragEffects effect, KeyModifiers modifiers)
    {
        Value = value;
        Position = position;
        Effect = effect;
        Modifiers = modifiers;
    }

    /// <summary>
    /// Gets the value dragged.
    /// </summary>
    public T Value { get; }

    /// <summary>
    /// Gets where the pointer is, in logical pixels from the target's top left.
    /// </summary>
    public Vector2 Position { get; }

    /// <summary>
    /// Gets the effect a drop chooses now, from the source's allowed effects, the target's accepted ones, and the modifier keys.
    /// </summary>
    public DragEffects Effect { get; }

    /// <summary>
    /// Gets the modifier keys held.
    /// </summary>
    public KeyModifiers Modifiers { get; }
}

/// <summary>
/// The files dropped from outside the app, as the value of a drag (design §7, "Drag and drop", the second PR of row DD1).
/// </summary>
/// <param name="Paths">The files' full paths.</param>
public sealed record DroppedFiles(IReadOnlyList<string> Paths);

/// <summary>
/// The text dropped from outside the app, as the value of a drag (design §7, "Drag and drop", the second PR of row DD1).
/// </summary>
/// <param name="Text">The text.</param>
public sealed record DroppedText(string Text);

/// <summary>
/// What a dragged value is outside the app (design §7, "Drag and drop", the second PR of row DD1): files, text, or both.
/// </summary>
/// <param name="Files">The files' full paths, or <see langword="null"/>.</param>
/// <param name="Text">The text, or <see langword="null"/>.</param>
public sealed record ExternalDragData(IReadOnlyList<string>? Files, string? Text);
