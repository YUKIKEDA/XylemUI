using System.Globalization;
using System.Runtime;
using System.Text;

namespace XylemUI.Testing;

/// <summary>
/// The Zero-Alloc gate (design §11.3): asserts that a steady state allocates nothing on the calling thread.
/// </summary>
/// <remarks>
/// <para>
/// The runs go in 10 windows after one window that is not measured. A failure lists each window's bytes, the methods compiled on
/// the thread in it, and the collections in it, which tell a one-off figure apart from an allocation that every run makes.
/// </para>
/// <para>
/// A background GC empties every thread's allocation context at the end of its mark without taking the unused rest off the
/// thread's count (<c>void_allocation</c> in gc.cpp, dotnet/runtime v10.0.9), so a window it overlaps counts up to about 8 KB that
/// the code did not allocate (Issue #332). A test project cannot always turn concurrent GC off, so a window across which
/// <c>GC.GetGCMemoryInfo(GCKind.Background).Index</c> changed, or in which a collection of generation 2 started, is run again, up to 3
/// times. A blocking collection before the first window and after each window waits for a background GC in progress to end: one
/// that started before a window can end its mark inside it and move the index only after it, and with the index alone the Core
/// tests on concurrent GC counted 5 to 8 KB in one window in 2 runs of 8 (Issue #460). The collection after the window is the one
/// that catches a background GC that starts just after the collection before it and ends after the window: CI's Windows runner
/// counted 7,912 bytes in one window with no overlap seen (Issue #526). A window that ran again is judged by its fewest bytes:
/// the code allocates the same in each run of a steady state, and an overlap only adds bytes. A window that every run overlapped
/// is listed with a note when the runs allocated; when every window counted 0 bytes the runs pass.
/// </para>
/// </remarks>
public static class PerformanceAssert
{
    private const int Windows = 10;

    /// <summary>
    /// The most times one window is run again because a background GC overlapped it.
    /// </summary>
    public const int MaxRetries = 3;

    /// <summary>
    /// Runs <paramref name="iteration"/> <paramref name="iterations"/> times, with the index of each run, and throws when the
    /// calling thread allocated. One window of runs goes first, unmeasured, with the indices −<paramref name="iterations"/> / 10
    /// to −1: the first call of the delegate compiles it, and on the runners that call counted about 7 KB (Issue #92).
    /// </summary>
    /// <param name="iterations">The number of runs, a multiple of 10.</param>
    /// <param name="iteration">One run of the steady state; made before the call, so that making it is not measured.</param>
    /// <returns>The runs made: the warm-up's, the measured ones, and those of the windows run again; a test that counts the work of
    /// its runs compares it with this.</returns>
    /// <exception cref="ZeroAllocationException">The runs allocated.</exception>
    public static int DoesNotAllocate(int iterations, Action<int> iteration) => DoesNotAllocate(iterations, null, iteration);

    /// <summary>
    /// The same, with <paramref name="prepare"/> run before each run and not measured: a change the steady state follows, whose
    /// own allocations are the caller's (an app that builds a new list), while the frame that follows it must allocate nothing.
    /// </summary>
    /// <param name="iterations">The number of runs, a multiple of 10.</param>
    /// <param name="prepare">What goes before each run, with its index; <see langword="null"/> for nothing.</param>
    /// <param name="iteration">One run of the steady state, measured.</param>
    /// <returns>The runs made: the warm-up's, the measured ones, and those of the windows run again.</returns>
    /// <exception cref="ZeroAllocationException">The runs allocated.</exception>
    public static int DoesNotAllocate(int iterations, Action<int>? prepare, Action<int> iteration)
    {
        ArgumentNullException.ThrowIfNull(iteration);
        if (iterations <= 0 || iterations % Windows != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "The number of runs is a multiple of 10 above 0.");
        }

        var windows = new Window[Windows];

        // The calls below are compiled before the first window.
        _ = JitInfo.GetCompiledMethodCount(currentThread: true);
        _ = GC.CollectionCount(0);
        _ = GC.GetAllocatedBytesForCurrentThread();
        _ = BackgroundGcIndex();

        int perWindow = iterations / Windows;
        int runs = perWindow;
        for (int warm = -perWindow; warm < 0; warm++)
        {
            prepare?.Invoke(warm);
            iteration(warm);
        }

