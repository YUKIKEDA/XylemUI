using System.Numerics;

namespace XylemUI;

/// <summary>
/// An event of the pointer, as a handler receives it (design §7, "Pointer input"). Each handler gets its own copy, whose
/// <see cref="CurrentTarget"/> and <see cref="LocalPosition"/> are its node's.
/// </summary>
internal readonly struct PointerEvent
{
    private readonly WindowInput _input;

    /// <summary>
    /// Initializes a new instance of the <see cref="PointerEvent"/> struct.
    /// </summary>
    internal PointerEvent(
        WindowInput input,
        PointerEventKind kind,
        NodeHandle target,
        NodeHandle currentTarget,
        Vector2 position,
        Vector2 localPosition,
        in PointerInput source
    )
    {
        _input = input;
        Kind = kind;
        Target = target;
        CurrentTarget = currentTarget;
        Position = position;
        LocalPosition = localPosition;
        Button = source.Button;
        Buttons = source.Buttons;
        Modifiers = source.Modifiers;
        ClickCount = source.ClickCount;
        Timestamp = source.Timestamp;
        PointerType = source.PointerType;
    }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public PointerEventKind Kind { get; }

    /// <summary>
    /// Gets the node the route starts from: the node hit, or the node that holds the capture. For an enter or a leave, the node
    /// that entered or left.
    /// </summary>
    public NodeHandle Target { get; }

    /// <summary>
    /// Gets the node of the handler called now.
    /// </summary>
    public NodeHandle CurrentTarget { get; }

    /// <summary>
    /// Gets the pointer's position in the window, in logical pixels: the physical position divided by the window's scale.
    /// </summary>
    public Vector2 Position { get; }

    /// <summary>
    /// Gets the pointer's position in <see cref="CurrentTarget"/>'s coordinates, in logical pixels: before the node's transforms,
    /// from the top left of its border box.
    /// </summary>
    public Vector2 LocalPosition { get; }

    /// <summary>
    /// Gets the button that went down or up; <see cref="PointerButton.None"/> for the other kinds.
    /// </summary>
    public PointerButton Button { get; }

    /// <summary>
    /// Gets the buttons held down.
    /// </summary>
    public PointerButtons Buttons { get; }

    /// <summary>
    /// Gets the modifier keys held down.
    /// </summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>
    /// Gets the presses in a row of the button, for a down, an up, and a click; 0 otherwise.
    /// </summary>
    public int ClickCount { get; }

    /// <summary>
    /// Gets when the platform received the input.
    /// </summary>
    public TimeSpan Timestamp { get; }

    /// <summary>
    /// Gets what the pointer is.
    /// </summary>
    public PointerType PointerType { get; }

    /// <summary>
    /// Stops the event: no handler after this one is called, in this phase or the next.
    /// </summary>
    public void StopPropagation() => _input.StopPropagation();
}

/// <summary>
/// The kinds of <see cref="PointerEvent"/>.
/// </summary>
internal enum PointerEventKind : byte
{
    /// <summary>
    /// A button went down. Routed.
    /// </summary>
    Down,

    /// <summary>
    /// A button went up. Routed.
    /// </summary>
    Up,

    /// <summary>
    /// The pointer moved. Routed.
    /// </summary>
    Move,

    /// <summary>
    /// The pointer came over the node or one of its descendants. Sent to the node alone.
    /// </summary>
    Enter,

    /// <summary>
    /// The pointer is over neither the node nor its descendants any more. Sent to the node alone.
    /// </summary>
    Leave,

    /// <summary>
    /// Another window took the capture; the press ends without an up. Routed.
    /// </summary>
    Cancel,

    /// <summary>
    /// The left button went down and up over the node or its descendants. Routed, in the bubble phase only.
    /// </summary>
    Click,
}

/// <summary>
/// The phase of a routed event a handler is called in (design §8.9).
/// </summary>
internal enum EventPhase : byte
{
    /// <summary>
    /// From the root down to the target.
    /// </summary>
    Capture,

    /// <summary>
    /// From the target up to the root.
    /// </summary>
    Bubble,
}

/// <summary>
/// Handles an event of the pointer (design §8.9). Its public name is decided in row V1d.
/// </summary>
/// <param name="e">The event.</param>
internal delegate void PointerHandler(in PointerEvent e);
