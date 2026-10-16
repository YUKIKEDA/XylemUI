using System.Runtime.ExceptionServices;

namespace XylemUI;

/// <summary>
/// Marks the synchronous render path that runs inside an OS callback, such as a resize inside the OS modal loop (design §2.11.1).
/// </summary>
/// <remarks>
/// <para>
/// While the path runs, application callbacks do not run on the spot: <see cref="RunOrDefer"/> queues them, and they run in order
/// when the outermost path ends. A second synchronous render that the first one causes (a nested resize) is not started; the
/// platform checks <see cref="IsActive"/> and drops it.
/// </para>
/// <para>The depth is a counter, not a flag: a nested window procedure that returns must not end the outer path.</para>
/// </remarks>
public static class SynchronousRenderGuard
{
    private static readonly Queue<Action> _deferred = new();
    private static int _depth;
    private static bool _draining;

    /// <summary>Gets a value indicating whether a synchronous render path is running on the UI thread.</summary>
    public static bool IsActive => _depth > 0;

    /// <summary>
    /// Starts a synchronous render path. Dispose the result to end it; the outermost end runs the deferred callbacks.
    /// </summary>
    /// <returns>The scope of the path.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static Scope Enter()
    {
        UiThread.VerifyAccess();
        _depth++;
        return new Scope(_depth);
    }

    /// <summary>
    /// Runs an application callback now, or after the synchronous render path when one is running.
    /// </summary>
    /// <param name="callback">The application callback.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static void RunOrDefer(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        UiThread.VerifyAccess();
        if (_depth > 0)
        {
            _deferred.Enqueue(callback);
            return;
        }

        callback();
    }

    private static void Exit(int depthAtEnter)
    {
        if (_depth != depthAtEnter)
        {
            Invariant.Fail("Synchronous render scopes ended out of order.");
        }

        _depth--;

        // A deferred callback may start a synchronous render of its own (a pending resize does). Its end must not drain the queue:
        // the drain that is already running takes everything in order, also what that render queues.
        if (_depth > 0 || _draining)
        {
            return;
        }

        // Every deferred callback runs, also after one throws; the exceptions are thrown together at the end (as Scope cleanups do,
        // design §2.3). The caller is a window procedure, whose barrier reports them.
        List<Exception>? errors = null;
        _draining = true;
        try
        {
            while (_deferred.TryDequeue(out Action? callback))
            {
                try
                {
                    callback();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }
        }
        finally
        {
            _draining = false;
        }

        if (errors is not null)
        {
            ThrowDeferredErrors(errors);
        }
    }

    private static void ThrowDeferredErrors(List<Exception> errors)
    {
        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Throw(errors[0]);
        }

        throw new AggregateException("Application callbacks deferred past a synchronous render threw.", errors);
    }

    /// <summary>
    /// Ends the synchronous render path started by <see cref="Enter"/>.
    /// </summary>
    public readonly ref struct Scope
    {
        private readonly int _depthAtEnter;

        internal Scope(int depthAtEnter) => _depthAtEnter = depthAtEnter;

        /// <summary>Ends the path. The outermost end runs the deferred callbacks in the order they were queued.</summary>
        public void Dispose() => Exit(_depthAtEnter);
    }
}
