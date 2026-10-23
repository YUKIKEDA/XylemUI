using System.Buffers.Binary;
using System.IO.Compression;

namespace XylemUI.Text;

/// <summary>
/// Reads a PNG image to premultiplied blue, green, red, and alpha, for the bitmap color glyphs of a font (design §5.0.3, "PNG
/// bitmaps", roadmap row T9). It reads every form of the format: color types 0, 2, 3, 4, and 6, bit depths 1 to 16, <c>tRNS</c>,
/// and Adam7 interlacing. A 16-bit value is rounded to 8 bits as <c>(v + 128) / 257</c>; the chunks that correct colors
/// (<c>gAMA</c>, <c>sRGB</c>, <c>iCCP</c>, <c>cHRM</c>) are skipped, so colors are taken as sRGB.
/// </summary>
/// <remarks>
/// A PNG comes from a font, so it is untrusted input. A PNG is broken, and <see cref="TryDecode"/> returns
/// <see langword="false"/> without throwing, when a chunk runs past the data, when the header is not the first chunk or comes
/// twice, when the check value (CRC-32) of a critical chunk does not match, when a side is 0 or larger than the caller's limit, when the image data chunks do not follow each other, when a
/// critical chunk is unknown, when the header asks for more bytes than the compressed data can inflate to (deflate expands at most
/// 1,032 times), when the compressed data fails to inflate or gives fewer bytes than the header asks for, or when a
/// row has an unknown filter. Inflating stops at the bytes the header asks for. The buffers are kept for the next image, so one
/// decoder is used on one thread.
/// </remarks>
internal sealed class PngDecoder
{
    private const uint Header = 0x49484452; // IHDR
    private const uint PaletteChunk = 0x504C5445; // PLTE
    private const uint TransparencyChunk = 0x74524E53; // tRNS
    private const uint Data = 0x49444154; // IDAT
    private const uint End = 0x49454E44; // IEND

    // Deflate expands at most 1,032 times (RFC 1951 blocks of 258-byte matches in about 2 bits), so data that would inflate past
    // that is broken before anything is allocated for it.
    private const long MaxInflation = 1032;

    // A buffer grown past this size for one large image goes back to the small size afterwards, so that one image does not keep
    // its memory for the rasterizer's lifetime.
    private const int KeptBufferBytes = 4 * 1024 * 1024;
    private const int SmallBufferBytes = 16 * 1024;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // The seven passes of Adam7: the first column and row, and the step between columns and between rows.
    private static ReadOnlySpan<byte> PassX => [0, 4, 0, 2, 0, 1, 0];

    private static ReadOnlySpan<byte> PassY => [0, 0, 4, 0, 2, 0, 1];

    private static ReadOnlySpan<byte> PassStepX => [8, 8, 4, 4, 2, 2, 1];

    private static ReadOnlySpan<byte> PassStepY => [8, 8, 8, 4, 4, 2, 2];

    private static readonly uint[] CrcTable = MakeCrcTable();

    private readonly uint[] _palette = new uint[256];

    // The palette premultiplied, as blue, green, red, and alpha; an index past the palette is transparent.
    private readonly uint[] _premultiplied = new uint[256];

    private byte[] _compressed = new byte[SmallBufferBytes];
    private byte[] _raw = new byte[SmallBufferBytes];
    private byte[] _pixels = new byte[SmallBufferBytes];
    private int _pixelLength;

    /// <summary>
    /// Gets the pixels of the last image <see cref="TryDecode"/> read: rows of width × 4 bytes, premultiplied blue, green, red, and
    /// alpha. It is valid until the next call.
    /// </summary>
    public ReadOnlySpan<byte> Pixels => _pixels.AsSpan(0, _pixelLength);

