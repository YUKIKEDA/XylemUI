using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Receives the pointer's input of a window (design §7, "Pointer input").
/// </summary>
/// <param name="input">The input, in the window's physical pixels.</param>
internal delegate void PointerInputHandler(in PointerInput input);

/// <content>
/// The pointer part of the window (design §7, "Pointer input"): turns the mouse messages into <see cref="PointerInput"/>, captures the
/// pointer, and shows the cursor the input chose.
/// </content>
public sealed unsafe partial class Win32Window : IPointerPlatform
{
    private const uint WmMouseMove = WM.WM_MOUSEMOVE;
    private const uint WmLButtonDown = WM.WM_LBUTTONDOWN;
    private const uint WmLButtonUp = WM.WM_LBUTTONUP;
    private const uint WmRButtonDown = WM.WM_RBUTTONDOWN;
    private const uint WmRButtonUp = WM.WM_RBUTTONUP;
    private const uint WmMButtonDown = WM.WM_MBUTTONDOWN;
    private const uint WmMButtonUp = WM.WM_MBUTTONUP;
    private const uint WmXButtonDown = WM.WM_XBUTTONDOWN;
    private const uint WmXButtonUp = WM.WM_XBUTTONUP;
    private const uint WmMouseLeave = WM.WM_MOUSELEAVE;
    private const uint WmCaptureChanged = WM.WM_CAPTURECHANGED;
    private const uint WmSetCursor = WM.WM_SETCURSOR;

    // The signature that marks a mouse message the OS made from touch or a pen, and the bit that marks touch (Flutter's
    // flutter_window.cc, lines 56 to 69).
    private const uint TouchOrPenMask = 0xFFFFFF00;
    private const uint TouchOrPenSignature = 0xFF515700;
    private const uint TouchBit = 0x80;

    private bool _trackingLeave;
    private bool _releasingCapture;
    private CursorKind _cursor;
    private PointerButton _clickButton;
    private int _clickCount;
    private long _clickTime;
    private int _clickX;
    private int _clickY;

    /// <summary>
    /// Gets or sets what receives the window's pointer input; <see langword="null"/> for nothing.
    /// </summary>
    internal PointerInputHandler? PointerInputReceived { get; set; }

    /// <summary>
    /// Gets or sets the clock of the inputs' timestamps and of the double-click time, for the tests with fake time.
    /// </summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets the cursor the input chose last.
    /// </summary>
    internal CursorKind Cursor => _cursor;

    /// <summary>
    /// Returns the input a mouse message reports, or <see langword="null"/> for a message that is not a mouse message. The state the
    /// message does not carry comes in the arguments: Alt is in no mouse message's <c>wParam</c> (PR #319's review).
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="wParam">The message's <c>wParam</c>: the buttons and the keys held down (<c>MK_*</c>), and for an extra button
    /// which one in the high word.</param>
    /// <param name="lParam">The message's <c>lParam</c>: the position in the client area, x in the low word and y in the high one,
    /// both signed.</param>
    /// <param name="alt">Whether an Alt key is down (<c>GetKeyState(VK_MENU)</c>).</param>
    /// <param name="extraInfo">The message's extra information (<c>GetMessageExtraInfo</c>), which tells a message made from touch
    /// or a pen.</param>
    /// <param name="clickCount">The presses in a row, for a press or a release.</param>
    /// <param name="timestamp">When the message was received.</param>
    /// <returns>The input.</returns>
    internal static PointerInput? Decode(uint message, nuint wParam, nint lParam, bool alt, nint extraInfo, int clickCount, TimeSpan timestamp)
    {
        (PointerInputKind Kind, PointerButton Button)? decoded = message switch
        {
            WmMouseMove => (PointerInputKind.Move, PointerButton.None),
            WmLButtonDown => (PointerInputKind.Down, PointerButton.Left),
            WmLButtonUp => (PointerInputKind.Up, PointerButton.Left),
            WmRButtonDown => (PointerInputKind.Down, PointerButton.Right),
            WmRButtonUp => (PointerInputKind.Up, PointerButton.Right),
            WmMButtonDown => (PointerInputKind.Down, PointerButton.Middle),
            WmMButtonUp => (PointerInputKind.Up, PointerButton.Middle),
            WmXButtonDown => (PointerInputKind.Down, ExtraButton(wParam)),
            WmXButtonUp => (PointerInputKind.Up, ExtraButton(wParam)),
            _ => null,
        };
        if (decoded is not { } d)
        {
            return null;
        }

        uint keys = (uint)wParam & 0xFFFF;
        PointerButtons buttons =
            ((keys & MK.MK_LBUTTON) != 0 ? PointerButtons.Left : 0)
            | ((keys & MK.MK_RBUTTON) != 0 ? PointerButtons.Right : 0)
            | ((keys & MK.MK_MBUTTON) != 0 ? PointerButtons.Middle : 0)
            | ((keys & MK.MK_XBUTTON1) != 0 ? PointerButtons.XButton1 : 0)
            | ((keys & MK.MK_XBUTTON2) != 0 ? PointerButtons.XButton2 : 0);
        KeyModifiers modifiers =
            ((keys & MK.MK_CONTROL) != 0 ? KeyModifiers.Control : 0)
            | ((keys & MK.MK_SHIFT) != 0 ? KeyModifiers.Shift : 0)
            | (alt ? KeyModifiers.Alt : 0);

        // The signature is in the low 32 bits of the extra information.
        uint info = (uint)extraInfo;
        PointerType type =
            (info & TouchOrPenMask) != TouchOrPenSignature ? PointerType.Mouse
            : (info & TouchBit) != 0 ? PointerType.Touch
            : PointerType.Pen;

        // The position is signed: left of or above the client area, while the pointer is captured, it is negative.
        int x = (short)((nuint)lParam & 0xFFFF);
        int y = (short)(((nuint)lParam >> 16) & 0xFFFF);
        int count = d.Kind == PointerInputKind.Move ? 0 : clickCount;
        return new PointerInput(d.Kind, x, y, d.Button, buttons, modifiers, count, timestamp, type);
    }

