using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace XylemUI.E2E;

// The costs of the reactive context (row RC1, Issue #838, design §2.13): the size of a scope's data; a lookup that finds a value
// provided 1, 10, 50, and 100 scopes above, and one that finds none and falls to the key's default from the same depths; a lookup
// under 10 provided keys, of the nearest and of the farthest; a scope made and disposed with no value on it; and a value provided
// around an empty body. Times are the median of 31 samples, each a loop long enough to read on the clock, divided by its count.
internal static class ContextCost
{
    private const int Samples = 31;
    private const int Lookups = 200_000;
    private const int Scopes = 10_000;

    private static readonly Context<object> Key = new();
    private static readonly Context<object> Missing = new(new object());
    private static readonly Context<object>[] Keys = [new(), new(), new(), new(), new(), new(), new(), new(), new(), new()];
    private static readonly object Value = new();

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("context");
        report.Number("scope_bytes", (long)Unsafe.SizeOf<ScopeData>());
        foreach (int depth in new[] { 1, 10, 50, 100 })
        {
            double found = 0;
            double missed = 0;
            ScopeHandle root = Scope.Create(() =>
                _ = Key.Provide(
                    Value,
                    () =>
                    {
                        Nest(
                            depth - 1,
                            () =>
                            {
                                found = Time(Lookups, static () => _ = Key.Use());
                                missed = Time(Lookups, static () => _ = Missing.Use());
                            }
                        );
                        return default;
                    }
                )
            );
            Scope.Dispose(root);
            report.Number($"found_depth_{depth}_ns", found);
            report.Number($"default_depth_{depth}_ns", missed);
        }

        double nearest = 0;
        double farthest = 0;
        ScopeHandle many = Scope.Create(() =>
            ProvideAll(
                0,
                () =>
                {
                    nearest = Time(Lookups, static () => _ = Keys[^1].Use());
                    farthest = Time(Lookups, static () => _ = Keys[0].Use());
                }
            )
        );
        Scope.Dispose(many);
        report.Number("keys_10_nearest_ns", nearest);
        report.Number("keys_10_farthest_ns", farthest);

        double scope = 0;
        double provide = 0;
        long bytes = 0;
        ScopeHandle outer = Scope.Create(() =>
        {
            scope = Time(Scopes, static () => Scope.Dispose(Scope.Create(static () => { })));
            provide = Time(Scopes, static () => _ = Key.Provide(Value, static () => default));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Scopes; i++)
            {
                _ = Key.Provide(Value, static () => default);
            }

            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        });
        Scope.Dispose(outer);
        report.Number("scope_create_dispose_ns", scope);
        report.Number("provide_ns", provide);
        report.Number("provide_bytes", (double)bytes / Scopes);
        return 0;
    }

    private static void Nest(int levels, Action innermost)
    {
        if (levels == 0)
        {
            innermost();
            return;
        }

        _ = Scope.Create(() => Nest(levels - 1, innermost));
    }

    private static void ProvideAll(int index, Action innermost)
    {
        if (index == Keys.Length)
        {
            innermost();
            return;
        }

        _ = Keys[index]
            .Provide(
                Value,
                () =>
                {
                    ProvideAll(index + 1, innermost);
                    return default;
                }
            );
    }

    // The median over the samples of one call's time in nanoseconds.
    private static double Time(int count, Action call)
    {
        double[] samples = new double[Samples];
        for (int sample = -3; sample < Samples; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                call();
            }

            if (sample >= 0)
            {
                samples[sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / count;
            }
        }

        Array.Sort(samples);
        return samples[Samples / 2];
    }
}
