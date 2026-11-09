using System.Text.Json;

namespace XylemUI.Imaging.Tests;

// The image decoders (design §5.0.4, "Decoders", row IM1) on small images of each format, against the pixels Pillow reads from the
// same files (Images/README.md, Images/make.py): JPEG with libjpeg-turbo, WebP with libwebp, and GIF, BMP, and ICO apart from the
// shim. PNG is tested against WIC on PngSuite (ImageDecoderWicTests).
public sealed class ImageDecoderTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Images");

    /// <summary>
    /// Each format reads to Pillow's size and premultiplied pixels: exactly where no alpha is premultiplied (JPEG, opaque WebP,
    /// GIF, BMP), and within 1 where an alpha is (WebP and ICO with alpha, whose premultiplication rounds its own way).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The images of Images/expected.json: baseline, progressive, and gray JPEG; lossy, lossless, and alpha WebP; the first frame
    ///   of a GIF; 24-bit and 8-bit BMP; an ICO of PNG entries and one of 32-bit bitmap entries.
    ///
    /// Steps:
    /// - Read each with sides up to 4,096 (for an ICO, its largest entry).
    ///
    /// Expected:
    /// - Each reads, with Pillow's size, its format, and Pillow's bytes within the tolerance above.
    /// </remarks>
    [Theory]
    [InlineData("baseline.jpg", nameof(ImageFormat.Jpeg), 0)]
    [InlineData("progressive.jpg", nameof(ImageFormat.Jpeg), 0)]
    [InlineData("gray.jpg", nameof(ImageFormat.Jpeg), 0)]
    [InlineData("lossy.webp", nameof(ImageFormat.WebP), 0)]
    [InlineData("lossless.webp", nameof(ImageFormat.WebP), 1)]
    [InlineData("alpha.webp", nameof(ImageFormat.WebP), 1)]
    [InlineData("first-frame.gif", nameof(ImageFormat.Gif), 0)]
    [InlineData("rgb24.bmp", nameof(ImageFormat.Bmp), 0)]
    [InlineData("palette8.bmp", nameof(ImageFormat.Bmp), 0)]
    [InlineData("png.ico", nameof(ImageFormat.Ico), 1)]
    [InlineData("bmp.ico", nameof(ImageFormat.Ico), 1)]
    public void TryDecode_EachFormat_ReadsWhatPillowReads(string name, string format, int tolerance)
    {
        var decoder = new ImageDecoder();

        Assert.True(decoder.TryDecode(File.ReadAllBytes(Path.Combine(Folder, name)), 4096, out int width, out int height));

        Assert.Equal(Expected(name), (width, height));
        Assert.Equal(format, decoder.Format.ToString());
        Assert.InRange(Largest(decoder.Pixels, File.ReadAllBytes(Path.Combine(Folder, name + ".bgra"))), 0, tolerance);
    }

    /// <summary>
    /// A JPEG is turned by its EXIF orientation, each of the 8, as Pillow's <c>exif_transpose</c> turns it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 16 × 8 picture of 8 colored blocks, saved as a JPEG with each orientation (1 to 8), at quality 100 without subsampling.
    ///
    /// Steps:
    /// - Read each.
    ///
    /// Expected:
    /// - Orientations 1 to 4 read 16 × 8, and 5 to 8 read 8 × 16; each image's bytes are Pillow's turned ones.
    /// </remarks>
    /// <param name="orientation">The orientation.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void TryDecode_ExifOrientation_TurnsTheImage(int orientation)
    {
        string name = $"exif-{orientation}.jpg";
        var decoder = new ImageDecoder();

        Assert.True(decoder.TryDecode(File.ReadAllBytes(Path.Combine(Folder, name)), 4096, out int width, out int height));

        Assert.Equal(orientation >= 5 ? (8, 16) : (16, 8), (width, height));
        Assert.Equal(0, Largest(decoder.Pixels, File.ReadAllBytes(Path.Combine(Folder, name + ".bgra"))));
    }

    /// <summary>
    /// What is not read is refused without throwing: a 12-bit JPEG (IM1's Grill, Q5), an animated WebP (the animations' PR), a
    /// truncated PNG, bytes of no format, no bytes, and an image larger than the caller's limit. A truncated JPEG is read, its
    /// missing rows filled by libjpeg-turbo, as Skia shows the part of an incomplete JPEG it has.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A JPEG whose frame header says 12-bit samples (<c>precision12.jpg</c>); an animated WebP of two frames; PngSuite's
    ///   <c>basn6a08.png</c> cut to its first half; 64 bytes of text; the baseline JPEG cut to its first half.
    ///
    /// Steps:
    /// - Read each with sides up to 4,096, then the 64 × 48 baseline JPEG with sides up to 32.
    ///
    /// Expected:
    /// - Each returns <see langword="false"/> with a size of 0 × 0, but the cut JPEG, which reads at 64 × 48.
    /// </remarks>
    [Fact]
    public void TryDecode_WhatIsNotRead_IsRefused()
    {
        var decoder = new ImageDecoder();
        byte[] baseline = File.ReadAllBytes(Path.Combine(Folder, "baseline.jpg"));

        Assert.False(decoder.TryDecode(File.ReadAllBytes(Path.Combine(Folder, "precision12.jpg")), 4096, out _, out _));
        Assert.False(decoder.TryDecode(File.ReadAllBytes(Path.Combine(Folder, "animated.webp")), 4096, out _, out _));
        byte[] png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Png", "PngSuite", "basn6a08.png"));
        Assert.False(decoder.TryDecode(png.AsSpan(0, png.Length / 2), 4096, out _, out _));
        Assert.False(decoder.TryDecode("this is no image of any format the decoders read, only text....."u8, 4096, out _, out _));
        Assert.False(decoder.TryDecode([], 4096, out _, out _));
        Assert.False(decoder.TryDecode(baseline, 32, out int width, out int height));
        Assert.Equal((0, 0), (width, height));
        Assert.True(decoder.TryDecode(baseline.AsSpan(0, baseline.Length / 2), 4096, out width, out height));
        Assert.Equal((64, 48), (width, height));
    }

    private static (int Width, int Height) Expected(string name)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "expected.json")));
        JsonElement entry = document.RootElement.GetProperty(name);
        return (entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32());
    }

    private static int Largest(ReadOnlySpan<byte> ours, byte[] expected)
    {
        Assert.Equal(expected.Length, ours.Length);
        int largest = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(ours[i] - expected[i]));
        }

        return largest;
    }
}
