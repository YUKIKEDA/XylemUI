namespace XylemUI.Text.Tests;

// The PNG decoder of bitmap color glyphs (design §5.0.3, "PNG bitmaps", roadmap row T9): what it reads, its rounding, and the
// limits it keeps on untrusted input. Every form of the format is checked against WIC on PngSuite
// (tests/XylemUI.Platform.Windows.Tests/PngDecoderWicTests.cs); these tests run on every OS, on small files TestPng writes. They
// run apart from the other tests (HeavyAllocationGroup, Issue #221).
[Collection(HeavyAllocationGroup.Name)]
public sealed class PngDecoderTests
{
    /// <summary>
    /// An 8-bit RGBA image reads to premultiplied blue, green, red, and alpha.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 2 x 1 RGBA image: opaque red, then blue at alpha 128.
    ///
    /// Steps:
    /// - Read it.
    ///
    /// Expected:
    /// - 2 x 1, with the bytes 0, 0, 255, 255 (red) and 128, 0, 0, 128 (blue premultiplied: 255 × 128 / 255 = 128).
    /// </remarks>
    [Fact]
    public void TryDecode_Rgba_IsPremultipliedBgra()
    {
        var decoder = new PngDecoder();

        Assert.True(decoder.TryDecode(TestPng.Valid(), 64, out int width, out int height));

        Assert.Equal((2, 1), (width, height));
        Assert.Equal([0, 0, 255, 255, 128, 0, 0, 128], decoder.Pixels.ToArray());
    }

    /// <summary>
    /// A 16-bit sample is rounded to 8 bits as <c>(v + 128) / 257</c>, not cut to its high byte.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 3 x 1 16-bit gray image with the samples 0x7A80, 0x7AFF, and 0xFFFF.
    ///
    /// Steps:
    /// - Read it.
    ///
    /// Expected:
    /// - The grays (0x7A80 + 128) / 257 = 122, (0x7AFF + 128) / 257 = 123 (its high byte is 0x7A = 122), and 255, opaque.
    /// </remarks>
    [Fact]
    public void TryDecode_SixteenBits_RoundsToTheNearest()
    {
        var decoder = new PngDecoder();
        byte[] png = TestPng.File(TestPng.Header(3, 1, 16, 0), TestPng.Data([0, 0x7A, 0x80, 0x7A, 0xFF, 0xFF, 0xFF]), TestPng.End());

        Assert.True(decoder.TryDecode(png, 64, out _, out _));

        Assert.Equal([122, 122, 122, 255, 123, 123, 123, 255, 255, 255, 255, 255], decoder.Pixels.ToArray());
    }

