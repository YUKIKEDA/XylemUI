// Measures what the cleanups of a scope cost (roadmap row S9, Issue #60, docs/adr/0001-scope-cleanup-closures.md).
//   1. Four ways to hold n cleanups and run them when the scope goes, n = 1,000 and 10,000: a capturing lambda and a static
//      lambda on the real Scope; an array of IDisposable objects (the shape of R3's DisposableBag) and an array of ids
//      (CleanupKind) as prototypes, since Scope only holds delegates. Time of register and of dispose (median of 31 rounds),
//      bytes allocated per round, and the collections and GC pause over all rounds.
//   2. A rebuild of part of a screen every frame: 100 or 1,000 scopes made and disposed, each with a capturing cleanup and a
//      child scope, for 600 frames (10 s at 60 Hz). Collections by generation, the total GC pause, and the largest pause
//      within one frame, next to the 16.6 ms budget.
// Moved from samples/ScopeCost into the benchmarks (roadmap row M1a). It prints text, not JSON: ADR 0001 quotes its rows.
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using ScopeHandle = XylemUI.Handle<XylemUI.ScopeData>;

namespace XylemUI.E2E;

internal static class ScopeCost
{
    public static int Run()
    {
        UiThread.Initialize();
        string runtime = JitInfo.GetCompiledMethodCount() > 0 ? "jit" : "nativeaot";
        Console.WriteLine($"ScopeCost build={runtime} gc={(GCSettings.IsServerGC ? "server" : "workstation")} latency={GCSettings.LatencyMode}");

        const int Rounds = 31;
        Console.WriteLine(
            "cleanups: n, kind | register us (median), dispose us (median) | bytes/round | gen0 gen1 gen2 | pause ms (all rounds) | % of 16.6 ms"
        );
        foreach (int n in new[] { 1_000, 10_000 })
        {
            foreach (CleanupKind kind in Enum.GetValues<CleanupKind>())
            {
                _ = Measure(kind, n, 3); // warm-up: the arenas and the buffers grow here
                Measure(kind, n, Rounds).Print(kind, n);
            }
        }

        Console.WriteLine("rebuild: scopes/frame | frames | gen0 gen1 gen2 | pause ms total, largest in a frame | frame us median, max");
        foreach (int scopes in new[] { 100, 1_000 })
        {
            Rebuild(scopes, 60);
            Rebuild(scopes, 600, print: true);
        }

        return 0;
    }

    private static Result Measure(CleanupKind kind, int n, int rounds)
    {
        var register = new List<double>();
        var dispose = new List<double>();
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        TimeSpan pause = GC.GetTotalPauseDuration();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < rounds; round++)
        {
            (double r, double d) = kind switch
            {
                CleanupKind.CapturingLambda => Delegates(n, capturing: true),
                CleanupKind.StaticLambda => Delegates(n, capturing: false),
                CleanupKind.DisposableArray => Disposables(n),
                _ => Ids(n),
            };
            register.Add(r);
            dispose.Add(d);
        }