        // A blocking collection waits for a background GC in progress, so none runs into the first window from before it; the one
        // after each window does so for the next.
        Settle();
        for (int window = 0; window < Windows; window++)
        {
            int start = window * perWindow;
            Window? fewest = null;
            for (int attempt = 0; ; attempt++)
            {
                // One that starts in the window counts a collection of generation 2 at its start; one that ends in it, or after it
                // with its mark in it, moves the index by the time the collection after the window returns.
                long gcBefore = BackgroundGcIndex();
                Window measured = Measure(start, perWindow, prepare, iteration);
                runs += perWindow;
                Settle();
                bool overlapped = BackgroundGcIndex() != gcBefore || measured.FullCollections > 0;
                if (fewest is null || measured.Bytes < fewest.Value.Bytes)
                {
                    fewest = measured;
                }

                windows[window] = fewest.Value with { Retries = attempt, Overlapped = overlapped };
                if (!overlapped || attempt == MaxRetries)
                {
                    break;
                }
            }
        }

        long total = 0;
        foreach (Window window in windows)
        {
            total += window.Bytes;
        }

        if (total == 0)
        {
            return runs;
        }

        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"The steady state allocated {total} bytes in {iterations} runs, {perWindow} runs a window:");
        for (int window = 0; window < Windows; window++)
        {
            Window w = windows[window];
            message.Append(
                CultureInfo.InvariantCulture,
                $"\n  window {window}: {w.Bytes} bytes, {w.Compiled} methods compiled on this thread, {w.Collections} collections ({w.FullCollections} of generation 2)"
            );
            if (w.Overlapped)
            {
                message.Append(
                    CultureInfo.InvariantCulture,
                    $", run {w.Retries} more times, and a background GC overlapped it each time (its bytes may be the GC's)"
                );
            }
            else if (w.Retries > 0)
            {
                message.Append(CultureInfo.InvariantCulture, $", run {w.Retries} more times for a background GC");
            }
        }

        throw new ZeroAllocationException(message.ToString());
    }

    /// <summary>
    /// Gets or sets where the number of background GCs that have ended is read; the runtime's by default. The tests of the retry
    /// set it to a count of their own, as a background GC cannot be made to overlap a window on demand.
    /// </summary>
    internal static Func<long> BackgroundGcCount { get; set; } = static () => GC.GetGCMemoryInfo(GCKind.Background).Index;

    /// <summary>
    /// Gets or sets what waits for a background GC in progress to end, before the first window and after each one: a blocking
    /// collection of generation 2 by default. The tests of the retry set it to move their count of background GCs, as a background
    /// GC that ends during the wait cannot be made on demand.
    /// </summary>
    internal static Action Settle { get; set; } = static () => GC.Collect(2, GCCollectionMode.Forced, blocking: true);

    // It allocates (GCMemoryInfo's data), so it is read outside the measured runs.
    private static long BackgroundGcIndex() => BackgroundGcCount();

    private static Window Measure(int start, int count, Action<int>? prepare, Action<int> iteration)
    {
        int collectionsBefore = GC.CollectionCount(0);
        int fullBefore = GC.CollectionCount(2);
        long compiledBefore = JitInfo.GetCompiledMethodCount(currentThread: true);
        long bytes = 0;
        if (prepare is null)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = start; index < start + count; index++)
            {
                iteration(index);
            }

            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        else
        {
            for (int index = start; index < start + count; index++)
            {
                prepare(index);
                long before = GC.GetAllocatedBytesForCurrentThread();
                iteration(index);
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            }
        }

        return new Window(
            bytes,
            JitInfo.GetCompiledMethodCount(currentThread: true) - compiledBefore,
            GC.CollectionCount(0) - collectionsBefore,
            GC.CollectionCount(2) - fullBefore,
            Retries: 0,
            Overlapped: false
        );
    }

    // What one window measured, and how many times it ran again because a background GC overlapped it.
    private readonly record struct Window(long Bytes, long Compiled, int Collections, int FullCollections, int Retries, bool Overlapped);
}

/// <summary>
/// Thrown by <see cref="PerformanceAssert"/> when a steady state allocated.
/// </summary>
public sealed class ZeroAllocationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ZeroAllocationException"/> class.
    /// </summary>
    public ZeroAllocationException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZeroAllocationException"/> class with the measurements.
    /// </summary>
    /// <param name="message">The measurements of each window.</param>
    public ZeroAllocationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZeroAllocationException"/> class with the measurements and a cause.
    /// </summary>
    /// <param name="message">The measurements.</param>
    /// <param name="innerException">The cause.</param>
    public ZeroAllocationException(string message, Exception innerException)
        : base(message, innerException) { }
}
