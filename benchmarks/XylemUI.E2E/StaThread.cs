using System.Runtime.ExceptionServices;

namespace XylemUI.E2E;

// Runs a scene on a new thread in COM's single-threaded apartment, as an app's Main with [STAThread] runs, and waits for it: the
// Windows host (Win32PlatformApp) initializes OLE on its UI thread and throws on a thread in the multithreaded apartment (design
// §6.0, "The order of creation", step 2). The scenes that draw in the host's window run there (Frames' --tree, `wm-size`, `app
// --tree`); the others stay on the main thread, whose apartment the program does not set. An exception of the scene is thrown
// again here, with its stack.
internal static class StaThread
{
    public static int Run(Func<int> scene)
    {
        int code = 0;
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                code = scene();
            }
            catch (Exception exception)
            {
                error = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
        return code;
    }
}
