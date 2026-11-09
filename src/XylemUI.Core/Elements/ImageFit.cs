namespace XylemUI;

/// <summary>
/// How an image fills its box (design §5.0.4, "Display"): CSS's <c>object-fit</c>, centered in the box as its
/// <c>object-position</c> of 50 % 50 %. An image's size is its pixels counted as logical pixels.
/// </summary>
public enum ImageFit
{
    /// <summary>The image is scaled to the box's size, its ratio kept, as large as it fits whole; the default.</summary>
    Contain,

    /// <summary>The image is stretched to the box's size, its ratio not kept.</summary>
    Fill,

    /// <summary>
    /// The image is scaled, its ratio kept, as small as it covers the box, and the part outside the box is cut off. Drawn smaller
    /// than the image, the cut part's edge pixels take a little of the pixels outside it: the smaller mipmaps they are read from hold
    /// means over both (IM1's Grill, Q21).
    /// </summary>
    Cover,

    /// <summary>As <see cref="None"/> when the image fits the box, and as <see cref="Contain"/> when it does not.</summary>
    ScaleDown,

    /// <summary>The image at its own size, centered, and the part outside the box cut off.</summary>
    None,
}
