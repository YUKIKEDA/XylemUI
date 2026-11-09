using System.Buffers.Binary;

namespace XylemUI.Imaging;

/// <summary>
/// Reads the container of an ICO file (design §5.0.4, "Decoders": the container is read here, the image in it is a PNG or a BMP
/// without its file header). It picks the entry nearest a size and returns its bytes; a BMP entry is read here too, as a device
/// independent bitmap of 32, 24, 8, 4, or 1 bits per pixel whose height counts the color rows and the rows of the AND mask.
/// </summary>
/// <remarks>
/// The file is untrusted: every offset and size is checked against the data. A 32-bit entry takes its alpha when any pixel has one,
/// as Windows draws it, and the AND mask otherwise; a 32-bit entry without an AND mask (as Pillow writes it) takes its alpha alone.
/// </remarks>
internal static class Ico
{
    private const int DirectoryBytes = 6;
    private const int EntryBytes = 16;
    private const int BitmapHeaderBytes = 40;

    /// <summary>
    /// Returns a value indicating whether bytes start as an ICO file (reserved 0, type 1, at least one entry).
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <returns><see langword="true"/> for an ICO file.</returns>
    public static bool Is(ReadOnlySpan<byte> data) =>
        data.Length >= DirectoryBytes
        && BinaryPrimitives.ReadUInt16LittleEndian(data) == 0
        && BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) == 1
        && BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) > 0;

    /// <summary>
    /// Finds the entry whose larger side is nearest to <paramref name="size"/>, preferring one at least as large, and returns its
    /// bytes.
    /// </summary>
    /// <param name="ico">The ICO file.</param>
    /// <param name="size">The side wanted, in pixels; <see cref="int.MaxValue"/> for the largest entry.</param>
    /// <param name="entry">The entry's bytes: a PNG file or a device independent bitmap.</param>
    /// <returns><see langword="true"/> when an entry lies inside the file.</returns>
    public static bool TryPick(ReadOnlySpan<byte> ico, int size, out ReadOnlySpan<byte> entry)
    {
        entry = default;
        if (!Is(ico))
        {
            return false;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..]);
        if (DirectoryBytes + ((long)count * EntryBytes) > ico.Length)
        {
            return false;
        }

        int best = -1;
        int bestSide = 0;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = ico.Slice(DirectoryBytes + (i * EntryBytes), EntryBytes);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            if (length == 0 || offset > (uint)ico.Length || length > (uint)ico.Length - offset)
            {
                continue;
            }

            // A side of 0 in the directory means 256.
            int side = Math.Max(record[0] == 0 ? 256 : record[0], record[1] == 0 ? 256 : record[1]);
            if (best < 0 || Better(side, bestSide, size))
            {
                best = i;
                bestSide = side;
            }
        }

        if (best < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> chosen = ico.Slice(DirectoryBytes + (best * EntryBytes), EntryBytes);
        entry = ico.Slice((int)BinaryPrimitives.ReadUInt32LittleEndian(chosen[12..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(chosen[8..]));
        return true;
    }

    /// <summary>
    /// Reads the size of a device independent bitmap of an ICO entry: the height of its color rows, half the height its header
    /// gives.
    /// </summary>
    /// <param name="bitmap">The entry's bytes, from its BITMAPINFOHEADER.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns><see langword="true"/> when the header is one this reads.</returns>
    public static bool TryReadBitmapSize(ReadOnlySpan<byte> bitmap, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bitmap.Length < BitmapHeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(bitmap) < BitmapHeaderBytes)
        {
            return false;
        }

        int w = BinaryPrimitives.ReadInt32LittleEndian(bitmap[4..]);
        int h = BinaryPrimitives.ReadInt32LittleEndian(bitmap[8..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(bitmap[14..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(bitmap[16..]);
        if (w <= 0 || w > 1024 || h <= 0 || h > 2048 || h % 2 != 0 || compression != 0 || bits is not (1 or 4 or 8 or 24 or 32))
        {
            return false;
        }

        width = w;
        height = h / 2;
        return true;
    }

    /// <summary>
    /// Reads a device independent bitmap of an ICO entry into premultiplied blue, green, red, and alpha, top-down.
    /// </summary>
    /// <param name="bitmap">The entry's bytes, from its BITMAPINFOHEADER.</param>
    /// <param name="pixels">Rows of width × 4 bytes, for the size <see cref="TryReadBitmapSize"/> gave.</param>
    /// <returns><see langword="true"/> when the bitmap lies inside the bytes.</returns>
    public static bool TryReadBitmap(ReadOnlySpan<byte> bitmap, Span<byte> pixels)
    {
        if (!TryReadBitmapSize(bitmap, out int width, out int height))
        {
            return false;
        }

        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(bitmap);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(bitmap[14..]);
        int used = BinaryPrimitives.ReadInt32LittleEndian(bitmap[32..]);
        int paletteEntries = bits <= 8 ? (used > 0 && used <= 1 << bits ? used : 1 << bits) : 0;
        int paletteStart = headerSize;
        int colorStart = paletteStart + (paletteEntries * 4);
        int colorStride = (((width * bits) + 31) / 32) * 4;
        int maskStride = ((width + 31) / 32) * 4;
        int maskStart = colorStart + (colorStride * height);
        if ((long)colorStart + ((long)colorStride * height) > bitmap.Length)
        {
            return false;
        }

        // A 32-bit entry may leave out its AND mask (Pillow writes none), and then its alpha is the only transparency.
        bool hasMask = (long)maskStart + ((long)maskStride * height) <= bitmap.Length;
        if (!hasMask && bits != 32)
        {
            return false;
        }

        ReadOnlySpan<byte> palette = bitmap.Slice(paletteStart, paletteEntries * 4);
        bool anyAlpha = false;
        for (int y = 0; y < height; y++)
        {
            // Bitmap rows are stored from the bottom.
            ReadOnlySpan<byte> row = bitmap.Slice(colorStart + ((height - 1 - y) * colorStride), colorStride);
            Span<byte> output = pixels.Slice(y * width * 4, width * 4);
            for (int x = 0; x < width; x++)
            {
                uint bgra = bits switch
                {
                    32 => BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]),
                    24 => row[x * 3] | ((uint)row[(x * 3) + 1] << 8) | ((uint)row[(x * 3) + 2] << 16) | 0xFF000000u,
                    _ => PaletteColor(palette, Index(row, x, bits)),
                };
                anyAlpha |= bits == 32 && (bgra >> 24) != 0;
                BinaryPrimitives.WriteUInt32LittleEndian(output[(x * 4)..], bgra);
            }
        }

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> mask = hasMask ? bitmap.Slice(maskStart + ((height - 1 - y) * maskStride), maskStride) : default;
            Span<byte> output = pixels.Slice(y * width * 4, width * 4);
            for (int x = 0; x < width; x++)
            {
                Span<byte> p = output.Slice(x * 4, 4);
                if (hasMask && (bits != 32 || !anyAlpha))
                {
                    // The AND mask: a set bit is transparent.
                    p[3] = (mask[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
                }

                Premultiply(p);
            }
        }

        return true;
    }

    // Whether a side is a better pick than the best so far: the smallest at least as large as the size wanted, else the largest.
    private static bool Better(int side, int best, int wanted)
    {
        bool fits = side >= wanted;
        bool bestFits = best >= wanted;
        return fits != bestFits ? fits : (fits ? side < best : side > best);
    }

    private static int Index(ReadOnlySpan<byte> row, int x, int bits) =>
        bits switch
        {
            8 => row[x],
            4 => (row[x >> 1] >> ((1 - (x & 1)) * 4)) & 0x0F,
            _ => (row[x >> 3] >> (7 - (x & 7))) & 0x01,
        };

    private static uint PaletteColor(ReadOnlySpan<byte> palette, int index) =>
        (index * 4) + 3 < palette.Length
            ? (BinaryPrimitives.ReadUInt32LittleEndian(palette[(index * 4)..]) & 0x00FFFFFFu) | 0xFF000000u
            : 0xFF000000u;

    private static void Premultiply(Span<byte> bgra)
    {
        int alpha = bgra[3];
        if (alpha == 255)
        {
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            bgra[i] = (byte)(((bgra[i] * alpha) + 127) / 255);
        }
    }
}
