namespace XylemUI;

/// <summary>
/// Code that runs once when created and again after a value it read changes. Effects run when the outermost batch ends, in
/// the order of their depth in the graph (design §2.2.2).
/// </summary>
/// <remarks>
/// An exception thrown by the code goes to <see cref="ReactiveRuntime.UnhandledExceptionHandler"/>. The effect keeps the values
/// it read before the exception and runs again when one of them changes.
/// </remarks>
public sealed class Effect : Computation, IDisposable
{
    private readonly Action _run;

    private Effect(Action run) => _run = run;

    /// <summary>
    /// Creates an effect and runs it once, now, as a batch: the effects of the signals it sets run after it returns. Inside a
    /// flush, they run in the flush's next cycle.
    /// </summary>
    /// <param name="run">The code to run. The signals and computed values it reads are recorded.</param>
    /// <returns>The new effect. Dispose it to stop it.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static Effect Create(Action run)
    {
        ArgumentNullException.ThrowIfNull(run);
        UiThread.VerifyAccess();
        var effect = new Effect(run);

        // The first run is a batch, so the signals it sets are flushed after it, not in the middle of it.
        ReactiveRuntime.EnterBatch();
        try
        {
            effect.RunAndRequeue();
        }
        finally
        {
            ReactiveRuntime.ExitBatch();
        }

        return effect;
    }

    /// <summary>
    /// Stops the effect. It no longer reads its sources and never runs again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public void Dispose()
    {
        UiThread.VerifyAccess();
        DisposeSources();
    }

    /// <inheritdoc />
    internal override void MarkDirty()
    {
        switch (Status)
        {
            case ComputationStatus.Clean when !IsDisposed:
                ReactiveRuntime.Enqueue(this);
                break;
            case ComputationStatus.Running:
                // Changed while it runs: it runs again in the next cycle (RunAndRequeue).
                Status = ComputationStatus.Dirty;
                break;
        }
    }

    /// <summary>
    /// Runs a queued effect if a value it read changed; otherwise only marks it clean.
    /// </summary>
    internal void RunQueued()
    {
        if (IsDisposed)
        {
            return;
        }

        if (AnySourceChanged())
        {
            RunAndRequeue();
        }
        else
        {
            Status = ComputationStatus.Clean;
        }
    }

    /// <inheritdoc />
    private protected override void Execute() => _run();

    /// <inheritdoc />
    private protected override void OnException(Exception exception) => ReactiveRuntime.ReportException(exception);

    private void RunAndRequeue()
    {
        // An effect that disposed itself while it ran is Clean here (DisposeSources), so it is not queued again.
        Run();
        if (Status == ComputationStatus.Dirty)
        {
            ReactiveRuntime.Enqueue(this);
        }
    }
}
