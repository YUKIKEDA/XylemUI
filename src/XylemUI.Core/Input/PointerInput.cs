namespace XylemUI;

/// <summary>
/// One input of the pointer as the platform hands it to Core (design §7, "Pointer input"): what happened, where in the window's
/// client area, and the state of the buttons and the modifier keys.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="X">The column, in the window's client coordinates (physical pixels), as Win32 gives it; not used by
/// <see cref="PointerInputKind.Leave"/> and <see cref="PointerInputKind.CaptureLost"/>.</param>
/// <param name="Y">The row, in physical pixels.</param>
/// <param name="Button">The button that went down or up; <see cref="PointerButton.None"/> for the other kinds.</param>
/// <param name="Buttons">The buttons held down now, after the change.</param>
/// <param name="Modifiers">The modifier keys held down.</param>
/// <param name="ClickCount">For a press, the presses of the same button in a row within the OS's double-click time and area; a
/// release has the count of its press. 0 for the other kinds.</param>
/// <param name="Timestamp">When the platform received the input.</param>
/// <param name="PointerType">What the pointer is.</param>
internal readonly record struct PointerInput(
    PointerInputKind Kind,
    int X,
    int Y,
    PointerButton Button,
    PointerButtons Buttons,
    KeyModifiers Modifiers,
    int ClickCount,
    TimeSpan Timestamp,
    PointerType PointerType
);

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
}

/// <summary>
/// A button of the pointer.
/// </summary>
internal enum PointerButton : byte
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
internal enum PointerButtons : byte
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
internal enum KeyModifiers : byte
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
}

/// <summary>
/// What a pointer is (design §8.10). Row V1c takes every pointer through the mouse messages, which the OS also makes from touch
/// and a pen; row I1 receives touch and pens on their own.
/// </summary>
internal enum PointerType : byte
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
