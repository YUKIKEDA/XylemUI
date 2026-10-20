namespace XylemUI.Text;

/// <summary>
/// Places a glyph across the line: on a quarter of a pixel, as a whole pixel and a subpixel step (design §5.0.3, the same four
/// steps as GPUI's <c>SUBPIXEL_VARIANTS_X</c> and Impeller's).
/// </summary>
public static class GlyphPosition
{
    /// <summary>
    /// The number of horizontal subpixel steps in a pixel.
    /// </summary>
    public const int SubpixelSteps = 4;

    /// <summary>
    /// Rounds a horizontal position to the nearest quarter of a pixel.
    /// </summary>
    /// <param name="x">The position, in physical pixels.</param>
    /// <param name="subpixelX">The quarters right of the returned pixel: 0 to <see cref="SubpixelSteps"/> − 1.</param>
    /// <returns>The whole pixel at or left of the rounded position.</returns>
    public static int Snap(float x, out int subpixelX)
    {
        int quarters = (int)MathF.Round(x * SubpixelSteps);

        // An arithmetic shift and a mask floor a negative position too: -0.25 is pixel -1 and step 3.
        subpixelX = quarters & (SubpixelSteps - 1);
        return quarters >> 2;
    }
}
