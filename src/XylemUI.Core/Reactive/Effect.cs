using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// Code that runs once when created and again after a value it read changes. Effects run when the outermost batch ends, in
/// the order of their depth in the graph (design §2.2.2).
/// </summary>
/// <remarks>
/// <para>
/// An exception thrown by the code goes to <see cref="ReactiveRuntime.UnhandledExceptionHandler"/>. The effect keeps the values
/// it read before the exception and runs again when one of them changes.
/// </para>
/// <para>
/// An effect is a scope (design §2.3): the effects, scopes, and cleanups made while it runs belong to it, and are disposed
/// before it runs again and when it is disposed. An effect made inside a scope belongs to that scope and is disposed with it.
/// </para>
/// </remarks>
public sealed class Effect : Computation, IDisposable
{
    private readonly Action _run;
    private ScopeRef _scope;

    private Effect(Action run) => _run = run;

    /// <summary>
    /// Creates an effect and runs it once, now, as a batch: the effects of the signals it sets run after it returns. Inside a
    /// flush, they run in the flush's next cycle.
    /// </summary>
    /// <param name="run">The code to run. The signals and computed values it reads are recorded.</param>
    /// <param name="owner">The name <see cref="ScopeTracker"/> shows for the effect; the caller's name by default.</param>
    /// <returns>The new effect. It belongs to the current scope, if any; dispose it to stop it sooner.</returns>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, or a computed value is computing (a computed value has no side effects).
    /// </exception>
    public static Effect Create(Action run, [CallerMemberName] string owner = "")
    {
        ArgumentNullException.ThrowIfNull(run);
        UiThread.VerifyAccess();
        if (ReactiveRuntime.Current is { } running && running is not Effect)
        {
            ThrowHelper.ThrowScopeInsideComputed();
        }

        var effect = new Effect(run);
        effect._scope = Scope.AllocateForEffect(effect, owner);

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

    /// <inheritdoc />
    private protected override bool IsLive => !IsDisposed;

    /// <summary>
    /// Stops the effect: what it owns is disposed (children first, then its cleanups, each in LIFO order), it no longer reads
    /// its sources, and it never runs again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="AggregateException">One or more of its cleanups threw; every cleanup still ran.</exception>
    public void Dispose()
    {
        UiThread.VerifyAccess();
        Scope.DisposeForEffect(_scope);
        DisposeSources();
    }

    /// <summary>
    /// Stops the effect when its scope is disposed, after what the scope owns.
    /// </summary>
    internal void StopForDispose() => DisposeSources();

    /// <summary>
    /// Queues an effect that a paused scope held, when the scope resumes; it runs if a source changed meanwhile.
    /// </summary>
    internal void Release()
    {
        if (!IsDisposed)
        {
            ReactiveRuntime.Enqueue(this);
        }
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

        // Under a paused scope (a Show's exit, design §2.7), it waits, queued, until the scope resumes.
        if (Scope.PausedCount > 0 && Scope.TryHold(_scope, this))
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
    private protected override void Execute()
    {
        ScopeRef previous = Scope.CurrentRef;
        Scope.CurrentRef = _scope;
        try
        {
            _run();
        }
        finally
        {
            Scope.CurrentRef = previous;
        }
    }

    /// <inheritdoc />
    private protected override void OnException(Exception exception) => ReactiveRuntime.ReportException(exception);

    private void RunAndRequeue()
    {
        // What the last run made goes first, in the scope's order; its cleanups' exceptions go to the handler.
        if (HasRun && Scope.IsAlive(_scope) && Scope.ClearForRerun(_scope) is { } errors)
        {
            ReactiveRuntime.ReportException(errors);
        }

        // An effect that disposed itself while it ran is Clean here (DisposeSources), so it is not queued again.
        Run();
        if (Status == ComputationStatus.Dirty)
        {
            ReactiveRuntime.Enqueue(this);
        }
    }
}