    /// <summary>
    /// A PNG past a limit of the design, or with a broken structure, is broken: <see cref="PngDecoder.TryDecode"/> returns
    /// <see langword="false"/> and throws nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Small PNG files, each one valid image with one thing broken.
    ///
    /// Steps:
    /// - Read each with a limit of 64 pixels a side.
    ///
    /// Expected:
    /// - Each returns <see langword="false"/> with width and height 0: a wrong signature; a side of 65 and of 0; a chunk longer
    ///   than the data left; image data before the header; a second header; image data chunks with another chunk between them;
    ///   an unknown critical chunk; a wrong check value of a critical chunk; color type 1 and bit depth 3; compression method 1;
    ///   data that inflates to fewer bytes than the header asks for; data that does not inflate; a row with filter type 5; a
    ///   palette image without a palette.
    /// </remarks>
    [Fact]
    public void TryDecode_Broken_ReturnsFalse()
    {
        byte[] header = TestPng.Header(2, 1, 8, 6);
        byte[] data = TestPng.Data(TestPng.Rows());
        byte[] end = TestPng.End();
        byte[] wrongSignature = TestPng.Valid();
        wrongSignature[1] = (byte)'Q';
        byte[] longChunk = TestPng.File(header, data, end);
        longChunk[8 + 25 + 3] = 0xFF; // the image data chunk's length, past the end
        byte[] compressionOne = TestPng.Header(2, 1, 8, 6);
        compressionOne[8 + 10] = 1;
        byte[] rows = TestPng.Rows();
        byte[] compressed = TestPng.Compressed(rows);
        byte[] filterFive = (byte[])rows.Clone();
        filterFive[0] = 5;
        byte[][] files =
        [
            wrongSignature,
            TestPng.File(TestPng.Header(65, 1, 8, 6), data, end),
            TestPng.File(TestPng.Header(0, 1, 8, 6), data, end),
            longChunk,
            TestPng.File(data, header, end),
            TestPng.File(header, header, data, end),
            TestPng.File(
                header,
                TestPng.Chunk("IDAT", compressed.AsSpan(0, 4)),
                TestPng.Chunk("tEXt", "a\0b"u8),
                TestPng.Chunk("IDAT", compressed.AsSpan(4)),
                end
            ),
            TestPng.File(header, TestPng.Chunk("QUUX", [1]), data, end),
            TestPng.File(TestPng.Chunk("IHDR", header.AsSpan(8, 13), wrongCheck: true), data, end),
            TestPng.File(TestPng.Header(2, 1, 8, 1), data, end),
            TestPng.File(TestPng.Header(2, 1, 3, 0), data, end),
            TestPng.File(compressionOne, data, end),
            TestPng.File(header, TestPng.Data(rows.AsSpan(0, 5)), end),
            TestPng.File(header, TestPng.Chunk("IDAT", [1, 2, 3, 4, 5]), end),
            TestPng.File(header, TestPng.Data(filterFive), end),
            TestPng.File(TestPng.Header(2, 1, 8, 3), TestPng.Data([0, 0, 0]), end),
        ];
        var decoder = new PngDecoder();

        for (int i = 0; i < files.Length; i++)
        {
            bool read = decoder.TryDecode(files[i], 64, out int width, out int height);

            Assert.False(read, $"File {i} read.");
            Assert.Equal((0, 0), (width, height));
            Assert.Equal(0, decoder.Pixels.Length);
        }
    }

    /// <summary>
    /// Inflating stops at the bytes the header asks for: compressed data that inflates to far more reads as the image, from the
    /// bytes it needs, without inflating the rest.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The 2 x 1 RGBA image whose data is followed, in the same zlib stream, by 64 MB of zeros (64 KB compressed).
    ///
    /// Steps:
    /// - Read it, measuring the bytes allocated on this thread.
    ///
    /// Expected:
    /// - It reads, with the pixels of the image, and allocates less than 1 MB (inflating the 64 MB would need a buffer of it).
    /// </remarks>
    [Fact]
    public void TryDecode_DataThatInflatesFurther_ReadsOnlyTheImage()
    {
        byte[] compressed = TestPng.CompressedWithZeros(TestPng.Rows(), 64 * 1024 * 1024);
        byte[] png = TestPng.File(TestPng.Header(2, 1, 8, 6), TestPng.Chunk("IDAT", compressed), TestPng.End());
        var decoder = new PngDecoder();
        _ = decoder.TryDecode(TestPng.Valid(), 64, out _, out _);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool read = decoder.TryDecode(png, 64, out int width, out int height);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(read);
        Assert.Equal((2, 1), (width, height));
        Assert.Equal([0, 0, 255, 255, 128, 0, 0, 128], decoder.Pixels.ToArray());
        Assert.InRange(allocated, 0, 1024 * 1024);
    }

