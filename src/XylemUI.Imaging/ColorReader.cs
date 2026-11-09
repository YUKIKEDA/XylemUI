using System.Buffers.Binary;
using System.IO.Compression;

namespace XylemUI.Imaging;

/// <summary>
/// Reads an image's color information, which the shim converts to sRGB with skcms (design §5.0.4, "EXIF orientation and color",
/// IM1's Grill, Q23 and Q24): a JPEG's ICC profile from its APP2 segments, a PNG's iCCP (or its cHRM and gAMA when it has neither an
/// iCCP nor an sRGB chunk), and a WebP's ICCP chunk. GIF and BMP have none, as Skia reads none. It reads untrusted bytes, managed,
/// with every offset checked against the data, as <see cref="Exif"/> reads the orientation (IM1 1a).
/// </summary>
/// <remarks>
/// A PNG's iCCP comes before its sRGB chunk, as in Skia: when an image has both, the ICC profile is the more specific one, and the
/// sRGB chunk is there for a decoder that does not manage colors (Skia's <c>SkPngCodec.cpp</c>, the comment of read_color_profile;
/// the PNG specification sets no order). Whether the profile fits the image and converts anything is the shim's to decide.
/// </remarks>
internal static class ColorReader
{
    // The marker of a JPEG's APP2 segment, and the bytes that start an ICC chunk in it: "ICC_PROFILE", a 0, the chunk's number from
    // 1, and the number of chunks.
    private const byte App2 = 0xE2;
    private const int IccHeader = 14;

    // The most bytes an iCCP's profile is inflated to: a profile is a few kilobytes, up to the hundreds of a CMYK lookup table, and
    // the compressed bytes are untrusted.
    private const int MaxProfileBytes = 4 * 1024 * 1024;

    // The room past a profile's end that skcms's parse reads ahead into (skcms_public.h: "typically 1-2 bytes").
    private const int ParsePadding = 4;

    /// <summary>
    /// Reads the color information of an image.
    /// </summary>
    /// <param name="data">The image's bytes.</param>
    /// <param name="format">The image's format.</param>
    /// <returns>The color information; <see cref="ColorInfo.None"/> when the image has none, or it is broken.</returns>
    public static ColorInfo Read(ReadOnlySpan<byte> data, ImageFormat format) =>
        format switch
        {
            ImageFormat.Jpeg => FromJpeg(data),
            ImageFormat.Png => FromPng(data),
            ImageFormat.WebP => FromWebP(data),
            _ => ColorInfo.None,
        };

    // The ICC chunks of the APP2 segments before the first scan, joined in the order of their numbers. Chunks that do not agree on
    // their count, a number outside it, a number twice, or a number missing give no profile (Skia's
    // SkJpegMetadataDecoderImpl.cpp).
    private static ColorInfo FromJpeg(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return ColorInfo.None;
        }

        List<(int Number, int Start, int Length)>? chunks = null;
        int count = 0;
        int at = 2;
        while (at + 4 <= jpeg.Length)
        {
            if (jpeg[at] != 0xFF)
            {
                break;
            }

            byte marker = jpeg[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            if (marker is 0xDA or 0xD9)
            {
                break;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(at + 2)..]);
            if (length < 2 || at + 2 + length > jpeg.Length)
            {
                break;
            }

            ReadOnlySpan<byte> payload = jpeg.Slice(at + 4, length - 2);
            if (marker == App2 && payload.Length > IccHeader && payload.StartsWith("ICC_PROFILE\0"u8))
            {
                int number = payload[12];
                int total = payload[13];
                if (total == 0 || (count != 0 && total != count) || number < 1 || number > total)
                {
                    return ColorInfo.None;
                }

                count = total;
                chunks ??= [];
                if (chunks.Exists(chunk => chunk.Number == number))
                {
                    return ColorInfo.None;
                }

                chunks.Add((number, at + 4 + IccHeader, payload.Length - IccHeader));
            }

            at += 2 + length;
        }

        if (chunks is null || chunks.Count != count)
        {
            return ColorInfo.None;
        }

        chunks.Sort(static (a, b) => a.Number.CompareTo(b.Number));
        int size = 0;
        foreach ((_, _, int length) in chunks)
        {
            size += length;
        }

        byte[] profile = new byte[size + ParsePadding];
        int written = 0;
        foreach ((_, int start, int length) in chunks)
        {
            jpeg.Slice(start, length).CopyTo(profile.AsSpan(written));
            written += length;
        }

