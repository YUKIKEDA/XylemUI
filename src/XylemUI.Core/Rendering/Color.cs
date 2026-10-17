namespace XylemUI;

/// <summary>
/// A color with 8-bit red, green, blue, and alpha channels, in sRGB, not premultiplied.
/// </summary>
/// <param name="R">The red channel.</param>
/// <param name="G">The green channel.</param>
/// <param name="B">The blue channel.</param>
/// <param name="A">The alpha channel; 255 is opaque.</param>
public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>
    /// Gets the channels packed into one value, red in the lowest byte (the RGBA8 layout a GPU reads as <c>R8G8B8A8</c>).
    /// </summary>
    public uint Packed => R | ((uint)G << 8) | ((uint)B << 16) | ((uint)A << 24);

    /// <summary>
    /// Returns the opaque color of a 24-bit <c>0xRRGGBB</c> value.
    /// </summary>
    /// <param name="rgb">The red, green, and blue channels, red in the highest byte.</param>
    /// <returns>The color.</returns>
    public static Color FromRgb(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
