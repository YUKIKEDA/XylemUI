using XylemUI.Imaging.Interop;

namespace XylemUI.Imaging;

/// <summary>
/// Reads an image from its bytes to premultiplied blue, green, red, and alpha (design §5.0.4, "Decoders"): PNG, GIF (its first frame),
/// and BMP with Wuffs, JPEG with libjpeg-turbo, WebP with libwebp, through the C shim, and ICO through <see cref="Ico"/>. A JPEG
/// is turned by its EXIF orientation (<see cref="Exif"/>). A PNG's checksums are checked, and a 12-bit JPEG is not read (IM1's
/// Grill, Q4 and Q5).
/// </summary>
/// <remarks>
/// The bytes are untrusted. An image that is broken, of a form not read, or larger than the caller's limit makes
/// <see cref="TryDecode"/> return <see langword="false"/> without throwing. The buffers are kept for the next image, so one decoder is
/// used on one thread.
/// </remarks>
internal sealed unsafe class ImageDecoder
{
    // A buffer grown past this size for one large image goes back to the small size afterwards, so that one image does not keep its
    // memory for the decoder's lifetime (as the PNG decoder of row T9 did).
    private const int KeptBufferBytes = 4 * 1024 * 1024;
    private const int SmallBufferBytes = 16 * 1024;

    private byte[] _decoded = new byte[SmallBufferBytes];
    private byte[] _pixels = new byte[SmallBufferBytes];
    private int _pixelLength;
    private bool _pixelsInDecoded;

    /// <summary>
    /// Gets the pixels of the last image <see cref="TryDecode"/> read: rows of width × 4 bytes, premultiplied blue, green, red, and
    /// alpha. It is valid until the next call.
    /// </summary>
    public ReadOnlySpan<byte> Pixels => (_pixelsInDecoded ? _decoded : _pixels).AsSpan(0, _pixelLength);

    /// <summary>
    /// Gets the format of the last image <see cref="TryDecode"/> read, or <see cref="ImageFormat.None"/>.
    /// </summary>
    public ImageFormat Format { get; private set; }

    /// <summary>
    /// Reads an image into <see cref="Pixels"/>.
    /// </summary>
    /// <param name="data">The image's bytes.</param>
    /// <param name="maxSide">The largest width and height to accept, in pixels; for an ICO, the side of the entry to pick.</param>
    /// <param name="width">The image's width, turned by its orientation, or 0 when it was not read.</param>
    /// <param name="height">The image's height, or 0.</param>
    /// <returns><see langword="true"/> when the image was read.</returns>
    public bool TryDecode(ReadOnlySpan<byte> data, int maxSide, out int width, out int height)
    {
        width = 0;
        height = 0;
        Format = ImageFormat.None;
        _pixelLength = 0;
        _pixelsInDecoded = true;
        ShrinkKeptBuffers();
        if (Ico.Is(data))
        {
            return TryDecodeIco(data, maxSide, out width, out height);
        }

        if (!TryDecodeWithShim(data, maxSide, out int w, out int h, out ImageFormat format))
        {
            return false;
        }

        Format = format;
        int orientation = format == ImageFormat.Jpeg ? Exif.Orientation(data) : 1;
        if (orientation != 1)
        {
            EnsureCapacity(ref _pixels, _pixelLength);
            Exif.Orient(_decoded, w, h, orientation, _pixels);
            _pixelsInDecoded = false;
            if (Exif.SwapsSides(orientation))
            {
                (w, h) = (h, w);
            }
        }

        width = w;
        height = h;
        return true;
    }

    private bool TryDecodeWithShim(ReadOnlySpan<byte> data, int maxSide, out int width, out int height, out ImageFormat format)
    {
        width = 0;
        height = 0;
        format = ImageFormat.None;
        if (data.IsEmpty)
        {
            return false;
        }

        int f;
        int w;
        int h;
        fixed (byte* bytes = data)
        {
            if (ImageShim.Info(bytes, (nuint)data.Length, &f, &w, &h) != ImageShim.Ok || w <= 0 || h <= 0 || w > maxSide || h > maxSide)
            {
                return false;
            }

            long length = (long)w * h * 4;
            if (length > Array.MaxLength)
            {
                return false;
            }

            EnsureCapacity(ref _decoded, (int)length);
            fixed (byte* pixels = _decoded)
            {
                if (ImageShim.Decode(bytes, (nuint)data.Length, pixels, (nuint)(w * 4), w, h) != ImageShim.Ok)
                {
                    return false;
                }
            }

            _pixelLength = (int)length;
        }

        width = w;
        height = h;
        format = (ImageFormat)f;
        return true;
    }

    private bool TryDecodeIco(ReadOnlySpan<byte> data, int maxSide, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!Ico.TryPick(data, maxSide, out ReadOnlySpan<byte> entry))
        {
            return false;
        }

        // An entry is a PNG file, or a bitmap without its file header.
        if (entry.Length >= 8 && entry[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            if (!TryDecodeWithShim(entry, maxSide, out width, out height, out _))
            {
                return false;
            }
        }
        else
        {
            if (!Ico.TryReadBitmapSize(entry, out int w, out int h) || w > maxSide || h > maxSide)
            {
                return false;
            }

            EnsureCapacity(ref _decoded, w * h * 4);
            if (!Ico.TryReadBitmap(entry, _decoded))
            {
                return false;
            }

            _pixelLength = w * h * 4;
            width = w;
            height = h;
        }

        Format = ImageFormat.Ico;
        return true;
    }

    private static void EnsureCapacity(ref byte[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            buffer = new byte[length];
        }
    }

    private void ShrinkKeptBuffers()
    {
        if (_decoded.Length > KeptBufferBytes)
        {
            _decoded = new byte[SmallBufferBytes];
        }

        if (_pixels.Length > KeptBufferBytes)
        {
            _pixels = new byte[SmallBufferBytes];
        }
    }
}
