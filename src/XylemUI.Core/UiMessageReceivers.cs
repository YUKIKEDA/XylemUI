namespace XylemUI;

/// <summary>
/// The receivers of one message type (design §2.12, "Receivers of a <see cref="UiMessage"/>"): a static table per type, so that
/// <see cref="UiDispatcher.Post{T}(T)"/> chooses it from the type it was called with and a drain looks nothing up.
/// </summary>
/// <remarks>
/// Every member belongs to the UI thread: <see cref="Add"/> checks it, and <see cref="Deliver"/> runs inside
/// <see cref="UiDispatcher.Drain"/>, which checks it. Other threads read only <see cref="Deliver"/>, which never changes.
/// </remarks>
/// <typeparam name="T">The message type.</typeparam>
internal static class UiMessageReceivers<T>
    where T : UiMessage
{
    // The receivers in the order they were added, linked through themselves (.cursor/rules/perf.mdc, "Collections of
    // subscribers"): a removal is O(1) and a delivery copies nothing.
    private static Receiver? _first;
    private static Receiver? _last;

    // The number of receivers ever added; each takes the next as its Id, so a delivery skips those added while it runs.
    private static long _added;

    /// <summary>
    /// Gets the delivery that a queued message of type <typeparamref name="T"/> carries to the drain: it hands the message to
    /// each receiver and returns how many it reached.
    /// </summary>
    internal static Func<UiMessage, Action<Exception>, int> Deliver { get; } = DeliverToReceivers;

    /// <summary>
    /// Adds a receiver that belongs to the current scope: the scope's disposal removes it.
    /// </summary>
    /// <param name="receive">The receiver.</param>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, there is no current scope, or a computed value is computing.
    /// </exception>
    internal static void Add(Action<T> receive)
    {
        UiThread.VerifyAccess();
        ScopeRef scope = Scope.CurrentRef;
        if (!Scope.IsAlive(scope))
        {
            ThrowHelper.ThrowReceiverWithNoScope();
        }

        var receiver = new Receiver(receive, scope);

        // Before the receiver is linked: a cleanup that cannot be registered (inside a computed value) leaves no receiver behind.
        Scope.OnCleanup(receiver.Remove);
        receiver.Id = ++_added;
        receiver.Previous = _last;
        if (_last is null)
        {
            _first = receiver;
        }
        else
        {
            _last.Next = receiver;
        }

        _last = receiver;
    }

    // Hands the message to each receiver that was there when the delivery started, in its scope, as an input handler runs in
    // its own (design §2.13). A receiver that throws is reported and the others still receive the message.
    private static int DeliverToReceivers(UiMessage message, Action<Exception> reportException)
    {
        // UiDispatcher.Post<T> took only a message whose type is T.
        var typed = (T)message;
        long added = _added;
        int reached = 0;

        // A receiver removed while it runs keeps its Next, so the walk goes on from it. A receiver removed before the walk
        // reaches it is either unlinked or reached through such a Next; its scope is then disposed or being disposed, and
        // TryEnter refuses it.
        for (Receiver? receiver = _first; receiver is not null && receiver.Id <= added; receiver = receiver.Next)
        {
            if (!Scope.TryEnter(receiver.Owner, out ScopeRef previous))
            {
                continue;
            }

            reached++;
            try
            {
                receiver.Receive(typed);
            }
            catch (Exception exception) when (exception is not InvariantViolationException)
            {
                reportException(exception);
            }
            finally
            {
                Scope.Exit(previous);
            }
        }

        return reached;
    }

    private static void Unlink(Receiver receiver)
    {
        if (receiver.Previous is { } previous)
        {
            previous.Next = receiver.Next;
        }
        else
        {
            _first = receiver.Next;
        }

        if (receiver.Next is { } next)
        {
            next.Previous = receiver.Previous;
        }
        else
        {
            _last = receiver.Previous;
        }
    }

    // One receiver, and its link in the list.
    private sealed class Receiver(Action<T> receive, ScopeRef scope)
    {
        public Action<T> Receive { get; } = receive;

        public ScopeRef Owner { get; } = scope;

        public long Id { get; set; }

        public Receiver? Previous { get; set; }

        public Receiver? Next { get; set; }

        // The scope's cleanup. Next stays as it was, for a delivery that is running this receiver.
        public void Remove() => Unlink(this);
    }
}
