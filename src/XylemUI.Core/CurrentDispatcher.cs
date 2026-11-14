namespace XylemUI;

/// <summary>
/// Holds the dispatcher of the UI thread that <see cref="UiDispatcher.Current"/> returns (design §2.12): the process has one
/// instance, which the static members use, and the tests make their own so that they do not depend on what the process has set.
/// </summary>
/// <remarks>
/// Any thread may read <see cref="Value"/>, since a worker reads it to post. A host replaces the value with <see cref="Exchange"/>
/// when it starts and gives back the value it replaced when it is disposed, so that an inner host (a real window's loop run inside
/// the headless tests' UI thread) leaves the outer one's dispatcher in place.
/// </remarks>
internal sealed class CurrentDispatcher
{
    private IUiDispatcher? _value;

    /// <summary>
    /// Gets the dispatcher that is set now. A worker posts to the one it read.
    /// </summary>
    /// <exception cref="InvalidOperationException">No host has set a dispatcher.</exception>
    public IUiDispatcher Value
    {
        get
        {
            IUiDispatcher? value = Volatile.Read(ref _value);
            if (value is null)
            {
                ThrowHelper.ThrowNoCurrentDispatcher();
            }

            return value;
        }
    }

    /// <summary>
    /// Replaces the dispatcher and returns the one it replaced. Any thread may call it.
    /// </summary>
    /// <param name="value">The new dispatcher; <see langword="null"/> for none.</param>
    /// <returns>The dispatcher that was set before, or <see langword="null"/> when none was.</returns>
    public IUiDispatcher? Exchange(IUiDispatcher? value) => Interlocked.Exchange(ref _value, value);
}
