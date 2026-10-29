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
        in PointerInput source,
        int dispatch
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
        PointerId = source.PointerId;
        IsPrimary = source.IsPrimary;
        Pressure = source.Pressure;
        TiltX = source.TiltX;
        TiltY = source.TiltY;
        Twist = source.Twist;
        IsEraser = (source.PenFlags & PenFlags.Eraser) != 0;
        IsInverted = (source.PenFlags & PenFlags.Inverted) != 0;
        HistoryCount = source.HistoryCount;
        Dispatch = dispatch;
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
    /// Gets the pointer's number (design §7, "Touch and pen"): 0 for the mouse; 1 and up for touches and pens, each its own while
    /// it is present, so that several fingers tell apart.
    /// </summary>
    public int PointerId { get; }

    /// <summary>
    /// Gets a value indicating whether the pointer is the primary one: the mouse always; a touch or a pen that came first among those
    /// of its kind present (W3C Pointer Events).
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Gets the pressure, 0 to 1: a pen's from its device; 0.5 for the mouse and a touch while a button or the contact is down, 0
    /// otherwise (W3C Pointer Events).
    /// </summary>
    public float Pressure { get; }

    /// <summary>
    /// Gets a pen's tilt along x, −90 to 90 degrees, positive to the right; 0 for the others.
    /// </summary>
    public float TiltX { get; }

    /// <summary>
    /// Gets a pen's tilt along y, −90 to 90 degrees, positive toward the user; 0 for the others.
    /// </summary>
    public float TiltY { get; }

    /// <summary>
    /// Gets a pen's clockwise twist, 0 to 359 degrees; 0 for the others.
    /// </summary>
    public float Twist { get; }

    /// <summary>
    /// Gets a value indicating whether a pen's eraser end makes the input.
    /// </summary>
    public bool IsEraser { get; }

    /// <summary>
    /// Gets a value indicating whether a pen is turned over.
    /// </summary>
    public bool IsInverted { get; }

    /// <summary>
    /// Gets the points the event's message coalesced, its own included.
    /// </summary>
    internal int HistoryCount { get; }

    /// <summary>
    /// Gets the serial of the delivery of the event's message; 0 for an event no pointer message made.
    /// </summary>
    internal int Dispatch { get; }

    /// <summary>
    /// Stops the event: no handler after this one is called, in this phase or the next.
    /// </summary>
    public void StopPropagation() => _input.StopPropagation();

    /// <summary>
    /// Returns the points the event's message brought (design §7, "Touch and pen"): a pen or a touch that moved faster than the
    /// messages came gives several, oldest first, the last being the event's own; the mouse gives its own alone. A drawing app reads
    /// them to draw a line through every point. An event no pointer message made (a key's click, the enters and leaves of a frame's
    /// test of the hover) gives none.
    /// </summary>
    /// <returns>The points, which hold until the event's delivery ends.</returns>
    /// <exception cref="InvalidOperationException">The event's delivery has ended: Win32 keeps the history of the thread's last
    /// pointer message only, so the points are read in the handler.</exception>
    public ReadOnlySpan<PointerPoint> GetIntermediatePoints() => _input.IntermediatePoints(this);
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
