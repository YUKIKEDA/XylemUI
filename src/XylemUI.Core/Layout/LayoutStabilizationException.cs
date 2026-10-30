namespace XylemUI;

/// <summary>
/// Passed to <see cref="ReactiveRuntime.UnhandledExceptionHandler"/> when a layout pass did not run out of dirty layout roots
/// within its cycle limit. The pass stops; the requests left are laid out by the next pass (design §2.4.1).
/// </summary>
public sealed class LayoutStabilizationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutStabilizationException"/> class.
    /// </summary>
    public LayoutStabilizationException()
        : base("Layout failed to stabilize within the configured cycle limit.") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutStabilizationException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public LayoutStabilizationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutStabilizationException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public LayoutStabilizationException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutStabilizationException"/> class with a message that names the cycle limit.
    /// </summary>
    /// <param name="cycleLimit">The cycle limit the layout pass reached.</param>
    internal LayoutStabilizationException(int cycleLimit)
        : base($"Layout failed to stabilize within the configured cycle limit ({cycleLimit} cycles).") { }
}
