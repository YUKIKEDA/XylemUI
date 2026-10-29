// The todo list of row V3c (design §2.7): items added by a button, marked done, removed, filtered, and reversed, in a scroll view.
using XylemUI;

namespace Todo;

internal static class Program
{
    // XylemApp.Run needs the UI thread in COM's single-threaded apartment (design §7, "Touch and pen", I1c).
    [STAThread]
    private static int Main() => XylemApp.Run(TodoApp.Todo, new WindowOptions("Todo", 420, 360));
}
