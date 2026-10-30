namespace XylemUI.Testing.Tests;

// The virtual clock (design §12.2): its time moves only when advanced, and its timers fire then, in the order of their due times.
public sealed class TestTimeTests
{
    /// <summary>
    /// Advancing fires each timer due on the way at its own due time, earliest first, and ends at the time asked for.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A clock with one-shot timers due at 30, 10, and 20 ms, each recording the clock's elapsed time when it fires.
    ///
    /// Steps:
    /// - Advance by 25 ms, then by 10 ms.
    ///
    /// Expected:
    /// - After 25 ms the timers of 10 and 20 ms fired at 10 and 20 ms, and the elapsed time is 25 ms; after 10 ms more the timer of
    ///   30 ms fired at 30 ms, and the elapsed time is 35 ms. The timestamp and the wall-clock time moved with it.
    /// </remarks>
    [Fact]
    public void Advance_FiresTheDueTimersAtTheirTimes_EarliestFirst()
    {
        var time = new TestTime();
        var fired = new List<double>();
        using ITimer late = time.CreateTimer(_ => fired.Add(time.Elapsed.TotalMilliseconds), null, Ms(30), Timeout.InfiniteTimeSpan);
        using ITimer early = time.CreateTimer(_ => fired.Add(time.Elapsed.TotalMilliseconds), null, Ms(10), Timeout.InfiniteTimeSpan);
        using ITimer middle = time.CreateTimer(_ => fired.Add(time.Elapsed.TotalMilliseconds), null, Ms(20), Timeout.InfiniteTimeSpan);

        time.Advance(Ms(25));
        Assert.Equal([10, 20], fired);
        Assert.Equal(Ms(25), time.Elapsed);

        time.Advance(Ms(10));
        Assert.Equal([10, 20, 30], fired);
        Assert.Equal(Ms(35), time.Elapsed);
        Assert.Equal(Ms(35), time.GetElapsedTime(0));
        Assert.Equal(TestTime.Start + Ms(35), time.GetUtcNow());
        Assert.Equal(0, time.PendingTimers);
    }

    /// <summary>
    /// A periodic timer fires once a period; a change to an infinite due time stops it, and a disposed one never fires.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A clock with a timer due at 10 ms that repeats every 10 ms, and another one-shot timer due at 5 ms.
    ///
    /// Steps:
    /// - Dispose the one-shot timer; advance by 35 ms; change the periodic timer to an infinite due time; advance by 50 ms.
    ///
    /// Expected:
    /// - The periodic timer fired 3 times (10, 20, 30 ms), then not again; the disposed one never fired; no timer is due.
    /// </remarks>
    [Fact]
    public void PeriodicTimer_FiresEachPeriod_UntilStopped()
    {
        var time = new TestTime();
        int periodic = 0;
        int disposed = 0;
        using ITimer timer = time.CreateTimer(_ => periodic++, null, Ms(10), Ms(10));
        ITimer once = time.CreateTimer(_ => disposed++, null, Ms(5), Timeout.InfiniteTimeSpan);
        once.Dispose();

        time.Advance(Ms(35));
        Assert.Equal(3, periodic);
        Assert.True(timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan));
        time.Advance(Ms(50));
        Assert.Equal(3, periodic);
        Assert.Equal(0, disposed);
        Assert.Equal(0, time.PendingTimers);
        Assert.Null(time.UntilNextTimer());
    }

    /// <summary>
    /// The time until the next timer is measured from now, and timers due at once fire in the order they were scheduled: a timer
    /// changed again goes after those scheduled before the change.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A clock advanced by 4 ms, with timers A, B, and C made in that order, all due 6 ms later.
    ///
    /// Steps:
    /// - Change A to the same due time; read the time until the next timer; advance by 6 ms.
    ///
    /// Expected:
    /// - 6 ms; B, C, then A fired.
    /// </remarks>
    [Fact]
    public void UntilNextTimer_IsFromNow_AndTimersDueAtOnceFireInTheOrderScheduled()
    {
        var time = new TestTime();
        time.Advance(Ms(4));
        var order = new List<char>();
        using ITimer a = time.CreateTimer(_ => order.Add('A'), null, Ms(6), Timeout.InfiniteTimeSpan);
        using ITimer b = time.CreateTimer(_ => order.Add('B'), null, Ms(6), Timeout.InfiniteTimeSpan);
        using ITimer c = time.CreateTimer(_ => order.Add('C'), null, Ms(6), Timeout.InfiniteTimeSpan);
        Assert.True(a.Change(Ms(6), Timeout.InfiniteTimeSpan));

        Assert.Equal(Ms(6), time.UntilNextTimer());
        time.Advance(Ms(6));
        Assert.Equal(['B', 'C', 'A'], order);
    }

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
}
