namespace XylemUI;

/// <summary>
/// What the input of a window asks of the platform (design §7, "Pointer input"): to capture the pointer and to show a cursor.
/// Core references no OS type; the window implements it.
/// </summary>
internal interface IPointerPlatform
{
    /// <summary>
    /// Captures the pointer to the window, so that its moves and releases come outside the window too (Win32's <c>SetCapture</c>).
    /// </summary>
    void Capture();

    /// <summary>
    /// Releases the capture (Win32's <c>ReleaseCapture</c>). The window does not report the release as a capture lost.
    /// </summary>
    void ReleaseCapture();

    /// <summary>
    /// Shows <paramref name="cursor"/> while the pointer is over the window's client area.
    /// </summary>
    /// <param name="cursor">The shape.</param>
    void SetCursor(CursorKind cursor);

    /// <summary>
    /// Writes the coalesced points of the pointer message being handled for a pointer, newest first, in the window's client
    /// coordinates (physical pixels), as Win32's pointer history gives them (design §7, "Touch and pen"). It is called while the
    /// message is delivered: Win32 keeps the history of the thread's last pointer message only.
    /// </summary>
    /// <param name="pointerId">The pointer's number, as <see cref="PointerInput.PointerId"/> has it.</param>
    /// <param name="destination">Where to write; no more points than it holds.</param>
    /// <returns>The points written; 0 when the platform keeps no history, which leaves the event's own point alone.</returns>
    int ReadHistory(int pointerId, Span<PointerHistoryPoint> destination) => 0;
}

/// <summary>
/// One coalesced point of a pointer's history as the platform reads it (design §7, "Touch and pen").
/// </summary>
/// <param name="X">The column, in the window's client coordinates (physical pixels).</param>
/// <param name="Y">The row, in physical pixels.</param>
/// <param name="Pressure">The pressure, 0 to 1.</param>
/// <param name="TiltX">The tilt along x, in degrees.</param>
/// <param name="TiltY">The tilt along y, in degrees.</param>
/// <param name="Twist">The twist, in degrees.</param>
/// <param name="Timestamp">When the point was taken.</param>
internal readonly record struct PointerHistoryPoint(int X, int Y, float Pressure, float TiltX, float TiltY, float Twist, TimeSpan Timestamp);
