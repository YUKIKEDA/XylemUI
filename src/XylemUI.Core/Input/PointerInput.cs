using System.Numerics;
using System.Runtime.InteropServices;

namespace XylemUI;

/// <summary>
/// One input of the pointer as the platform hands it to Core (design §7, "Pointer input"): what happened, where in the window's
/// client area, and the state of the buttons and the modifier keys.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="X">The column, in the window's client coordinates (physical pixels), as Win32 gives it. A touch's or a pen's
/// <see cref="PointerInputKind.Leave"/> has where it left; the mouse's leave and <see cref="PointerInputKind.CaptureLost"/> do not use it.</param>
/// <param name="Y">The row, in physical pixels.</param>
/// <param name="Button">The button that went down or up; <see cref="PointerButton.None"/> for the other kinds.</param>
/// <param name="Buttons">The buttons held down now, after the change.</param>
/// <param name="Modifiers">The modifier keys held down.</param>
/// <param name="ClickCount">For a press, the presses of the same button in a row within the OS's double-click time and area; a
/// release has the count of its press. 0 for the other kinds.</param>
/// <param name="Timestamp">When the platform received the input.</param>
/// <param name="PointerType">What the pointer is.</param>
/// <param name="WheelDelta">For a <see cref="PointerInputKind.Wheel"/>, how far the wheel scrolls, in logical pixels: positive moves
/// the scroll position down and to the right (design §3.4.1); zero for the other kinds.</param>
/// <param name="PointerId">The pointer's number (design §7, "Touch and pen"): 0 for the mouse; 1 and up for touches and pens, which
/// the platform gives back when the pointer leaves.</param>
/// <param name="IsPrimary">Whether the pointer is the primary one: the mouse always; a touch or a pen that came first among those of
/// its kind present.</param>
/// <param name="Pressure">The pressure, 0 to 1: a pen's from its device; 0.5 for the mouse and a touch while a button or the contact
/// is down, 0 otherwise (W3C Pointer Events).</param>
/// <param name="TiltX">A pen's tilt along x, −90 to 90 degrees, positive to the right; 0 for the others.</param>
/// <param name="TiltY">A pen's tilt along y, −90 to 90 degrees, positive toward the user; 0 for the others.</param>
/// <param name="Twist">A pen's clockwise twist, 0 to 359 degrees; 0 for the others.</param>
/// <param name="PenFlags">A pen's eraser and inverted marks.</param>
/// <param name="HistoryCount">The points the message coalesced, its own included (Win32's <c>historyCount</c>); 1 for the mouse.</param>
// Auto, so that the runtime packs the byte-sized values together: in their declared order the padding made it 80 bytes, and
// NativeAOT copied it with a call to Memmove (row I1a's VTune count; docs/benchmarks/i1a-touch-pen.md).
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PointerInput(
    PointerInputKind Kind,
    int X,
    int Y,
    PointerButton Button,
    PointerButtons Buttons,
    KeyModifiers Modifiers,
    int ClickCount,
    TimeSpan Timestamp,
    PointerType PointerType,
    Vector2 WheelDelta = default,
    int PointerId = 0,
    bool IsPrimary = true,
    float Pressure = 0,
    float TiltX = 0,
    float TiltY = 0,
    float Twist = 0,
    PenFlags PenFlags = PenFlags.None,
    int HistoryCount = 1
);

/// <summary>
/// The marks of a pen (design §7, "Touch and pen").
/// </summary>
[Flags]
internal enum PenFlags : byte
{
    /// <summary>
    /// No mark.
    /// </summary>
    None = 0,

    /// <summary>
    /// The eraser end touches or hovers (Win32's <c>PEN_FLAG_ERASER</c>).
    /// </summary>
    Eraser = 1,

    /// <summary>
    /// The pen is turned over (Win32's <c>PEN_FLAG_INVERTED</c>).
    /// </summary>
    Inverted = 2,
}

