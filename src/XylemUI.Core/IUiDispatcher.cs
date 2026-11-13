namespace XylemUI;

/// <summary>
/// The only way for code on another thread to hand work to the UI thread (design §2.12, Invariant 10).
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues a message for the receivers of type <typeparamref name="T"/> (<see cref="UiMessage.On{T}(Action{T})"/>) and wakes the
    /// UI thread. Safe to call from any thread.
    /// </summary>
    /// <typeparam name="T">The message's own type, which chooses its receivers.</typeparam>
    /// <param name="message">The message to hand to the receivers on the UI thread.</param>
    /// <exception cref="ArgumentException">
    /// The message's type is not <typeparamref name="T"/>: it is posted through a variable of a base type.
    /// </exception>
    void Post<T>(T message)
        where T : UiMessage;
}
