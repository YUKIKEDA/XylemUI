namespace XylemUI;

/// <summary>
/// The data of one scope in the ownership tree (design §2.3, §2.5). It is opaque: code holds a <see cref="Handle{T}"/> to it,
/// returned by <see cref="Scope.Create"/>, and passes it back to <see cref="Scope.Dispose"/>.
/// </summary>
/// <remarks>
/// The children and the cleanups are lists linked through the arenas, newest first, so that walking them from the head is the
/// LIFO order of the disposal contract and creating a scope in steady state allocates nothing (the arena slots are reused).
/// </remarks>
internal struct ScopeData
{
    /// <summary>The owner; <see langword="default"/> for a scope created outside any scope.</summary>
    internal ScopeRef Parent;

    /// <summary>The newest child, the first to be disposed.</summary>
    internal ScopeRef LastChild;

    /// <summary>The next older sibling in the parent's list.</summary>
    internal ScopeRef OlderSibling;

    /// <summary>The next newer sibling in the parent's list, kept so that a child is unlinked in O(1).</summary>
    internal ScopeRef NewerSibling;

    /// <summary>The newest cleanup, the first to run.</summary>
    internal Handle<CleanupEntry> LastCleanup;

    /// <summary>The effect this scope belongs to, disposed with it; <see langword="null"/> for a scope made by <see cref="Scope.Create"/>.</summary>
    internal Effect? Effect;

    /// <summary>The id in <see cref="ScopeTracker"/>; 0 when the tracker was off when the scope was made.</summary>
    internal int TrackerId;

    /// <summary>Set while the scope disposes its children and cleanups, so that a nested dispose of it does nothing.</summary>
    internal bool Disposing;

    /// <summary>
    /// Whether the scope is paused: the effects under it are held, not run, until it resumes (design §2.7, a <c>Show</c>'s
    /// exit).
    /// </summary>
    internal bool Paused;
}
