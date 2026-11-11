using System.Buffers;
using XylemUI.Imaging.Interop;

namespace XylemUI.Imaging;

/// <summary>
/// What <see cref="ImageAnimation.Next"/> gave.
/// </summary>
internal enum AnimationStep
{
    /// <summary>A frame was written.</summary>
    Frame,

    /// <summary>The animation played the plays its file asks for; nothing was written.</summary>
    End,

    /// <summary>The next frame could not be decoded; nothing usable was written.</summary>
    Broken,
}

/// <summary>
/// The frames of an animation, one at a time: what an image cache plays (<see cref="ImageAnimation"/>, and the stand-ins of the
/// cache's tests).
/// </summary>
internal interface IAnimationFrames : IDisposable
{
    /// <summary>
    /// Gets the bytes the frames' decoder holds besides the image's own bytes and the frame it writes.
    /// </summary>
    long HeldBytes { get; }

    /// <summary>
    /// Composes the next frame into a buffer: the first frame of the next play after the last of one.
    /// </summary>
    /// <param name="pixels">Where the frame goes, premultiplied BGRA, 4 bytes for each pixel of a frame.</param>
    /// <param name="duration">How long the frame shows, as the file gives it.</param>
    /// <returns>What was written.</returns>
    AnimationStep Next(Span<byte> pixels, out TimeSpan duration);
}

/// <summary>
/// The decoder of an animated GIF or WebP (design §5.0.4, "Animations"): it gives one frame at a time, the whole canvas composed,
/// at the size the image element reads the image at (<see cref="ImageDecoder.TargetSize"/>). The frames are composed at the image's
/// own size in the native decoder, which keeps two canvases, and each is shrunk on its way out. A WebP's colors are converted from
/// its ICC profile to sRGB (IM1's Grill, Q47); a GIF's are not.
/// </summary>
/// <remarks>
/// The bytes are untrusted: an image that is broken makes <see cref="TryOpen"/> return <see langword="null"/> or
/// <see cref="Next"/> return <see cref="AnimationStep.Broken"/> without throwing. The image's bytes are pinned, not copied, until
/// <see cref="Dispose"/>. One thread uses a decoder at a time.
/// </remarks>
internal sealed unsafe class ImageAnimation : IAnimationFrames
{
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;

    // The buffer the colors are converted into before they are shrunk, as large as the canvas; null when the frames are not both
    // converted and shrunk.
    private readonly byte[]? _converted;
    private readonly bool _converts;
    private static int _open;
    private MemoryHandle _pin;
    private nint _native;

