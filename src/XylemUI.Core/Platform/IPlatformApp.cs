namespace XylemUI;

/// <summary>
/// The app tier of the platform contract (design §6.0): what an OS host has once per UI thread. It runs the UI thread's loop and
/// owns the dispatcher that the loop drains.
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
