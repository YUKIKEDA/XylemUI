using System.Runtime.ExceptionServices;

namespace XylemUI;

/// <summary>
/// Creates computed values.
/// </summary>
public static class Computed
{
    /// <summary>
    /// Creates a computed value. It is computed when first read, and again when read after a value it read has changed.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="compute">Computes the value. The signals and computed values it reads are recorded.</param>
    /// <param name="comparer">
    /// Decides whether a new result equals the previous one; an equal result does not run the readers again. <see langword="null"/>
    /// for the default.
    /// </param>
    /// <returns>The new computed value.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static Computed<T> Create<T>(Func<T> compute, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(compute);
        UiThread.VerifyAccess();
        return new Computed<T>(compute, comparer);
    }
}

/// <summary>
/// A value computed from signals and other computed values. It is lazy: it is computed only when read, and only when a value it
/// read has changed since (design §2.2.2).
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <remarks>
/// <para>
/// An exception thrown by the computation is kept in place of the value and thrown again by each <see cref="Get"/>, with the
/// original stack, until a value it read changes.
/// </para>
/// <para>
/// No scope owns a computed value. While an effect or another computed value reads it, it is registered with what it read, and
/// their changes mark it dirty. When its last reader goes, it leaves them: it is no longer marked dirty, and nothing in the graph
/// refers to it, so it can be collected. A <see cref="Get"/> without readers checks whether any signal changed since it last
/// computed, and if one did, whether one of its own sources changed (design §2.2.2).
/// </para>
/// </remarks>
public sealed class Computed<T> : Computation, IReadOnlySignal<T>, IDisposable
{
    private readonly Func<T> _compute;
    private readonly IEqualityComparer<T>? _comparer;
    private T _value = default!;
    private ExceptionDispatchInfo? _error;

    // The global version at which the value was last known current while nothing read it; 0 when unknown.
    private ulong _currentAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="Computed{T}"/> class, not yet computed.
    /// </summary>
    /// <param name="compute">Computes the value.</param>
    /// <param name="comparer">Decides whether a new result equals the previous one; <see langword="null"/> for the default.</param>
    internal Computed(Func<T> compute, IEqualityComparer<T>? comparer)
    {
        _compute = compute;
        _comparer = comparer;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The computed value was disposed.</exception>
    /// <exception cref="InvalidOperationException">The computation reads its own value.</exception>
    public T Get()
    {
        UiThread.VerifyAccess();
        ReactiveRuntime.VerifyNotInLayout();
        if (IsDisposed)
        {
            ThrowHelper.ThrowComputedDisposed();
        }

        Refresh();
        TrackRead();
        _error?.Throw();
        return _value;
    }

    /// <summary>
    /// Stops the computed value: it no longer reads its sources, and <see cref="Get"/> throws.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public void Dispose()
    {
        UiThread.VerifyAccess();
        DisposeSources();
    }

    /// <inheritdoc />
    private protected override bool IsLive => ObserverCount > 0 && !IsDisposed;

    /// <inheritdoc />
    internal override void Refresh()
    {
        if (ObserverCount == 0 && Status != ComputationStatus.Running)
        {
            RefreshUnobserved();
            return;
        }

        switch (Status)
        {
            case ComputationStatus.Clean:
                return;
            case ComputationStatus.Running:
                ThrowHelper.ThrowComputedReadsItself();
                break;
            default:
                if (!HasRun || AnySourceChanged())
                {
                    Run();
                }
                else
                {
                    Status = ComputationStatus.Clean;
                }

                break;
        }
    }

    /// <inheritdoc />
    private protected override void OnFirstObserver()
    {
        if (IsDisposed)
        {
            return;
        }

        // The status is current here: a reader registers only after Get refreshed this value at the current global version, and a
        // computed reader that registers with its sources refreshed them the same way (S9, a mutant that marked it dirty here
        // changed nothing).
        LinkSources();
    }

    /// <inheritdoc />
    private protected override void OnLastObserverRemoved()
    {
        _currentAt = Status == ComputationStatus.Clean ? ReactiveRuntime.GlobalVersion : 0;
        UnlinkSources();
    }

    /// <inheritdoc />
    internal override void MarkDirty()
    {
        if (IsDisposed || Status == ComputationStatus.Dirty)
        {
            return;
        }

        // Clean or Running. A computed value that a source changes while it runs stays dirty for the next read.
        Status = ComputationStatus.Dirty;
        MarkObserversDirty();
    }

    /// <inheritdoc />
    private protected override void Execute()
    {
        T next = _compute();
        bool equal =
            HasRun && _error is null && (_comparer is null ? EqualityComparer<T>.Default.Equals(_value, next) : _comparer.Equals(_value, next));
        if (!equal)
        {
            _value = next;
            _error = null;
            Version++;
        }
    }

    // Without readers no change marks this dirty, so the status says nothing; the global version and the sources' versions do.
    private void RefreshUnobserved()
    {
        ulong now = ReactiveRuntime.GlobalVersion;
        if (HasRun && _currentAt == now)
        {
            return;
        }

        if (!HasRun || AnySourceChanged())
        {
            Run();
        }

        Status = ComputationStatus.Clean;
        _currentAt = now;
    }

    /// <inheritdoc />
    private protected override void OnException(Exception exception)
    {
        _value = default!;
        _error = ExceptionDispatchInfo.Capture(exception);
        Version++;
    }
}
