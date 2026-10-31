namespace XylemUI.Testing.Tests;

// The Zero-Alloc gate (design §11.3): 10 measured windows after one that is not, the failure's list, and the retry of a window that
// a background GC overlapped. The retry tests count the background GCs themselves, so this class does not run beside others.
[Collection(nameof(PerformanceAssertTests))]
[CollectionDefinition(nameof(PerformanceAssertTests), DisableParallelization = true)]
public sealed class PerformanceAssertTests : IDisposable
{
    private readonly Func<long> _count = PerformanceAssert.BackgroundGcCount;
    private readonly Action _settle = PerformanceAssert.Settle;

    /// <inheritdoc/>
    public void Dispose()
    {
        PerformanceAssert.BackgroundGcCount = _count;
        PerformanceAssert.Settle = _settle;
    }

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
    /// - It passes and returns 110 runs; the indices −10 to 99 each ran once.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_NoAllocation_Passes()
    {
        var ran = new int[110];
        Assert.Equal(110, PerformanceAssert.DoesNotAllocate(100, index => ran[index + 10]++));
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
    /// - It passes, after 1 warm-up run, 10 measured runs, and 1 more for window 3: 12 runs, which it returns.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_ABackgroundGcOverlapsAWindow_RunsItAgain()
    {
        long gcs = 0;
        int runs = 0;
        PerformanceAssert.BackgroundGcCount = () => gcs;
        int returned = PerformanceAssert.DoesNotAllocate(
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
        Assert.Equal(12, returned);
    }

    /// <summary>
    /// A window in which a collection of generation 2 starts runs again: a background GC counts one at its start, and its mark may
    /// end in the window before its index moves.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 10 runs, windows of 1; a run that, the first time it is index 6, starts a blocking collection of generation 2.
    ///
    /// Steps:
    /// - Assert that the runs allocate nothing.
    ///
    /// Expected:
    /// - It passes after 12 runs: window 6 ran again once.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_AGenerationTwoCollectionInAWindow_RunsItAgain()
    {
        bool collected = false;
        int runs = PerformanceAssert.DoesNotAllocate(
            10,
            index =>
            {
                if (index == 6 && !collected)
                {
                    collected = true;
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
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
    /// A background GC that ends after a window, during the wait that follows it, counts as an overlap, and the window runs again:
    /// its mark may have ended in the window (Issue #526).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 10 runs, windows of 1; a background-GC count that the wait after window 4 moves, the first time only; runs that allocate
    ///   nothing.
    ///
    /// Steps:
    /// - Assert that the runs allocate nothing.
    ///
    /// Expected:
    /// - It passes after 12 runs: window 4 ran again once.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_ABackgroundGcEndsAfterAWindow_RunsItAgain()
    {
        long gcs = 0;
        int runs = 0;
        int last = int.MinValue;
        bool moved = false;
        PerformanceAssert.BackgroundGcCount = () => gcs;
        PerformanceAssert.Settle = () =>
        {
            if (last == 4 && !moved)
            {
                moved = true;
                gcs++;
            }
        };

        int returned = PerformanceAssert.DoesNotAllocate(
            10,
            index =>
            {
                runs++;
                last = index;
            }
        );

        Assert.Equal(12, runs);
        Assert.Equal(12, returned);
    }

    /// <summary>
    /// A window that ran again is judged by its fewest bytes: a steady state allocates the same in each run, and an overlap only
    /// adds bytes, so a run of 0 bytes shows that the others' were not the code's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - 10 runs, windows of 1; a background-GC count that moves in each run of window 2; window 2 allocates 100 bytes in its last
    ///   three runs and nothing in its first.
    ///
    /// Steps:
    /// - Assert that the runs allocate nothing.
    ///
    /// Expected:
    /// - It passes after 14 runs: window 2 ran 3 more times, and its first run counted 0 bytes.
    /// </remarks>
    [Fact]
    public void DoesNotAllocate_ARetriedWindow_IsJudgedByItsFewestBytes()
    {
        long gcs = 0;
        int runs = 0;
        int windowTwo = 0;
        PerformanceAssert.BackgroundGcCount = () => gcs;
        int returned = PerformanceAssert.DoesNotAllocate(
            10,
            index =>
            {
                runs++;
                if (index == 2)
                {
                    gcs++;
                    if (windowTwo++ > 0)
                    {
                        GC.KeepAlive(new byte[100]);
                    }
                }
            }
        );

        Assert.Equal(14, runs);
        Assert.Equal(14, returned);
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
