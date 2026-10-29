using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The pointer part of the window (row V1c, design §7, "Pointer input"): the mouse messages as PointerInput, the click count, the
// capture, and the cursor. Messages are sent to a hidden window with SendMessage, as Win32 delivers them.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class Win32WindowPointerTests(UiThreadFixture ui)
{
    /// <summary>
    /// A mouse message decodes to its kind, its button, the buttons and keys of its wParam, its signed position, the Alt and Windows
    /// keys given apart, and a pressure of 0.5 while a button is down (W3C Pointer Events); a message the OS made from touch or a pen,
    /// which its extra information marks, decodes to nothing (design §7, "Touch and pen").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None: the decoding is a function of the message's values.
    ///
    /// Steps:
    /// - Decode a left press with the left button, Control, and Shift down at (-3, 70000 truncated to 4464), Alt and a Windows key
    ///   down; an extra-button release of the second extra button with Control given in the key state; a move with the right button
    ///   held; the same press with the touch signature, and with the pen signature; a message that is no mouse message.
    ///
    /// Expected:
    /// - Down, left, left held, Control | Shift | Alt | Meta, (-3, 4464), the mouse, the count given, pressure 0.5. Up, the second
    ///   extra button, none held, no modifier (Control comes from wParam alone), pressure 0. Move with a count of 0 and pressure 0.5.
    ///   None for the two marked presses and for the other message.
    /// </remarks>
    [Fact]
    public void Decode_MouseMessages_GiveTheirInput()
    {
        TimeSpan at = TimeSpan.FromSeconds(3);
        nint position = (nint)(((uint)(70000 & 0xFFFF) << 16) | unchecked((uint)-3 & 0xFFFF));
        PointerInput? down = Win32Window.Decode(
            WM.WM_LBUTTONDOWN,
            MK.MK_LBUTTON | MK.MK_CONTROL | MK.MK_SHIFT,
            position,
            keyState: KeyModifiers.Alt | KeyModifiers.Meta,
            0,
            2,
            at
        );
        PointerInput? up = Win32Window.Decode(WM.WM_XBUTTONUP, (nuint)(XBUTTON2 << 16), 0, keyState: KeyModifiers.Control, 0, 1, at);
        PointerInput? move = Win32Window.Decode(WM.WM_MOUSEMOVE, MK.MK_RBUTTON, (nint)((20 << 16) | 10), KeyModifiers.None, 0, 5, at);
        PointerInput? fromTouch = Win32Window.Decode(
            WM.WM_LBUTTONDOWN,
            MK.MK_LBUTTON,
            position,
            KeyModifiers.None,
            unchecked((nint)0xFF515780),
            1,
            at
        );
        PointerInput? fromPen = Win32Window.Decode(WM.WM_LBUTTONDOWN, MK.MK_LBUTTON, position, KeyModifiers.None, unchecked((nint)0xFF515700), 1, at);

        Assert.Equal(
            new PointerInput(
                PointerInputKind.Down,
                -3,
                70000 - 65536,
                PointerButton.Left,
                PointerButtons.Left,
                KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta,
                2,
                at,
                PointerType.Mouse,
                Pressure: 0.5f
            ),
            down
        );
        Assert.Equal(
            new PointerInput(PointerInputKind.Up, 0, 0, PointerButton.XButton2, PointerButtons.None, KeyModifiers.None, 1, at, PointerType.Mouse),
            up
        );
        Assert.Equal(
            new PointerInput(
                PointerInputKind.Move,
                10,
                20,
                PointerButton.None,
                PointerButtons.Right,
                KeyModifiers.None,
                0,
                at,
                PointerType.Mouse,
                Pressure: 0.5f
            ),
            move
        );
        Assert.Null(fromTouch);
        Assert.Null(fromPen);
        Assert.Null(Win32Window.Decode(WM.WM_KEYDOWN, 0, 0, KeyModifiers.None, 0, 1, at));
    }

    /// <summary>
    /// A wheel message decodes to a wheel of logical pixels: a notch is the lines (vertical) or characters (horizontal) set times
    /// 100/3, a vertical notch up scrolls up (negative), a horizontal notch right scrolls right (positive), a part of a notch is a
    /// part of it, and the keys of wParam come as for the other messages (design §3.4.1).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None: the decoding is a function of the message's values and the settings given.
    ///
    /// Steps:
    /// - Decode WM_MOUSEWHEEL of +120 with Shift at (5, 6), 3 lines; of −120, 5 lines; of +30, 3 lines. Decode WM_MOUSEHWHEEL of
    ///   +120, 3 characters.
    ///
    /// Expected:
    /// - (0, −100) with Shift at (5, 6), kind Wheel, no button, count 0; (0, 166.67); (0, −25); (100, 0).
    /// </remarks>
    [Fact]
    public void Decode_WheelMessages_GiveLogicalPixels()
    {
        TimeSpan at = TimeSpan.FromSeconds(1);
        nint position = (nint)((6 << 16) | 5);
        PointerInput? up = Win32Window.Decode(WM.WM_MOUSEWHEEL, (nuint)(120 << 16) | MK.MK_SHIFT, position, KeyModifiers.None, 0, 7, at, 3, 3);
        PointerInput? down = Win32Window.Decode(WM.WM_MOUSEWHEEL, unchecked((nuint)(uint)(-120 << 16)), position, KeyModifiers.None, 0, 0, at, 5, 3);
        PointerInput? part = Win32Window.Decode(WM.WM_MOUSEWHEEL, (nuint)(30 << 16), position, KeyModifiers.None, 0, 0, at, 3, 3);
        PointerInput? right = Win32Window.Decode(WM.WM_MOUSEHWHEEL, (nuint)(120 << 16), position, KeyModifiers.None, 0, 0, at, 3, 3);

        Assert.Equal(
            new PointerInput(
                PointerInputKind.Wheel,
                5,
                6,
                PointerButton.None,
                PointerButtons.None,
                KeyModifiers.Shift,
                0,
                at,
                PointerType.Mouse,
                new System.Numerics.Vector2(0, -100)
            ),
            up
        );
        Assert.Equal(500f / 3, down!.Value.Delta.Y, 3);
        Assert.Equal(-25f, part!.Value.Delta.Y, 3);
        Assert.Equal(new System.Numerics.Vector2(100, 0), right!.Value.Delta);
    }

    /// <summary>
    /// The window delivers a wheel at the client position of the screen point its message carries, with the wheel settings of
    /// Windows, and counts no click.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window that records its inputs; the lines a notch scrolls on this machine (SPI_GETWHEELSCROLLLINES).
    ///
    /// Steps:
    /// - Send WM_MOUSEWHEEL of −120 with the screen point of the client's (12, 34).
    ///
    /// Expected:
    /// - One wheel input at (12, 34), of (0, lines × 100/3), with a count of 0.
    /// </remarks>
    [Fact]
    public void WheelMessage_IsDeliveredAtItsClientPosition() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("wheel", 200, 100);
            var inputs = new List<PointerInput>();
            window.PointerInputReceived = (in PointerInput input) => inputs.Add(input);
            uint lines;
            Assert.True(SystemParametersInfoW(SPI.SPI_GETWHEELSCROLLLINES, 0, &lines, 0) != BOOL.FALSE);
            var point = new POINT { x = 12, y = 34 };
            Assert.True(ClientToScreen(window.Hwnd, &point) != BOOL.FALSE);

            nint lParam = (nint)(((uint)(ushort)(short)point.y << 16) | (ushort)(short)point.x);
            _ = SendMessageW(window.Hwnd, WM.WM_MOUSEWHEEL, unchecked((nuint)(uint)(-120 << 16)), lParam);

            PointerInput input = Assert.Single(inputs);
            Assert.Equal((PointerInputKind.Wheel, 12, 34, 0), (input.Kind, input.X, input.Y, input.ClickCount));
            Assert.Equal(lines * 100f / 3, input.Delta.Y, 3);
        });

    /// <summary>
    /// The window delivers its mouse messages as inputs: a press counts the presses in a row within the double-click time and area,
    /// a release takes its press's count, and the leave of the window comes as a leave.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a clock that the test moves; the double-click time and area of this machine.
    ///
    /// Steps:
    /// - Send a move to (10, 10); a press and release at (10, 10); a press at (11, 10) 1 ms later; a release; a press at (11, 10)
    ///   after the double-click time; a press at (11, 10) of the right button; WM_MOUSELEAVE.
    ///
    /// Expected:
    /// - Move; down 1, up 1; down 2, up 2; down 1 (too late); right down 1 (another button); leave.
    /// </remarks>
    [Fact]
    public void Messages_AreDeliveredWithTheirClickCount() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("pointer", 200, 100);
            var time = new StepTime();
            window.Time = time;
            var inputs = new List<(PointerInputKind Kind, PointerButton Button, int Count)>();
            window.PointerInputReceived = (in PointerInput input) => inputs.Add((input.Kind, input.Button, input.ClickCount));

            Send(window, WM.WM_MOUSEMOVE, 0, 10, 10);
            Send(window, WM.WM_LBUTTONDOWN, MK.MK_LBUTTON, 10, 10);
            Send(window, WM.WM_LBUTTONUP, 0, 10, 10);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Send(window, WM.WM_LBUTTONDOWN, MK.MK_LBUTTON, 11, 10);
            Send(window, WM.WM_LBUTTONUP, 0, 11, 10);
            time.Advance(TimeSpan.FromMilliseconds(GetDoubleClickTime() + 1));
            Send(window, WM.WM_LBUTTONDOWN, MK.MK_LBUTTON, 11, 10);
            Send(window, WM.WM_RBUTTONDOWN, MK.MK_RBUTTON | MK.MK_LBUTTON, 11, 10);
            _ = SendMessageW(window.Hwnd, WM.WM_MOUSELEAVE, default, default);

            Assert.Equal(
                [
                    (PointerInputKind.Move, PointerButton.None, 0),
                    (PointerInputKind.Down, PointerButton.Left, 1),
                    (PointerInputKind.Up, PointerButton.Left, 1),
                    (PointerInputKind.Down, PointerButton.Left, 2),
                    (PointerInputKind.Up, PointerButton.Left, 2),
                    (PointerInputKind.Down, PointerButton.Left, 1),
                    (PointerInputKind.Down, PointerButton.Right, 1),
                    (PointerInputKind.Leave, PointerButton.None, 0),
                ],
                inputs
            );
        });

    /// <summary>
    /// The window captures the pointer and releases it for the input without reporting its own release as a loss, while another
    /// window taking the capture comes as a capture lost.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window that records its inputs.
    ///
    /// Steps:
    /// - Capture through the window's <see cref="IPointerPlatform"/>; read the capture; release it. Capture again; send
    ///   WM_CAPTURECHANGED naming no window, as when another takes it.
    ///
    /// Expected:
    /// - The capture is the window's while held and nobody's after the release; only the sent message delivers a capture lost.
    /// </remarks>
    [Fact]
    public void Capture_OwnReleaseIsNoLoss_AnotherWindowsIs() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("capture", 200, 100);
            var kinds = new List<PointerInputKind>();
            window.PointerInputReceived = (in PointerInput input) => kinds.Add(input.Kind);
            var platform = (IPointerPlatform)window;

            platform.Capture();
            HWND held = GetCapture();
            platform.ReleaseCapture();
            HWND after = GetCapture();
            platform.Capture();
            _ = SendMessageW(window.Hwnd, WM.WM_CAPTURECHANGED, default, default);
            platform.ReleaseCapture();

            Assert.Equal(window.Hwnd, held);
            Assert.Equal(HWND.NULL, after);
            Assert.Equal([PointerInputKind.CaptureLost], kinds);
        });

    /// <summary>
    /// WM_SETCURSOR over the client area shows the cursor the input chose and returns TRUE, which stops the processing; over the
    /// frame it is left to the window's default handling (PR #327's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose input chose the text cursor.
    ///
    /// Steps:
    /// - Send WM_SETCURSOR with HTCLIENT and read the cursor; send it with HTCAPTION and read the cursor.
    ///
    /// Expected:
    /// - The first returns TRUE and the thread's cursor is the system's text cursor; after the second, DefWindowProc has set the
    ///   arrow of the window class.
    /// </remarks>
    [Fact]
    public void SetCursorMessage_InTheClientArea_ShowsTheChosenCursor() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("cursor", 200, 100);
            ((IPointerPlatform)window).SetCursor(CursorKind.IBeam);

            LRESULT client = SendMessageW(window.Hwnd, WM.WM_SETCURSOR, (WPARAM)(nint)window.Hwnd, HTCLIENT);
            HCURSOR inClient = GetCursor();
            _ = SendMessageW(window.Hwnd, WM.WM_SETCURSOR, (WPARAM)(nint)window.Hwnd, HTCAPTION);

            Assert.Equal(1, (nint)client);
            Assert.Equal(CursorKind.IBeam, window.Cursor);
            Assert.Equal(LoadCursorW(HINSTANCE.NULL, IDC.IDC_IBEAM), inClient);
            Assert.Equal(LoadCursorW(HINSTANCE.NULL, IDC.IDC_ARROW), GetCursor());
        });

    private static void Send(Win32Window window, uint message, uint keys, int x, int y) =>
        _ = SendMessageW(window.Hwnd, message, keys, (nint)(((uint)y << 16) | ((uint)x & 0xFFFF)));

    // A clock that moves only when the test moves it.
    private sealed class StepTime : TimeProvider
    {
        private long _now = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now;

        public void Advance(TimeSpan by) => _now += by.Ticks;
    }
}
