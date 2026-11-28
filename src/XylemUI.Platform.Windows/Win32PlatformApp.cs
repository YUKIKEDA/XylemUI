using XylemUI.Text;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The app tier of the Windows host (design §6.0): the UI thread's message loop, its dispatcher, OLE, DirectWrite's fonts and glyph
/// rasterizers, the OS's settings, the clipboard, and the window it opens (<see cref="Win32PlatformWindow"/>), one at a time until
/// row MW1. Making it initializes OLE on the UI thread, and sets the loop's dispatcher as <see cref="UiDispatcher.Current"/> and as
/// the thread's synchronization context; disposing it puts back the dispatcher and the context it replaced, uninitializes OLE, and
/// disposes the fonts.
/// </summary>
/// <remarks>
/// The loop is <see cref="Win32MessageLoop"/>, which is static: the process has one UI thread. The handler of the exceptions that
/// callbacks throw (<see cref="Win32MessageLoop.CallbackExceptionHandler"/>) stays there, outside the contract.
/// </remarks>
internal sealed class Win32PlatformApp : IPlatformApp
{
    private readonly IUiDispatcher? _previousDispatcher;
    private readonly SynchronizationContext? _previousContext;
    private readonly Win32PlatformSettings _settings;
    private readonly DirectWriteFontProvider _fonts;
    private Win32PlatformWindow? _window;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32PlatformApp"/> class on the UI thread. When it throws, it has changed
    /// nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, the thread is in COM's multithreaded apartment (the app's <c>Main</c> lacks
    /// <c>[STAThread]</c>), OLE failed to initialize, or DirectWrite could not create the fonts' factory, fallback, or collection.
    /// </exception>
    [UiThreadOnly]
    public Win32PlatformApp()
    {
        // Checks the thread first, so that a thread that is not the UI thread throws with OLE untouched and no fonts made.
        Dispatcher = Win32MessageLoop.Dispatcher;

        // Reading Windows' settings changes nothing, so it comes before what a failure would have to put back.
        _settings = new Win32PlatformSettings();

        // The UI thread's one font provider (design §3.2). DirectWrite's shared factory needs no COM apartment, and a provider that
        // throws has released what it made, so it comes before OLE: a failure here leaves nothing to put back.
        _fonts = new DirectWriteFontProvider();

        // Before the first window, which makes its DirectManipulation viewport with COM and registers its drop target with OLE
        // (design §7, "Touch and pen", I1c; "Drag and drop"). It throws before the dispatcher and the context change, so a
        // failure leaves only the fonts to dispose.
        try
        {
            Com.InitializeUiThread();
        }
        catch
        {
            _fonts.Dispose();
            throw;
        }

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
    /// <remarks>
    /// DirectWrite's system font fallback (<see cref="DirectWriteFontProvider"/>), made with the host and disposed with it.
    /// </remarks>
    public IFontProvider Fonts => _fonts;

    /// <inheritdoc />
    /// <remarks>
    /// Read when the host is made, and again on each <c>WM_SETTINGCHANGE</c>, <c>WM_SYSCOLORCHANGE</c>, and
    /// <c>WM_DWMCOLORIZATIONCOLORCHANGED</c> the host's window receives.
    /// </remarks>
    public IPlatformSettings Settings => _settings;

    /// <inheritdoc />
    public IClipboardBackend Clipboard { get; } = new Win32Clipboard();

    /// <summary>
    /// Gets a value indicating whether the windows the host opens draw with the WARP software rasterizer, for the tests.
    /// </summary>
    internal bool UseWarp { get; init; }

    /// <summary>
    /// Gets a function that makes, from the renderer of each window the host opens, the target the window gives its core; for the
    /// end-to-end benchmark, which times the draw and the present through it, on the frames and on the synchronous resize path
    /// (Issue #933). <see langword="null"/> gives the core the renderer itself.
    /// </summary>
    internal Func<IRenderTarget, IRenderTarget>? WrapTarget { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// The loop's scheduler holds one frame clock, which a second window would take from the first (design §6.0, "The frame clock
    /// and the scheduler"), so a window counts as open until it is disposed, when it gives the clock back.
    /// </remarks>
    [UiThreadOnly]
    public IPlatformWindow OpenWindow(WindowCore core, string title, float width, float height, bool show)
    {
        UiThread.VerifyAccess();
        if (_window is not null)
        {
            throw new InvalidOperationException("A window of the app is open: a second window comes with row MW1 (design §6.0).");
        }

        _window = new Win32PlatformWindow(core, _settings, title, width, height, show, UseWarp, () => _window = null, WrapTarget);
        return _window;
    }

    /// <inheritdoc />
    public IGlyphRasterizer CreateRasterizer() => new DirectWriteGlyphRasterizer();

    /// <inheritdoc />
    [UiThreadOnly]
    public int Run() => Win32MessageLoop.Run();

    /// <inheritdoc />
    [UiThreadOnly]
    public void Quit(int exitCode) => Win32MessageLoop.Quit(exitCode);

    /// <inheritdoc />
    public void Wake() => Win32MessageLoop.Wake();

    /// <summary>
    /// Puts back the dispatcher and the synchronization context that the constructor replaced, uninitializes OLE, and disposes the
    /// fonts. A second call on the UI thread does nothing. A call off the UI thread throws and changes nothing (design §6.0).
    /// </summary>
    /// <remarks>
    /// The app disposes the host after the window core and the text tools made from <see cref="Fonts"/> (design §6.0, "The order
    /// of creation", step 8), so nothing reads the fonts after this.
    /// </remarks>
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

        // In the reverse of the order the constructor made them: the fonts were made before OLE.
        _fonts.Dispose();
    }
}
