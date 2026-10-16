namespace XylemUI;

/// <summary>
/// Passed to <see cref="ReactiveRuntime.UnhandledExceptionHandler"/> when a flush did not stabilize within
/// <see cref="ReactiveRuntime.MaxFlushIterations"/> cycles, typically because an effect keeps setting a value it reads. The flush
/// stops; the effects still queued run in the next flush (design §2.2.2).
/// </summary>
public sealed class ReactiveStabilizationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReactiveStabilizationException"/> class.
    /// </summary>
    public ReactiveStabilizationException()
        : base("Reactive graph failed to stabilize within the configured cycle limit.") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReactiveStabilizationException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public ReactiveStabilizationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReactiveStabilizationException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public ReactiveStabilizationException(string message, Exception innerException)
        : base(message, innerException) { }

    internal ReactiveStabilizationException(int cycleLimit)
        : base($"Reactive graph failed to stabilize within the configured cycle limit ({cycleLimit} cycles).") { }
}
