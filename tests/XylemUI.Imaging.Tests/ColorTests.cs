using System.Text.Json;

namespace XylemUI.Imaging.Tests;

// Embedded color profiles converted to sRGB with skcms (design §5.0.4, "EXIF orientation and color", IM1's Grill, Q23 to Q30): the
// images of make_color.py against the pixels LittleCMS converts them to (Images/README.md), the images that convert nothing, and
// the joining of a JPEG's ICC chunks.
public sealed class ColorTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Images");

    /// <summary>
    /// Each image with a profile reads to the pixels LittleCMS converts it to sRGB with the relative colorimetric intent, each
    /// channel within 2 (skcms and LittleCMS compute with other precisions): RGB profiles in JPEG, PNG with alpha, and WebP; a gray
    /// profile on a gray JPEG; a CMYK profile on a CMYK JPEG; and a PNG's cHRM and gAMA.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The images of Images/expected-color.json and their .bgra files (make_color.py, Pillow 12.1.0, LittleCMS 2.17).
    ///
    /// Steps:
    /// - Read each with TryDecodeTo at its own size.
    ///
    /// Expected:
    /// - Each reads at its size, and each byte is within 2 of the expected one.
    /// </remarks>
    /// <param name="name">The image.</param>
    [Theory]
    [InlineData("p3.jpg")]
    [InlineData("adobe.jpg")]
    [InlineData("p3-alpha.png")]
    [InlineData("p3.webp")]
    [InlineData("gray-icc.jpg")]
    [InlineData("cmyk-icc.jpg")]
    [InlineData("chrm-gama.png")]
    public void TryDecodeTo_ImageWithAProfile_IsConvertedToSrgb(string name)
    {
        var decoder = new ImageDecoder();

        Assert.True(
            decoder.TryDecodeTo(File.ReadAllBytes(Path.Combine(Folder, name)), 0, 0, 8192, out byte[]? pixels, out int width, out int height)
        );

        JsonElement size = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "expected-color.json"))).RootElement.GetProperty(name);
        Assert.Equal((size.GetProperty("width").GetInt32(), size.GetProperty("height").GetInt32()), (width, height));
        byte[] expected = File.ReadAllBytes(Path.Combine(Folder, name + ".bgra"));
        int largest = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(expected[i] - pixels![i]));
        }

        Assert.InRange(largest, 0, 2);
    }

    /// <summary>
    /// An image whose profile converts nothing reads to the pixels of the same image read with no profile: sRGB's own profile (close
    /// to sRGB, Q29), bytes that are no profile, and a profile of the wrong kind (gray on an RGB JPEG, Q26).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - srgb-icc.jpg, broken-icc.jpg, and wrong-kind-icc.jpg (make_color.py): the same drawing with each profile.
    ///
    /// Steps:
    /// - Read each with TryDecodeTo, and with TryDecode, which reads no color information.
    ///
    /// Expected:
    /// - The two reads give the same bytes.
    /// </remarks>
    /// <param name="name">The image.</param>
    [Theory]
    [InlineData("srgb-icc.jpg")]
    [InlineData("broken-icc.jpg")]
    [InlineData("wrong-kind-icc.jpg")]
    public void TryDecodeTo_ProfileThatConvertsNothing_ReadsAsWithNone(string name)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(Folder, name));
        var decoder = new ImageDecoder();
        Assert.True(decoder.TryDecodeTo(data, 0, 0, 8192, out byte[]? colored, out _, out _));

        Assert.True(decoder.TryDecode(data, 8192, out _, out _));

        Assert.Equal(decoder.Pixels.ToArray(), colored);
    }

    /// <summary>
    /// A JPEG's ICC chunks are joined in the order of their numbers, whatever the order of their segments; chunks that do not agree
    /// on their count, a number twice, or a number missing give no profile.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - JPEG headers of APP2 segments only: chunks "AB" (2 of 2) then "CD" (1 of 2); chunk 1 of 2 then 2 of 3; chunk 1 of 2 twice;
    ///   chunk 1 of 2 alone.
    ///
    /// Steps:
    /// - Read the color information of each.
    ///
    /// Expected:
    /// - The first gives the profile "CDAB" (4 bytes, with zeros past it); the others give none.
    /// </remarks>
    [Fact]
    public void Read_JpegChunks_AreJoinedInOrderOrRefused()
    {
        ColorInfo joined = ColorReader.Read(Jpeg(Chunk(2, 2, "AB"), Chunk(1, 2, "CD")), ImageFormat.Jpeg);
        Assert.Equal(4, joined.IccLength);
        Assert.Equal("CDAB"u8.ToArray(), joined.Icc![..4]);
        Assert.All(joined.Icc[4..], b => Assert.Equal(0, b));

        Assert.True(ColorReader.Read(Jpeg(Chunk(1, 2, "AB"), Chunk(2, 3, "CD")), ImageFormat.Jpeg).IsNone);
        Assert.True(ColorReader.Read(Jpeg(Chunk(1, 2, "AB"), Chunk(1, 2, "CD")), ImageFormat.Jpeg).IsNone);
        Assert.True(ColorReader.Read(Jpeg(Chunk(1, 2, "AB")), ImageFormat.Jpeg).IsNone);
    }

    /// <summary>
    /// A PNG's iCCP comes before its sRGB chunk, and its sRGB chunk before its cHRM and gAMA (Q24).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - p3-alpha.png (an iCCP); chrm-gama.png (cHRM and gAMA).
    ///
    /// Steps:
    /// - Read the color information of each.
    ///
    /// Expected:
    /// - p3-alpha.png: a profile, no chromaticities. chrm-gama.png: no profile, Adobe RGB's primaries, and the gamma 0.45455.
    /// </remarks>
    [Fact]
    public void Read_Png_TakesTheIccpOrTheChromaticities()
    {
        ColorInfo iccp = ColorReader.Read(File.ReadAllBytes(Path.Combine(Folder, "p3-alpha.png")), ImageFormat.Png);
        Assert.True(iccp.IccLength > 128);
        Assert.Null(iccp.Chromaticities);

        ColorInfo chunks = ColorReader.Read(File.ReadAllBytes(Path.Combine(Folder, "chrm-gama.png")), ImageFormat.Png);
        Assert.Null(chunks.Icc);
        Assert.Equal([0.3127f, 0.329f, 0.64f, 0.33f, 0.21f, 0.71f, 0.15f, 0.06f], chunks.Chromaticities!);
        Assert.Equal(0.45455f, chunks.Gamma);
    }

    // A JPEG's start and the given segments.
    private static byte[] Jpeg(params byte[][] segments) => [0xFF, 0xD8, .. segments.SelectMany(s => s), 0xFF, 0xDA];

    // An APP2 segment with an ICC chunk.
    private static byte[] Chunk(int number, int count, string data)
    {
        byte[] payload = [.. "ICC_PROFILE\0"u8, (byte)number, (byte)count, .. System.Text.Encoding.ASCII.GetBytes(data)];
        int length = payload.Length + 2;
        return [0xFF, 0xE2, (byte)(length >> 8), (byte)length, .. payload];
    }
}