/// <summary>
/// One of the points a pointer message coalesced (design §7, "Touch and pen"), as <see cref="PointerEvent.GetIntermediatePoints"/>
/// gives it.
/// </summary>
/// <param name="Position">The position in the window, in logical pixels, as <see cref="PointerEvent.Position"/> has it.</param>
/// <param name="Pressure">The pressure, 0 to 1.</param>
/// <param name="TiltX">The tilt along x, in degrees.</param>
/// <param name="TiltY">The tilt along y, in degrees.</param>
/// <param name="Twist">The twist, in degrees.</param>
/// <param name="Timestamp">When the point was taken.</param>
public readonly record struct PointerPoint(Vector2 Position, float Pressure, float TiltX, float TiltY, float Twist, TimeSpan Timestamp);

/// <summary>
/// What a <see cref="PointerInput"/> reports.
/// </summary>
internal enum PointerInputKind : byte
{
    /// <summary>
    /// The pointer moved.
    /// </summary>
    Move,

    /// <summary>
    /// A button went down.
    /// </summary>
    Down,

    /// <summary>
    /// A button went up.
    /// </summary>
    Up,

    /// <summary>
    /// The pointer left the window's client area.
    /// </summary>
    Leave,

    /// <summary>
    /// Another window took the capture of the pointer.
    /// </summary>
    CaptureLost,

    /// <summary>
    /// The wheel turned, or a touchpad scrolled (row V2): <see cref="PointerInput.WheelDelta"/> says how far.
    /// </summary>
    Wheel,
}

/// <summary>
/// A button of the pointer.
/// </summary>
public enum PointerButton : byte
{
    /// <summary>
    /// No button.
    /// </summary>
    None,

    /// <summary>
    /// The left button (the primary one).
    /// </summary>
    Left,

    /// <summary>
    /// The right button.
    /// </summary>
    Right,

    /// <summary>
    /// The middle button.
    /// </summary>
    Middle,

    /// <summary>
    /// The first extra button (back).
    /// </summary>
    XButton1,

    /// <summary>
    /// The second extra button (forward).
    /// </summary>
    XButton2,
}

/// <summary>
/// The buttons of the pointer held down.
/// </summary>
[Flags]
public enum PointerButtons : byte
{
    /// <summary>
    /// No button.
    /// </summary>
    None = 0,

    /// <summary>
    /// The left button.
    /// </summary>
    Left = 1,

    /// <summary>
    /// The right button.
    /// </summary>
    Right = 2,

    /// <summary>
    /// The middle button.
    /// </summary>
    Middle = 4,

    /// <summary>
    /// The first extra button.
    /// </summary>
    XButton1 = 8,

    /// <summary>
    /// The second extra button.
    /// </summary>
    XButton2 = 16,
}

/// <summary>
/// The modifier keys held down.
/// </summary>
[Flags]
public enum KeyModifiers : byte
{
    /// <summary>
    /// No modifier key.
    /// </summary>
    None = 0,

    /// <summary>
    /// A Control key.
    /// </summary>
    Control = 1,

    /// <summary>
    /// A Shift key.
    /// </summary>
    Shift = 2,

    /// <summary>
    /// An Alt key.
    /// </summary>
    Alt = 4,

    /// <summary>
    /// A Windows key (row K1).
    /// </summary>
    Meta = 8,
}

/// <summary>
/// What a pointer is (design §8.10). The mouse comes through the mouse messages; touches and pens through Win32's pointer messages
/// (row I1a), and the mouse messages the OS makes from them are dropped.
/// </summary>
public enum PointerType : byte
{
    /// <summary>
    /// A mouse or a touchpad.
    /// </summary>
    Mouse,

    /// <summary>
    /// A finger on a touch screen.
    /// </summary>
    Touch,

    /// <summary>
    /// A pen.
    /// </summary>
    Pen,
}
