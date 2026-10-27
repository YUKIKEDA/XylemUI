using XylemUI.Platform.Windows;

namespace XylemUI;

/// <summary>
/// Starts an app (design §2.13, "The window"): opens a window, builds its tree from the root function, and runs the UI thread's
/// message loop until the last window closes.
/// </summary>
public static class XylemApp
{
    private static int _openWindows;

    /// <summary>
    /// Opens a window whose content <paramref name="root"/> makes, and runs the message loop until the last window is closed. The
    /// calling thread becomes the UI thread. <paramref name="root"/> runs once, in the window's root scope; closing the window
    /// disposes that scope, which takes out every node, handler, and effect it made.
    /// </summary>
    /// <param name="root">The root function: makes the window's content with <see cref="Elements"/> and returns it.</param>
    /// <param name="options">The window; <see langword="null"/> for the defaults of <see cref="WindowOptions"/>.</param>
    /// <returns>The exit code of the message loop: 0 when the last window closed.</returns>
    /// <exception cref="InvalidOperationException">
    /// Another thread is the UI thread, a message loop already runs, or the window, its renderer, or the root function failed.
    /// </exception>
    /// <exception cref="ArgumentException">The options' size is not a positive number.</exception>
    public static int Run(Func<Element> root, WindowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        UiThread.Initialize();
        if (_openWindows > 0)
        {
            throw new InvalidOperationException("XylemApp.Run runs one message loop; it already runs.");
        }

        using var window = new DesktopWindow(root, options ?? new WindowOptions());
        Open(window);
        window.Show();
        return Win32MessageLoop.Run();
    }

    // Counts the window as open until it closes; the last to close ends the loop.
    internal static void Open(DesktopWindow window)
    {
        _openWindows++;
        window.Closed += static (_, _) =>
        {
            if (--_openWindows == 0)
            {
                Win32MessageLoop.Quit();
            }
        };
    }
}
