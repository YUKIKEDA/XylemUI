namespace XylemUI;

/// <summary>
/// The color space a gradient interpolates in, with the colors premultiplied by their alpha (design §5.0, "Shapes, brushes, and
/// shadows").
/// </summary>
public enum GradientColorSpace : byte
{
    /// <summary>The sRGB values as they are, as CSS interpolates by default.</summary>
    Srgb,

    /// <summary>Oklab, where the steps look even to the eye; GPUI offers it.</summary>
    Oklab,
}