    /// <summary>
    /// Reads a PNG image into <see cref="Pixels"/>.
    /// </summary>
    /// <param name="png">The PNG data, from its signature.</param>
    /// <param name="maxSide">The largest width and height to accept, in pixels.</param>
    /// <param name="width">The image's width, or 0 for a broken PNG.</param>
    /// <param name="height">The image's height, or 0 for a broken PNG.</param>
    /// <returns><see langword="false"/> for a broken PNG (see the remarks of the type).</returns>
    public bool TryDecode(ReadOnlySpan<byte> png, int maxSide, out int width, out int height)
    {
        width = 0;
        height = 0;
        _pixelLength = 0;
        Shrink(ref _pixels);
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
        {
            return false;
        }

        var image = default(Image);
        bool seenHeader = false;
        bool seenPalette = false;
        bool dataEnded = false;
        int compressedLength = 0;
        int paletteCount = 0;
        int transparencyKey = -1;
        (int Red, int Green, int Blue) transparencyColor = (-1, -1, -1);
        _palette.AsSpan().Fill(0xFF000000);
        int at = Signature.Length;
        while (at < png.Length)
        {
            if (png.Length - at < 12)
            {
                return false;
            }

            uint length = BinaryPrimitives.ReadUInt32BigEndian(png[at..]);
            uint kind = BinaryPrimitives.ReadUInt32BigEndian(png[(at + 4)..]);
            if (length > (uint)(png.Length - at - 12))
            {
                return false;
            }

            ReadOnlySpan<byte> body = png.Slice(at + 8, (int)length);
            uint check = BinaryPrimitives.ReadUInt32BigEndian(png[(at + 8 + (int)length)..]);
            bool critical = (kind & 0x20000000) == 0;
            bool intact = Crc32(png.Slice(at + 4, 4 + (int)length)) == check;
            at += 12 + (int)length;

            // A critical chunk with a wrong check value breaks the image; an ancillary one is skipped, as libpng does.
            if (!intact)
            {
                if (critical)
                {
                    return false;
                }

                continue;
            }

            if (!seenHeader && kind != Header)
            {
                return false;
            }

            if (compressedLength > 0 && kind != Data)
            {
                dataEnded = true;
            }

            switch (kind)
            {
                case Header:
                    if (seenHeader || !TryReadHeader(body, maxSide, out image))
                    {
                        return false;
                    }

                    seenHeader = true;
                    break;
                case PaletteChunk:
                    if (seenPalette || compressedLength > 0 || body.Length % 3 != 0 || body.Length / 3 is 0 or > 256)
                    {
                        return false;
                    }

                    seenPalette = true;
                    paletteCount = body.Length / 3;
                    for (int i = 0; i < paletteCount; i++)
                    {
                        _palette[i] = 0xFF000000 | ((uint)body[i * 3] << 16) | ((uint)body[(i * 3) + 1] << 8) | body[(i * 3) + 2];
                    }

                    break;
                case TransparencyChunk:
                    if (image.ColorType == 3)
                    {
                        for (int i = 0; i < Math.Min(body.Length, 256); i++)
                        {
                            _palette[i] = (_palette[i] & 0x00FFFFFF) | ((uint)body[i] << 24);
                        }
                    }
                    else if (image.ColorType == 0 && body.Length >= 2)
                    {
                        transparencyKey = BinaryPrimitives.ReadUInt16BigEndian(body);
                    }
                    else if (image.ColorType == 2 && body.Length >= 6)
                    {
                        transparencyColor = (
                            BinaryPrimitives.ReadUInt16BigEndian(body),
                            BinaryPrimitives.ReadUInt16BigEndian(body[2..]),
                            BinaryPrimitives.ReadUInt16BigEndian(body[4..])
                        );
                    }

                    break;
                case Data:
                    if (dataEnded)
                    {
                        return false;
                    }

                    Grow(ref _compressed, compressedLength + body.Length);
                    body.CopyTo(_compressed.AsSpan(compressedLength));
                    compressedLength += body.Length;
                    break;
                case End:
                    at = png.Length;
                    break;
                default:
                    // A critical chunk (its first letter in upper case) that a reader does not know cannot be skipped.
                    if (critical)
                    {
                        return false;
                    }

                    break;
            }
        }

        if (!seenHeader || compressedLength == 0 || (image.ColorType == 3 && !seenPalette))
        {
            return false;
        }

        long expected = RawLength(image);
        long pixelBytes = (long)image.Width * image.Height * 4;
        if (expected > int.MaxValue || pixelBytes > int.MaxValue || expected > (MaxInflation * compressedLength) + 1024)
        {
            Shrink(ref _compressed);
            return false;
        }

        Grow(ref _raw, (int)expected);
        bool inflated = TryInflate(compressedLength, (int)expected);
        Shrink(ref _compressed);
        if (!inflated)
        {
            Shrink(ref _raw);
            return false;
        }

        Grow(ref _pixels, (int)pixelBytes);
        _pixelLength = (int)pixelBytes;
        for (int i = 0; i < 256; i++)
        {
            uint color = _palette[i];
            _premultiplied[i] =
                i < paletteCount
                    ? Premultiplied((int)((color >> 16) & 0xFF), (int)((color >> 8) & 0xFF), (int)(color & 0xFF), (int)(color >> 24))
                    : 0;
        }

        var transparency = new Transparency(transparencyKey, transparencyColor);
        bool written = TryUnfilterAndWrite(image, transparency);
        Shrink(ref _raw);
        if (!written)
        {
            _pixelLength = 0;
            return false;
        }

        width = image.Width;
        height = image.Height;
        return true;
    }