    /// <inheritdoc/>
    void IPointerPlatform.Capture() => _ = SetCapture(_hwnd);

    /// <inheritdoc/>
    void IPointerPlatform.ReleaseCapture()
    {
        // Win32 sends WM_CAPTURECHANGED for this release too; it is no capture lost.
        _releasingCapture = true;
        try
        {
            _ = ReleaseCapture();
        }
        finally
        {
            _releasingCapture = false;
        }
    }

    /// <inheritdoc/>
    void IPointerPlatform.SetCursor(CursorKind cursor)
    {
        _cursor = cursor;

        // Shown at once while the pointer is over the client area or captured; elsewhere WM_SETCURSOR shows it when the pointer comes.
        if (_trackingLeave || GetCapture() == _hwnd)
        {
            _ = SetCursor(LoadCursorW(HINSTANCE.NULL, CursorId(cursor)));
        }
    }

    private static PointerButton ExtraButton(nuint wParam) => ((uint)wParam >> 16) == XBUTTON1 ? PointerButton.XButton1 : PointerButton.XButton2;

    private static char* CursorId(CursorKind cursor) =>
        cursor switch
        {
            CursorKind.IBeam => IDC.IDC_IBEAM,
            CursorKind.Hand => IDC.IDC_HAND,
            CursorKind.Crosshair => IDC.IDC_CROSS,
            CursorKind.Wait => IDC.IDC_WAIT,
            CursorKind.Progress => IDC.IDC_APPSTARTING,
            CursorKind.Help => IDC.IDC_HELP,
            CursorKind.NotAllowed => IDC.IDC_NO,
            CursorKind.ResizeAll => IDC.IDC_SIZEALL,
            CursorKind.ResizeEW => IDC.IDC_SIZEWE,
            CursorKind.ResizeNS => IDC.IDC_SIZENS,
            CursorKind.ResizeNWSE => IDC.IDC_SIZENWSE,
            CursorKind.ResizeNESW => IDC.IDC_SIZENESW,
            _ => IDC.IDC_ARROW,
        };

    // The pointer's messages. Returns whether the message was handled; WM_SETCURSOR sets the result it returns.
    private bool HandlePointerMessage(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam, ref LRESULT result)
    {
        switch (message)
        {
            case WmMouseLeave:
                _trackingLeave = false;
                Deliver(new PointerInput(PointerInputKind.Leave, 0, 0, PointerButton.None, 0, 0, 0, Now(), PointerType.Mouse));
                return true;
            case WmCaptureChanged:
                // lParam is the window that takes the capture; a release of the window's own is not a loss.
                if (!_releasingCapture && (HWND)(nint)lParam != hwnd)
                {
                    Deliver(new PointerInput(PointerInputKind.CaptureLost, 0, 0, PointerButton.None, 0, 0, 0, Now(), PointerType.Mouse));
                }

                return true;
            case WmSetCursor:
                // In the client area the input's cursor; on the frame, DefWindowProc's.
                if (((nuint)lParam & 0xFFFF) == HTCLIENT)
                {
                    // TRUE stops the processing; FALSE would let the parent window or DefWindowProc set its own (PR #327's review).
                    _ = SetCursor(LoadCursorW(HINSTANCE.NULL, CursorId(_cursor)));
                    result = 1;
                    return true;
                }

                return false;
            default:
                break;
        }

        // Only a mouse message reads the clock, the Alt key, and the extra information (PR #327's review).
        if (message is < WM.WM_MOUSEFIRST or > WM.WM_MOUSELAST)
        {
            return false;
        }

        TimeSpan now = Now();
        bool alt = GetKeyState(VK.VK_MENU) < 0;
        if (Decode(message, wParam, lParam, alt, GetMessageExtraInfo(), 0, now) is not { } input)
        {
            return false;
        }

        if (input.Kind == PointerInputKind.Move && !_trackingLeave)
        {
            var track = new TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                dwFlags = TME.TME_LEAVE,
                hwndTrack = hwnd,
            };
            _trackingLeave = TrackMouseEvent(&track) != BOOL.FALSE;
        }

        if (input.Kind != PointerInputKind.Move)
        {
            input = input with { ClickCount = Count(input) };
        }

        Deliver(input);
        return true;
    }

    // Counts a press as GPUI's ClickState does: one more when it is the same button, within the OS's double-click time, and within
    // the OS's double-click rectangle around the press before; 1 otherwise. A release takes its press's count.
    private int Count(in PointerInput input)
    {
        if (input.Kind == PointerInputKind.Up)
        {
            return _clickCount;
        }

        long now = Time.GetTimestamp();
        bool again =
            _clickCount > 0
            && input.Button == _clickButton
            && Time.GetElapsedTime(_clickTime, now).TotalMilliseconds <= GetDoubleClickTime()
            && Math.Abs(input.X - _clickX) * 2 <= GetSystemMetrics(SM.SM_CXDOUBLECLK)
            && Math.Abs(input.Y - _clickY) * 2 <= GetSystemMetrics(SM.SM_CYDOUBLECLK);
        _clickCount = again ? _clickCount + 1 : 1;
        _clickButton = input.Button;
        _clickTime = now;
        _clickX = input.X;
        _clickY = input.Y;
        return _clickCount;
    }

    private TimeSpan Now() => Time.GetElapsedTime(0);

    private void Deliver(in PointerInput input) => PointerInputReceived?.Invoke(in input);
}