        return new Result(
            Median(register),
            Median(dispose),
            (GC.GetAllocatedBytesForCurrentThread() - bytes) / rounds,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            (GC.GetTotalPauseDuration() - pause).TotalMilliseconds
        );
    }

    // The real Scope: one scope with n cleanups.
    private static (double Register, double Dispose) Delegates(int n, bool capturing)
    {
        long start = Stopwatch.GetTimestamp();
        ScopeHandle scope = capturing
            ? Scope.Create(() =>
            {
                for (int i = 0; i < n; i++)
                {
                    int captured = i;
                    Scope.OnCleanup(() => Sink.Value += captured);
                }
            })
            : Scope.Create(() =>
            {
                for (int i = 0; i < n; i++)
                {
                    Scope.OnCleanup(static () => Sink.Value++);
                }
            });
        long registered = Stopwatch.GetTimestamp();
        Scope.Dispose(scope);
        long disposed = Stopwatch.GetTimestamp();
        return (Stopwatch.GetElapsedTime(start, registered).TotalMicroseconds, Stopwatch.GetElapsedTime(registered, disposed).TotalMicroseconds);
    }

    // The R3 DisposableBag shape: one object per cleanup in a reused array, disposed last first.
    private static (double Register, double Dispose) Disposables(int n)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++)
        {
            Bag.Add(new Unsubscribe(i));
        }

        long registered = Stopwatch.GetTimestamp();
        Bag.DisposeAll();
        long disposed = Stopwatch.GetTimestamp();
        return (Stopwatch.GetElapsedTime(start, registered).TotalMicroseconds, Stopwatch.GetElapsedTime(registered, disposed).TotalMicroseconds);
    }

    // Ids: a kind and a payload per cleanup in a reused array of structs, run by a switch, last first.
    private static (double Register, double Dispose) Ids(int n)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++)
        {
            IdBag.Add(i);
        }

        long registered = Stopwatch.GetTimestamp();
        IdBag.RunAll();
        long disposed = Stopwatch.GetTimestamp();
        return (Stopwatch.GetElapsedTime(start, registered).TotalMicroseconds, Stopwatch.GetElapsedTime(registered, disposed).TotalMicroseconds);
    }

    private static void Rebuild(int scopes, int frames, bool print = false)
    {
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        TimeSpan pauseStart = GC.GetTotalPauseDuration();
        double largestPause = 0;
        var frameTimes = new List<double>(frames);
        var live = new ScopeHandle[scopes];
        for (int frame = 0; frame < frames; frame++)
        {
            TimeSpan before = GC.GetTotalPauseDuration();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < scopes; i++)
            {
                Scope.Dispose(live[i]);
                int captured = frame + i;
                live[i] = Scope.Create(() =>
                {
                    Scope.OnCleanup(() => Sink.Value += captured);
                    _ = Scope.Create(static () => { });
                });
            }

            frameTimes.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
            largestPause = Math.Max(largestPause, (GC.GetTotalPauseDuration() - before).TotalMilliseconds);
        }

        foreach (ScopeHandle scope in live)
        {
            Scope.Dispose(scope);
        }

        if (print)
        {
            double total = (GC.GetTotalPauseDuration() - pauseStart).TotalMilliseconds;
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"rebuild: {scopes} | {frames} | {GC.CollectionCount(0) - gen0} {GC.CollectionCount(1) - gen1} {GC.CollectionCount(2) - gen2} | {total:F3}, {largestPause:F3} | {Median(frameTimes):F1}, {frameTimes.Max():F1}"
                )
            );
        }
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }
}

internal enum CleanupKind
{
    CapturingLambda,
    StaticLambda,
    DisposableArray,
    IdArray,
}

internal readonly record struct Result(double Register, double Dispose, long Bytes, int Gen0, int Gen1, int Gen2, double PauseMs)
{
    public void Print(CleanupKind kind, int n) =>
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"cleanups: {n}, {kind} | {Register:F1}, {Dispose:F1} | {Bytes} | {Gen0} {Gen1} {Gen2} | {PauseMs:F3} | {(Register + Dispose) / 16_666.7 * 100:F2}"
            )
        );
}

internal static class Sink
{
    public static long Value;
}

internal sealed class Unsubscribe(int id) : IDisposable
{
    public void Dispose() => Sink.Value += id;
}

internal static class Bag
{
    private static IDisposable?[] _items = new IDisposable?[16];
    private static int _count;

    public static void Add(IDisposable item)
    {
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[_count++] = item;
    }

    public static void DisposeAll()
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            _items[i]!.Dispose();
            _items[i] = null;
        }

        _count = 0;
    }
}

internal static class IdBag
{
    private static (byte Kind, int Payload)[] _items = new (byte, int)[16];
    private static int _count;

    public static void Add(int payload)
    {
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[_count++] = (1, payload);
    }

    public static void RunAll()
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            switch (_items[i].Kind)
            {
                case 1:
                    Sink.Value += _items[i].Payload;
                    break;
            }
        }

        _count = 0;
    }
}
