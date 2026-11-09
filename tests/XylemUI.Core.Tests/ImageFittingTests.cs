using System.Numerics;

namespace XylemUI.Tests;

// Where an image is drawn in its box (design §5.0.4, "Display"): CSS's object-fit with object-position 50 % 50 % (CSS Images 3,
// §5.5), and the part of the image drawn, for an image larger than the box and one smaller.
public sealed class ImageFittingTests
{
    /// <summary>
    /// Each fit draws the rectangle and the part CSS's object-fit gives: for a 200 x 100 image in a 100 x 100 box at (10, 20), and
    /// for a 50 x 20 image in the same box.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The box (10, 20, 100, 100).
    ///
    /// Steps:
    /// - Place each image with each fit.
    ///
    /// Expected:
    /// - 200 x 100: Contain and ScaleDown at half size, (10, 45, 100, 50) of the whole; Fill the box of the whole; Cover and None the
    ///   box of the middle (50, 0, 100, 100).
    /// - 50 x 20: Contain at twice, (10, 50, 100, 40) of the whole; Cover at five times, the box of (15, 0, 20, 20); Fill the box of
    ///   the whole; None and ScaleDown at its size, centered: (35, 60, 50, 20) of the whole.
    /// </remarks>
    /// <param name="width">The image's width.</param>
    /// <param name="height">The image's height.</param>
    /// <param name="fit">The fit.</param>
    /// <param name="drawn">The rectangle drawn, as left, top, width, and height.</param>
    /// <param name="part">The part drawn, as left, top, width, and height.</param>
    [Theory]
    [InlineData(200, 100, ImageFit.Contain, new float[] { 10, 45, 100, 50 }, new float[] { 0, 0, 200, 100 })]
    [InlineData(200, 100, ImageFit.ScaleDown, new float[] { 10, 45, 100, 50 }, new float[] { 0, 0, 200, 100 })]
    [InlineData(200, 100, ImageFit.Fill, new float[] { 10, 20, 100, 100 }, new float[] { 0, 0, 200, 100 })]
    [InlineData(200, 100, ImageFit.Cover, new float[] { 10, 20, 100, 100 }, new float[] { 50, 0, 100, 100 })]
    [InlineData(200, 100, ImageFit.None, new float[] { 10, 20, 100, 100 }, new float[] { 50, 0, 100, 100 })]
    [InlineData(50, 20, ImageFit.Contain, new float[] { 10, 50, 100, 40 }, new float[] { 0, 0, 50, 20 })]
    [InlineData(50, 20, ImageFit.Cover, new float[] { 10, 20, 100, 100 }, new float[] { 15, 0, 20, 20 })]
    [InlineData(50, 20, ImageFit.Fill, new float[] { 10, 20, 100, 100 }, new float[] { 0, 0, 50, 20 })]
    [InlineData(50, 20, ImageFit.None, new float[] { 35, 60, 50, 20 }, new float[] { 0, 0, 50, 20 })]
    [InlineData(50, 20, ImageFit.ScaleDown, new float[] { 35, 60, 50, 20 }, new float[] { 0, 0, 50, 20 })]
    public void Place_EachFit_IsCssObjectFit(int width, int height, ImageFit fit, float[] drawn, float[] part)
    {
        Vector4 placed = ImageFitting.Place(new Vector4(10, 20, 100, 100), width, height, fit, out Vector4 destination);

        Assert.Equal(new Vector4(drawn[0], drawn[1], drawn[2], drawn[3]), destination);
        Assert.Equal(new Vector4(part[0], part[1], part[2], part[3]), placed);
    }
}
