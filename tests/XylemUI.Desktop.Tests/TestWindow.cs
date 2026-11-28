using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Desktop.Tests;

// The window of an app as XylemApp.Run makes it (XylemApp.OpenWindow: design §6.0, "The order of creation", steps 4 to 7), for the
// tests of a real window: the Windows host's window, hidden unless asked to show after its first frame, on WARP when asked, opened
// by a host of the tests' own (TestHost). The tests run the loop themselves, so nothing sets a dispatcher, a context, or OLE, as
// before row PL1 (Issue #928). The window owns its host, whose fonts its text tools are made from: one it makes, or one a test that
// watches the window's creation made and gives it (useWarp is then the host's).
internal sealed class TestWindow : IDisposable
{
    private readonly TextContext _text;
    private bool _disposed;

    public TestWindow(Func<Element> root, WindowOptions options, bool show = false, bool useWarp = false, TestHost? host = null)
    {
        Host = host ?? new TestHost(useWarp);
        try
        {
            _text = new TextContext(Host.Fonts);
            try
            {
                Core = XylemApp.OpenWindow(Host, _text, root, options, show);
            }
            catch
            {
                _text.Dispose();
                throw;
            }
        }
        catch
        {
            Host.Dispose();
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

    // The core disposes the root and the window; the text tools and then the host, which owns the fonts, go after it, as XylemApp.Run
    // disposes them.
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Core.Dispose();
        _text.Dispose();
        Host.Dispose();
    }
}

// A host for the tests' windows: it opens the Windows host's window (Win32PlatformWindow) on WARP with no check for a second one,
// and sets no dispatcher, synchronization context, or OLE, as the tests opened windows before row PL1 (design §6.0, "4a の形";
// Issue #928). Its settings are the Windows host's, read when it is made: a test that sets their readers sets them before. Its fonts
// are DirectWrite's, made with it and disposed with it, and its rasterizers DirectWrite's, as the Windows host's. It records the
// codes it is asked to quit with, quitting the message loop with each as the Windows host does. The function that wraps the renderer
// and the message hook, when given, go to each window it opens (as Win32PlatformApp.WrapTarget does, and Win32Window.MessageHook),
// before the window core connects.
internal sealed class TestHost(bool useWarp, Func<IRenderTarget, IRenderTarget>? wrapTarget = null, Func<Win32Window, uint, bool>? messageHook = null)
    : IPlatformApp
{
    private readonly Win32PlatformSettings _settings = new();
    private readonly DirectWriteFontProvider _fonts = new();

    public List<int> Quits { get; } = [];

    // The window opened last; kept after it is disposed.
    public Win32PlatformWindow? Window { get; private set; }

    // What wraps each window it opens before the window core gets it, for a test that records the core's calls; null gives the
    // window itself.
    public Func<IPlatformWindow, IPlatformWindow>? WrapWindow { get; init; }

    public UiDispatcher Dispatcher => Win32MessageLoop.Dispatcher;

    public IFontProvider Fonts => _fonts;

    public IPlatformSettings Settings => _settings;

    public IClipboardBackend Clipboard { get; } = new Win32Clipboard();

    public IGlyphRasterizer CreateRasterizer() => new DirectWriteGlyphRasterizer();

    public IPlatformWindow OpenWindow(WindowCore core, string title, float width, float height)
    {
        Window = new Win32PlatformWindow(core, _settings, title, width, height, useWarp, wrapTarget: wrapTarget);
        Window.Window.MessageHook = messageHook;
        return WrapWindow is null ? Window : WrapWindow(Window);
    }

    public int Run() => Win32MessageLoop.Run();

    public void Quit(int exitCode)
    {
        Quits.Add(exitCode);
        Win32MessageLoop.Quit(exitCode);
    }

    public void Wake() => Win32MessageLoop.Wake();

    public void Dispose() => _fonts.Dispose();
}
