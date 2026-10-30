namespace XylemUI.Testing.Tests;

// The Zero-Alloc gate (design §11.3): 10 measured windows after one that is not, the failure's list, and the retry of a window that
// a background GC overlapped. The retry tests count the background GCs themselves, so this class does not run beside others.
[Collection(nameof(PerformanceAssertTests))]
[CollectionDefinition(nameof(PerformanceAssertTests), DisableParallelization = true)]
public sealed class PerformanceAssertTests : IDisposable
{
    private readonly Func<long> _count = PerformanceAssert.BackgroundGcCount;

    /// <inheritdoc/>
    public void Dispose() => PerformanceAssert.BackgroundGcCount = _count;

    /// <summary>
    /// A steady state that allocates nothing passes, with each index from the warm-up's negatives to the last run once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 100 runs, so windows of 10.
    ///
    /// Steps:
    /// - Assert that a run that records its index into an array made before allocates nothing.
    ///
    /// Expected:
    /// - It passes; the indices −10 to 99 each ran once.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_NoAllocation_Passes()
    {
        var ran = new int[110];
        PerformanceAssert.DoesNotAllocate(100, index => ran[index + 10]++);
        Assert.All(ran, count => Assert.Equal(1, count));
    }

    /// <summary>
    /// A run that allocates fails with the bytes of each window.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 10 runs, so windows of 1; a run that allocates an array of 100 bytes.
    ///
    /// Steps:
    /// - Assert that it allocates nothing.
    ///
    /// Expected:
    /// - It throws <see cref="ZeroAllocationException"/> whose message lists 10 windows, each with the same bytes above 100.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_Allocation_ThrowsWithEachWindow()
    {
        ZeroAllocationException error = Assert.Throws<ZeroAllocationException>(() =>
            PerformanceAssert.DoesNotAllocate(10, _ => GC.KeepAlive(new byte[100]))
        );
        Assert.Contains("in 10 runs, 1 runs a window", error.Message, StringComparison.Ordinal);
        Assert.Contains("\n  window 9: ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prepare step is not measured: its allocations do not fail the runs that follow it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 10 runs; a prepare step that allocates 100 bytes, and a run that allocates nothing.
    ///
    /// Steps:
    /// - Assert that the runs allocate nothing.
    ///
    /// Expected:
    /// - It passes.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_PrepareAllocates_IsNotMeasured()
    {
        object? kept = null;
        PerformanceAssert.DoesNotAllocate(10, _ => kept = new byte[100], _ => { });
        GC.KeepAlive(kept);
    }

    /// <summary>
    /// A window across which a background GC ended runs again, and the window measured again is the one that counts.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 10 runs, windows of 1; a background-GC count that moves once, across window 3's first run; a run that allocates nothing.
    ///
    /// Steps:
    /// - Assert that the runs allocate nothing, counting the runs.
    ///
    /// Expected:
    /// - It passes, after 1 warm-up run, 10 measured runs, and 1 more for window 3: 12 runs.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_ABackgroundGcOverlapsAWindow_RunsItAgain()
    {
        long gcs = 0;
        int runs = 0;
        PerformanceAssert.BackgroundGcCount = () => gcs;
        PerformanceAssert.DoesNotAllocate(
            10,
            index =>
            {
                runs++;
                if (index == 3 && runs == 5)
                {
                    gcs++;
                }
            }
        );
        Assert.Equal(12, runs);
    }

    /// <summary>
    /// A window that background GCs overlap every time runs again 3 times, and a failure says so.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 10 runs, windows of 1; a background-GC count that moves on each run; a run that allocates 100 bytes.
    ///
    /// Steps:
    /// - Assert that the runs allocate nothing.
    ///
    /// Expected:
    /// - It throws <see cref="ZeroAllocationException"/> after 1 + 10 × 4 runs, and the message says each window ran 3 more times
    ///   with a background GC each time.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_BackgroundGcsOverlapEveryTime_FailsAfterThreeRetriesAndSaysSo()
    {
        long gcs = 0;
        int runs = 0;
        PerformanceAssert.BackgroundGcCount = () => gcs;
        ZeroAllocationException error = Assert.Throws<ZeroAllocationException>(() =>
            PerformanceAssert.DoesNotAllocate(
                10,
                _ =>
                {
                    runs++;
                    gcs++;
                    GC.KeepAlive(new byte[100]);
                }
            )
        );
        Assert.Equal(1 + (10 * (PerformanceAssert.MaxRetries + 1)), runs);
        Assert.Contains("run 3 more times, and a background GC overlapped it each time", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The runs are a multiple of 10 above 0.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Assert with 15 runs, and with 0.
    ///
    /// Expected:
    /// - Both throw <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_RunsNotAMultipleOfTen_Throws()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceAssert.DoesNotAllocate(15, _ => { }));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceAssert.DoesNotAllocate(0, _ => { }));
    }
}
