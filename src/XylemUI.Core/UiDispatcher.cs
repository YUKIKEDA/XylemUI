using System.Collections.Concurrent;

namespace XylemUI;

/// <summary>
/// The UI thread's queue: messages from <see cref="Post"/> and <c>await</c> continuations from <see cref="SynchronizationContext"/>
/// go through it, in the order they were queued (design §2.12).
/// </summary>
/// <remarks>
/// <para>
/// Any thread may queue. Only the UI thread drains, with <see cref="Drain"/>, which the platform's message loop calls once when it
/// starts (so that nothing queued before the loop is lost) and once per iteration. Queuing calls the platform's wake, so a
/// blocked UI thread wakes up.
/// </para>
/// <para>
/// A synchronous render (design §2.11.1) never drains: <see cref="Drain"/> does nothing while
/// <see cref="SynchronousRenderGuard.IsActive"/>, and the work waits for the loop.
/// </para>
/// <para>
/// Each message and each continuation runs as one <see cref="ReactiveRuntime.Batch(Action)"/>: the effects of the signals it sets
/// run once, when it returns.
/// </para>
/// </remarks>
public sealed class UiDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly Action _wake;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiDispatcher"/> class.
    /// </summary>
    /// <param name="wake">Wakes the UI thread if it is waiting. Called from any thread after something is queued.</param>
    public UiDispatcher(Action wake)
    {
        ArgumentNullException.ThrowIfNull(wake);
        _wake = wake;
        SynchronizationContext = new UiSynchronizationContext(this);
        UnbatchedSynchronizationContext = new UiSynchronizationContext(this, batched: false);
    }

    /// <summary>
    /// Gets or sets the handler that applies a message on the UI thread. A message drained while it is <see langword="null"/>
    /// fails the invariant (design §0.1): a message with no handler would be lost without notice.
    /// </summary>
    public Action<UiMessage>? MessageHandler { get; set; }

    /// <summary>
    /// Gets the synchronization context that resumes <c>await</c> continuations on the UI thread through this queue.
    /// </summary>
    public SynchronizationContext SynchronizationContext { get; }

    /// <summary>
    /// Gets a synchronization context that resumes continuations through this queue without a batch around each: the headless
    /// tests' UI thread (design §12.5, "Threads"), where a test's body and its continuations run as items of this queue, and a
    /// batch around one would hold every effect back until the test's body ended.
    /// </summary>
    internal SynchronizationContext UnbatchedSynchronizationContext { get; }

    /// <inheritdoc />
    public void Post(UiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Enqueue(new WorkItem(message, null, null, Batched: true));
    }

    /// <summary>
    /// Runs the work that was queued before this call, in order. Work queued while it runs waits for the next call.
    /// </summary>
    /// <param name="reportException">Receives an exception from a message handler or a continuation; the drain goes on.</param>
    /// <returns>The number of items that ran.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public int Drain(Action<Exception> reportException)
    {
        ArgumentNullException.ThrowIfNull(reportException);
        UiThread.VerifyAccess();
        if (SynchronousRenderGuard.IsActive)
        {
            return 0;
        }

        // A snapshot of the count: a handler that queues more work (an await that completes at once) cannot keep the drain going.
        int count = _queue.Count;
        int ran = 0;
        while (ran < count && _queue.TryDequeue(out WorkItem item))
        {
            ran++;
            Action<UiMessage>? handler = MessageHandler;
            if (item.Message is not null && handler is null)
            {
                Invariant.Fail("A UiMessage was drained with no MessageHandler set.");
            }

            // Each item is one batch: however many signals it sets, the effects run once, after it (design §2.2.1). An unbatched
            // item is a headless test's (UnbatchedSynchronizationContext).
            if (item.Batched)
            {
                ReactiveRuntime.EnterBatch();
            }

            try
            {
                if (item.Message is { } message)
                {
                    handler!(message);
                }
                else
                {
                    item.Callback!(item.State);
                }
            }
            catch (Exception exception)
            {
                reportException(exception);
            }
            finally
            {
                if (item.Batched)
                {
                    ReactiveRuntime.ExitBatch();
                }
            }
        }

        return ran;
    }

    /// <summary>
    /// Queues a callback to run on the UI thread, after the work queued before it. Any thread may call it.
    /// </summary>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The object passed to <paramref name="callback"/>.</param>
    /// <param name="batched">Whether it runs in a batch of its own; <see langword="false"/> only for <see cref="UnbatchedSynchronizationContext"/>.</param>
    internal void PostCallback(SendOrPostCallback callback, object? state, bool batched = true) =>
        Enqueue(new WorkItem(null, callback, state, batched));

    private void Enqueue(WorkItem item)
    {
        _queue.Enqueue(item);
        _wake();
    }

    private readonly record struct WorkItem(UiMessage? Message, SendOrPostCallback? Callback, object? State, bool Batched);
}
