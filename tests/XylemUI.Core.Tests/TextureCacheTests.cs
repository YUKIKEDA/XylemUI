namespace XylemUI.Core.Tests;

// The table of a window's images (design §5.0.4, "Owner and lifetime"; row IM1): generational handles, the queue of uploads that
// the renderer empties and that drops the pixels, and the queue of released slots whose textures it lets go.
public sealed class TextureCacheTests
{
    /// <summary>
    /// An added image is held with its size and its pixels, and queued for its upload once; taking the uploads drops the pixels.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty cache.
    ///
    /// Steps:
    /// - Add a 3 x 2 image; read its slot; take the uploads; read the slot again.
    ///
    /// Expected:
    /// - The handle is alive, its generation is 1, and the size is 3 x 2; the slot is queued once with the array given.
    /// - After taking the uploads, the queue is empty and the slot has no pixels, while the image is still alive.
    /// </remarks>
    [Fact]
    public void Add_QueuesTheImage_AndTakingTheUploadsDropsItsPixels()
    {
        var cache = new TextureCache();
        byte[] pixels = new byte[3 * 2 * 4];

        ImageHandle image = cache.Add(3, 2, pixels);

        Assert.True(cache.IsAlive(image));
        Assert.Equal(1u, image.Generation);
        cache.GetSize(image, out int width, out int height);
        Assert.Equal((3, 2), (width, height));
        Assert.Equal([image.Index], cache.PendingUploads.ToArray());
        Assert.Equal(1u, cache.GetSlot(image.Index, out _, out _, out byte[]? queued));
        Assert.Same(pixels, queued);

        cache.TakeUploads();

        Assert.True(cache.PendingUploads.IsEmpty);
        Assert.Equal(1u, cache.GetSlot(image.Index, out _, out _, out queued));
        Assert.Null(queued);
        Assert.True(cache.IsAlive(image));
    }

    /// <summary>
    /// A released image's handle points to no image, its slot is queued once for the renderer, and the next image takes the slot
    /// with the next generation, which the old handle does not reach.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A cache with one image, its upload taken.
    ///
    /// Steps:
    /// - Release it; add another image; take the released slots.
    ///
    /// Expected:
    /// - After the release, the old handle is not alive, the count is 0, and the slot is queued as released, its generation 0.
    /// - The new image has the same slot and generation 2; the old handle is still not alive; the released queue empties.
    /// </remarks>
    [Fact]
    public void Release_MovesTheSlotsGeneration_AndQueuesItOnce()
    {
        var cache = new TextureCache();
        ImageHandle first = cache.Add(1, 1, new byte[4]);
        cache.TakeUploads();

        cache.Release(first);

        Assert.False(cache.IsAlive(first));
        Assert.Equal(0, cache.Count);
        Assert.Equal([first.Index], cache.Released.ToArray());
        Assert.Equal(0u, cache.GetSlot(first.Index, out _, out _, out _));

        ImageHandle second = cache.Add(2, 2, new byte[16]);

        Assert.Equal(first.Index, second.Index);
        Assert.Equal(2u, second.Generation);
        Assert.False(cache.IsAlive(first));
        Assert.True(cache.IsAlive(second));
        Assert.Equal(1, cache.SlotCount);

        cache.TakeReleased();

        Assert.True(cache.Released.IsEmpty);
    }

    /// <summary>
    /// Each queue holds a slot once, so with no renderer to empty them, images added and released again and again in one slot keep
    /// the queues at one entry and the arrays from growing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty cache, never drawn.
    ///
    /// Steps:
    /// - 1,000 times: add a 1 x 1 image and release it.
    ///
    /// Expected:
    /// - Each queue holds the one slot; the table has one slot; the growth count is 0.
    /// </remarks>
    [Fact]
    public void Queues_WithNoRenderer_HoldEachSlotOnce()
    {
        var cache = new TextureCache();
        for (int i = 0; i < 1_000; i++)
        {
            cache.Release(cache.Add(1, 1, new byte[4]));
        }

        Assert.Equal([0], cache.PendingUploads.ToArray());
        Assert.Equal([0], cache.Released.ToArray());
        Assert.Equal(1, cache.SlotCount);
        Assert.Equal(0, cache.GrowthCount);
    }

