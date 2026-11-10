namespace XylemUI;

/// <summary>
/// Where an entry of a window's image cache stands (design §5.0.4, "Cache").
/// </summary>
internal enum ImageEntryState : byte
{
    /// <summary>
    /// Its decode was asked for and its result has not arrived.
    /// </summary>
    Decoding,

    /// <summary>
    /// It holds an image.
    /// </summary>
    Ready,

    /// <summary>
    /// Its decode failed: the cache does not keep it, and the elements that waited for it show their error parts.
    /// </summary>
    Failed,

    /// <summary>
    /// The cache let it go: its image is released, or its decode was skipped or its result dropped.
    /// </summary>
    Gone,
}
