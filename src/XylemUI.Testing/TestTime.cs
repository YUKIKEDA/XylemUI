namespace XylemUI.Testing;

/// <summary>
/// A virtual clock for the tests (design §12.2): its time moves only when a test advances it, and its timers fire then, on the
/// thread that advances it, in the order of their due times.
/// </summary>
/// <remarks>
/// Flutter's tests run in <c>fake_async</c> and GPUI's in its <c>TestScheduler</c>, both their own; this one has no dependency. Its
/// timestamp counts <see cref="TimeSpan"/> ticks from 0, and its wall-clock time starts at <see cref="Start"/> in UTC.
/// </remarks>
public sealed class TestTime : TimeProvider
{
    // The timers that are due to fire, in no order; few are alive at once, so finding the next is a walk.
    private readonly List<TestTimer> _timers = [];
    private readonly DateTimeOffset _start;
    private long _elapsed;
    private long _created;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestTime"/> class, whose wall-clock time starts at <see cref="Start"/>.
    /// </summary>
    public TestTime()
        : this(Start) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestTime"/> class with the wall-clock time it starts at.
    /// </summary>
    /// <param name="start">The time at 0 elapsed.</param>
    public TestTime(DateTimeOffset start) => _start = start;

    /// <summary>
    /// Gets the wall-clock time a clock made with no start begins at: 2000-01-01 00:00 UTC.
    /// </summary>
    public static DateTimeOffset Start { get; } = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Gets the time the clock has been advanced by since it was made.
    /// </summary>
    public TimeSpan Elapsed => TimeSpan.FromTicks(_elapsed);

    /// <summary>
    /// Gets the number of timers that are due to fire.
    /// </summary>
    public int PendingTimers => _timers.Count;

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <inheritdoc/>
    public override long GetTimestamp() => _elapsed;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _start + Elapsed;

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new TestTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Advances the clock, firing each timer that falls due on the way at its due time, earliest first; timers due at the same time
    /// fire in the order they were scheduled, by <see cref="CreateTimer"/> or by the last <see cref="ITimer.Change"/>.
    /// </summary>
    /// <param name="by">How far; 0 or more.</param>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        long end = checked(_elapsed + by.Ticks);
        while (Next() is { } timer && timer.Due <= end)
        {
            _elapsed = timer.Due;
            timer.Fire();
        }

        _elapsed = end;
    }

    /// <summary>
    /// Returns how long until the next timer is due, or <see langword="null"/> when none is.
    /// </summary>
    /// <returns>The time until the earliest due time; 0 for a timer that is due now.</returns>
    public TimeSpan? UntilNextTimer() => Next() is { } timer ? TimeSpan.FromTicks(Math.Max(0, timer.Due - _elapsed)) : null;

    private TestTimer? Next()
    {
        TestTimer? next = null;
        foreach (TestTimer timer in _timers)
        {
            if (next is null || timer.Due < next.Due || (timer.Due == next.Due && timer.Order < next.Order))
            {
                next = timer;
            }
        }

        return next;
    }

    // A timer of the clock: due at a tick of the elapsed time, or not scheduled.
    private sealed class TestTimer(TestTime time, TimerCallback callback, object? state) : ITimer
    {
        private long _period;

        public long Due { get; private set; }

        public long Order { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, Timeout.InfiniteTimeSpan);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, Timeout.InfiniteTimeSpan);
            _ = time._timers.Remove(this);
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                return true;
            }

            Due = checked(time._elapsed + dueTime.Ticks);
            _period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
            Order = time._created++;
            time._timers.Add(this);
            return true;
        }

        public void Fire()
        {
            // A period of 0 fires once, as System.Threading.Timer does.
            if (_period > 0)
            {
                Due += _period;
            }
            else
            {
                _ = time._timers.Remove(this);
            }

            callback(state);
        }

        public void Dispose() => _ = time._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
