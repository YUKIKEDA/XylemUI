namespace XylemUI;

/// <summary>
/// One image of a window's cache (design §5.0.4, "Cache", "The entry and the three sets"): the image, the number of elements that
/// show it or wait for it, and the bytes it counts for. Only <see cref="WindowImageCache"/> writes it, on the UI thread.
/// </summary>
/// <param name="key">The key the entry was made for.</param>
internal sealed class ImageEntry(ImageKey key)
{
    /// <summary>
    /// Gets the key the entry was made for.
    /// </summary>
    public ImageKey Key { get; } = key;

    /// <summary>
    /// Gets or sets where the entry stands.
    /// </summary>
    public ImageEntryState State { get; set; }

    /// <summary>
    /// Gets or sets the image; <see langword="default"/> unless the entry is <see cref="ImageEntryState.Ready"/>.
    /// </summary>
    public ImageHandle Image { get; set; }

    /// <summary>
    /// Gets or sets the image's width, in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Gets or sets the image's height, in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Gets or sets the bytes the image counts for against the limit: 4 for each pixel.
    /// </summary>
    public long Bytes { get; set; }

    /// <summary>
    /// Gets or sets the number of elements that show the image or wait for its decode.
    /// </summary>
    public int Elements { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cache's index finds the entry by its key. An entry that
    /// <see cref="ImageCache.Evict"/> or <see cref="ImageCache.Clear"/> took out lives on while elements show it or wait for it.
    /// </summary>
    public bool Indexed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the entry is in the counted set, which the limits bound.
    /// </summary>
    public bool Counted { get; set; }

    /// <summary>
    /// Gets or sets the entry of the counted set used just before this one; <see langword="null"/> for the oldest.
    /// </summary>
    public ImageEntry? Older { get; set; }

    /// <summary>
    /// Gets or sets the entry of the counted set used just after this one; <see langword="null"/> for the newest.
    /// </summary>
    public ImageEntry? Newer { get; set; }

    /// <summary>
    /// Gets or sets the decode in flight; <see langword="null"/> once its result arrived, or when the cache dropped it.
    /// </summary>
    public ImageRequest? Request { get; set; }

    /// <summary>
    /// Gets or sets the first element that waits for the decode; the elements are linked through
    /// <see cref="ImageBlock.NextWaiting"/> in the order they asked.
    /// </summary>
    public ImageBlock? FirstWaiting { get; set; }

    /// <summary>
    /// Gets or sets the last element that waits for the decode.
    /// </summary>
    public ImageBlock? LastWaiting { get; set; }
}
