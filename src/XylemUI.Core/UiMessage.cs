namespace XylemUI;

/// <summary>
/// The base of every message that carries a result from a worker thread or asynchronous I/O to the UI thread (design §2.12).
/// </summary>
/// <remarks>
/// A worker creates a message and posts it with <see cref="IUiDispatcher.Post"/>. It never touches the engine's types itself; the
/// UI thread applies the message. Each feature that produces results off the UI thread adds its own derived record.
/// </remarks>
public abstract record UiMessage;
