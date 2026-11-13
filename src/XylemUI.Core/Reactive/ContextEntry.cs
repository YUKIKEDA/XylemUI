namespace XylemUI;

/// <summary>
/// The store of the values of one context key, which a scope tells to drop a value when the scope is disposed.
/// </summary>
internal interface IContextStore
{
    /// <summary>
    /// Drops the value in a slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    void Release(int slot);
}

/// <summary>
/// One provided value in the list of a scope: the key, and the slot of the value in the key's store.
/// </summary>
internal struct ContextEntry
{
    /// <summary>The key.</summary>
    internal IContextStore Key;

    /// <summary>The slot of the value in the key's store.</summary>
    internal int Slot;

    /// <summary>The next entry of the list.</summary>
    internal Handle<ContextEntry> Older;
}
