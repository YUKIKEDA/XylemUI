namespace XylemUI;

/// <summary>
/// One cleanup of a scope (design §2.3), in a list linked newest first.
/// </summary>
/// <param name="Run">The cleanup.</param>
/// <param name="Older">The cleanup registered before this one in the same scope.</param>
internal readonly record struct CleanupEntry(Action Run, Handle<CleanupEntry> Older);
