namespace XylemUI.Imaging;

/// <summary>
/// The formats the decoders read (design §5.0.4, "Formats"); the values of the first five are the shim's.
/// </summary>
internal enum ImageFormat
{
    /// <summary>No format the decoders read.</summary>
    None = 0,

    /// <summary>PNG, read by Wuffs.</summary>
    Png = 1,

    /// <summary>JPEG, read by libjpeg-turbo.</summary>
    Jpeg = 2,

    /// <summary>WebP, read by libwebp.</summary>
    WebP = 3,

    /// <summary>GIF, read by Wuffs (its first frame).</summary>
    Gif = 4,

    /// <summary>BMP, read by Wuffs.</summary>
    Bmp = 5,

    /// <summary>ICO, whose container <see cref="Ico"/> reads; the image in it is a PNG or a BMP.</summary>
    Ico = 6,
}
