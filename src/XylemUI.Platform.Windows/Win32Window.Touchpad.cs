using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

// A touchpad's pan through DirectManipulation (design §7, "Touch and pen", row I1c): the window hands a touchpad's contact to its
// viewport on DM_POINTERHITTEST, polls the viewport once a frame from the interaction's begin to its end, and delivers the pan's
// start, updates, and end with the frame's time, at the cursor where the pan started.
public sealed unsafe partial class Win32Window
{
    // winuser.h line 2424 (Windows SDK 10.0.26100); TerraFX does not name it.
    private const uint DmPointerHitTest = 0x0250;

    // The Win32 id the pan's number is kept under: a pan is no Win32 pointer, and Win32 gives no pointer this id.
    private const uint PanWin32Id = uint.MaxValue;

    private TouchpadGestures? _touchpad;
    private int _panNumber;
    private int _panX;
    private int _panY;

    /// <summary>
    /// Gets or sets what asks for the window's next frame when a touchpad's interaction begins (design §7, "Touch and pen", I1c);
    /// the frames then poll the viewport while it goes on.
    /// </summary>
    internal Action? GestureFrameNeeded { get; set; }

    /// <summary>
    /// Gets the window's DirectManipulation viewport: DirectManipulation's, made with the window, <see langword="null"/> when it
    /// could not be made, or the tests' (<see cref="UseTouchpadViewport"/>).
    /// </summary>
    internal ITouchpadViewport? TouchpadViewport => _touchpad?.Viewport;

    /// <summary>
    /// Replaces the window's viewport, disposing the one before: the tests' fake, or none.
    /// </summary>
    /// <param name="viewport">The viewport, or <see langword="null"/>.</param>
    internal void UseTouchpadViewport(ITouchpadViewport? viewport)
    {
        _touchpad?.Viewport.Dispose();
        _touchpad = viewport is null ? null : new TouchpadGestures(viewport);
    }

    /// <inheritdoc />
    bool IPointerPlatform.UpdateGestures(TimeSpan frameTime)
    {
        if (_touchpad is not { } touchpad)
        {
            return false;
        }

        touchpad.Viewport.Update();
        touchpad.CheckReset();
        while (touchpad.TryDequeue(out TouchpadPanKind kind, out float x, out float y))
        {
            DeliverPan(kind, x, y, frameTime);
        }

        return touchpad.Polling;
    }

    /// <summary>
    /// Takes a change of the viewport's status, from its handler.
    /// </summary>
    /// <param name="current">The status now.</param>
    /// <param name="previous">The status before.</param>
    internal void TouchpadStatusChanged(DIRECTMANIPULATION_STATUS current, DIRECTMANIPULATION_STATUS previous) =>
        _touchpad?.OnStatusChanged(current, previous);

    /// <summary>
    /// Takes a new translation of the viewport's content, from its handler.
    /// </summary>
    /// <param name="x">The translation along x, in physical pixels.</param>
    /// <param name="y">The translation along y, in physical pixels.</param>
    internal void TouchpadContentUpdated(float x, float y) => _touchpad?.OnContentUpdated(x, y);

    /// <summary>
    /// Takes an interaction's begin or end, from its handler: a begin asks for a frame, which polls the viewport.
    /// </summary>
    /// <param name="begin">Whether it began.</param>
    internal void TouchpadInteraction(bool begin)
    {
        if (_touchpad?.OnInteraction(begin) == true)
        {
            GestureFrameNeeded?.Invoke();
        }
    }

    // Made in the constructor, after the window's size; a window without it scrolls its touchpad by the wheel's messages.
    private void CreateTouchpad(HWND hwnd)
    {
        RECT client;
        _ = GetClientRect(hwnd, &client);
        if (DirectManipulationViewport.TryCreate(hwnd, _id, client.right, client.bottom) is { } viewport)
        {
            _touchpad = new TouchpadGestures(viewport);
        }
    }

    // DM_POINTERHITTEST hands a touchpad's contact to the viewport, and asks for no frame: the interaction's begin does (Chromium's
    // direct_manipulation_helper_win.cc lines 248 to 268). The wheel's messages are dropped while the OS moves the content. Returns
    // whether the message was handled.
    private bool HandleTouchpadMessage(uint message, WPARAM wParam)
    {
        if (_touchpad is not { } touchpad)
        {
            return false;
        }

        if (message == DmPointerHitTest)
        {
            uint win32 = (uint)((nuint)wParam & 0xFFFF);
            if (PointerReader.TryGetType(win32, out POINTER_INPUT_TYPE type) && type == POINTER_INPUT_TYPE.PT_TOUCHPAD)
            {
                touchpad.Viewport.SetContact(win32);
            }

            return false;
        }

        return message is WmMouseWheel or WmMouseHWheel && touchpad.DropsWheel;
    }

    private void ResizeTouchpad(WPARAM wParam, LPARAM lParam)
    {
        if (_touchpad is { } touchpad && (nuint)wParam != SizeMinimized)
        {
            touchpad.Viewport.Resize((int)((nuint)lParam & 0xFFFF), (int)(((nuint)lParam >> 16) & 0xFFFF));
        }
    }

    // Before the window is destroyed: the manager deactivates for the window.
    private void DestroyTouchpad()
    {
        _touchpad?.Viewport.Dispose();
        _touchpad = null;
    }

    // A queued input of the pan: the start takes a number and the cursor's position, which the updates and the end keep; an
    // update's change goes from physical to logical pixels; the end gives the number back.
    private void DeliverPan(TouchpadPanKind kind, float x, float y, TimeSpan frameTime)
    {
        PointerInputKind input;
        switch (kind)
        {
            case TouchpadPanKind.Start:
                POINT cursor;
                _ = GetCursorPos(&cursor);
                (_panX, _panY) = ToClient(cursor);
                _panNumber = NumberOf(PanWin32Id, PointerType.Touchpad);
                input = PointerInputKind.PanStart;
                break;
            case TouchpadPanKind.Update:
                input = PointerInputKind.PanUpdate;
                break;
            default:
                input = PointerInputKind.PanEnd;
                break;
        }

        if (_panNumber == 0)
        {
            return;
        }

        var delta = input == PointerInputKind.PanUpdate ? new System.Numerics.Vector2(x, y) / ScaleFactor : default;
        Deliver(
            new PointerInput(
                input,
                _panX,
                _panY,
                PointerButton.None,
                PointerButtons.None,
                KeyModifiers.None,
                0,
                frameTime,
                PointerType.Touchpad,
                delta,
                _panNumber,
                IsPrimary: true
            )
        );

        if (input == PointerInputKind.PanEnd)
        {
            _pointerTaken[_panNumber] = false;
            _panNumber = 0;
        }
    }
}
