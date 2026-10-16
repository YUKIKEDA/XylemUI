namespace XylemUI;

/// <summary>
/// Resumes <c>await</c> continuations on the UI thread through the <see cref="UiDispatcher"/> queue (design §2.12).
/// </summary>
internal sealed class UiSynchronizationContext(UiDispatcher dispatcher) : SynchronizationContext
{
    /// <inheritdoc />
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        dispatcher.PostCallback(d, state);
    }

    /// <summary>
    /// Runs the callback on the UI thread and waits for it. On the UI thread it runs at once; from another thread it is queued and
    /// the caller blocks until the UI thread has run it, and an exception from the callback is thrown again to the caller.
    /// </summary>
    /// <remarks>
    /// On the UI thread the callback runs even inside a synchronous render, where application callbacks are otherwise deferred
    /// (design §2.11.1). The framework itself never calls <c>Send</c>.
    /// </remarks>
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (UiThread.CheckAccess())
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Exception? error = null;
        dispatcher.PostCallback(
            _ =>
            {
                try
                {
                    d(state);
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    done.Set();
                }
            },
            null
        );
        done.Wait();
        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        }
    }

    /// <inheritdoc />
    public override SynchronizationContext CreateCopy() => this;
}
