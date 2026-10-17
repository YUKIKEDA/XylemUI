using System.Diagnostics;

namespace XylemUI;

/// <summary>
/// The reactive scheduler of the UI thread: batches, the flush of effects, and the handler of their exceptions (design §2.2.1,
/// §2.2.2).
/// </summary>
/// <remarks>
/// All of it belongs to the UI thread (design §2.12), so the state is in plain static fields.
/// </remarks>
public static class ReactiveRuntime
{
    /// <summary>
    /// The number of stabilization cycles one flush may take. A cycle runs the effects queued when it starts; effects that
    /// become dirty during it run in the next cycle. Reaching the limit means the graph did not stabilize, not that a cycle in
    /// the graph was found.
    /// </summary>
    public const int MaxFlushIterations = 100;

    private static Action<Exception> _unhandledExceptionHandler = DefaultUnhandledExceptionHandler;
    private static EffectQueue _queue = new();
    private static EffectQueue _nextQueue = new();
    private static int _batchDepth;
    private static int _layoutDepth;
    private static bool _flushing;

    /// <summary>
    /// Gets or sets the handler that receives an exception thrown by an effect, and a <see cref="ReactiveStabilizationException"/>
    /// when a flush does not stabilize. The default writes to <see cref="Trace"/>. An exception thrown by the handler is dropped.
    /// </summary>
    public static Action<Exception> UnhandledExceptionHandler
    {
        get => _unhandledExceptionHandler;
        set => _unhandledExceptionHandler = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the computation that is running, whose reads are recorded.
    /// </summary>
    internal static Computation? Current { get; set; }

    /// <summary>
    /// Gets the number of stabilization cycles run so far. Tests use it to see in which cycle an effect ran.
    /// </summary>
    internal static ulong CycleCount { get; private set; }

    /// <summary>
    /// Gets the number of effects waiting for the next flush.
    /// </summary>
    internal static int QueuedCount => _queue.Count + _nextQueue.Count;

    /// <summary>
    /// Runs <paramref name="action"/> as one batch: signals set inside it only mark their readers, and the effects run once
    /// when the outermost batch ends, even if <paramref name="action"/> throws (design §2.2.1).
    /// </summary>
    /// <param name="action">The code that sets signals.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static void Batch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        UiThread.VerifyAccess();
        EnterBatch();
        try
        {
            action();
        }
        finally
        {
            ExitBatch();
        }
    }

    /// <summary>
    /// Runs <paramref name="func"/> as one batch and returns its result (see <see cref="Batch(Action)"/>).
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="func">The code that sets signals.</param>
    /// <returns>The result of <paramref name="func"/>.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static T Batch<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        UiThread.VerifyAccess();
        EnterBatch();
        try
        {
            return func();
        }
        finally
        {
            ExitBatch();
        }
    }

    /// <summary>
    /// Opens a layout pass: until <see cref="ExitLayout"/>, reading or setting a signal fails the invariant (design §2.4.1:
    /// layout is a pure computation over what is written on the nodes).
    /// </summary>
    internal static void EnterLayout() => _layoutDepth++;

    /// <summary>
    /// Closes a layout pass opened by <see cref="EnterLayout"/>.
    /// </summary>
    internal static void ExitLayout() => _layoutDepth--;

    /// <summary>
    /// Fails the invariant when a layout pass is running. Called by every read and write of a signal or a computed value.
    /// </summary>
    internal static void VerifyNotInLayout()
    {
        if (_layoutDepth != 0)
        {
            Invariant.Fail("A signal or a computed value was read or set during layout.");
        }
    }

    /// <summary>
    /// Opens a batch. Each call is closed by one <see cref="ExitBatch"/> in a <c>finally</c>.
    /// </summary>
    internal static void EnterBatch() => _batchDepth++;

    /// <summary>
    /// Closes a batch. Closing the outermost one flushes, unless a flush is already running: then the effects queued meanwhile
    /// run in its next cycle.
    /// </summary>
    internal static void ExitBatch()
    {
        if (--_batchDepth == 0 && !_flushing)
        {
            Flush();
        }
    }

    /// <summary>
    /// Queues an effect for the current flush, or for the next cycle while a flush runs.
    /// </summary>
    internal static void Enqueue(Effect effect)
    {
        effect.Status = ComputationStatus.Queued;
        (_flushing ? _nextQueue : _queue).Enqueue(effect, effect.Depth);
    }

    /// <summary>
    /// Passes an exception to the handler. An exception from the handler itself is dropped, so the flush goes on.
    /// </summary>
    internal static void ReportException(Exception exception)
    {
        try
        {
            _unhandledExceptionHandler(exception);
        }
        catch (Exception handlerException) when (handlerException is not InvariantViolationException)
        {
            // A handler that throws must not stop the flush.
        }
    }

    // Runs the queued effects in depth order, one stabilization cycle at a time. When the limit is reached, the effects of the
    // next cycle stay queued for the next flush and the handler receives the diagnostic (design §2.2.2).
    private static void Flush()
    {
        _flushing = true;
        try
        {
            int cycles = 0;
            while (_queue.Count > 0)
            {
                if (++cycles > MaxFlushIterations)
                {
                    ReportException(new ReactiveStabilizationException(MaxFlushIterations));
                    return;
                }

                CycleCount++;
                _queue.RunAll();

                (_queue, _nextQueue) = (_nextQueue, _queue);
            }
        }
        finally
        {
            _flushing = false;
        }
    }

    private static void DefaultUnhandledExceptionHandler(Exception exception) => Trace.TraceError("Unhandled exception in an effect: {0}", exception);
}
