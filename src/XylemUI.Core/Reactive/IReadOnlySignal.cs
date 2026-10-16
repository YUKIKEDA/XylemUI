namespace XylemUI;

/// <summary>
/// A value that can be read and tracked: a <see cref="Signal{T}"/> or a <see cref="Computed{T}"/> (design §2.2).
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public interface IReadOnlySignal<out T>
{
    /// <summary>
    /// Gets the current value. Inside a computed value or an effect, the read is recorded, and a later change runs it again.
    /// </summary>
    /// <returns>The current value.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    T Get();
}
