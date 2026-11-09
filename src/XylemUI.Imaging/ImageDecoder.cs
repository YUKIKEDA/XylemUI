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
    /// Gets the scale, in eighths, the last <see cref="TryDecodeTo"/> decoded a JPEG at; 8 for the other formats.
    /// </summary>
    public int LastEighths { get; private set; } = 8;

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

    /// <summary>
    /// Reads an image for the image element (design §5.0.4, "Decoding size"): at the size <see cref="TargetSize"/> gives, a JPEG at
    /// the smallest of libjpeg-turbo's scales (eighths) that is not smaller, then shrunk to the size by the mean over each pixel's
    /// area (<see cref="BoxResize"/>). An ICO's entry is the one nearest the size's longer side.
    /// </summary>
    /// <param name="data">The image's bytes.</param>
    /// <param name="targetWidth">The width asked for, in pixels; 0 for none.</param>
    /// <param name="targetHeight">The height asked for, in pixels; 0 for none.</param>
    /// <param name="maxSide">The longest side of the result: the textures' limit.</param>
    /// <param name="pixels">The pixels, premultiplied BGRA, in an array of their own that the caller keeps; null when not read.</param>
    /// <param name="width">The result's width, or 0.</param>
    /// <param name="height">The result's height, or 0.</param>
    /// <returns><see langword="true"/> when the image was read.</returns>
    public bool TryDecodeTo(
        ReadOnlySpan<byte> data,
        int targetWidth,
        int targetHeight,
        int maxSide,
        out byte[]? pixels,
        out int width,
        out int height
    )
    {
        pixels = null;
        width = 0;
        height = 0;
        Format = ImageFormat.None;
        _pixelLength = 0;
        _pixelsInDecoded = true;
        ShrinkKeptBuffers();
        int w;
        int h;
        if (Ico.Is(data))
        {
            int side = Math.Max(targetWidth, targetHeight);
            if (!TryDecodeIco(data, side > 0 ? side : 256, out w, out h))
            {
                return false;
            }
        }
        else
        {
            int f;
            int sourceWidth;
            int sourceHeight;
            fixed (byte* bytes = data)
            {
                if (data.IsEmpty || ImageShim.Info(bytes, (nuint)data.Length, &f, &sourceWidth, &sourceHeight) != ImageShim.Ok)
                {
                    return false;
                }
            }

            ImageFormat format = (ImageFormat)f;
            int orientation = format == ImageFormat.Jpeg ? Exif.Orientation(data) : 1;
            bool swaps = Exif.SwapsSides(orientation);
            int eighths = 8;
            if (format == ImageFormat.Jpeg)
            {
                // The target in the stored image's sides, before the orientation turns them.
                (int shownWidth, int shownHeight) = swaps ? (sourceHeight, sourceWidth) : (sourceWidth, sourceHeight);
                (int tw, int th) = TargetSize(shownWidth, shownHeight, targetWidth, targetHeight, maxSide);
                (int storedWidth, int storedHeight) = swaps ? (th, tw) : (tw, th);
                while (
                    eighths > 1
                    && ImageShim.Scaled(sourceWidth, eighths - 1) >= storedWidth
                    && ImageShim.Scaled(sourceHeight, eighths - 1) >= storedHeight
                )
                {
                    eighths--;
                }
            }

            LastEighths = eighths;
            if (!TryDecodeScaled(data, eighths, out w, out h))
            {
                return false;
            }

            Format = format;
            if (orientation != 1)
            {
                EnsureCapacity(ref _pixels, _pixelLength);
                Exif.Orient(_decoded, w, h, orientation, _pixels);
                _pixelsInDecoded = false;
                if (swaps)
                {
                    (w, h) = (h, w);
                }
            }
        }

        (width, height) = TargetSize(w, h, targetWidth, targetHeight, maxSide);
        pixels = new byte[width * height * 4];
        if (width == w && height == h)
        {
            Pixels.CopyTo(pixels);
        }
        else
        {
            BoxResize.Shrink(Pixels, w, h, pixels, width, height);
        }

        return true;
    }

    /// <summary>
    /// Returns the size an image is read at for the image element (design §5.0.4, "Decoding size", IM1's Grill, Q17 and Q19): with
    /// one side asked for, the other keeps the image's ratio (Flutter's <c>instantiateCodec</c>: the height the width's floor, the
    /// width rounded); with both, both; a side asked for larger than the image's is the image's (no enlarging, Flutter's
    /// <c>ResizeImagePolicy.exact</c> without <c>allowUpscaling</c>). A size longer than <paramref name="maxSide"/> is shrunk, its
    /// ratio kept, until its longer side is <paramref name="maxSide"/>.
    /// </summary>
    /// <param name="width">The image's width.</param>
    /// <param name="height">The image's height.</param>
    /// <param name="targetWidth">The width asked for; 0 for none.</param>
    /// <param name="targetHeight">The height asked for; 0 for none.</param>
    /// <param name="maxSide">The longest side.</param>
    /// <returns>The size, each side at least 1.</returns>
    public static (int Width, int Height) TargetSize(int width, int height, int targetWidth, int targetHeight, int maxSide)
    {
        int tw = targetWidth > 0 ? Math.Min(targetWidth, width) : 0;
        int th = targetHeight > 0 ? Math.Min(targetHeight, height) : 0;
        if (tw == 0 && th == 0)
        {
            (tw, th) = (width, height);
        }
        else if (tw == 0)
        {
            tw = (int)Math.Round(th * ((double)width / height), MidpointRounding.AwayFromZero);
        }
        else if (th == 0)
        {
            th = (int)(tw / ((double)width / height));
        }

        int longer = Math.Max(tw, th);
        if (longer > maxSide)
        {
            double scale = (double)maxSide / longer;
            tw = (int)Math.Round(tw * scale, MidpointRounding.AwayFromZero);
            th = (int)Math.Round(th * scale, MidpointRounding.AwayFromZero);
        }

        return (Math.Max(1, tw), Math.Max(1, th));
    }

    // The shim's decode of a whole image, a JPEG at `eighths`, into the decoded buffer.
    private bool TryDecodeScaled(ReadOnlySpan<byte> data, int eighths, out int width, out int height)
    {
        width = 0;
        height = 0;
        int f;
        int w;
        int h;
        fixed (byte* bytes = data)
        {
            if (ImageShim.Info(bytes, (nuint)data.Length, &f, &w, &h) != ImageShim.Ok || w <= 0 || h <= 0)
            {
                return false;
            }

            w = ImageShim.Scaled(w, eighths);
            h = ImageShim.Scaled(h, eighths);
            long length = (long)w * h * 4;
            if (length > Array.MaxLength)
            {
                return false;
            }

            EnsureCapacity(ref _decoded, (int)length);
            fixed (byte* pixels = _decoded)
            {
                if (ImageShim.Decode(bytes, (nuint)data.Length, pixels, (nuint)(w * 4), w, h, eighths) != ImageShim.Ok)
                {
                    return false;
                }
            }

            _pixelLength = (int)length;
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
                if (ImageShim.Decode(bytes, (nuint)data.Length, pixels, (nuint)(w * 4), w, h, 8) != ImageShim.Ok)
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