    /// <summary>
    /// A header that asks for more bytes than its compressed data can inflate to is broken before anything is allocated for the
    /// image, and a large image does not keep large buffers: a PNG is untrusted input (PR #212's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 4,096 x 4,096 16-bit RGBA header (128 MB of rows) with an image data chunk of 10 compressed bytes; a decoder that has
    ///   read the 2 x 1 image.
    ///
    /// Steps:
    /// - Read the large header with a limit of 4,096 a side, measuring the bytes allocated on this thread; then read a 1,100 x 1,100
    ///   gray image of zeros (1.2 MB of rows, 4.8 MB of pixels) and the 2 x 1 image, measuring the bytes allocated by the last.
    ///
    /// Expected:
    /// - The large header returns <see langword="false"/> and allocates less than 1 MB.
    /// - The 1,100 x 1,100 image reads; reading the 2 x 1 image after it allocates only the small buffers again (less than 64 KB),
    ///   which shows the 4.8 MB buffer was let go.
    /// </remarks>
    [Fact]
    public void TryDecode_LargeHeaderOrLargeImage_AllocatesLittle()
    {
        var decoder = new PngDecoder();
        _ = decoder.TryDecode(TestPng.Valid(), 64, out _, out _);
        byte[] large = TestPng.File(TestPng.Header(4096, 4096, 16, 6), TestPng.Data(new byte[10]), TestPng.End());
        byte[] rows = new byte[1100 * 1101];
        byte[] gray = TestPng.File(TestPng.Header(1100, 1100, 8, 0), TestPng.Data(rows), TestPng.End());

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool read = decoder.TryDecode(large, 4096, out _, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(decoder.TryDecode(gray, 4096, out int width, out _));
        long afterLarge = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(decoder.TryDecode(TestPng.Valid(), 64, out _, out _));
        long small = GC.GetAllocatedBytesForCurrentThread() - afterLarge;

        Assert.False(read);
        Assert.InRange(allocated, 0, 1024 * 1024);
        Assert.Equal(1100, width);
        Assert.InRange(small, 1, 64 * 1024);
    }

    /// <summary>
    /// An ancillary chunk that is unknown, or whose check value is wrong, is skipped; a palette index past the palette is
    /// transparent.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The 2 x 1 RGBA image with an unknown ancillary chunk "quUx" and a "tEXt" chunk with a wrong check value before its data;
    ///   and a 2 x 1 palette image of one entry (green) with the indices 0 and 1.
    ///
    /// Steps:
    /// - Read both.
    ///
    /// Expected:
    /// - The first reads as the image; the second is opaque green, then transparent black. The image's data split into two image
    ///   data chunks, one after the other, reads as the image too.
    /// </remarks>
    [Fact]
    public void TryDecode_AncillaryChunkAndIndexPastThePalette_AreSkippedAndTransparent()
    {
        var decoder = new PngDecoder();
        byte[] withChunks = TestPng.File(
            TestPng.Header(2, 1, 8, 6),
            TestPng.Chunk("quUx", [1, 2]),
            TestPng.Chunk("tEXt", "a\0b"u8, wrongCheck: true),
            TestPng.Data(TestPng.Rows()),
            TestPng.End()
        );
        byte[] palette = TestPng.File(TestPng.Header(2, 1, 8, 3), TestPng.Chunk("PLTE", [0, 255, 0]), TestPng.Data([0, 0, 1]), TestPng.End());

        Assert.True(decoder.TryDecode(withChunks, 64, out _, out _));
        Assert.Equal([0, 0, 255, 255, 128, 0, 0, 128], decoder.Pixels.ToArray());
        Assert.True(decoder.TryDecode(palette, 64, out _, out _));
        Assert.Equal([0, 255, 0, 255, 0, 0, 0, 0], decoder.Pixels.ToArray());
        byte[] compressed = TestPng.Compressed(TestPng.Rows());
        byte[] split = TestPng.File(
            TestPng.Header(2, 1, 8, 6),
            TestPng.Chunk("IDAT", compressed.AsSpan(0, 4)),
            TestPng.Chunk("IDAT", compressed.AsSpan(4)),
            TestPng.End()
        );
        Assert.True(decoder.TryDecode(split, 64, out _, out _));
        Assert.Equal([0, 0, 255, 255, 128, 0, 0, 128], decoder.Pixels.ToArray());
    }
}