    /// <summary>
    /// An image that is rewritten is queued once for its upload and keeps its pixels past it, for the next frame of an animation
    /// to be written into; an upload that waits can be withdrawn; and the kept pixels can be dropped, at once or after the upload
    /// that waits (design §5.0.4, "Owner and lifetime").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A cache with one 2 x 1 image whose upload was taken.
    ///
    /// Steps:
    /// - Rewrite it twice with one array, take the uploads, and withdraw. Rewrite and withdraw twice. Rewrite, drop the pixels,
    ///   and take the uploads. Rewrite, take the uploads, and drop the pixels. Rewrite with 4 bytes, and with null. Release the
    ///   image; rewrite, withdraw, and drop.
    ///
    /// Expected:
    /// - The image is queued once, and the slot holds the array after the upload; nothing is there to withdraw. A waiting upload
    ///   is withdrawn once and the queue is empty. Pixels dropped while an upload waits go with the upload, and at once
    ///   otherwise. The wrong length throws ArgumentException and null ArgumentNullException. On a released image, rewriting
    ///   fails the invariant (checked in the Debug configuration), withdrawing returns false, and dropping does nothing.
    /// </remarks>
    [Fact]
    public void Rewrite_QueuesTheImageAgain_AndKeepsItsPixelsPastTheUpload()
    {
        var cache = new TextureCache();
        ImageHandle image = cache.Add(2, 1, new byte[8]);
        cache.TakeUploads();
        byte[] next = new byte[8];

        cache.Rewrite(image, next);
        cache.Rewrite(image, next);
        Assert.Equal([image.Index], cache.PendingUploads.ToArray());
        cache.TakeUploads();
        _ = cache.GetSlot(image.Index, out _, out _, out byte[]? kept);
        Assert.Same(next, kept);
        Assert.False(cache.Withdraw(image));

        cache.Rewrite(image, next);
        Assert.True(cache.Withdraw(image));
        Assert.Equal(0, cache.PendingUploads.Length);
        Assert.False(cache.Withdraw(image));

        cache.Rewrite(image, next);
        cache.DropPixels(image);
        _ = cache.GetSlot(image.Index, out _, out _, out byte[]? waiting);
        Assert.Same(next, waiting);
        cache.TakeUploads();
        _ = cache.GetSlot(image.Index, out _, out _, out byte[]? dropped);
        Assert.Null(dropped);

        cache.Rewrite(image, next);
        cache.TakeUploads();
        cache.DropPixels(image);
        _ = cache.GetSlot(image.Index, out _, out _, out byte[]? droppedAtOnce);
        Assert.Null(droppedAtOnce);

        _ = Assert.Throws<ArgumentException>(() => cache.Rewrite(image, new byte[4]));
        _ = Assert.Throws<ArgumentNullException>(() => cache.Rewrite(image, null!));
        cache.Release(image);
#if DEBUG
        // Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.
        _ = Assert.Throws<InvariantViolationException>(() => cache.Rewrite(image, next));
#endif
        Assert.False(cache.Withdraw(image));
        cache.DropPixels(image);
    }

    /// <summary>
    /// The arguments of Add are checked: the pixels, a side from 1 to the longest, and 4 bytes a pixel.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty cache.
    ///
    /// Steps:
    /// - Add null pixels; a width of 0; a height past MaxSide; 3 x 3 with 35 bytes.
    ///
    /// Expected:
    /// - ArgumentNullException, ArgumentOutOfRangeException twice, and ArgumentException; nothing is held.
    /// </remarks>
    [Fact]
    public void Add_BadArguments_Throw()
    {
        var cache = new TextureCache();

        _ = Assert.Throws<ArgumentNullException>(() => cache.Add(1, 1, null!));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => cache.Add(0, 1, []));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => cache.Add(1, TextureCache.MaxSide + 1, new byte[4 * (TextureCache.MaxSide + 1)]));
        _ = Assert.Throws<ArgumentException>(() => cache.Add(3, 3, new byte[35]));
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.SlotCount);
    }

    /// <summary>
    /// Releasing a handle that points to no image (released before, or <see langword="default"/>) is a bug of its owner and fails
    /// the invariant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release builds end the process instead).
    /// - A cache with one image, released.
    ///
    /// Steps:
    /// - Release it again; release the default handle; ask the size of the released one.
    ///
    /// Expected:
    /// - Each throws InvariantViolationException.
    /// </remarks>
    [Fact]
    public void Release_AHandleOfNoImage_FailsTheInvariant()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
        var cache = new TextureCache();
        ImageHandle image = cache.Add(1, 1, new byte[4]);
        cache.Release(image);

        _ = Assert.Throws<InvariantViolationException>(() => cache.Release(image));
        _ = Assert.Throws<InvariantViolationException>(() => cache.Release(default));
        _ = Assert.Throws<InvariantViolationException>(() => cache.GetSize(image, out _, out _));
    }
}
