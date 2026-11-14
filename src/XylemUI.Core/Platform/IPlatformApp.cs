namespace XylemUI;

/// <summary>
/// The app tier of the platform contract (design §6.0): what an OS host has once per UI thread. It runs the UI thread's loop, owns
/// the dispatcher that the loop drains and the OS's clipboard, and opens the windows.
/// </summary>
/// <remarks>
/// A host sets <see cref="Dispatcher"/> as <see cref="UiDispatcher.Current"/> and as the UI thread's synchronization context when
/// it is made, and puts back the values it replaced when it is disposed (design §2.12). The app makes it on the UI thread and
/// disposes it there, after the windows it opened. <see cref="IDisposable.Dispose"/> called off the UI thread throws
/// <see cref="InvalidOperationException"/> and changes nothing; a second call on the UI thread does nothing (design §6.0).
/// </remarks>
internal interface IPlatformApp : IDisposable
{
    /// <summary>
    /// Gets the UI thread's queue, as the concrete type the loop drains.
    /// </summary>
    UiDispatcher Dispatcher { get; }

    /// <summary>
    /// Gets the OS's clipboard as plain text (design §4.9, "The clipboard"). The OS has one, so the app tier has it, and a window that
    /// closes does not take it away: on Windows its owner is a message-only window of the host (design §6.0).
    /// </summary>
    IClipboardBackend Clipboard { get; }

    /// <summary>
    /// Makes a window with the renderer, the frame clock, the input method, and the accessibility adapter paired with it, for
    /// <paramref name="core"/>, whose tree the adapter reads.
    /// </summary>
    /// <param name="core">The window core the window serves.</param>
    /// <param name="title">The title bar's text.</param>
    /// <param name="width">The width of the client area, in logical pixels.</param>
    /// <param name="height">The height of the client area, in logical pixels.</param>
    /// <param name="show"><see langword="true"/> to show the window before its renderer is made.</param>
    /// <returns>The window; the core disposes it.</returns>
    /// <remarks>
    /// A window counts as open until it is disposed, not until it closes: the loop's one frame clock comes back to the host when the
    /// window is disposed (design §6.0, "4a の形"; Issue #928).
    /// </remarks>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread, or a window of the host is open: a second
    /// window comes with row MW1.</exception>
    [UiThreadOnly]
    IPlatformWindow OpenWindow(WindowCore core, string title, float width, float height, bool show);

    /// <summary>
    /// Handles the OS's messages, drains <see cref="Dispatcher"/>, and runs the frames, until <see cref="Quit"/> is called.
    /// </summary>
    /// <returns>The exit code passed to <see cref="Quit"/>.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    int Run();

    /// <summary>
    /// Makes <see cref="Run"/> return once the messages already queued are handled.
    /// </summary>
    /// <param name="exitCode">The value <see cref="Run"/> returns.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    void Quit(int exitCode);

    /// <summary>
    /// Wakes the UI thread if its loop waits. Safe to call from any thread; does nothing before the loop has started.
    /// </summary>
    void Wake();
}