    private static bool TryReadHeader(ReadOnlySpan<byte> body, int maxSide, out Image image)
    {
        image = default;
        if (body.Length != 13)
        {
            return false;
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(body);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
        int depth = body[8];
        int type = body[9];
        bool valid = type switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => depth is 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => false,
        };
        if (!valid || width == 0 || height == 0 || width > (uint)maxSide || height > (uint)maxSide)
        {
            return false;
        }

        // Compression 0 (deflate), filter method 0, and interlacing none or Adam7 are the only ones the format defines.
        if (body[10] != 0 || body[11] != 0 || body[12] > 1)
        {
            return false;
        }

        int channels = type switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            _ => 4,
        };
        image = new Image((int)width, (int)height, depth, type, channels, body[12] == 1);
        return true;
    }

    // The bytes of the filtered rows: one filter byte and the row's packed samples for each row, of each pass when interlaced.
    // The check value of the format (ISO 3309, the polynomial 0xEDB88320), over a chunk's type and data.
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<uint> table = CrcTable;
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc = table[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] MakeCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static long RawLength(in Image image)
    {
        if (!image.Interlaced)
        {
            return (image.RowBytes(image.Width) + 1L) * image.Height;
        }

        long length = 0;
        for (int pass = 0; pass < 7; pass++)
        {
            (int columns, int rows) = PassSize(image, pass);
            if (columns > 0 && rows > 0)
            {
                length += (image.RowBytes(columns) + 1L) * rows;
            }
        }

        return length;
    }

    private static (int Columns, int Rows) PassSize(in Image image, int pass) =>
        ((image.Width - PassX[pass] + PassStepX[pass] - 1) / PassStepX[pass], (image.Height - PassY[pass] + PassStepY[pass] - 1) / PassStepY[pass]);

    private static void Shrink(ref byte[] buffer)
    {
        if (buffer.Length > KeptBufferBytes)
        {
            buffer = new byte[SmallBufferBytes];
        }
    }

    private static void Grow(ref byte[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            Array.Resize(ref buffer, Math.Max(length, buffer.Length * 2));
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int toLeft = Math.Abs(estimate - left);
        int toUp = Math.Abs(estimate - up);
        int toUpLeft = Math.Abs(estimate - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left
            : toUp <= toUpLeft ? up
            : upLeft;
    }

    private static uint Premultiplied(int red, int green, int blue, int alpha) =>
        (uint)((((blue * alpha) + 127) / 255) | ((((green * alpha) + 127) / 255) << 8) | ((((red * alpha) + 127) / 255) << 16) | (alpha << 24));

    private static int Eight(int sample, int depth) =>
        depth switch
        {
            16 => (sample + 128) / 257,
            8 => sample,
            _ => sample * 255 / ((1 << depth) - 1),
        };

    private unsafe bool TryInflate(int compressedLength, int expected)
    {
        try
        {
            fixed (byte* compressed = _compressed)
            {
                using var source = new UnmanagedMemoryStream(compressed, compressedLength);
                using var inflater = new ZLibStream(source, CompressionMode.Decompress);
                return inflater.ReadAtLeast(_raw.AsSpan(0, expected), expected, throwOnEndOfStream: false) == expected;
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private bool TryUnfilterAndWrite(in Image image, Transparency transparency)
    {
        int offset = 0;
        if (!image.Interlaced)
        {
            return TryPass(image, 0, 0, 1, 1, image.Width, image.Height, ref offset, transparency);
        }

        for (int pass = 0; pass < 7; pass++)
        {
            (int columns, int rows) = PassSize(image, pass);
            if (
                columns > 0
                && rows > 0
                && !TryPass(image, PassX[pass], PassY[pass], PassStepX[pass], PassStepY[pass], columns, rows, ref offset, transparency)
            )
            {
                return false;
            }
        }

        return true;
    }

    // Unfilters the rows of one pass in place and writes its pixels at their places in the image.
    private bool TryPass(
        in Image image,
        int firstX,
        int firstY,
        int stepX,
        int stepY,
        int columns,
        int rows,
        ref int offset,
        Transparency transparency
    )
    {
        int rowBytes = image.RowBytes(columns);
        int bytesPerPixel = Math.Max(1, image.Depth * image.Channels / 8);
        Span<byte> raw = _raw;
        Span<uint> pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(_pixels.AsSpan(0, _pixelLength));
        for (int row = 0; row < rows; row++)
        {
            int filter = raw[offset];
            Span<byte> line = raw.Slice(offset + 1, rowBytes);
            Span<byte> previous = row == 0 ? default : raw.Slice(offset - rowBytes, rowBytes);
            offset += rowBytes + 1;
            if (!TryUnfilter(filter, line, previous, bytesPerPixel))
            {
                return false;
            }

            // The forms emoji fonts use (in Noto Color Emoji, 3,932 of 4,004 bitmaps are 8-bit palette ones) take a loop of their own.
            Span<uint> target = pixels[((firstY + (row * stepY)) * image.Width)..];
            if (image.ColorType == 3 && image.Depth == 8)
            {
                for (int column = 0; column < columns; column++)
                {
                    target[firstX + (column * stepX)] = _premultiplied[line[column]];
                }
            }
            else if (image.ColorType == 6 && image.Depth == 8)
            {
                for (int column = 0; column < columns; column++)
                {
                    int at = column * 4;
                    target[firstX + (column * stepX)] = Premultiplied(line[at], line[at + 1], line[at + 2], line[at + 3]);
                }
            }
            else
            {
                for (int column = 0; column < columns; column++)
                {
                    target[firstX + (column * stepX)] = Pixel(image, line, column, transparency);
                }
            }
        }

        return true;
    }

    private static bool TryUnfilter(int filter, Span<byte> line, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        bool first = previous.IsEmpty;
        switch (filter)
        {
            case 0:
                return true;
            case 1:
                for (int x = bytesPerPixel; x < line.Length; x++)
                {
                    line[x] += line[x - bytesPerPixel];
                }

                return true;
            case 2:
                if (!first)
                {
                    for (int x = 0; x < line.Length; x++)
                    {
                        line[x] += previous[x];
                    }
                }

                return true;
            case 3:
                for (int x = 0; x < line.Length; x++)
                {
                    int left = x >= bytesPerPixel ? line[x - bytesPerPixel] : 0;
                    int up = first ? 0 : previous[x];
                    line[x] += (byte)((left + up) >> 1);
                }

                return true;
            case 4:
                for (int x = 0; x < line.Length; x++)
                {
                    int left = x >= bytesPerPixel ? line[x - bytesPerPixel] : 0;
                    int up = first ? 0 : previous[x];
                    int upLeft = x >= bytesPerPixel && !first ? previous[x - bytesPerPixel] : 0;
                    line[x] += (byte)Paeth(left, up, upLeft);
                }

                return true;
            default:
                return false;
        }
    }

    private uint Pixel(in Image image, ReadOnlySpan<byte> line, int column, Transparency transparency)
    {
        int depth = image.Depth;

        // An index past the palette is out of the format; it is transparent, as no color is defined for it (_premultiplied).
        if (image.ColorType == 3)
        {
            return _premultiplied[Sample(line, column, depth)];
        }

        int channel = column * image.Channels;
        int c0 = Sample(line, channel, depth);
        switch (image.ColorType)
        {
            case 0:
            {
                int gray = Eight(c0, depth);
                return Premultiplied(gray, gray, gray, c0 == transparency.Gray ? 0 : 255);
            }

            case 2:
            {
                int c1 = Sample(line, channel + 1, depth);
                int c2 = Sample(line, channel + 2, depth);
                bool clear = (c0, c1, c2) == transparency.Color;
                return Premultiplied(Eight(c0, depth), Eight(c1, depth), Eight(c2, depth), clear ? 0 : 255);
            }

            case 4:
            {
                int gray = Eight(c0, depth);
                return Premultiplied(gray, gray, gray, Eight(Sample(line, channel + 1, depth), depth));
            }

            default:
                return Premultiplied(
                    Eight(c0, depth),
                    Eight(Sample(line, channel + 1, depth), depth),
                    Eight(Sample(line, channel + 2, depth), depth),
                    Eight(Sample(line, channel + 3, depth), depth)
                );
        }
    }

    // The sample at an index of a row: packed from the high bits for depths under 8, big-endian for 16.
    private static int Sample(ReadOnlySpan<byte> line, int index, int depth) =>
        depth switch
        {
            8 => line[index],
            16 => (line[index * 2] << 8) | line[(index * 2) + 1],
            _ => (line[(index * depth) >> 3] >> (8 - depth - ((index * depth) & 7))) & ((1 << depth) - 1),
        };

    private readonly record struct Image(int Width, int Height, int Depth, int ColorType, int Channels, bool Interlaced)
    {
        public int RowBytes(int columns) => (int)((((long)columns * Channels * Depth) + 7) / 8);
    }

    // The tRNS key of gray and true-color images, in the image's own samples; -1 for none.
    private readonly record struct Transparency(int Gray, (int Red, int Green, int Blue) Color);
}
