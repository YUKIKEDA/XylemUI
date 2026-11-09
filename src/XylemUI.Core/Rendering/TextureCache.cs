using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The images of a window: the table of design §5.0.4 ("Owner and lifetime") that an <see cref="ImageHandle"/> points into. Each
/// image keeps its decoded pixels until the renderer has made its texture from them, and the renderer keeps the texture of each slot
/// (<see cref="IRenderTarget.Draw"/>).
/// </summary>
/// <remarks>
/// <para>
/// It belongs to the UI thread, as the tree that owns it. <see cref="Add"/> queues the image for its upload, and
/// <see cref="Release"/> queues its slot for the renderer to let its texture go; the renderer reads both queues before it draws and
/// empties them (<see cref="TakeUploads"/>, <see cref="TakeReleased"/>). Each queue holds a slot once, so with no renderer to empty
/// them they hold at most as many slots as the table.
/// </para>
/// <para>
/// A released slot takes the next image with its generation moved on, so a handle kept after its release points to no image. A
/// texture is let go through the renderer and not at once: Direct3D 11 keeps a released texture until the GPU is done with it, and
/// a backend that must wait for the GPU does so in its renderer (design §5.0.2).
/// </para>
/// </remarks>
internal sealed class TextureCache
{
    /// <summary>
    /// The longest side of an image, in pixels: the longest a texture has at feature level 10.0, the lowest the D3D11 renderer runs
    /// on (16,384 from 11.0). The decoder is asked for an image this size or smaller (row IM1, 1b-2).
    /// </summary>
    public const int MaxSide = 8192;

    private Slot[] _slots = new Slot[16];
    private int _usedSlots;
    private int[] _free = new int[16];
    private int _freeCount;
    private int[] _uploads = new int[16];
    private int _uploadCount;
    private int[] _released = new int[16];
    private int _releasedCount;

    /// <summary>
    /// Gets the number of images added and not released.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the number of slots ever used: the length of the table the renderer keeps a texture for each of.
    /// </summary>
    public int SlotCount => _usedSlots;

    /// <summary>
    /// Gets the number of times an array of the table grew; a steady state adds none.
    /// </summary>
    public int GrowthCount { get; private set; }

    /// <summary>
    /// Gets the slots whose images wait for their upload, in the order they were added.
    /// </summary>
    public ReadOnlySpan<int> PendingUploads => _uploads.AsSpan(0, _uploadCount);

    /// <summary>
    /// Gets the slots released since the renderer last took them, whose textures it lets go.
    /// </summary>
    public ReadOnlySpan<int> Released => _released.AsSpan(0, _releasedCount);

