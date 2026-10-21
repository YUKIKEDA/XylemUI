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
    /// Rounds a horizontal position to the nearest quarter of a pixel; a position halfway between two quarters goes to the one
    /// nearer 0, as GPUI's <c>round_half_toward_zero</c> does (Zed v1.24.3, <c>crates/gpui/src/util.rs</c>, lines 130 to 132).
    /// </summary>
    /// <param name="x">The position, in physical pixels.</param>
    /// <param name="subpixelX">The quarters right of the returned pixel: 0 to <see cref="SubpixelSteps"/> − 1.</param>
    /// <returns>The whole pixel at or left of the rounded position.</returns>
    public static int Snap(float x, out int subpixelX)
    {
        float scaled = x * SubpixelSteps;
        int quarters = (int)MathF.CopySign(MathF.Ceiling(MathF.Abs(scaled) - 0.5f), scaled);

        // An arithmetic shift and a mask floor a negative position too: -0.25 is pixel -1 and step 3.
        subpixelX = quarters & (SubpixelSteps - 1);
        return quarters >> 2;
    }

    /// <summary>
    /// Rounds a horizontal position to the nearest whole pixel, halfway to the one nearer 0: where a color glyph goes, which has no
    /// subpixel steps (design §5.0.3, "Color glyphs"; GPUI's <c>paint_emoji</c>, Zed v1.24.3, <c>crates/gpui/src/window.rs</c>,
    /// lines 4850 to 4856).
    /// </summary>
    /// <param name="x">The position, in physical pixels.</param>
    /// <returns>The whole pixel.</returns>
    public static int Round(float x) => (int)MathF.CopySign(MathF.Ceiling(MathF.Abs(x) - 0.5f), x);
}
