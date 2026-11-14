using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Desktop.Tests;

// The window of an app as XylemApp.Run makes it (XylemApp.OpenWindow: design §6.0, "The order of creation", steps 4 to 7), for the
// tests of a real window: the Windows host's window, hidden unless asked, on WARP when asked, opened by a host of the tests' own
// (TestHost). The tests run the loop themselves, so nothing sets a dispatcher, a context, or OLE, as before row PL1 (Issue #928).
internal sealed class TestWindow : IDisposable
{
    private readonly DirectWriteFontProvider _fonts = new();
    private readonly TextContext _text;
    private bool _disposed;

    public TestWindow(Func<Element> root, WindowOptions options, bool show = false, bool useWarp = false)
    {
        Host = new TestHost(useWarp);
        _text = new TextContext(_fonts);
        try
        {
            Core = XylemApp.OpenWindow(Host, _text, root, options, () => Host.Window!.UiLanguages, show);
        }
        catch
        {
            _text.Dispose();
            _fonts.Dispose();
            throw;
        }
    }

    // After the window closed and its root scope was disposed.
    public event EventHandler? Closed
    {
        add => Core.Closed += value;
        remove => Core.Closed -= value;
    }

    public WindowCore Core { get; }

    public TestHost Host { get; }

    public UiTree Tree => Core.Tree;

    public Win32Window Window => Host.Window!.Window;

    public WindowFrames Frames => Core.Frames;

    public WindowInput Input => Core.Input;

    public ScopeHandle RootScope => Core.RootScope;

    // The core disposes the root and the window; the text tools go after it, as XylemApp.Run disposes them.
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Core.Dispose();
        _text.Dispose();
        _fonts.Dispose();
    }
}

// A host for the tests' windows: it opens the Windows host's window (Win32PlatformWindow) on WARP with no check for a second one,
// and sets no dispatcher, synchronization context, or OLE, as the tests opened windows before row PL1 (design §6.0, "4a の形";
// Issue #928). It records the codes it is asked to quit with, quitting the message loop with each as the Windows host does.
internal sealed class TestHost(bool useWarp) : IPlatformApp
{
    public List<int> Quits { get; } = [];

    // The window opened last; kept after it is disposed.
    public Win32PlatformWindow? Window { get; private set; }

    public UiDispatcher Dispatcher => Win32MessageLoop.Dispatcher;

    public IClipboardBackend Clipboard { get; } = new Win32Clipboard();

    public IPlatformWindow OpenWindow(WindowCore core, string title, float width, float height, bool show)
    {
        Window = new Win32PlatformWindow(core, title, width, height, show, useWarp);
        return Window;
    }

    public int Run() => Win32MessageLoop.Run();

    public void Quit(int exitCode)
    {
        Quits.Add(exitCode);
        Win32MessageLoop.Quit(exitCode);
    }

    public void Wake() => Win32MessageLoop.Wake();

    public void Dispose() { }
}