    private ImageAnimation(nint native, MemoryHandle pin, int canvasWidth, int canvasHeight, int width, int height, bool converts)
    {
        _native = native;
        _pin = pin;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        Width = width;
        Height = height;
        _converts = converts;
        _ = Interlocked.Increment(ref _open);
        if (converts && Shrinks)
        {
            _converted = new byte[canvasWidth * canvasHeight * 4];
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="ImageAnimation"/> class. A decoder nobody disposed (a tree thrown away without its
    /// cache being released, a result that never reached the UI thread) still closes its native decoder and lets the image's bytes
    /// go.
    /// </summary>
    ~ImageAnimation() => Close();

    /// <summary>
    /// Gets the number of decoders that are open; for the tests.
    /// </summary>
    public static int OpenCount => Volatile.Read(ref _open);

    /// <summary>
    /// Gets the width of a frame, in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of a frame, in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the bytes the decoder holds besides the image's own and the frame it writes: its two canvases at the image's size, and
    /// the buffer the colors are converted into before they are shrunk (design §5.0.4, "The bytes counted").
    /// </summary>
    public long HeldBytes => ((long)_canvasWidth * _canvasHeight * 4 * 2) + (_converted?.Length ?? 0);

    private bool Shrinks => Width != _canvasWidth || Height != _canvasHeight;

    /// <summary>
    /// Returns whether an image is an animation this class opens: a WebP whose header says so, or a GIF with a second frame.
    /// </summary>
    /// <param name="data">The image's bytes.</param>
    /// <returns><see langword="true"/> for an animation.</returns>
    public static bool IsAnimation(ReadOnlySpan<byte> data)
    {
        int format;
        int width;
        int height;
        int animated;
        fixed (byte* bytes = data)
        {
            return !data.IsEmpty && ImageShim.Info(bytes, (nuint)data.Length, &format, &width, &height, &animated) == ImageShim.Ok && animated != 0;
        }
    }

    /// <summary>
    /// Opens an animation's decoder.
    /// </summary>
    /// <param name="data">The image's bytes, which stay pinned until the decoder is disposed.</param>
    /// <param name="targetWidth">The width asked for, in pixels; 0 for none.</param>
    /// <param name="targetHeight">The height asked for, in pixels; 0 for none.</param>
    /// <param name="maxSide">The longest side of a frame: the textures' limit.</param>
    /// <returns>The decoder, or <see langword="null"/> when the image is no animation, is broken, or has a canvas of more bytes
    /// than an array holds.</returns>
    public static ImageAnimation? TryOpen(ReadOnlyMemory<byte> data, int targetWidth, int targetHeight, int maxSide)
    {
        if (data.IsEmpty)
        {
            return null;
        }

        MemoryHandle pin = data.Pin();
        nint native;
        int format;
        int width;
        int height;
        int converts;
        int status;
        if (ImageShim.Info((byte*)pin.Pointer, (nuint)data.Length, &format, &width, &height, &converts) != ImageShim.Ok || converts == 0)
        {
            pin.Dispose();
            return null;
        }

        // A WebP's profile; a GIF has none to read, and its colors are taken as they are (ColorReader).
        ColorInfo color = ColorReader.Read(data.Span, (ImageFormat)format);
        fixed (byte* icc = color.Icc)
        {
            var nativeColor = new ImageShim.Color { Icc = icc, IccLength = (nuint)color.IccLength };
            status = ImageShim.AnimationOpen(
                (byte*)pin.Pointer,
                (nuint)data.Length,
                color.Icc is null ? null : &nativeColor,
                (nuint)Array.MaxLength,
                &native,
                &width,
                &height,
                &converts
            );
        }

        if (status != ImageShim.Ok)
        {
            pin.Dispose();
            return null;
        }

        (int w, int h) = ImageDecoder.TargetSize(width, height, targetWidth, targetHeight, maxSide);
        return new ImageAnimation(native, pin, width, height, w, h, converts != 0);
    }

    /// <inheritdoc/>
    public AnimationStep Next(Span<byte> pixels, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        ObjectDisposedException.ThrowIf(_native == 0, this);
        int length = Width * Height * 4;
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, length);
        byte* frame;
        int milliseconds;
        int status;

        // The colors are converted straight into the frame's buffer when the frame is not shrunk.
        fixed (byte* target = pixels)
        {
            fixed (byte* converted = _converted)
            {
                byte* into = Shrinks ? converted : target;
                status = ImageShim.AnimationNext(_native, _converts ? into : null, &frame, &milliseconds);
                if (status == ImageShim.Ok)
                {
                    if (Shrinks)
                    {
                        BoxResize.Shrink(
                            new ReadOnlySpan<byte>(frame, _canvasWidth * _canvasHeight * 4),
                            _canvasWidth,
                            _canvasHeight,
                            pixels,
                            Width,
                            Height
                        );
                    }
                    else if (frame != target)
                    {
                        new ReadOnlySpan<byte>(frame, length).CopyTo(pixels);
                    }
                }
            }
        }

        if (status != ImageShim.Ok)
        {
            return status == ImageShim.End ? AnimationStep.End : AnimationStep.Broken;
        }

        duration = TimeSpan.FromMilliseconds(milliseconds);
        return AnimationStep.Frame;
    }

    /// <summary>
    /// Closes the native decoder and lets the image's bytes go.
    /// </summary>
    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    private void Close()
    {
        nint native = Interlocked.Exchange(ref _native, 0);
        if (native != 0)
        {
            ImageShim.AnimationClose(native);
            _pin.Dispose();
            _ = Interlocked.Decrement(ref _open);
        }
    }
}
