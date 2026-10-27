using System.Numerics;

namespace XylemUI;

/// <summary>
/// An event of the pointer, as a handler receives it (design §7, "Pointer input"). Each handler gets its own copy, whose
/// <see cref="CurrentTarget"/> and <see cref="LocalPosition"/> are its node's.
/// </summary>
public readonly struct PointerEvent
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
        TargetNode = target;
        CurrentNode = currentTarget;
        Position = position;
        LocalPosition = localPosition;
        Button = source.Button;
        Buttons = source.Buttons;
        Modifiers = source.Modifiers;
        ClickCount = source.ClickCount;
        Timestamp = source.Timestamp;
        PointerType = source.PointerType;
        WheelDelta = source.WheelDelta;
    }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public PointerEventKind Kind { get; }

    /// <summary>
    /// Gets the node the route starts from: the node hit, or the node that holds the capture. For an enter or a leave, the node
    /// that entered or left.
    /// </summary>
    public Element Target => new(_input.Tree, TargetNode);

    /// <summary>
    /// Gets the node of the handler called now.
    /// </summary>
    public Element CurrentTarget => new(_input.Tree, CurrentNode);

    /// <summary>
    /// Gets the node of <see cref="Target"/>.
    /// </summary>
    internal NodeHandle TargetNode { get; }

    /// <summary>
    /// Gets the node of <see cref="CurrentTarget"/>.
    /// </summary>
    internal NodeHandle CurrentNode { get; }

    /// <summary>
    /// Gets the pointer's position in the window, in logical pixels: the physical position divided by the window's scale.
    /// </summary>
    public Vector2 Position { get; }

    /// <summary>
    /// Gets the pointer's position in <see cref="CurrentTarget"/>'s coordinates, in logical pixels: before the node's transforms,
    /// from the top left of its border box. It is not a number (<see cref="float.NaN"/>) for a node that wrote no hit region in the
    /// last frame: an ancestor on the route that got its first handler after that frame, before the next one draws.
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
    /// Gets how far a <see cref="PointerEventKind.Wheel"/> scrolls, in logical pixels: positive moves the scroll position down and
    /// to the right. One notch of a mouse's wheel is the lines Windows sets times 100/3 (design §3.4.1); a touchpad gives less. Zero
    /// for the other kinds.
    /// </summary>
    public Vector2 WheelDelta { get; }

    /// <summary>
    /// Stops the event: no handler after this one is called, in this phase or the next.
    /// </summary>
    public void StopPropagation() => _input.StopPropagation();
}

/// <summary>
/// The kinds of <see cref="PointerEvent"/>.
/// </summary>
public enum PointerEventKind : byte
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

    /// <summary>
    /// The wheel turned, or a touchpad scrolled (<see cref="PointerEvent.WheelDelta"/>). Routed from the node under the pointer,
    /// even while a button is captured; a scroll container whose position it changes stops it in the bubble phase (design §3.4.1).
    /// </summary>
    Wheel,
}

/// <summary>
/// The phase of a routed event a handler is called in (design §8.9).
/// </summary>
public enum EventPhase : byte
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
/// Handles an event of the pointer (design §8.9). It is not named <c>EventHandler</c>, which the implicit usings' <see cref="System.EventHandler{TEventArgs}"/> would hide (row V1d).
/// </summary>
/// <param name="e">The event.</param>
public delegate void PointerHandler(in PointerEvent e);
