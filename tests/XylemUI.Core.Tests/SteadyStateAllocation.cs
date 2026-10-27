using System.Globalization;
using System.Runtime;
using System.Text;

namespace XylemUI.Tests;

/// <summary>
/// Measures the allocations of a steady state on the calling thread, for the Zero-Alloc tests (design §11.3).
/// </summary>
/// <remarks>
/// The iterations run in windows, and a failure lists each window's bytes, the methods compiled on this thread in it, and the
/// collections in it, which tell a one-off figure apart from an allocation that every iteration makes. The one-off kilobytes the
/// runners reported came from background GCs, which empty a thread's allocation context at the end of their mark without taking
/// its unused rest off the thread's count; the test projects run without concurrent GC (Issue #332, <c>.cursor/rules/bench.mdc</c>).
/// </remarks>
internal static class SteadyStateAllocation
{
    private const int Windows = 10;

    /// <summary>
    /// Runs <paramref name="iteration"/> <paramref name="iterations"/> times, with the index of each run, and asserts that the
    /// calling thread allocated nothing. One window of runs goes first, unmeasured, with the indices
    /// −<paramref name="iterations"/> / 10 to −1: the first call of the delegate compiles it, and on the runners that call
    /// counted about 7 KB in the first window (Issue #92).
    /// </summary>
    /// <param name="iterations">The number of runs, a multiple of 10.</param>
    /// <param name="iteration">One run of the steady state; created before the call, so that creating it is not measured.</param>
    public static void AssertNone(int iterations, Action<int> iteration)
    {
        Assert.Equal(0, iterations % Windows);
        var bytes = new long[Windows];
        var compiled = new long[Windows];
        var collections = new int[Windows];
        var fullCollections = new int[Windows];

        // The calls below are compiled before the first window.
        _ = JitInfo.GetCompiledMethodCount(currentThread: true);
        _ = GC.CollectionCount(0);
        _ = GC.GetAllocatedBytesForCurrentThread();

        int perWindow = iterations / Windows;
        for (int warm = -perWindow; warm < 0; warm++)
        {
            iteration(warm);
        }

        int index = 0;
        for (int window = 0; window < Windows; window++)
        {
            int collectionsBefore = GC.CollectionCount(0);
            int fullBefore = GC.CollectionCount(2);
            long compiledBefore = JitInfo.GetCompiledMethodCount(currentThread: true);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int end = index + perWindow; index < end; index++)
            {
                iteration(index);
            }

            bytes[window] = GC.GetAllocatedBytesForCurrentThread() - before;
            compiled[window] = JitInfo.GetCompiledMethodCount(currentThread: true) - compiledBefore;
            collections[window] = GC.CollectionCount(0) - collectionsBefore;
            fullCollections[window] = GC.CollectionCount(2) - fullBefore;
        }

        if (bytes.Sum() == 0)
        {
            return;
        }

        var message = new StringBuilder();
        message.Append(
            CultureInfo.InvariantCulture,
            $"The steady state allocated {bytes.Sum()} bytes in {iterations} runs, {perWindow} runs a window:"
        );
        for (int window = 0; window < Windows; window++)
        {
            message.Append(
                CultureInfo.InvariantCulture,
                $"\n  window {window}: {bytes[window]} bytes, {compiled[window]} methods compiled on this thread, {collections[window]} collections ({fullCollections[window]} of generation 2)"
            );
        }

        Assert.Fail(message.ToString());
    }
}
