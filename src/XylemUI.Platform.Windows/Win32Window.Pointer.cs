using System.Numerics;
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
    private const uint WmMouseWheel = WM.WM_MOUSEWHEEL;
    private const uint WmMouseHWheel = WM.WM_MOUSEHWHEEL;

    // One notch of a wheel (WHEEL_DELTA), and the logical pixels of one line or one character of its scrolling: Chromium's and
    // Flutter's 100/3, so that the default 3 lines are 100 pixels (Flutter's flutter_window.cc 469 to 479, design §3.4.1).
    private const float WheelNotch = 120;
    private const float PixelsPerWheelLine = 100f / 3;

    // The signature that marks a mouse message the OS made from touch or a pen (Flutter's flutter_window.cc, lines 56 to 69).
    private const uint TouchOrPenMask = 0xFFFFFF00;
    private const uint TouchOrPenSignature = 0xFF515700;

    // A pen's pressure in POINTER_PEN_INFO runs from 0 to 1024 (Microsoft's POINTER_PEN_INFO page).
    private const float PenPressureMax = 1024;

    // Taps of a touch or a pen count as one double tap within this distance, in logical pixels (Avalonia's
    // DefaultPlatformSettings.cs lines 17 to 33; design §7, "Touch and pen"). The mouse's SM_CXDOUBLECLK is too narrow for a finger.
    private const float TapDoubleDistance = 50;

    // The most coalesced points a message's history gives (design §7, "Touch and pen").
    private const int MaxHistory = 512;

    // The pointer flags of POINTER_INFO.pointerFlags that the input reads, with the values of Microsoft's "Pointer Flags" page;
    // TerraFX does not name them.
    private const uint PointerFlagInContact = 0x00000004;
    private const uint PointerFlagFirstButton = 0x00000010;
    private const uint PointerFlagSecondButton = 0x00000020;
    private const uint PointerFlagCanceled = 0x00008000;

    // The lines a notch of the vertical wheel scrolls and the characters of the horizontal one, which Windows sets for the user
    // (SPI_GETWHEELSCROLLLINES, SPI_GETWHEELSCROLLCHARS). Read when the first window is made and on each WM_SETTINGCHANGE, as
    // GPUI's system_settings.rs reads them.
    private static float _wheelLines = 3;
    private static float _wheelChars = 3;

    private bool _trackingLeave;
    private bool _releasingCapture;
    private CursorKind _cursor;
    private PointerButton _clickButton;
    private PointerType _clickType;
    private int _clickCount;
    private long _clickTime;
    private int _clickX;
    private int _clickY;

    // The number of the last press, and whether it is a touch's or a pen's that has not been released: a second tap counts only after
    // the first is released (Flutter's DoubleTapGestureRecognizer, multitap.dart lines 223 to 263).
    private int _clickPointer;
    private bool _clickOpen;

    // The touches and pens present, by their number (index 0, the mouse's, is not used): Win32's pointer id, the type, whether the
    // number is taken, and whether the pointer is primary.
    private uint[] _pointerWin32 = new uint[8];
    private PointerType[] _pointerType = new PointerType[8];
    private bool[] _pointerTaken = new bool[8];
    private bool[] _pointerPrimary = new bool[8];
    private int[] _pointerClicks = new int[8];
    private POINTER_PEN_INFO[]? _penHistory;
    private POINTER_TOUCH_INFO[]? _touchHistory;

    /// <summary>
    /// Gets or sets what receives the window's pointer input; <see langword="null"/> for nothing.
    /// </summary>
    internal PointerInputHandler? PointerInputReceived { get; set; }

    /// <summary>
    /// Gets or sets what reads the pointer messages' information: Win32's, or the tests' (design §7, "Touch and pen").
    /// </summary>
    internal IPointerReader PointerReader { get; set; } = Win32PointerReader.Instance;

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
    /// message does not carry comes in the arguments: Alt and the Windows keys are in no mouse message's <c>wParam</c> (PR #319's and
    /// PR #352's reviews).
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="wParam">The message's <c>wParam</c>: the buttons and the keys held down (<c>MK_*</c>), and for an extra button
    /// which one in the high word.</param>
    /// <param name="lParam">The message's <c>lParam</c>: the position in the client area, x in the low word and y in the high one,
    /// both signed.</param>
    /// <param name="keyState">The modifiers the message does not carry, read from the keyboard's state: <see cref="KeyModifiers.Alt"/>
    /// (<c>GetKeyState(VK_MENU)</c>) and <see cref="KeyModifiers.Meta"/> (<c>VK_LWIN</c>, <c>VK_RWIN</c>); the others are ignored.</param>
    /// <param name="extraInfo">The message's extra information (<c>GetMessageExtraInfo</c>), which tells a message the OS made from
    /// touch or a pen: such a message gives no input, as touches and pens come through the pointer messages (design §7, "Touch and
    /// pen", as Flutter's <c>flutter_window.cc</c> lines 57 to 69 and 733 to 736).</param>
    /// <param name="clickCount">The presses in a row, for a press or a release.</param>
    /// <param name="timestamp">When the message was received.</param>
    /// <param name="wheelLines">The lines a notch of the vertical wheel scrolls.</param>
    /// <param name="wheelChars">The characters a notch of the horizontal wheel scrolls.</param>
    /// <returns>The input; <see langword="null"/> for a message the OS made from touch or a pen.</returns>
    /// <remarks>
    /// A wheel's <c>lParam</c> is in screen coordinates; the caller gives it in client coordinates, as the other messages have it.
    /// Its amount is the signed high word of <c>wParam</c>, in notches of 120: a vertical wheel turned up is positive and scrolls
    /// up, so its sign is turned; a horizontal one turned right is positive and scrolls right. An amount smaller than a notch
    /// (a touchpad) goes on as a fraction.
    /// </remarks>
    internal static PointerInput? Decode(
        uint message,
        nuint wParam,
        nint lParam,
        KeyModifiers keyState,
        nint extraInfo,
        int clickCount,
        TimeSpan timestamp,
        float wheelLines = 3,
        float wheelChars = 3
    )
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
            WmMouseWheel or WmMouseHWheel => (PointerInputKind.Wheel, PointerButton.None),
            _ => null,
        };

        // The signature is in the low 32 bits of the extra information.
        if (decoded is not { } d || ((uint)extraInfo & TouchOrPenMask) == TouchOrPenSignature)
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
            | (keyState & (KeyModifiers.Alt | KeyModifiers.Meta));

        // The position is signed: left of or above the client area, while the pointer is captured, it is negative.
        int x = (short)((nuint)lParam & 0xFFFF);
        int y = (short)(((nuint)lParam >> 16) & 0xFFFF);
        int count = d.Kind is PointerInputKind.Move or PointerInputKind.Wheel ? 0 : clickCount;
        Vector2 wheel = default;
        if (d.Kind == PointerInputKind.Wheel)
        {
            float notches = (short)(((uint)wParam >> 16) & 0xFFFF) / WheelNotch;
            wheel =
                message == WmMouseWheel
                    ? new Vector2(0, -notches * wheelLines * PixelsPerWheelLine)
                    : new Vector2(notches * wheelChars * PixelsPerWheelLine, 0);
        }

        // W3C Pointer Events: a device without pressure reports 0.5 while a button is down.
        float pressure = buttons != PointerButtons.None ? 0.5f : 0;
        return new PointerInput(d.Kind, x, y, d.Button, buttons, modifiers, count, timestamp, PointerType.Mouse, wheel, Pressure: pressure);
    }

    /// <summary>
    /// Returns the input of a touch's or a pen's pointer message (design §7, "Touch and pen"), from the pointer's information: the
    /// first button is the left one (the contact of a touch or a pen's tip), the second the right one (a pen's barrel button, as W3C
    /// Pointer Events takes it), and the button whose change Win32 reports makes the down or the up; a message with no change is a
    /// move. A pen's pressure is its device's, 0 to 1; a touch's, and a pen's without pressure, is 0.5 in contact and 0 otherwise.
    /// </summary>
    /// <param name="info">The pointer's information.</param>
    /// <param name="pen">A pen's information; <see langword="null"/> for a touch.</param>
    /// <param name="x">The column in the client area, in physical pixels.</param>
    /// <param name="y">The row, in physical pixels.</param>
    /// <param name="pointerId">The pointer's number.</param>
    /// <param name="isPrimary">Whether the pointer is primary.</param>
    /// <param name="keyState">The modifier keys, read from the keyboard's state (<c>GetKeyState</c>, as Microsoft's POINTER_INFO page says for its <c>dwKeyStates</c>).</param>
    /// <param name="timestamp">When the message was received.</param>
    /// <returns>The input, with no click count.</returns>
    internal static PointerInput DecodePointer(
        in POINTER_INFO info,
        POINTER_PEN_INFO? pen,
        int x,
        int y,
        int pointerId,
        bool isPrimary,
        KeyModifiers keyState,
        TimeSpan timestamp
    )
    {
        (PointerInputKind Kind, PointerButton Button) change = info.ButtonChangeType switch
        {
            POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_FIRSTBUTTON_DOWN => (PointerInputKind.Down, PointerButton.Left),
            POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_FIRSTBUTTON_UP => (PointerInputKind.Up, PointerButton.Left),
            POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_SECONDBUTTON_DOWN => (PointerInputKind.Down, PointerButton.Right),
            POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_SECONDBUTTON_UP => (PointerInputKind.Up, PointerButton.Right),
            _ => (PointerInputKind.Move, PointerButton.None),
        };
        uint flags = info.pointerFlags;
        PointerButtons buttons =
            ((flags & PointerFlagFirstButton) != 0 ? PointerButtons.Left : 0) | ((flags & PointerFlagSecondButton) != 0 ? PointerButtons.Right : 0);
        KeyModifiers modifiers = keyState;
        float pressure = (flags & PointerFlagInContact) != 0 ? 0.5f : 0;
        float tiltX = 0;
        float tiltY = 0;
        float twist = 0;
        PenFlags penFlags = PenFlags.None;
        if (pen is { } p)
        {
            // The pen's own information, whose flags are the message's (POINTER_PEN_INFO holds its POINTER_INFO).
            (pressure, tiltX, tiltY, twist) = PenValues(p);
            penFlags = ((p.penFlags & PEN_FLAG_ERASER) != 0 ? PenFlags.Eraser : 0) | ((p.penFlags & PEN_FLAG_INVERTED) != 0 ? PenFlags.Inverted : 0);
        }

        return new PointerInput(
            change.Kind,
            x,
            y,
            change.Button,
            buttons,
            modifiers,
            0,
            timestamp,
            pen is null ? PointerType.Touch : PointerType.Pen,
            default,
            pointerId,
            isPrimary,
            pressure,
            tiltX,
            tiltY,
            twist,
            penFlags,
            (int)Math.Max(1, info.historyCount)
        );
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
    int IPointerPlatform.ReadHistory(int pointerId, Span<PointerHistoryPoint> destination)
    {
        if (pointerId <= 0 || pointerId >= _pointerTaken.Length || !_pointerTaken[pointerId] || destination.IsEmpty)
        {
            return 0;
        }

        uint win32 = _pointerWin32[pointerId];
        int wanted = Math.Min(destination.Length, MaxHistory);
        int read;
        if (_pointerType[pointerId] == PointerType.Pen)
        {
            _penHistory ??= new POINTER_PEN_INFO[MaxHistory];
            read = PointerReader.ReadPenHistory(win32, _penHistory.AsSpan(0, wanted));
            for (int i = 0; i < read; i++)
            {
                ref readonly POINTER_PEN_INFO p = ref _penHistory[i];
                (float pressure, float tiltX, float tiltY, float twist) = PenValues(p);
                (int x, int y) = ToClient(p.pointerInfo.ptPixelLocation);
                destination[i] = new PointerHistoryPoint(x, y, pressure, tiltX, tiltY, twist, TimeOf(p.pointerInfo));
            }
        }
        else
        {
            _touchHistory ??= new POINTER_TOUCH_INFO[MaxHistory];
            read = PointerReader.ReadTouchHistory(win32, _touchHistory.AsSpan(0, wanted));
            for (int i = 0; i < read; i++)
            {
                ref readonly POINTER_TOUCH_INFO t = ref _touchHistory[i];
                bool contact = (t.pointerInfo.pointerFlags & PointerFlagInContact) != 0;
                (int x, int y) = ToClient(t.pointerInfo.ptPixelLocation);
                destination[i] = new PointerHistoryPoint(x, y, contact ? 0.5f : 0, 0, 0, 0, TimeOf(t.pointerInfo));
            }
        }

        return read;
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

    // A pen's pressure (0 to 1), tilts, and twist, each 0 when its mask leaves it out; a pressure left out is 0.5 in contact.
    private static (float Pressure, float TiltX, float TiltY, float Twist) PenValues(in POINTER_PEN_INFO pen)
    {
        uint mask = pen.penMask;
        float contact = (pen.pointerInfo.pointerFlags & PointerFlagInContact) != 0 ? 0.5f : 0;
        return (
            (mask & PEN_MASK_PRESSURE) != 0 ? pen.pressure / PenPressureMax : contact,
            (mask & PEN_MASK_TILT_X) != 0 ? pen.tiltX : 0,
            (mask & PEN_MASK_TILT_Y) != 0 ? pen.tiltY : 0,
            (mask & PEN_MASK_ROTATION) != 0 ? pen.rotation : 0
        );
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
            case WM.WM_POINTERDOWN or WM.WM_POINTERUPDATE or WM.WM_POINTERUP or WM.WM_POINTERLEAVE or WM.WM_POINTERCAPTURECHANGED:
                // DefWindowProc still runs: the mouse messages it makes from them are dropped (Flutter's flutter_window.cc 878).
                HandleTouchOrPen(message, (uint)((nuint)wParam & 0xFFFF), lParam);
                return false;
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

        // Only a mouse message reads the clock, the Alt and Windows keys, and the extra information (PR #327's review).
        if (message is < WM.WM_MOUSEFIRST or > WM.WM_MOUSELAST)
        {
            return false;
        }

        // A wheel's position is in screen coordinates; the others' in the client area's.
        if (message is WmMouseWheel or WmMouseHWheel)
        {
            var point = new POINT { x = (short)((nuint)lParam & 0xFFFF), y = (short)(((nuint)lParam >> 16) & 0xFFFF) };
            _ = ScreenToClient(hwnd, &point);
            lParam = (nint)(((uint)(ushort)(short)point.y << 16) | (ushort)(short)point.x);
        }

        TimeSpan now = Now();
        KeyModifiers keyState = (IsDown(VK.VK_MENU) ? KeyModifiers.Alt : 0) | (IsMetaDown() ? KeyModifiers.Meta : 0);
        if (Decode(message, wParam, lParam, keyState, GetMessageExtraInfo(), 0, now, _wheelLines, _wheelChars) is not { } input)
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

        if (input.Kind is PointerInputKind.Down or PointerInputKind.Up)
        {
            input = input with { ClickCount = Count(input) };
        }

        Deliver(input);
        return true;
    }

    // A touch's or a pen's pointer message (design §7, "Touch and pen"): the mouse and a touchpad come through the mouse messages.
    // The leave gives the number back, a cancel or a capture changed loses the press, and the others make an input.
    private void HandleTouchOrPen(uint message, uint win32, LPARAM lParam)
    {
        if (!PointerReader.TryGetType(win32, out POINTER_INPUT_TYPE kind) || kind is not (POINTER_INPUT_TYPE.PT_TOUCH or POINTER_INPUT_TYPE.PT_PEN))
        {
            return;
        }

        PointerType type = kind == POINTER_INPUT_TYPE.PT_PEN ? PointerType.Pen : PointerType.Touch;
        int id = NumberOf(win32, type);
        TimeSpan now = Now();
        if (message is WM.WM_POINTERLEAVE or WM.WM_POINTERCAPTURECHANGED)
        {
            // WM_POINTERLEAVE's lParam is the point in physical screen coordinates; WM_POINTERCAPTURECHANGED's is the window that takes
            // the capture (Microsoft's pages of the two messages).
            PointerInputKind ending = message == WM.WM_POINTERLEAVE ? PointerInputKind.Leave : PointerInputKind.CaptureLost;
            (int leftX, int leftY) =
                ending == PointerInputKind.Leave
                    ? ToClient(new POINT { x = (short)((nuint)lParam & 0xFFFF), y = (short)(((nuint)lParam >> 16) & 0xFFFF) })
                    : (0, 0);
            Deliver(new PointerInput(ending, leftX, leftY, PointerButton.None, 0, 0, 0, now, type, default, id, _pointerPrimary[id]));
            ForgetPress(id);
            if (message == WM.WM_POINTERLEAVE)
            {
                _pointerTaken[id] = false;
            }

            return;
        }

        POINTER_INFO info;
        POINTER_PEN_INFO? pen = null;
        if (type == PointerType.Pen)
        {
            if (!PointerReader.TryGetPenInfo(win32, out POINTER_PEN_INFO penInfo))
            {
                return;
            }

            info = penInfo.pointerInfo;
            pen = penInfo;
        }
        else
        {
            if (!PointerReader.TryGetTouchInfo(win32, out POINTER_TOUCH_INFO touchInfo))
            {
                return;
            }

            info = touchInfo.pointerInfo;
        }

        if ((info.pointerFlags & PointerFlagCanceled) != 0)
        {
            ForgetPress(id);
            Deliver(new PointerInput(PointerInputKind.CaptureLost, 0, 0, PointerButton.None, 0, 0, 0, now, type, default, id, _pointerPrimary[id]));
            return;
        }

        (int x, int y) = ToClient(info.ptPixelLocation);
        KeyModifiers keyState =
            (IsDown(VK.VK_CONTROL) ? KeyModifiers.Control : 0)
            | (IsDown(VK.VK_SHIFT) ? KeyModifiers.Shift : 0)
            | (IsDown(VK.VK_MENU) ? KeyModifiers.Alt : 0)
            | (IsMetaDown() ? KeyModifiers.Meta : 0);
        PointerInput input = DecodePointer(info, pen, x, y, id, _pointerPrimary[id], keyState, now);
        if (input.Kind is PointerInputKind.Down or PointerInputKind.Up)
        {
            input = input with { ClickCount = Count(input) };
        }

        Deliver(input);
    }

    // The number of a touch or a pen: the one it took, or the lowest free one from 1. It is primary when no other pointer of its
    // type is present (W3C Pointer Events).
    private int NumberOf(uint win32, PointerType type)
    {
        int free = -1;
        bool another = false;
        for (int i = 1; i < _pointerTaken.Length; i++)
        {
            if (!_pointerTaken[i])
            {
                free = free < 0 ? i : free;
                continue;
            }

            if (_pointerWin32[i] == win32)
            {
                return i;
            }

            another |= _pointerType[i] == type;
        }

        if (free < 0)
        {
            free = _pointerTaken.Length;
            Array.Resize(ref _pointerWin32, free * 2);
            Array.Resize(ref _pointerType, free * 2);
            Array.Resize(ref _pointerTaken, free * 2);
            Array.Resize(ref _pointerPrimary, free * 2);
            Array.Resize(ref _pointerClicks, free * 2);
        }

        _pointerTaken[free] = true;
        _pointerWin32[free] = win32;
        _pointerType[free] = type;
        _pointerPrimary[free] = !another;
        return free;
    }

    private (int X, int Y) ToClient(POINT screen)
    {
        POINT point = screen;
        _ = ScreenToClient(_hwnd, &point);
        return (point.x, point.y);
    }

    // When a point of the history was taken: its performance count, which is the stopwatch's clock on Windows; the clock now when
    // the information has none.
    private TimeSpan TimeOf(in POINTER_INFO info) => info.PerformanceCount != 0 ? Time.GetElapsedTime(0, (long)info.PerformanceCount) : Now();

    // Reads the wheel's lines and characters a notch (SPI_GETWHEELSCROLLLINES, SPI_GETWHEELSCROLLCHARS); a call that fails keeps
    // the value before.
    private static void ReadWheelSettings()
    {
        uint lines;
        if (SystemParametersInfoW(SPI.SPI_GETWHEELSCROLLLINES, 0, &lines, 0) != BOOL.FALSE)
        {
            _wheelLines = lines;
        }

        uint chars;
        if (SystemParametersInfoW(SPI.SPI_GETWHEELSCROLLCHARS, 0, &chars, 0) != BOOL.FALSE)
        {
            _wheelChars = chars;
        }
    }

    // Counts a press as GPUI's ClickState does: one more when it is the same button of the same kind of pointer, within the OS's
    // double-click time, and within the OS's double-click rectangle around the press before, or for a touch or a pen within 50
    // logical pixels (design §7, "Touch and pen") once the press before is released; 1 otherwise. A release takes its press's count.
    private int Count(in PointerInput input)
    {
        int id = input.PointerId;
        if (input.Kind == PointerInputKind.Up)
        {
            if (id == 0)
            {
                return _clickCount;
            }

            _clickOpen &= id != _clickPointer;
            return _pointerClicks[id];
        }

        long now = Time.GetTimestamp();
        bool near =
            input.PointerType == PointerType.Mouse
                ? Math.Abs(input.X - _clickX) * 2 <= GetSystemMetrics(SM.SM_CXDOUBLECLK)
                    && Math.Abs(input.Y - _clickY) * 2 <= GetSystemMetrics(SM.SM_CYDOUBLECLK)
                : MathF.Sqrt(((input.X - _clickX) * (input.X - _clickX)) + ((input.Y - _clickY) * (input.Y - _clickY)))
                    <= TapDoubleDistance * ScaleFactor;
        bool again =
            _clickCount > 0
            && input.Button == _clickButton
            && input.PointerType == _clickType
            && !_clickOpen
            && Time.GetElapsedTime(_clickTime, now).TotalMilliseconds <= GetDoubleClickTime()
            && near;
        _clickCount = again ? _clickCount + 1 : 1;
        _clickButton = input.Button;
        _clickType = input.PointerType;
        _clickTime = now;
        _clickX = input.X;
        _clickY = input.Y;
        _clickPointer = id;
        _clickOpen = id != 0;
        _pointerClicks[id] = _clickCount;
        return _clickCount;
    }

    // A touch's or a pen's press that ends with no release (canceled, or its pointer gone) is no first tap.
    private void ForgetPress(int id)
    {
        if (_clickOpen && id == _clickPointer)
        {
            _clickOpen = false;
            _clickCount = 0;
        }
    }

    private TimeSpan Now() => Time.GetElapsedTime(0);

    private void Deliver(in PointerInput input) => PointerInputReceived?.Invoke(in input);
}
