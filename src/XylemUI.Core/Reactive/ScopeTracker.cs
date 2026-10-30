namespace XylemUI;

/// <summary>
/// Records the live scopes while it is enabled, to find the ones that were never disposed (design §2.3, the R3
/// <c>ObservableTracker</c> idea). Off by default: then creating and disposing a scope costs one read of a static flag here.
/// </summary>
/// <remarks>
/// An effect has a scope of its own, so live effects are listed too; one made outside any scope shows with
/// <see cref="TrackedScope.IsOwned"/> <see langword="false"/>. Scopes made while the tracker was off are not listed. All members
/// belong to the UI thread.
/// </remarks>
public static class ScopeTracker
{
    private static readonly Dictionary<int, TrackedScope> Live = [];
    private static int _lastId;

    /// <summary>
    /// Gets or sets a value indicating whether new scopes are recorded.
    /// </summary>
    public static bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a recorded scope also keeps the stack trace of its creation. Only read while
    /// <see cref="IsEnabled"/> is <see langword="true"/>.
    /// </summary>
    public static bool CaptureStackTrace { get; set; }

    /// <summary>
    /// Calls <paramref name="action"/> for each recorded scope that is still live, oldest first.
    /// </summary>
    /// <param name="action">Receives each scope.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public static void ForEachActiveScope(Action<TrackedScope> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        UiThread.VerifyAccess();
        foreach (TrackedScope scope in Live.Values.OrderBy(scope => scope.Id).ToArray())
        {
            action(scope);
        }
    }

    /// <summary>
    /// Records a scope that was just made.
    /// </summary>
    /// <param name="owner">The name given when it was made.</param>
    /// <param name="isEffect">Whether it is the scope of an effect.</param>
    /// <param name="isOwned">Whether it was made inside another scope.</param>
    /// <returns>The scope's id, which <see cref="Remove"/> takes.</returns>
    internal static int Add(string owner, bool isEffect, bool isOwned)
    {
        int id = ++_lastId;
        string? stack = CaptureStackTrace ? Environment.StackTrace : null;
        Live.Add(id, new TrackedScope(id, owner, isEffect, isOwned, stack));
        return id;
    }

    /// <summary>
    /// Forgets a scope that was disposed; an id that is not recorded is ignored.
    /// </summary>
    /// <param name="id">The id <see cref="Add"/> returned.</param>
    internal static void Remove(int id) => _ = Live.Remove(id);
}

/// <summary>
/// A live scope recorded by <see cref="ScopeTracker"/>.
/// </summary>
/// <param name="Id">The order of creation, from 1.</param>
/// <param name="Owner">The name given when it was made: the caller of <see cref="Scope.Create"/> or <see cref="Effect.Create"/> by default.</param>
/// <param name="IsEffect">Whether it is the scope of an effect.</param>
/// <param name="IsOwned">Whether it was made inside another scope; an effect made outside any scope is <see langword="false"/>.</param>
/// <param name="StackTrace">The stack trace of its creation, when <see cref="ScopeTracker.CaptureStackTrace"/> was on.</param>
public readonly record struct TrackedScope(int Id, string Owner, bool IsEffect, bool IsOwned, string? StackTrace);