        return ColorInfo.FromIcc(profile, size);
    }

    // The chunks before the first IDAT: iCCP first, then sRGB (no conversion), then cHRM and gAMA (Q24). A chunk that runs past the
    // data ends the reading with what was read.
    private static ColorInfo FromPng(ReadOnlySpan<byte> png)
    {
        if (png.Length < 8 || !png[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ColorInfo.None;
        }

        bool srgb = false;
        bool hasChromaticities = false;
        bool hasGamma = false;
        var chromaticities = new float[8];
        float gamma = 0;
        int at = 8;
        while (at + 8 <= png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png[at..]);
            ReadOnlySpan<byte> type = png.Slice(at + 4, 4);
            if (length > int.MaxValue || at + 12 + (long)length > png.Length || type.SequenceEqual("IDAT"u8))
            {
                break;
            }

            ReadOnlySpan<byte> chunk = png.Slice(at + 8, (int)length);
            if (type.SequenceEqual("iCCP"u8))
            {
                return FromIccp(chunk);
            }

            if (type.SequenceEqual("sRGB"u8))
            {
                srgb = true;
            }
            else if (type.SequenceEqual("gAMA"u8) && chunk.Length == 4)
            {
                hasGamma = true;
                gamma = BinaryPrimitives.ReadUInt32BigEndian(chunk) / 100_000f;
            }
            else if (type.SequenceEqual("cHRM"u8) && chunk.Length == 32)
            {
                hasChromaticities = true;
                for (int i = 0; i < 8; i++)
                {
                    chromaticities[i] = BinaryPrimitives.ReadUInt32BigEndian(chunk[(i * 4)..]) / 100_000f;
                }
            }

            at += 12 + (int)length;
        }

        if (srgb || !(hasChromaticities || hasGamma))
        {
            return ColorInfo.None;
        }

        return new ColorInfo(null, 0, hasChromaticities ? chromaticities : null, hasGamma ? gamma : 0);
    }

    // An iCCP: a name of 1 to 79 bytes, a 0, the compression method (0, zlib), and the compressed profile.
    private static ColorInfo FromIccp(ReadOnlySpan<byte> chunk)
    {
        int name = chunk.IndexOf((byte)0);
        if (name < 1 || name > 79 || name + 2 > chunk.Length || chunk[name + 1] != 0)
        {
            return ColorInfo.None;
        }

        try
        {
            using var compressed = new MemoryStream(chunk[(name + 2)..].ToArray());
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            byte[] profile = new byte[64 * 1024];
            int size = 0;
            while (true)
            {
                if (size + ParsePadding == profile.Length)
                {
                    if (profile.Length >= MaxProfileBytes)
                    {
                        return ColorInfo.None;
                    }

                    Array.Resize(ref profile, profile.Length * 2);
                }

                int read = inflate.Read(profile, size, profile.Length - ParsePadding - size);
                if (read == 0)
                {
                    break;
                }

                size += read;
            }

            Array.Clear(profile, size, ParsePadding);
            return size == 0 ? ColorInfo.None : ColorInfo.FromIcc(profile, size);
        }
        catch (InvalidDataException)
        {
            return ColorInfo.None;
        }
    }

    // The ICCP chunk of a WebP's RIFF container: "RIFF", the size, "WEBP", then chunks of a fourcc, a little-endian size, and the
    // data padded to an even length.
    private static ColorInfo FromWebP(ReadOnlySpan<byte> webp)
    {
        if (webp.Length < 12 || !webp[..4].SequenceEqual("RIFF"u8) || !webp.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ColorInfo.None;
        }

        int at = 12;
        while (at + 8 <= webp.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(webp[(at + 4)..]);
            if (length > int.MaxValue || at + 8 + (long)length > webp.Length)
            {
                break;
            }

            if (webp.Slice(at, 4).SequenceEqual("ICCP"u8))
            {
                byte[] profile = new byte[length + ParsePadding];
                webp.Slice(at + 8, (int)length).CopyTo(profile);
                return ColorInfo.FromIcc(profile, (int)length);
            }

            at += 8 + (int)length + (int)(length & 1);
        }

        return ColorInfo.None;
    }
}

/// <summary>
/// An image's color information for the shim: an ICC profile in an array with room past its end, or a PNG's cHRM and gAMA.
/// </summary>
/// <param name="Icc">The profile's bytes, followed by zeros that skcms's parse may read; null for none.</param>
/// <param name="IccLength">The length of the profile in <paramref name="Icc"/>.</param>
/// <param name="Chromaticities">A PNG's cHRM: white, red, green, and blue x and y; null for none.</param>
/// <param name="Gamma">A PNG's gAMA, the encoding gamma; 0 for none.</param>
internal readonly record struct ColorInfo(byte[]? Icc, int IccLength, float[]? Chromaticities, float Gamma)
{
    /// <summary>
    /// Gets the information of an image with none: its colors are taken as sRGB.
    /// </summary>
    public static ColorInfo None => default;

    /// <summary>
    /// Gets a value indicating whether there is anything for the shim to read.
    /// </summary>
    public bool IsNone => Icc is null && Chromaticities is null && Gamma == 0;

    /// <summary>
    /// Returns the information of an ICC profile.
    /// </summary>
    /// <param name="icc">The profile, with room past its end.</param>
    /// <param name="length">The profile's length.</param>
    /// <returns>The information.</returns>
    public static ColorInfo FromIcc(byte[] icc, int length) => new(icc, length, null, 0);
}
