using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The pointer part of the window (row V1c, design §7, "Pointer input"): the mouse messages as PointerInput, the click count, the
// capture, and the cursor. Messages are sent to a hidden window with SendMessage, as Win32 delivers them.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class Win32WindowPointerTests(UiThreadFixture ui)
{
    /// <summary>
    /// A mouse message decodes to its kind, its button, the buttons and keys of its wParam, its signed position, the Alt state given
    /// apart, and the pointer type its extra information marks.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None: the decoding is a function of the message's values.
    ///
    /// Steps:
    /// - Decode a left press with the left button, Control, and Shift down at (-3, 70000 truncated to 4464), Alt down, and the touch
    ///   signature; an extra-button release of the second extra button with the pen signature; a move with no signature; a message
    ///   that is no mouse message.
    ///
    /// Expected:
    /// - Down, left, left held, Control | Shift | Alt, (-3, 4464), touch, the count given. Up, the second extra button, none held,
    ///   pen. Move with a count of 0, mouse. None for the other message.
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
            alt: true,
            unchecked((nint)0xFF515780),
            2,
            at
        );
        PointerInput? up = Win32Window.Decode(WM.WM_XBUTTONUP, (nuint)(XBUTTON2 << 16), 0, alt: false, unchecked((nint)0xFF515700), 1, at);
        PointerInput? move = Win32Window.Decode(WM.WM_MOUSEMOVE, MK.MK_RBUTTON, (nint)((20 << 16) | 10), alt: false, 0, 5, at);

        Assert.Equal(
            new PointerInput(
                PointerInputKind.Down,
                -3,
                70000 - 65536,
                PointerButton.Left,
                PointerButtons.Left,
                KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt,
                2,
                at,
                PointerType.Touch
            ),
            down
        );
        Assert.Equal(
            new PointerInput(PointerInputKind.Up, 0, 0, PointerButton.XButton2, PointerButtons.None, KeyModifiers.None, 1, at, PointerType.Pen),
            up
        );
        Assert.Equal(
            new PointerInput(PointerInputKind.Move, 10, 20, PointerButton.None, PointerButtons.Right, KeyModifiers.None, 0, at, PointerType.Mouse),
            move
        );
        Assert.Null(Win32Window.Decode(WM.WM_KEYDOWN, 0, 0, alt: false, 0, 1, at));
    }

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
    /// WM_SETCURSOR over the client area shows the cursor the input chose, and is left to the window's default handling on the
    /// frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose input chose the text cursor.
    ///
    /// Steps:
    /// - Send WM_SETCURSOR with HTCLIENT and read the cursor; send it with HTCAPTION.
    ///
    /// Expected:
    /// - The first is handled (0) and the thread's cursor is the system's text cursor; the second goes to DefWindowProc, which
    ///   returns 0 for a top-level window too, so only the cursor is read.
    /// </remarks>
    [Fact]
    public void SetCursorMessage_InTheClientArea_ShowsTheChosenCursor() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("cursor", 200, 100);
            ((IPointerPlatform)window).SetCursor(CursorKind.IBeam);

            LRESULT result = SendMessageW(window.Hwnd, WM.WM_SETCURSOR, (WPARAM)(nint)window.Hwnd, HTCLIENT);

            Assert.Equal(0, (nint)result);
            Assert.Equal(CursorKind.IBeam, window.Cursor);
            Assert.Equal(LoadCursorW(HINSTANCE.NULL, IDC.IDC_IBEAM), GetCursor());
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
