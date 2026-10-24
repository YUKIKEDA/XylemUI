using System.Numerics;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// The colors of a gradient's stops as the shader interpolates them: premultiplied by their alpha, in sRGB as it is or in Oklab
/// (design §5.0, "Shapes, brushes, and shadows"; Oklab is Björn Ottosson's, https://bottosson.github.io/posts/oklab/, the
/// matrices of its reference code).
/// </summary>
internal static class GradientColors
{
    /// <summary>
    /// Returns a stop's color premultiplied, as sRGB values or as Oklab's L, a, and b.
    /// </summary>
    /// <param name="color">The color, sRGB, not premultiplied.</param>
    /// <param name="oklab">Whether to return Oklab.</param>
    /// <returns>The premultiplied channels and the alpha.</returns>
    public static Vector4 Premultiplied(Color color, bool oklab)
    {
        float alpha = color.A / 255f;
        var srgb = new Vector3(color.R, color.G, color.B) / 255f;
        Vector3 channels = oklab ? Oklab(new Vector3(ToLinear(srgb.X), ToLinear(srgb.Y), ToLinear(srgb.Z))) : srgb;
        return new Vector4(channels * alpha, alpha);
    }

    /// <summary>
    /// Returns the Oklab of a linear sRGB color.
    /// </summary>
    /// <param name="linear">The linear red, green, and blue.</param>
    /// <returns>L, a, and b.</returns>
    public static Vector3 Oklab(Vector3 linear)
    {
        float l = MathF.Cbrt((0.4122214708f * linear.X) + (0.5363325363f * linear.Y) + (0.0514459929f * linear.Z));
        float m = MathF.Cbrt((0.2119034982f * linear.X) + (0.6806995451f * linear.Y) + (0.1073969566f * linear.Z));
        float s = MathF.Cbrt((0.0883024619f * linear.X) + (0.2817188376f * linear.Y) + (0.6299787005f * linear.Z));
        return new Vector3(
            (0.2104542553f * l) + (0.7936177850f * m) - (0.0040720468f * s),
            (1.9779984951f * l) - (2.4285922050f * m) + (0.4505937099f * s),
            (0.0259040371f * l) + (0.7827717662f * m) - (0.8086757660f * s)
        );
    }

    /// <summary>
    /// Returns the linear value of an sRGB channel.
    /// </summary>
    /// <param name="channel">The channel, 0 to 1.</param>
    /// <returns>The linear value.</returns>
    public static float ToLinear(float channel) => channel <= 0.04045f ? channel / 12.92f : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
}
