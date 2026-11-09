namespace XylemUI;

/// <summary>
/// Identifies one image of a window's <see cref="TextureCache"/> together with the generation of its slot (design §5.0.4, "Owner and
/// lifetime"): a handle kept after <see cref="TextureCache.Release"/> no longer points to an image, even once the slot holds another.
/// </summary>
/// <param name="Index">The slot of the image in the cache.</param>
/// <param name="Generation">
/// The generation of the slot when the image was added. Generations start at 1, so <see langword="default"/> points to no image.
/// </param>
internal readonly record struct ImageHandle(int Index, uint Generation)
{
    /// <summary>
    /// Gets a value indicating whether this is the <see langword="default"/> handle, which points to no image.
    /// </summary>
    public bool IsNone => Generation == 0;
}
