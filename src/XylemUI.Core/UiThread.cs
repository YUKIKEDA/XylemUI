namespace XylemUI;

/// <summary>
/// Records which thread is the UI thread and checks that UI-thread-only APIs are called on it.
/// </summary>
/// <remarks>
/// The engine's types have no locks and belong to the UI thread (design §2.12, Invariant 8).
/// Public entry points call <see cref="VerifyAccess"/> in every build.
/// </remarks>
public static class UiThread
{
    // 0 means not initialized. Managed thread ids start at 1.
    private static int _threadId;

    /// <summary>
    /// Gets a value indicating whether a UI thread has been recorded.
    /// </summary>
    public static bool IsInitialized => Volatile.Read(ref _threadId) != 0;

    /// <summary>
    /// Records the calling thread as the UI thread.
    /// </summary>
    /// <remarks>
    /// The platform host calls this once, on the thread that creates the windows. A second call from the same thread does nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Another thread is already the UI thread.</exception>
    public static void Initialize()
    {
        int current = Environment.CurrentManagedThreadId;
        int previous = Interlocked.CompareExchange(ref _threadId, current, 0);
        if (previous != 0 && previous != current)
        {
            ThrowHelper.ThrowUiThreadAlreadyInitialized();
        }
    }

    /// <summary>
    /// Returns a value indicating whether the calling thread is the UI thread.
    /// </summary>
    /// <returns><see langword="true"/> on the UI thread; otherwise <see langword="false"/>.</returns>
    public static bool CheckAccess() => Volatile.Read(ref _threadId) == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Throws when the calling thread is not the UI thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread, or no UI thread has been recorded.</exception>
    public static void VerifyAccess()
    {
        if (!CheckAccess())
        {
            ThrowHelper.ThrowNotOnUiThread();
        }
    }
}
