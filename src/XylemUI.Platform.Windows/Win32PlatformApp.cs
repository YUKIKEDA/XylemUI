namespace XylemUI.Platform.Windows;

/// <summary>
/// The app tier of the Windows host (design §6.0): the UI thread's message loop, its dispatcher, and OLE. Making it initializes
/// OLE on the UI thread, and sets the loop's dispatcher as <see cref="UiDispatcher.Current"/> and as the thread's synchronization
/// context; disposing it puts back the dispatcher and the context it replaced, and uninitializes OLE.
/// </summary>
/// <remarks>
/// The loop is <see cref="Win32MessageLoop"/>, which is static: the process has one UI thread. The handler of the exceptions that
/// callbacks throw (<see cref="Win32MessageLoop.CallbackExceptionHandler"/>) stays there, outside the contract.
/// </remarks>
internal sealed class Win32PlatformApp : IPlatformApp
{
    private readonly IUiDispatcher? _previousDispatcher;
    private readonly SynchronizationContext? _previousContext;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32PlatformApp"/> class on the UI thread. When it throws, it has changed
    /// nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, the thread is in COM's multithreaded apartment (the app's <c>Main</c> lacks
    /// <c>[STAThread]</c>), or OLE failed to initialize.
    /// </exception>
    [UiThreadOnly]
    public Win32PlatformApp()
    {
        // Checks the thread first, so that a thread that is not the UI thread throws with OLE untouched.
        Dispatcher = Win32MessageLoop.Dispatcher;

        // Before the first window, which makes its DirectManipulation viewport with COM and registers its drop target with OLE
        // (design §7, "Touch and pen", I1c; "Drag and drop"). It throws before the dispatcher and the context change, so a
        // failure leaves nothing to put back.
        Com.InitializeUiThread();

        // The loop's queue is where workers post, from the root function on (design §2.12). The loop's context goes in before the
        // window too: the root function makes elements that keep it, as an image does, whose decode posts its result to it
        // (design §5.0.4, "Decoding threads"; Issue #785).
        _previousContext = SynchronizationContext.Current;
        _previousDispatcher = UiDispatcher.Exchange(Dispatcher);
        SynchronizationContext.SetSynchronizationContext(Dispatcher.SynchronizationContext);
    }

    /// <inheritdoc />
    public UiDispatcher Dispatcher { get; }

    /// <inheritdoc />
    [UiThreadOnly]
    public int Run() => Win32MessageLoop.Run();

    /// <inheritdoc />
    [UiThreadOnly]
    public void Quit(int exitCode) => Win32MessageLoop.Quit(exitCode);

    /// <inheritdoc />
    public void Wake() => Win32MessageLoop.Wake();

    /// <summary>
    /// Puts back the dispatcher and the synchronization context that the constructor replaced, and uninitializes OLE. A second
    /// call on the UI thread does nothing. A call off the UI thread throws and changes nothing (design §6.0).
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    public void Dispose()
    {
        // Before the flag: another thread would put back its own context, uninitialize OLE where it was never initialized, and
        // leave the UI thread's OLE initialized, since the flag would already be set.
        UiThread.VerifyAccess();
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The headless tests run a real window's loop inside work of their own UI thread, whose queue they post to afterwards.
        SynchronizationContext.SetSynchronizationContext(_previousContext);
        _ = UiDispatcher.Exchange(_previousDispatcher);
        Com.UninitializeUiThread();
    }
}
