using System.Buffers.Binary;
using System.IO.Compression;
using XylemUI.Text;

namespace XylemUI.Testing;

/// <summary>
/// Writes and reads the PNG files of the screenshots and the goldens (design §12.3, "PNG"): RGBA, 8 bits a channel, not interlaced,
/// deflated with the BCL's <see cref="ZLibStream"/>; no dependency. Reading goes through <c>XylemUI.Text</c>'s decoder, which reads
/// every form of the format.
/// </summary>
internal static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>
    /// Returns a PNG file of an image.
    /// </summary>
    /// <param name="pixels">The colors from the top left, row by row.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] Encode(ReadOnlySpan<Color> pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException("The pixels are width × height colors.", nameof(pixels));
        }

        using var file = new MemoryStream();
        file.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // bits a channel
        header[9] = 6; // color type: RGBA
        header[10] = 0; // deflate
        header[11] = 0; // filter method 0
        header[12] = 0; // not interlaced
        WriteChunk(file, "IHDR"u8, header);

        // Each row starts with its filter, 0 (none).
        using var raw = new MemoryStream();
        using (var deflate = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + (width * 4)];
            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<Color> line = pixels.Slice(y * width, width);
                for (int x = 0; x < width; x++)
                {
                    Color c = line[x];
                    int at = 1 + (x * 4);
                    row[at] = c.R;
                    row[at + 1] = c.G;
                    row[at + 2] = c.B;
                    row[at + 3] = c.A;
                }

                deflate.Write(row);
            }
        }

        WriteChunk(file, "IDAT"u8, raw.ToArray());
        WriteChunk(file, "IEND"u8, []);
        return file.ToArray();
    }

    /// <summary>
    /// Reads a PNG file into colors, or returns <see langword="null"/> when it is broken.
    /// </summary>
    /// <param name="png">The file's bytes.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The colors from the top left, row by row, or <see langword="null"/>.</returns>
    public static Color[]? Decode(ReadOnlySpan<byte> png, out int width, out int height)
    {
        var decoder = new PngDecoder();
        if (!decoder.TryDecode(png, 16_384, out width, out height))
        {
            return null;
        }

        // The decoder gives premultiplied blue, green, red, and alpha; the colors of a frame are opaque, so for them it is the
        // color itself. A translucent pixel is divided back.
        ReadOnlySpan<byte> bgra = decoder.Pixels;
        var colors = new Color[width * height];
        for (int i = 0; i < colors.Length; i++)
        {
            byte b = bgra[i * 4];
            byte g = bgra[(i * 4) + 1];
            byte r = bgra[(i * 4) + 2];
            byte a = bgra[(i * 4) + 3];
            colors[i] = a is 0 or 255 ? new Color(r, g, b, a) : new Color(Unpremultiply(r, a), Unpremultiply(g, a), Unpremultiply(b, a), a);
        }

        return colors;
    }

    private static byte Unpremultiply(byte value, byte alpha) => (byte)Math.Min(255, ((value * 255) + (alpha / 2)) / alpha);

    private static void WriteChunk(Stream file, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        file.Write(word);
        file.Write(type);
        file.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        file.Write(word);
    }

    // The CRC-32 of PNG (ISO 3309), over the chunk's type and data.
    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
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
}
