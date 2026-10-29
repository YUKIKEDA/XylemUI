using TerraFX.Interop.DirectX;

namespace XylemUI.Platform.Windows;

/// <summary>
/// What the window asks of its DirectManipulation viewport (design §7, "Touch and pen", row I1c): DirectManipulation's, or the
/// tests' (as Flutter's <c>direct_manipulation_unittests.cc</c> fakes the viewport).
/// </summary>
internal interface ITouchpadViewport : IDisposable
{
    /// <summary>
    /// Hands a touchpad's contact to the viewport (<c>IDirectManipulationViewport::SetContact</c>).
    /// </summary>
    /// <param name="pointerId">Win32's pointer id, from <c>DM_POINTERHITTEST</c>.</param>
    void SetContact(uint pointerId);

    /// <summary>
    /// Lets the viewport process its input (<c>IDirectManipulationUpdateManager::Update</c>), which may call the handler back.
    /// </summary>
    void Update();

    /// <summary>
    /// Sets the viewport's rectangle to the client area (<c>SetViewportRect</c>).
    /// </summary>
    /// <param name="width">The client width, in physical pixels.</param>
    /// <param name="height">The client height, in physical pixels.</param>
    void Resize(int width, int height);

    /// <summary>
    /// Reads the translation of the viewport's primary content (<c>GetContentTransform</c>'s fifth and sixth values).
    /// </summary>
    /// <returns>The translation, in physical pixels.</returns>
    (float X, float Y) ReadTranslation();

    /// <summary>
    /// Moves the content back to where it started (<c>GetViewportRect</c>, then <c>ZoomToRect</c> without animation).
    /// </summary>
    void Reset();
}

/// <summary>
/// Turns a DirectManipulation viewport's states and transforms into a touchpad's pan (design §7, "Touch and pen", row I1c), as
/// Flutter's <c>direct_manipulation.cc</c> does (lines 49 to 158): a start when the viewport runs, an update for each change of the
/// translation from the start's, and an end when it stops running. The OS's inertia moves the content too, but gives no update;
/// when it ends, the content goes back to where it started. The handler's callbacks queue what they make; the window delivers the
/// queue after each <see cref="ITouchpadViewport.Update"/>, in the frame.
/// </summary>
/// <remarks>
/// The frames go on, polling the viewport, from the interaction's begin to its end, and until the content is back, as Chromium's
/// helper polls (<c>direct_manipulation_event_handler_win.cc</c> lines 291 to 305). The queue is an array it reuses.
/// </remarks>
internal sealed class TouchpadGestures(ITouchpadViewport viewport)
{
    private (TouchpadPanKind Kind, float X, float Y)[] _queue = new (TouchpadPanKind, float, float)[16];
    private int _head;
    private int _count;
    private float _startX;
    private float _startY;
    private float _lastX;
    private float _lastY;
    private bool _running;
    private bool _inertia;
    private bool _resetting;
    private bool _interacting;

    /// <summary>
    /// Gets the viewport.
    /// </summary>
    public ITouchpadViewport Viewport { get; } = viewport;

    /// <summary>
    /// Gets a value indicating whether the frames go on polling the viewport: an interaction runs, or the content goes back.
    /// </summary>
    public bool Polling => _interacting || _resetting;

    /// <summary>
    /// Gets a value indicating whether the wheel's messages are dropped: from the pan's start until the OS's inertia ends, while
    /// the OS moves the content and may also send the same motion as a wheel (design §7, "Touch and pen", I1c).
    /// </summary>
    public bool DropsWheel => _running || _inertia;

    /// <summary>
    /// Gets the number of pan inputs waiting to be delivered.
    /// </summary>
    public int Pending => _count;

    /// <summary>
    /// Takes an interaction's begin or end (<c>IDirectManipulationInteractionEventHandler::OnInteraction</c>).
    /// </summary>
    /// <param name="begin">Whether it began.</param>
    /// <returns>Whether the frames must start polling: a begin while they did not.</returns>
    public bool OnInteraction(bool begin)
    {
        bool start = begin && !Polling;
        _interacting = begin;
        return start;
    }

