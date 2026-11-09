using System.Numerics;

namespace XylemUI;

/// <summary>
/// The data of an <see cref="CommandTag.Image"/> command past its rectangle and its color: the image, and the part of it drawn.
/// </summary>
internal struct ImageRecord
{
    /// <summary>
    /// The image.
    /// </summary>
    public ImageHandle Image;

    /// <summary>
    /// The part of the image drawn, in the image's pixels: left, top, width, and height.
    /// </summary>
    public Vector4 Source;
}
