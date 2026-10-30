using System.Text;

namespace XylemUI.Testing;

/// <summary>
/// The mouse of a <see cref="HeadlessApp"/>: it sends pointer input into the window's input as the OS's mouse messages do (design
/// §12.4), in the window's physical pixels, so hit testing, capture, and bubbling run as they do in an app. Each call runs the UI
/// queue after the input.
/// </summary>
public sealed class HeadlessPointer
{
    private readonly HeadlessApp _app;
    private PointerButtons _buttons;
    private int _x;
    private int _y;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeadlessPointer"/> class for an app's window.
    /// </summary>
    /// <param name="app">The app.</param>
    internal HeadlessPointer(HeadlessApp app) => _app = app;

    /// <summary>
    /// Gets where the pointer is, in the window's physical pixels.
    /// </summary>
    public (int X, int Y) Position => (_x, _y);

    /// <summary>
    /// Moves the pointer to the center of a node's box, presses the left button, and releases it there.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    public void Click(Element element, KeyModifiers modifiers = KeyModifiers.None)
    {
        (int x, int y) = _app.CenterOf(element);
        Move(x, y, modifiers);
        Down(PointerButton.Left, modifiers);
        Up(PointerButton.Left, modifiers);
    }

    /// <summary>
    /// Moves the pointer, with the buttons it holds.
    /// </summary>
    /// <param name="x">The x of the window's client area, in physical pixels.</param>
    /// <param name="y">The y.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    public void Move(int x, int y, KeyModifiers modifiers = KeyModifiers.None)
    {
        _x = x;
        _y = y;
        Send(PointerInputKind.Move, PointerButton.None, modifiers, clickCount: 0);
    }

    /// <summary>
    /// Presses a button where the pointer is.
    /// </summary>
    /// <param name="button">The button.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    public void Down(PointerButton button = PointerButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        _buttons |= Bit(button);
        Send(PointerInputKind.Down, button, modifiers, clickCount: 1);
    }

    /// <summary>
    /// Releases a button where the pointer is.
    /// </summary>
    /// <param name="button">The button.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    public void Up(PointerButton button = PointerButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        _buttons &= ~Bit(button);
        Send(PointerInputKind.Up, button, modifiers, clickCount: 1);
    }

    private static PointerButtons Bit(PointerButton button) =>
        button switch
        {
            PointerButton.Left => PointerButtons.Left,
            PointerButton.Right => PointerButtons.Right,
            PointerButton.Middle => PointerButtons.Middle,
            PointerButton.XButton1 => PointerButtons.XButton1,
            PointerButton.XButton2 => PointerButtons.XButton2,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "The button is one of the five a mouse has."),
        };

    private void Send(PointerInputKind kind, PointerButton button, KeyModifiers modifiers, int clickCount)
    {
        _app.VerifyRunning();
        var input = new PointerInput(kind, _x, _y, button, _buttons, modifiers, clickCount, _app.Time.Elapsed, PointerType.Mouse);
        _app.Input.Handle(input);
        _ = _app.DrainQueue();
    }
}

/// <summary>
/// The keyboard of a <see cref="HeadlessApp"/>: it sends keys into the window's input as the OS's key messages do (design §12.4),
/// so they go to the focused node with capture and bubbling. Each call runs the UI queue after the input. Typing text is row P02b's
/// (<c>TextInputEvent</c>).
/// </summary>
public sealed class HeadlessKeyboard
{
    private readonly HeadlessApp _app;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeadlessKeyboard"/> class for an app's window.
    /// </summary>
    /// <param name="app">The app.</param>
    internal HeadlessKeyboard(HeadlessApp app) => _app = app;

    /// <summary>
    /// Presses a key and releases it.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <returns><see langword="true"/> when a handler handled the press.</returns>
    public bool Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        bool handled = Down(key, modifiers);
        _ = Up(key, modifiers);
        return handled;
    }

    /// <summary>
    /// Presses a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="isRepeat">Whether the press repeats a key that is held.</param>
    /// <returns><see langword="true"/> when a handler handled it.</returns>
    public bool Down(Key key, KeyModifiers modifiers = KeyModifiers.None, bool isRepeat = false) => Send(KeyEventKind.Down, key, modifiers, isRepeat);

    /// <summary>
    /// Releases a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <returns><see langword="true"/> when a handler handled it.</returns>
    public bool Up(Key key, KeyModifiers modifiers = KeyModifiers.None) => Send(KeyEventKind.Up, key, modifiers, isRepeat: false);

    private bool Send(KeyEventKind kind, Key key, KeyModifiers modifiers, bool isRepeat)
    {
        _app.VerifyRunning();
        var input = new KeyInput(kind, key, PhysicalKey.None, default(Rune), modifiers, isRepeat, _app.Time.Elapsed);
        bool handled = _app.Input.Handle(input);
        _ = _app.DrainQueue();
        return handled;
    }
}
