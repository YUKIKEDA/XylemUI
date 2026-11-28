using System.Runtime.InteropServices;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// Starts an app (design §2.13, "The window"): opens a window, builds its tree from the root function, and runs the UI thread's
/// message loop until the last window closes.
/// </summary>
public static class XylemApp
{
    // The color behind the tree. The text that sets no color is black (WindowFrames), so the window is white, as Avalonia's light
    // theme draws a window.
    private static readonly Color Background = Color.FromRgb(0xFFFFFF);

    private static int _openWindows;
    private static bool _running;

    /// <summary>
    /// Opens a window whose content <paramref name="root"/> makes, and runs the message loop until the last window is closed. The
    /// calling thread becomes the UI thread. <paramref name="root"/> runs once, in the window's root scope; closing the window
    /// disposes that scope, which takes out every node, handler, and effect it made.
    /// </summary>
    /// <param name="root">The root function: makes the window's content with <see cref="Elements"/> and returns it.</param>
    /// <param name="options">The window; <see langword="null"/> for the defaults of <see cref="WindowOptions"/>.</param>
    /// <returns>The exit code of the message loop: 0 when the last window closed.</returns>
    /// <exception cref="InvalidOperationException">
    /// Another thread is the UI thread, a message loop already runs, no platform host is registered for the runtime identifier
    /// (the app references no host for it, or is built without the source generator of <c>XylemUI.Core</c>; design §6.0), the
    /// calling thread is in COM's multithreaded apartment (the Windows host; the app's <c>Main</c> lacks <c>[STAThread]</c>), the
    /// window, its renderer, or the root function failed, or no default key bindings are registered (the app is built without the
    /// source generator of <c>XylemUI.Core</c> and did not call <see cref="Keymap.RegisterDefaults"/>).
    /// </exception>
    /// <exception cref="ArgumentException">The options' size is not a positive number, or their
    /// <see cref="WindowOptions.SupportedLocales"/> hold a <see langword="null"/>.</exception>
    public static int Run(Func<Element> root, WindowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        Keymap.RequireDefaults();
        UiThread.Initialize();
        if (_running)
        {
            throw new InvalidOperationException("XylemApp.Run runs one message loop; it already runs.");
        }

        // The host the app was built with, which its module initializer registered (design §6.0, "ホストの選び方"). Read before the
        // loop counts as running, so that a run that throws for want of one leaves, as the throw above does, only the calling
        // thread recorded as the UI thread: no loop counts as running, and no dispatcher or window is made.
        Func<IPlatformApp> host =
            PlatformHostRegistry.Factory
            ?? throw new InvalidOperationException(
                "No platform host is registered for the runtime identifier '"
                    + RuntimeInformation.RuntimeIdentifier
                    + "': the app references no host for it, or is built without the source generator of XylemUI.Core."
            );

        // The loop can end with windows open (Win32MessageLoop.Quit from the app); the next run starts with none counted.
        _running = true;
        try
        {
            // The host sets its dispatcher and the loop's synchronization context before the window, whose root function makes
            // elements that use them, and the Windows host initializes OLE (design §6.0, "The order of creation"). Declared first,
            // it is disposed after the window core (which disposes the window) and the text tools, and puts back what it replaced
            // when anything from here on throws too, so that the app can run again. A host that throws while it is made has
            // changed nothing. The text tools are the UI thread's one set, made from the host's fonts, which the host disposes
            // after them.
            using IPlatformApp app = host();
            using var text = new TextContext(app.Fonts);
            using WindowCore core = OpenWindow(app, text, root, options ?? new WindowOptions(), show: true);
            Open(core, app);
            return app.Run();
        }
        finally
        {
            _running = false;
            _openWindows = 0;
        }
    }

    /// <summary>
    /// Makes a window core, has <paramref name="app"/> open its window, connects them, runs the root function, draws the first
    /// frame at the window's size, and gives the tree to assistive technology: steps 4 to 7 of the order of creation (design §6.0).
    /// What was made is disposed when a step throws.
    /// </summary>
    /// <param name="app">The host that opens the window.</param>
    /// <param name="text">The text tools of the UI thread; the caller disposes them after the core.</param>
    /// <param name="root">The root function.</param>
    /// <param name="options">The window.</param>
    /// <param name="show"><see langword="true"/> to show the window; the tests keep it hidden.</param>
    /// <returns>The window core, which owns the window; the caller disposes it.</returns>
    /// <exception cref="ArgumentException">The options' size is not a positive number, or their supported locales hold a
    /// <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread, or the window, its renderer, or the root
    /// function failed.</exception>
    internal static WindowCore OpenWindow(IPlatformApp app, TextContext text, Func<Element> root, WindowOptions options, bool show)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Title);
        if (!(options.Width > 0 && options.Height > 0 && float.IsFinite(options.Width) && float.IsFinite(options.Height)))
        {
            throw new ArgumentException("The window's width and height are finite numbers above 0.", nameof(options));
        }

        WindowLocale.VerifySupported(options.SupportedLocales, nameof(options));

        UiThread.VerifyAccess();
        var core = new WindowCore(text, app.Settings, app.Clipboard, options.SupportedLocales, options.ResolveLocale);
        try
        {
            IPlatformWindow window = app.OpenWindow(core, options.Title, options.Width, options.Height, show);
            GlyphAtlas atlas;
            try
            {
                atlas = new GlyphAtlas(app.CreateRasterizer);
            }
            catch
            {
                // Not yet the core's: it owns the window from Connect on.
                window.Dispose();
                throw;
            }

            core.Connect(window, atlas, Background);
            window.Attach();

            // The root function in the theme's wrapper the app chose (design §13.2, "根で置くもの"), then the first frame at the
            // window's size in logical pixels: the host's physical size divided by its scale (design §6.0, "The order of creation",
            // step 7). The OS's appearance is the one of a host that does not read it, until row TH1's fifth pull request reads
            // Windows' (design §13.2, "macOS と Linux").
            (int width, int height) = window.ClientSize;
            float scale = window.ScaleFactor;
            var size = new Size(width / scale, height / scale);
            core.Mount(root, WindowRoot.ReadWrapper(), default, "XylemApp.Run", size, scale);

            // The tree has its root now: assistive technology that asked while the window showed gets the tree.
            window.RootMounted();
            return core;
        }
        catch
        {
            core.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Counts the window of <paramref name="core"/> as open until it closes; the last window to close quits the loop of
    /// <paramref name="app"/> with 0.
    /// </summary>
    /// <param name="core">The window core of the window that was opened.</param>
    /// <param name="app">The host whose loop runs the window.</param>
    internal static void Open(WindowCore core, IPlatformApp app)
    {
        _openWindows++;
        core.Closed += (_, _) =>
        {
            if (--_openWindows == 0)
            {
                app.Quit(0);
            }
        };
    }
}
