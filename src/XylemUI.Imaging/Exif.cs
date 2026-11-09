using System.Buffers.Binary;

namespace XylemUI.Imaging;

/// <summary>
/// Reads a JPEG's EXIF orientation (tag 0x0112 of IFD0 in the APP1 segment that starts with <c>Exif\0\0</c>) from the segments
/// before the first scan, and turns pixels by it (design §5.0.4, "EXIF orientation and color"). It reads untrusted bytes, so every
/// offset is checked against the data before it is read.
/// </summary>
/// <remarks>
/// It reads the markers itself, managed and bounds-checked, rather than through the decoder: spike S11 (Issue #730) read the same
/// orientations as libjpeg-turbo, and Wuffs's JPEG decoder does not report APPn segments. The values are TIFF's: 1 as it is, 2 mirrored,
/// 3 turned half round, 4 flipped, 5 transposed, 6 turned a quarter clockwise, 7 transversed, 8 turned a quarter counterclockwise.
/// </remarks>
internal static class Exif
{
    private const int OrientationTag = 0x0112;
    private const int ShortType = 3;

    /// <summary>
    /// Returns the orientation of a JPEG, 1 to 8; 1 when it has none or its EXIF is broken.
    /// </summary>
    /// <param name="jpeg">The JPEG's bytes.</param>
    /// <returns>The orientation.</returns>
    public static int Orientation(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return 1;
        }

        int at = 2;
        while (at + 4 <= jpeg.Length)
        {
            if (jpeg[at] != 0xFF)
            {
                return 1;
            }

            byte marker = jpeg[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            // The first scan or the end: the segments after it are not read.
            if (marker is 0xDA or 0xD9)
            {
                return 1;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(at + 2)..]);
            if (length < 2 || at + 2 + length > jpeg.Length)
            {
                return 1;
            }

            ReadOnlySpan<byte> payload = jpeg.Slice(at + 4, length - 2);
            if (marker == 0xE1 && payload.StartsWith("Exif\0\0"u8))
            {
                return FromTiff(payload[6..]);
            }

            at += 2 + length;
        }

        return 1;
    }

    /// <summary>
    /// Returns a value indicating whether an orientation swaps the width and the height (5 to 8).
    /// </summary>
    /// <param name="orientation">The orientation, 1 to 8.</param>
    /// <returns><see langword="true"/> for 5, 6, 7, and 8.</returns>
    public static bool SwapsSides(int orientation) => orientation is >= 5 and <= 8;

    /// <summary>
    /// Writes the pixels of an image turned by an orientation.
    /// </summary>
    /// <param name="source">Rows of <paramref name="width"/> × 4 bytes, as decoded.</param>
    /// <param name="width">The decoded width.</param>
    /// <param name="height">The decoded height.</param>
    /// <param name="orientation">The orientation, 1 to 8.</param>
    /// <param name="destination">The turned pixels: rows of the turned width × 4 bytes (the height when the sides swap).</param>
    public static void Orient(ReadOnlySpan<byte> source, int width, int height, int orientation, Span<byte> destination)
    {
        bool swaps = SwapsSides(orientation);
        int outWidth = swaps ? height : width;
        ReadOnlySpan<uint> from = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(source[..(width * height * 4)]);
        Span<uint> to = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(destination[..(width * height * 4)]);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (int tx, int ty) = orientation switch
                {
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (height - 1 - y, x),
                    7 => (height - 1 - y, width - 1 - x),
                    8 => (y, width - 1 - x),
                    _ => (x, y),
                };
                to[(ty * outWidth) + tx] = from[(y * width) + x];
            }
        }
    }

    // The orientation of IFD0 of a TIFF structure, little or big endian; 1 when absent, broken, or out of range.
    private static int FromTiff(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
        {
            return 1;
        }

        bool little = tiff[0] == 'I' && tiff[1] == 'I';
        if (!little && !(tiff[0] == 'M' && tiff[1] == 'M'))
        {
            return 1;
        }

        uint ifd = U32(tiff, 4, little);
        if (ifd > (uint)(tiff.Length - 2))
        {
            return 1;
        }

        int entries = U16(tiff, (int)ifd, little);
        for (int i = 0; i < entries; i++)
        {
            long entry = ifd + 2L + (i * 12L);
            if (entry + 12 > tiff.Length)
            {
                return 1;
            }

            if (U16(tiff, (int)entry, little) == OrientationTag && U16(tiff, (int)entry + 2, little) == ShortType)
            {
                int value = U16(tiff, (int)entry + 8, little);
                return value is >= 1 and <= 8 ? value : 1;
            }
        }

        return 1;
    }

    private static int U16(ReadOnlySpan<byte> s, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(s[at..]) : BinaryPrimitives.ReadUInt16BigEndian(s[at..]);

    private static uint U32(ReadOnlySpan<byte> s, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(s[at..]) : BinaryPrimitives.ReadUInt32BigEndian(s[at..]);
}
