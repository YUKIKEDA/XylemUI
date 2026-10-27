namespace XylemUI;

/// <summary>
/// A scope of the ownership tree (design §2.3), as <see cref="Scope.Create"/> returns it and <see cref="Scope.Run"/> and
/// <see cref="Scope.Dispose"/> take it. <see langword="default"/> is no scope.
/// </summary>
public readonly record struct ScopeHandle
{
    internal ScopeHandle(ScopeRef value) => Value = value;

    /// <summary>
    /// Gets a value indicating whether this is no scope: <see langword="default"/>, or <see cref="Scope.Current"/> outside every
    /// scope.
    /// </summary>
    public bool IsNone => Value.IsNone;

    /// <summary>
    /// Gets the scope's handle in the arena.
    /// </summary>
    internal ScopeRef Value { get; }
}
