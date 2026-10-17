namespace XylemUI;

/// <summary>
/// Creates signals.
/// </summary>
public static class Signal
{
    /// <summary>
    /// Creates a signal that holds <paramref name="value"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The first value.</param>
    /// <param name="comparer">Decides whether a new value equals the current one; <see langword="null"/> for the default.</param>
    /// <returns>The new signal.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static Signal<T> Create<T>(T value, IEqualityComparer<T>? comparer = null)
    {
        UiThread.VerifyAccess();
        return new Signal<T>(value, comparer);
    }
}

/// <summary>
/// A value that the UI thread reads and writes, and that runs again the computed values and effects that read it when it
/// changes (design §2.2).
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public sealed class Signal<T> : ReactiveNode, IReadOnlySignal<T>
{
    private readonly IEqualityComparer<T>? _comparer;
    private T _value;

    internal Signal(T value, IEqualityComparer<T>? comparer)
    {
        _value = value;
        _comparer = comparer;
    }

    /// <inheritdoc />
    internal override uint Depth => 0;

    /// <inheritdoc />
    public T Get()
    {
        UiThread.VerifyAccess();
        ReactiveRuntime.VerifyNotInLayout();
        TrackRead();
        return _value;
    }

    /// <summary>
    /// Sets the value. A value equal to the current one does nothing. Otherwise the readers are marked dirty, and the effects
    /// run when the outermost batch ends (design §2.2.1); outside a batch, before this method returns.
    /// </summary>
    /// <param name="value">The new value.</param>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, or a computed value is computing: a computed value has no side effects.
    /// </exception>
    public void Set(T value)
    {
        UiThread.VerifyAccess();
        ReactiveRuntime.VerifyNotInLayout();
        if (ReactiveRuntime.Current is { } running && running is not Effect)
        {
            ThrowHelper.ThrowSetInsideComputed();
        }

        bool equal = _comparer is null ? EqualityComparer<T>.Default.Equals(_value, value) : _comparer.Equals(_value, value);
        if (equal)
        {
            return;
        }

        _value = value;
        Version++;
        ReactiveRuntime.AdvanceGlobalVersion();
        ReactiveRuntime.EnterBatch();
        try
        {
            MarkObserversDirty();
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }
    }
}
