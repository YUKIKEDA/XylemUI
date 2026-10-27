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
}