    /// <summary>
    /// Takes a change of the viewport's status (<c>OnViewportStatusChanged</c>).
    /// </summary>
    /// <param name="current">The status now.</param>
    /// <param name="previous">The status before.</param>
    public void OnStatusChanged(DIRECTMANIPULATION_STATUS current, DIRECTMANIPULATION_STATUS previous)
    {
        // The content going back makes its own changes, which are no pan (Flutter's during_synthesized_reset_, lines 69 to 72).
        if (_resetting)
        {
            if (current == DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_READY)
            {
                _resetting = false;
            }

            return;
        }

        if (current == DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_RUNNING)
        {
            if (!_running)
            {
                // The translation the content has now is the start's: it may not be back from the last gesture yet (Flutter's
                // initial_gesture_data_, lines 74 to 90).
                (_startX, _startY) = Viewport.ReadTranslation();
                (_lastX, _lastY) = (0, 0);
                _running = true;
                _inertia = false;
                Enqueue(TouchpadPanKind.Start, 0, 0);
            }

            return;
        }

        if (previous == DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_RUNNING && _running)
        {
            _running = false;
            _inertia = current == DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_INERTIA;
            Enqueue(TouchpadPanKind.End, 0, 0);
        }

        if (previous == DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_INERTIA && current != DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_INERTIA)
        {
            _inertia = false;
            if (current == DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_READY)
            {
                _resetting = true;
                Viewport.Reset();
            }
        }
    }

    /// <summary>
    /// Takes a new transform of the content (<c>OnContentUpdated</c>): while the viewport runs, the change of the translation since
    /// the last update; during the OS's inertia, nothing (Flutter, line 152).
    /// </summary>
    /// <param name="x">The translation along x, in physical pixels.</param>
    /// <param name="y">The translation along y, in physical pixels.</param>
    public void OnContentUpdated(float x, float y)
    {
        if (!_running || _resetting)
        {
            return;
        }

        float totalX = x - _startX;
        float totalY = y - _startY;
        float dx = totalX - _lastX;
        float dy = totalY - _lastY;
        (_lastX, _lastY) = (totalX, totalY);
        if (dx != 0 || dy != 0)
        {
            Enqueue(TouchpadPanKind.Update, dx, dy);
        }
    }

    /// <summary>
    /// Ends the content's going back once its translation reads 0 again: a reset whose status changes do not come would otherwise
    /// keep the frames polling.
    /// </summary>
    public void CheckReset()
    {
        if (_resetting && Viewport.ReadTranslation() == (0, 0))
        {
            _resetting = false;
        }
    }

    /// <summary>
    /// Takes the oldest pan input waiting.
    /// </summary>
    /// <param name="kind">What it is.</param>
    /// <param name="x">An update's change along x, in physical pixels.</param>
    /// <param name="y">An update's change along y, in physical pixels.</param>
    /// <returns>Whether one was waiting.</returns>
    public bool TryDequeue(out TouchpadPanKind kind, out float x, out float y)
    {
        if (_count == 0)
        {
            (kind, x, y) = (default, 0, 0);
            return false;
        }

        (kind, x, y) = _queue[_head];
        _head = (_head + 1) % _queue.Length;
        _count--;
        return true;
    }

    private void Enqueue(TouchpadPanKind kind, float x, float y)
    {
        if (_count == _queue.Length)
        {
            var grown = new (TouchpadPanKind, float, float)[_queue.Length * 2];
            for (int i = 0; i < _count; i++)
            {
                grown[i] = _queue[(_head + i) % _queue.Length];
            }

            _queue = grown;
            _head = 0;
        }

        _queue[(_head + _count) % _queue.Length] = (kind, x, y);
        _count++;
    }
}

/// <summary>
/// What a queued input of a touchpad's pan is.
/// </summary>
internal enum TouchpadPanKind : byte
{
    /// <summary>
    /// The pan started.
    /// </summary>
    Start,

    /// <summary>
    /// The pan moved.
    /// </summary>
    Update,

    /// <summary>
    /// The pan ended.
    /// </summary>
    End,
}
