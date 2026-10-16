namespace XylemUI;

/// <summary>
/// The only way for code on another thread to hand work to the UI thread (design §2.12, Invariant 10).
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues a message for the UI thread and wakes it. Safe to call from any thread.
    /// </summary>
    /// <param name="message">The message to apply on the UI thread.</param>
    void Post(UiMessage message);
}
