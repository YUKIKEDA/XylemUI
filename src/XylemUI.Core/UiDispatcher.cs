using System.Collections.Concurrent;

namespace XylemUI;

/// <summary>
/// The UI thread's queue: messages from <see cref="Post{T}(T)"/> and <c>await</c> continuations from
/// <see cref="SynchronizationContext"/> go through it, in the order they were queued (design §2.12).
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
/// Each message, with every receiver it goes to, and each continuation runs as one <see cref="ReactiveRuntime.Batch(Action)"/>:
/// the effects of the signals it sets run once, when it returns.
/// </para>
/// </remarks>
public sealed class UiDispatcher : IUiDispatcher
{
    // The process's one value of Current; the tests make their own CurrentDispatcher.
    private static readonly CurrentDispatcher CurrentValue = new();

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
    /// Gets the dispatcher of the UI thread, which the app's host set when it started: a worker posts its messages to it
    /// (<see cref="IUiDispatcher.Post{T}(T)"/>). Any thread may read it, and a worker posts to the one it read:
    /// what it posts after that host has ended is never run.
    /// </summary>
    /// <remarks>
    /// The host sets it when it starts and gives back the one it replaced when it is disposed (design §2.12): on Windows
    /// <c>XylemApp.Run</c>, and in the headless tests <c>HeadlessSession</c> when its UI thread starts.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No host has set a dispatcher.</exception>
    public static IUiDispatcher Current => CurrentValue.Value;

    /// <summary>
    /// Gets the number of messages that were dropped because every receiver of their type had been removed before they were
    /// drained: the result of a worker that arrived after the scope that asked for it was disposed. For diagnostics; read on the
    /// UI thread.
    /// </summary>
    public long DroppedMessageCount { get; private set; }

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
    public void Post<T>(T message)
        where T : UiMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        // The receivers are chosen by T, with no lookup by the message's own type (design §2.12): a message posted as a base type
        // would reach the base type's receivers, so it is refused here, on the thread that made the mistake.
        if (message.GetType() != typeof(T))
        {
            ThrowHelper.ThrowMessagePostedAsBaseType(nameof(message));
        }

        Enqueue(new WorkItem(UiMessageReceivers<T>.Deliver, message, null, null, Batched: true));
    }

    /// <summary>
    /// Runs the work that was queued before this call, in order. Work queued while it runs waits for the next call.
    /// </summary>
    /// <param name="reportException">Receives an exception from a message's receiver or a continuation; the drain goes on.</param>
    /// <returns>The number of items that ran.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    public int Drain(Action<Exception> reportException)
    {
        ArgumentNullException.ThrowIfNull(reportException);
        UiThread.VerifyAccess();
        if (SynchronousRenderGuard.IsActive)
        {
            return 0;
        }

        // A snapshot of the count: a receiver that queues more work (an await that completes at once) cannot keep the drain going.
        int count = _queue.Count;
        int ran = 0;
        while (ran < count && _queue.TryDequeue(out WorkItem item))
        {
            ran++;
            bool lost = false;

            // Each item is one batch: however many signals it sets, the effects run once, after it (design §2.2.1). An unbatched
            // item is a headless test's (UnbatchedSynchronizationContext).
            if (item.Batched)
            {
                ReactiveRuntime.EnterBatch();
            }

            try
            {
                if (item.Deliver is { } deliver)
                {
                    int reached = deliver(item.Message!, reportException);
                    lost = reached < 0;
                    if (reached == 0)
                    {
                        DroppedMessageCount++;
                    }
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

            // A message whose type never had a receiver would be lost without notice (design §0.1).
            if (lost)
            {
                Invariant.Fail("A UiMessage was drained whose type never had a receiver (UiMessage.On).");
            }
        }

        return ran;
    }

    /// <summary>
    /// Replaces the dispatcher that <see cref="Current"/> returns and returns the one it replaced. A host calls it when it starts,
    /// and again with the returned value when it is disposed. Any thread may call it.
    /// </summary>
    /// <param name="value">The host's dispatcher; <see langword="null"/> for none.</param>
    /// <returns>The dispatcher that was set before, or <see langword="null"/> when none was.</returns>
    internal static IUiDispatcher? Exchange(IUiDispatcher? value) => CurrentValue.Exchange(value);

    /// <summary>
    /// Queues a callback to run on the UI thread, after the work queued before it. Any thread may call it.
    /// </summary>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The object passed to <paramref name="callback"/>.</param>
    /// <param name="batched">Whether it runs in a batch of its own; <see langword="false"/> only for <see cref="UnbatchedSynchronizationContext"/>.</param>
    internal void PostCallback(SendOrPostCallback callback, object? state, bool batched = true) =>
        Enqueue(new WorkItem(null, null, callback, state, batched));

    private void Enqueue(WorkItem item)
    {
        _queue.Enqueue(item);
        _wake();
    }

    // A message carries the delivery of its type's receivers (UiMessageReceivers<T>.Deliver); a continuation, its callback.
    private readonly record struct WorkItem(
        Func<UiMessage, Action<Exception>, int>? Deliver,
        UiMessage? Message,
        SendOrPostCallback? Callback,
        object? State,
        bool Batched
    );
}
