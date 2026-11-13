namespace XylemUI;

/// <summary>
/// The base of every message that carries a result from a worker thread or asynchronous I/O to the UI thread (design §2.12).
/// </summary>
/// <remarks>
/// A worker creates a message and posts it with <see cref="IUiDispatcher.Post{T}(T)"/>. It never touches the engine's types itself;
/// the UI thread hands the message to the receivers of its type, which <see cref="On{T}(Action{T})"/> adds. Each feature that
/// produces results off the UI thread adds its own derived record.
/// </remarks>
public abstract record UiMessage
{
    /// <summary>
    /// Adds a receiver of the messages of type <typeparamref name="T"/> that are posted as <typeparamref name="T"/>. It belongs to
    /// the current scope, and that scope's disposal removes it. Each message goes to every receiver of its type, in the order they
    /// were added, each in its own scope; a receiver added while a message is handed out gets the messages after it.
    /// </summary>
    /// <remarks>
    /// A message drained when no receiver of its type is there fails the invariant (design §0.1): it would be lost without notice.
    /// </remarks>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="receive">The receiver. It runs on the UI thread, in one batch with the other receivers of the message.</param>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, there is no current scope, or a computed value is computing.
    /// </exception>
    public static void On<T>(Action<T> receive)
        where T : UiMessage
    {
        ArgumentNullException.ThrowIfNull(receive);
        UiMessageReceivers<T>.Add(receive);
    }
}
