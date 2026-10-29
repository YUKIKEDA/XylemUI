// The counter of design §2.13 (row V1d): a column of a text and a button; each click adds one to the count the text shows.
using XylemUI;

namespace Counter;

internal static class Program
{
    // XylemApp.Run needs the UI thread in COM's single-threaded apartment (design §7, "Touch and pen", I1c).
    [STAThread]
    private static int Main() => XylemApp.Run(CounterApp.Counter, new WindowOptions("Counter", 320, 200));
}
