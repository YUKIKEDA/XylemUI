namespace XylemUI.Text.Tests;

// How a bitmap color glyph is scaled to the size it is drawn at (design §5.0.3, "PNG bitmaps", roadmap row T9): down by the
// average over the area each pixel covers, up by bilinear interpolation, premultiplied throughout.
public sealed class BitmapScalerTests
{
    /// <summary>
    /// The same size copies the image; a uniform color stays uniform scaled down and up.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scaler; a 3 x 2 image of distinct bytes; a 109 x 109 image of one premultiplied color (60, 30, 90, 200).
    ///
    /// Steps:
    /// - Scale the first to 3 x 2; the second to 20 x 20 and to 150 x 150.
    ///
    /// Expected:
    /// - The first is its bytes; every pixel of the others is (60, 30, 90, 200).
    /// </remarks>
    [Fact]
    public void Scale_SameSizeOrUniform_KeepsTheImage()
    {
        var scaler = new BitmapScaler();
        byte[] distinct = [.. Enumerable.Range(1, 24).Select(i => (byte)(i * 10))];
        byte[] copy = new byte[24];
        byte[] uniform = Fill(109, 109, 60, 30, 90, 200);

        scaler.Scale(distinct, 3, 2, copy, 3, 2, 12);
        byte[] down = Scale(scaler, uniform, 109, 109, 20, 20);
        byte[] up = Scale(scaler, uniform, 109, 109, 150, 150);

        Assert.Equal(distinct, copy);
        Assert.All(Pixels(down), pixel => Assert.Equal((60, 30, 90, 200), pixel));
        Assert.All(Pixels(up), pixel => Assert.Equal((60, 30, 90, 200), pixel));
    }

    /// <summary>
    /// Scaling down averages the area each destination pixel covers: by 2, each pixel is the mean of its 2 x 2 block; by a ratio
    /// that is not whole, the sum of the image is the source's sum times the ratio of the areas, within the rounding.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scaler; a 4 x 2 image whose blue channel is 0, 40, 80, 120 in the first row and 160, 200, 240, 255 in the second (the
    ///   other channels 0, alpha 255); a 109 x 109 image of bytes that vary with the position.
    ///
    /// Steps:
    /// - Scale the first to 2 x 1; the second to 20 x 20.
    ///
    /// Expected:
    /// - The blues are (0 + 40 + 160 + 200) / 4 = 100 and (80 + 120 + 240 + 255) / 4 = 173.75, rounded to 174; alpha 255.
    /// - For each channel, the sum of the 20 x 20 image is the 109 x 109 sum × 400 / 11,881, within 0.5 a pixel (the rounding).
    /// </remarks>
    [Fact]
    public void Scale_Down_AveragesTheCoveredArea()
    {
        var scaler = new BitmapScaler();
        byte[] block = new byte[4 * 2 * 4];
        byte[] blues = [0, 40, 80, 120, 160, 200, 240, 255];
        for (int i = 0; i < 8; i++)
        {
            block[i * 4] = blues[i];
            block[(i * 4) + 3] = 255;
        }

        byte[] varied = new byte[109 * 109 * 4];
        for (int i = 0; i < varied.Length; i++)
        {
            varied[i] = (byte)((i * 37) % 251);
        }

        byte[] half = Scale(scaler, block, 4, 2, 2, 1);
        byte[] small = Scale(scaler, varied, 109, 109, 20, 20);

        Assert.Equal([100, 0, 0, 255, 174, 0, 0, 255], half);
        for (int channel = 0; channel < 4; channel++)
        {
            double source = Sum(varied, channel) * 400.0 / (109 * 109);
            Assert.InRange(Sum(small, channel), source - (0.5 * 400), source + (0.5 * 400));
        }
    }

    /// <summary>
    /// Scaling up interpolates between the centers of the source pixels, holding the edge pixels beyond the edge.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scaler and a 2 x 1 image: blue 0 then 255, alpha 255.
    ///
    /// Steps:
    /// - Scale it to 4 x 1.
    ///
    /// Expected:
    /// - The centers of the four pixels are at source x −0.25, 0.25, 0.75, and 1.25, so the blues are 0, 0.25 × 255 = 63.75 (64),
    ///   0.75 × 255 = 191.25 (191), and 255.
    /// </remarks>
    [Fact]
    public void Scale_Up_IsBilinear()
    {
        var scaler = new BitmapScaler();

        byte[] wide = Scale(scaler, [0, 0, 0, 255, 255, 0, 0, 255], 2, 1, 4, 1);

        Assert.Equal([0, 64, 191, 255], Pixels(wide).Select(pixel => pixel.Blue));
    }

    private static byte[] Scale(BitmapScaler scaler, byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        byte[] destination = new byte[width * height * 4];
        scaler.Scale(source, sourceWidth, sourceHeight, destination, width, height, width * 4);
        return destination;
    }

    private static byte[] Fill(int width, int height, byte blue, byte green, byte red, byte alpha)
    {
        byte[] image = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            (image[i * 4], image[(i * 4) + 1], image[(i * 4) + 2], image[(i * 4) + 3]) = (blue, green, red, alpha);
        }

        return image;
    }

    private static IEnumerable<(int Blue, int Green, int Red, int Alpha)> Pixels(byte[] image)
    {
        for (int i = 0; i < image.Length; i += 4)
        {
            yield return (image[i], image[i + 1], image[i + 2], image[i + 3]);
        }
    }

    private static long Sum(byte[] image, int channel)
    {
        long sum = 0;
        for (int i = channel; i < image.Length; i += 4)
        {
            sum += image[i];
        }

        return sum;
    }
}
