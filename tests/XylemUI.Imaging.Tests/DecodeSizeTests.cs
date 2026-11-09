namespace XylemUI.Imaging.Tests;

// Decoding to a size (design §5.0.4, "Decoding size", IM1's Grill, Q16 to Q19): the size taken from the one or two sides asked for,
// the JPEG's DCT scale, and the shrink by the mean over each pixel's area.
public sealed class DecodeSizeTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Images");

    /// <summary>
    /// The size is Flutter's: one side gives the other by the image's ratio (the height the floor, the width rounded), both are
    /// taken as they are, a side past the image's is the image's, and a size past the longest side keeps its ratio.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An image of 200 x 150 (and 9,000 x 100 for the longest side), with a longest side of 8,192 unless the case says.
    ///
    /// Steps:
    /// - Ask for the sizes of the cases.
    ///
    /// Expected:
    /// - Each gives the size of the case.
    /// </remarks>
    /// <param name="width">The image's width.</param>
    /// <param name="height">The image's height.</param>
    /// <param name="targetWidth">The width asked for; 0 for none.</param>
    /// <param name="targetHeight">The height asked for; 0 for none.</param>
    /// <param name="maxSide">The longest side.</param>
    /// <param name="expectedWidth">The width given.</param>
    /// <param name="expectedHeight">The height given.</param>
    [Theory]
    [InlineData(200, 150, 0, 0, 8192, 200, 150)]
    [InlineData(200, 150, 100, 0, 8192, 100, 75)]
    [InlineData(200, 150, 0, 100, 8192, 133, 100)]
    [InlineData(200, 150, 101, 0, 8192, 101, 75)]
    [InlineData(200, 150, 50, 50, 8192, 50, 50)]
    [InlineData(200, 150, 400, 0, 8192, 200, 150)]
    [InlineData(200, 150, 400, 100, 8192, 200, 100)]
    [InlineData(9000, 100, 0, 0, 8192, 8192, 91)]
    [InlineData(200, 150, 0, 0, 100, 100, 75)]
    public void TargetSize_Cases_AreFluttersWithTheLongestSide(
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        int maxSide,
        int expectedWidth,
        int expectedHeight
    ) => Assert.Equal((expectedWidth, expectedHeight), ImageDecoder.TargetSize(width, height, targetWidth, targetHeight, maxSide));

    /// <summary>
    /// The shrink is the mean over each target pixel's area: whole squares when the ratio is whole, each source pixel weighted by the
    /// part covered when it is not, and a flat image stays flat.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 4 x 2 image whose pixels' first channel is 0, 40, 80, 120 in the first row and 160, 200, 240, 255 in the second, the others
    ///   0; and a 3 x 3 image of one color (10, 20, 30, 255).
    ///
    /// Steps:
    /// - Shrink the first to 2 x 1, and to 3 x 1; shrink the second to 2 x 2.
    ///
    /// Expected:
    /// - 2 x 1: (0 + 40 + 160 + 200) / 4 = 100 and (80 + 120 + 240 + 255) / 4 = 173.75, rounded to 174.
    /// - 3 x 1: each third covers a source pixel and a third of the next: (0 + 40/3 + 160 + 200/3) / (8/3) = 90, (2/3·40 + 2/3·80 +
    ///   2/3·200 + 2/3·240) / (8/3) = 140, (80/3 + 120 + 240/3 + 255) / (8/3) = 180.6, rounded to 181.
    /// - The flat image is (10, 20, 30, 255) everywhere.
    /// </remarks>
    [Fact]
    public void Shrink_IsTheMeanOverEachPixelsArea()
    {
        byte[] source = new byte[4 * 2 * 4];
        byte[] firsts = [0, 40, 80, 120, 160, 200, 240, 255];
        for (int i = 0; i < 8; i++)
        {
            source[i * 4] = firsts[i];
        }

        byte[] half = new byte[2 * 4];
        BoxResize.Shrink(source, 4, 2, half, 2, 1);
        Assert.Equal((byte)100, half[0]);
        Assert.Equal((byte)174, half[4]);

        byte[] thirds = new byte[3 * 4];
        BoxResize.Shrink(source, 4, 2, thirds, 3, 1);
        Assert.Equal([90, 140, 181], new[] { thirds[0], thirds[4], thirds[8] });

        byte[] flat = new byte[3 * 3 * 4];
        for (int i = 0; i < flat.Length; i += 4)
        {
            (flat[i], flat[i + 1], flat[i + 2], flat[i + 3]) = ((byte)10, (byte)20, (byte)30, (byte)255);
        }

        byte[] shrunk = new byte[2 * 2 * 4];
        BoxResize.Shrink(flat, 3, 3, shrunk, 2, 2);
        for (int i = 0; i < shrunk.Length; i += 4)
        {
            Assert.Equal((10, 20, 30, 255), (shrunk[i], shrunk[i + 1], shrunk[i + 2], shrunk[i + 3]));
        }
    }

    /// <summary>
    /// A JPEG asked for a quarter of its width is decoded at libjpeg-turbo's 2/8 and is that size, its colors those of the whole
    /// image's mean; asked for a size its scales do not hit, it is decoded at the smallest scale not below it and shrunk.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - baseline.jpg, 64 x 48.
    ///
    /// Steps:
    /// - Decode it whole; to the width 16; to the width 20; to the width 1,000.
    ///
    /// Expected:
    /// - 16: 16 x 12 at 2 eighths; each channel's mean within 2 of the whole image's.
    /// - 20: 20 x 15 at 3 eighths (24 x 18, the smallest not below), shrunk.
    /// - 1,000: 64 x 48 at 8 eighths (no enlarging).
    /// </remarks>
    [Fact]
    public void TryDecodeTo_Jpeg_TakesTheSmallestScaleNotBelow()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(Folder, "baseline.jpg"));
        var decoder = new ImageDecoder();
        Assert.True(decoder.TryDecodeTo(data, 0, 0, 8192, out byte[]? whole, out int width, out int height));
        Assert.Equal((64, 48), (width, height));

        Assert.True(decoder.TryDecodeTo(data, 16, 0, 8192, out byte[]? quarter, out width, out height));
        Assert.Equal((16, 12, 2), (width, height, decoder.LastEighths));
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(Math.Abs(Mean(whole!, channel) - Mean(quarter!, channel)), 0, 2);
        }

        Assert.True(decoder.TryDecodeTo(data, 20, 0, 8192, out _, out width, out height));
        Assert.Equal((20, 15, 3), (width, height, decoder.LastEighths));

        Assert.True(decoder.TryDecodeTo(data, 1000, 0, 8192, out _, out width, out height));
        Assert.Equal((64, 48, 8), (width, height, decoder.LastEighths));
    }

    /// <summary>
    /// The size asked for is of the image as shown: a JPEG whose orientation swaps its sides is scaled by its stored sides.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - exif-6.jpg: stored 16 x 8, shown 8 x 16.
    ///
    /// Steps:
    /// - Decode it to the width 4.
    ///
    /// Expected:
    /// - 4 x 8, decoded at 4 eighths (stored 8 x 4).
    /// </remarks>
    [Fact]
    public void TryDecodeTo_TurnedJpeg_SizeIsOfTheShownImage()
    {
        var decoder = new ImageDecoder();

        Assert.True(decoder.TryDecodeTo(File.ReadAllBytes(Path.Combine(Folder, "exif-6.jpg")), 4, 0, 8192, out _, out int width, out int height));

        Assert.Equal((4, 8, 4), (width, height, decoder.LastEighths));
    }

    /// <summary>
    /// Formats with no scale of their own (PNG, ICO) are decoded whole and shrunk, and an image past the longest side is shrunk to
    /// it, its ratio kept.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - PngSuite's basn2c08.png (32 x 32) and png.ico (entries up to 48 x 48).
    ///
    /// Steps:
    /// - Decode the PNG to the height 8; with a longest side of 16; the ICO to the width 16.
    ///
    /// Expected:
    /// - 8 x 8, 16 x 16, and 16 x 16, each at 8 eighths.
    /// </remarks>
    [Fact]
    public void TryDecodeTo_OtherFormats_AreShrunk()
    {
        var decoder = new ImageDecoder();
        byte[] png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Png", "PngSuite", "basn2c08.png"));

        Assert.True(decoder.TryDecodeTo(png, 0, 8, 8192, out _, out int width, out int height));
        Assert.Equal((8, 8, 8), (width, height, decoder.LastEighths));
        Assert.True(decoder.TryDecodeTo(png, 0, 0, 16, out _, out width, out height));
        Assert.Equal((16, 16), (width, height));
        Assert.True(decoder.TryDecodeTo(File.ReadAllBytes(Path.Combine(Folder, "png.ico")), 16, 0, 8192, out _, out width, out height));
        Assert.Equal((16, 16), (width, height));
    }

    /// <summary>
    /// Bytes that are no image, and the shim asked for a scale of a format that has none, are refused without throwing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Four bytes that are no image.
    ///
    /// Steps:
    /// - Decode them to the width 4.
    ///
    /// Expected:
    /// - Not read; no pixels; size 0 x 0.
    /// </remarks>
    [Fact]
    public void TryDecodeTo_NoImage_IsRefused()
    {
        var decoder = new ImageDecoder();

        Assert.False(decoder.TryDecodeTo([1, 2, 3, 4], 4, 0, 8192, out byte[]? pixels, out int width, out int height));

        Assert.Null(pixels);
        Assert.Equal((0, 0), (width, height));
    }

    private static double Mean(byte[] pixels, int channel)
    {
        long sum = 0;
        for (int i = channel; i < pixels.Length; i += 4)
        {
            sum += pixels[i];
        }

        return (double)sum / (pixels.Length / 4);
    }
}
