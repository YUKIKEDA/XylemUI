using System.Diagnostics;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of dispatching an action (row KM1a, design §7, "Shortcuts and actions"): a chain of 10 nodes with no window, an action
// dispatched from the deepest one, a capture handler at the top that lets it go on and a bubble handler at the top that handles
// it, so that both phases walk the whole route. Before the timing, the action is made by its name from the table the source
// generator wrote: in the NativeAOT executable this is the check that the table is filled with no reflection.
internal static class ActionCost
{
    private const int Depth = 10;
    private const int Dispatches = 100_000;
    private const int Samples = 21;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("actions");
        if (!ActionRegistry.TryCreate("e2e::Probe", out IAction? made) || made is not Probe probe)
        {
            throw new InvalidOperationException("The generated table did not register 'e2e::Probe'.");
        }

        if (!ActionRegistry.TryGetName(typeof(Probe), out string? name) || name != "e2e::Probe")
        {
            throw new InvalidOperationException("The generated table did not name the type of 'e2e::Probe'.");
        }

        var tree = new UiTree();
        NodeHandle top = tree.CreateNode(new LayoutStyle());
        NodeHandle leaf = top;
        for (int i = 1; i < Depth; i++)
        {
            NodeHandle child = tree.CreateNode(new LayoutStyle());
            tree.AppendChild(leaf, child);
            leaf = child;
        }

        int captured = 0;
        int handled = 0;
        tree.AddActionHandler(top, EventPhase.Capture, (in ActionEvent<Probe> _) => captured++);
        tree.AddActionHandler(top, EventPhase.Bubble, (in ActionEvent<Probe> _) => handled++);

        // Unmeasured dispatches first, so that the route's array has its size and the handlers are compiled.
        for (int i = 0; i < Dispatches; i++)
        {
            _ = tree.Actions.Dispatch(leaf, probe);
        }

        var nanoseconds = new double[Samples];
        long bytes = 0;
        for (int sample = 0; sample < Samples; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Dispatches; i++)
            {
                _ = tree.Actions.Dispatch(leaf, probe);
            }

            nanoseconds[sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / Dispatches;
            bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }

        int expected = Dispatches * (Samples + 1);
        if (captured != expected || handled != expected)
        {
            throw new InvalidOperationException($"The handlers ran {captured} and {handled} times, not {expected}.");
        }

        Array.Sort(nanoseconds);
        report.Number("depth", Depth);
        report.Number("dispatch_ns_p50", nanoseconds[Samples / 2]);
        report.Number("dispatch_ns_min", nanoseconds[0]);
        report.Number("dispatch_ns_max", nanoseconds[^1]);
        report.Number("bytes_per_dispatch", (double)bytes / ((long)Dispatches * Samples));
        return 0;
    }

    [Action("e2e::Probe")]
    internal sealed class Probe : IAction;
}
