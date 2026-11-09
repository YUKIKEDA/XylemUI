namespace XylemUI;

/// <summary>
/// How an image element reads, sizes, and shows its image (design §5.0.4, "API"): Flutter's <c>Image</c>'s <c>fit</c>,
/// <c>cacheWidth</c> and <c>cacheHeight</c>, <c>loadingBuilder</c> and <c>errorBuilder</c>, and <c>semanticLabel</c>, in one value.
/// </summary>
public sealed class ImageOptions
{
    /// <summary>
    /// Gets how the image fills its box; <see cref="ImageFit.Contain"/> when not set.
    /// </summary>
    public ImageFit Fit { get; init; }

    /// <summary>
    /// Gets the width to decode the image at, in the image's pixels; the image's own when <see langword="null"/>. With only one of
    /// the two, the other keeps the image's ratio; neither is taken past the image's own (design §5.0.4, "Decoding size").
    /// </summary>
    public int? DecodeWidth { get; init; }

    /// <summary>
    /// Gets the height to decode the image at, in the image's pixels; the image's own when <see langword="null"/>.
    /// </summary>
    public int? DecodeHeight { get; init; }

    /// <summary>
    /// Gets what is shown while the image is read; nothing (a box of size 0) when <see langword="null"/>.
    /// </summary>
    public Func<Element>? Loading { get; init; }

    /// <summary>
    /// Gets what is shown when the image could not be read; nothing (a box of size 0) when <see langword="null"/>.
    /// </summary>
    public Func<Element>? Error { get; init; }

    /// <summary>
    /// Gets the name a screen reader reads for the image, with the role of an image; with none, the image is decoration and is left
    /// out of the accessibility tree (design §5.0.4, "Screen readers").
    /// </summary>
    public string? Label { get; init; }
}
