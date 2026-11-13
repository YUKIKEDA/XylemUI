using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacProbe;

// The wakeup of the report's Q6 (option a, Flutter's form): a CFRunLoopSource in common modes on the main run loop, signalled from a
// worker with CFRunLoopSourceSignal + CFRunLoopWakeUp; its perform callback is where UiDispatcher.Drain would run. No window needed.
internal static unsafe class RunLoopProbes
{
    private const int Wakeups = 20;
    private static readonly double[] s_latencyMicroseconds = new double[Wakeups];
    private static long s_signalTimestamp;
    private static int s_performed;
    private static nint s_source;
    private static nint s_mainLoop;
    private static nint s_defaultMode;

    // Records the time since the worker's signal; reads and writes statics only, cannot throw.
    [UnmanagedCallersOnly]
    private static void Perform(nint info)
    {
        long now = Stopwatch.GetTimestamp();
        int index = Volatile.Read(ref s_performed);
        if (index < Wakeups)
        {
            s_latencyMicroseconds[index] = Stopwatch.GetElapsedTime(Volatile.Read(ref s_signalTimestamp), now).TotalMicroseconds;
        }
        Volatile.Write(ref s_performed, index + 1);
    }

    public static void Run()
    {
        Report.Step(
            "source",
            () =>
            {
                s_mainLoop = CF.CFRunLoopGetMain();
                nint current = CF.CFRunLoopGetCurrent();
                nint cf = Frameworks.Handle("CoreFoundation");
                s_defaultMode = ObjC.ReadExport(cf, "kCFRunLoopDefaultMode");
                nint commonModes = ObjC.ReadExport(cf, "kCFRunLoopCommonModes");
                CFRunLoopSourceContext context = default;
                delegate* unmanaged<nint, void> perform = &Perform;
                context.Perform = (nint)perform;
                s_source = CF.CFRunLoopSourceCreate(0, 0, &context);
                if (s_source == 0)
                {
                    throw new ProbeException("CFRunLoopSourceCreate returned NULL");
                }
                CF.CFRunLoopAddSource(s_mainLoop, s_source, commonModes);
                Report.Value(
                    "source",
                    $"ok version-0 source added to the main run loop in kCFRunLoopCommonModes; this thread's run loop is the main one: {current == s_mainLoop}"
                );
            }
        );
        Report.Step(
            "idle_cpu",
            () =>
            {
                TimeSpan cpu = Environment.CpuUsage.TotalTime;
                long start = Stopwatch.GetTimestamp();
                int rc = CF.CFRunLoopRunInMode(s_defaultMode, 1.0, 0);
                double wallMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                double cpuMs = (Environment.CpuUsage.TotalTime - cpu).TotalMilliseconds;
                Report.Value(
                    "idle_cpu",
                    $"{cpuMs:F1} ms CPU (whole process, the 4 Hz watchdog included) over {wallMs:F0} ms of CFRunLoopRunInMode with one idle source (rc {rc}; 3 = timed out)"
                );
            }
        );
        Report.Step(
            "wakeup_latency",
            () =>
            {
                Volatile.Write(ref s_performed, 0);
                var worker = new Thread(Signal) { IsBackground = true, Name = "probe-signaller" };
                worker.Start();
                long start = Stopwatch.GetTimestamp();
                while (Volatile.Read(ref s_performed) < Wakeups && Stopwatch.GetElapsedTime(start).TotalSeconds < 15)
                {
                    CF.CFRunLoopRunInMode(s_defaultMode, 0.5, 1);
                }
                worker.Join(TimeSpan.FromSeconds(5));
                int count = Math.Min(Volatile.Read(ref s_performed), Wakeups);
                if (count == 0)
                {
                    throw new ProbeException("the perform callback never ran");
                }
                double[] sorted = s_latencyMicroseconds[..count];
                Array.Sort(sorted);
                Report.Value(
                    "wakeup_latency",
                    $"{count}/{Wakeups} wakeups; median {sorted[count / 2]:F0} us, max {sorted[count - 1]:F0} us from the worker's signal to perform on the main thread"
                );
            }
        );
    }

    private static void Signal()
    {
        for (int i = 0; i < Wakeups; i++)
        {
            Thread.Sleep(20);
            Volatile.Write(ref s_signalTimestamp, Stopwatch.GetTimestamp());
            CF.CFRunLoopSourceSignal(s_source);
            CF.CFRunLoopWakeUp(s_mainLoop);
            long waitStart = Stopwatch.GetTimestamp();
            while (Volatile.Read(ref s_performed) <= i && Stopwatch.GetElapsedTime(waitStart).TotalSeconds < 2)
            {
                Thread.Sleep(1);
            }
        }
    }
}
