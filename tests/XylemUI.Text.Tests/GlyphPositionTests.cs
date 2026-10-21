namespace XylemUI.Text.Tests;

// Placing a glyph on a quarter of a pixel (T4, design §5.0.3).
public sealed class GlyphPositionTests
{
    /// <summary>
    /// A position rounds to the nearest quarter of a pixel, halfway toward 0 as GPUI rounds, split into the whole pixel at or left
    /// of it and the quarters right of that pixel; a negative position floors too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Snap 10.25, 40.714, 2.9, 3.1, 0, −0.25, −1.6, and the halfway positions 0.375, 0.625, and −0.375.
    ///
    /// Expected:
    /// - (10, 1), (40, 3), (3, 0), (3, 0), (0, 0), (−1, 3), (−2, 2): 2.9 and 3.1 round to 3.0; −1.6 rounds to −1.5, which is pixel −2
    ///   and two quarters.
    /// - (0, 1), (0, 2), (−1, 3): 0.375 goes to 0.25 and 0.625 to 0.5 (toward 0, not to the even quarter), −0.375 to −0.25.
    /// - Every position is within ⅛ of a pixel of pixel + step / 4.
    /// </remarks>
    [Fact]
    public void Snap_RoundsToAQuarterAndSplitsIt()
    {
        (float X, int Pixel, int Step)[] cases =
        [
            (10.25f, 10, 1),
            (40.714f, 40, 3),
            (2.9f, 3, 0),
            (3.1f, 3, 0),
            (0f, 0, 0),
            (-0.25f, -1, 3),
            (-1.6f, -2, 2),
            (0.375f, 0, 1),
            (0.625f, 0, 2),
            (-0.375f, -1, 3),
        ];

        foreach ((float x, int pixel, int step) in cases)
        {
            int snapped = GlyphPosition.Snap(x, out int subpixel);
            Assert.Equal((pixel, step), (snapped, subpixel));
            Assert.InRange(x - (snapped + (subpixel / 4f)), -0.125f, 0.125f);
        }
    }
}