    /// <summary>
    /// Adds a decoded image and queues it for its upload.
    /// </summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="pixels">
    /// The pixels, premultiplied BGRA, 8 bits a channel, rows from the top with no padding: the decoder's output (design §5.0.4). The
    /// cache keeps the array until the upload and the caller does not write it again.
    /// </param>
    /// <returns>The handle of the image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pixels"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The width or the height is not from 1 to <see cref="MaxSide"/>.</exception>
    /// <exception cref="ArgumentException">The length of <paramref name="pixels"/> is not 4 bytes for each pixel.</exception>
    public ImageHandle Add(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSide);
        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException("The pixels are not 4 bytes for each pixel of the image.", nameof(pixels));
        }

        int index;
        if (_freeCount > 0)
        {
            index = _free[--_freeCount];
        }
        else
        {
            if (_usedSlots == _slots.Length)
            {
                Array.Resize(ref _slots, _slots.Length * 2);
                GrowthCount++;
            }

            index = _usedSlots++;
            _slots[index].Generation = 1;
        }

        ref Slot slot = ref _slots[index];
        slot.Alive = true;
        slot.Width = width;
        slot.Height = height;
        slot.Pixels = pixels;
        if (!slot.UploadQueued)
        {
            slot.UploadQueued = true;
            Append(ref _uploads, ref _uploadCount, index);
        }

        Count++;
        return new ImageHandle(index, slot.Generation);
    }

    /// <summary>
    /// Lets the image go: its handle points to no image from now on, and the renderer lets its texture go before its next draw.
    /// Releasing a handle that points to no image is a bug of its owner and fails the invariant.
    /// </summary>
    /// <param name="image">The image.</param>
    public void Release(ImageHandle image)
    {
        if (!IsAlive(image))
        {
            Invariant.Fail("An image was released that the texture cache does not hold.");
        }

        ref Slot slot = ref _slots[image.Index];
        slot.Alive = false;
        slot.Pixels = null;

        // Generation 0 is no image's, so the generation skips it when it wraps.
        slot.Generation = slot.Generation == uint.MaxValue ? 1 : slot.Generation + 1;
        if (!slot.ReleaseQueued)
        {
            slot.ReleaseQueued = true;
            Append(ref _released, ref _releasedCount, image.Index);
        }

        Append(ref _free, ref _freeCount, image.Index);
        Count--;
    }

    /// <summary>
    /// Returns whether a handle points to an image of the cache.
    /// </summary>
    /// <param name="image">The handle.</param>
    /// <returns><see langword="false"/> for a released image and for <see langword="default"/>.</returns>
    public bool IsAlive(ImageHandle image) =>
        (uint)image.Index < (uint)_usedSlots && _slots[image.Index].Alive && _slots[image.Index].Generation == image.Generation;

    /// <summary>
    /// Returns the size of an image.
    /// </summary>
    /// <param name="image">The image, which the cache holds.</param>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    public void GetSize(ImageHandle image, out int width, out int height)
    {
        if (!IsAlive(image))
        {
            Invariant.Fail("The size was asked of an image the texture cache does not hold.");
        }

        width = _slots[image.Index].Width;
        height = _slots[image.Index].Height;
    }

    /// <summary>
    /// Returns the image a slot holds now, for the renderer.
    /// </summary>
    /// <param name="index">The slot, below <see cref="SlotCount"/>.</param>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="pixels">The pixels not uploaded yet; <see langword="null"/> once they were.</param>
    /// <returns>The generation of the image; 0 when the slot holds none.</returns>
    public uint GetSlot(int index, out int width, out int height, out byte[]? pixels)
    {
        ref readonly Slot slot = ref _slots[index];
        width = slot.Width;
        height = slot.Height;
        pixels = slot.Pixels;
        return slot.Alive ? slot.Generation : 0;
    }

    /// <summary>
    /// Marks the queued images uploaded: their pixels are dropped (design §5.0.4) and the queue is emptied.
    /// </summary>
    public void TakeUploads()
    {
        foreach (int index in _uploads.AsSpan(0, _uploadCount))
        {
            ref Slot slot = ref _slots[index];
            slot.UploadQueued = false;
            slot.Pixels = null;
        }

        _uploadCount = 0;
    }

    /// <summary>
    /// Empties the queue of released slots, whose textures the renderer has let go.
    /// </summary>
    public void TakeReleased()
    {
        foreach (int index in _released.AsSpan(0, _releasedCount))
        {
            _slots[index].ReleaseQueued = false;
        }

        _releasedCount = 0;
    }

    private void Append(ref int[] queue, ref int count, int index)
    {
        if (count == queue.Length)
        {
            Grow(ref queue);
        }

        queue[count++] = index;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(ref int[] queue)
    {
        Array.Resize(ref queue, queue.Length * 2);
        GrowthCount++;
    }

    // A slot of the table: its generation, whether it holds an image, the image's size and its pixels until the upload, and whether
    // it is in the queue of uploads or of releases.
    private struct Slot
    {
        public uint Generation;
        public bool Alive;
        public int Width;
        public int Height;
        public byte[]? Pixels;
        public bool UploadQueued;
        public bool ReleaseQueued;
    }
}
