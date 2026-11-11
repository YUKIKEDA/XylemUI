using System.Diagnostics;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of dispatching an action (row KM1a, design §7, "Shortcuts and actions"): a chain of boxes with no window, 1 and 50 deep
// as the key chains of the input scenario are, an action dispatched from the deepest one. Every box has a capture handler, which
// lets the action go on, and a bubble handler that passes it on, so that both phases call a handler at each box: 2 x depth handlers
// a dispatch, the count us_per_key_depth_* has. Before the timing, the action is made by its name from the table the source
// generator wrote: in the NativeAOT executable this is the check that the table is filled with no reflection.
internal static class ActionCost
{
    private const int Dispatches = 10_000;
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

        foreach (int depth in new[] { 1, 50 })
        {
            var tree = new UiTree();
            int handlers = 0;
            NodeHandle leaf = default;
            for (int i = 0; i < depth; i++)
            {
                NodeHandle node = tree.CreateNode(new LayoutStyle());
                if (!leaf.IsNone)
                {
                    tree.AppendChild(leaf, node);
                }

                tree.AddActionHandler(node, EventPhase.Capture, (in ActionEvent<Probe> _) => handlers++);
                tree.AddActionHandler(
                    node,
                    EventPhase.Bubble,
                    (in ActionEvent<Probe> e) =>
                    {
                        handlers++;
                        e.Propagate();
                    }
                );
                leaf = node;
            }

            // Unmeasured dispatches first, so that the route's array has its size and the handlers are compiled.
            for (int i = 0; i < Dispatches; i++)
            {
                _ = tree.Actions.Dispatch(leaf, probe);
            }

            handlers = 0;
            var microseconds = new double[Samples];
            long bytes = 0;

            // The clock's first call in the process allocates 40 bytes (measured: without this call, the first sample of the first
            // chain counted them and no other sample counted any), so it is made before the samples.
            _ = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp()).TotalMicroseconds;
            for (int sample = 0; sample < Samples; sample++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < Dispatches; i++)
                {
                    _ = tree.Actions.Dispatch(leaf, probe);
                }

                microseconds[sample] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Dispatches;
                bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
            }

            long dispatches = (long)Dispatches * Samples;
            if (handlers != dispatches * 2 * depth)
            {
                throw new InvalidOperationException($"The handlers ran {handlers} times, not {dispatches * 2 * depth}.");
            }

            Array.Sort(microseconds);
            report.Number($"us_per_action_depth_{depth}_p50", microseconds[Samples / 2]);
            report.Number($"action_handlers_per_dispatch_depth_{depth}", (double)handlers / dispatches);
            report.Number($"bytes_per_action_depth_{depth}", (double)bytes / dispatches);
        }

        return 0;
    }

    [Action("e2e::Probe")]
    internal sealed class Probe : IAction;
}
