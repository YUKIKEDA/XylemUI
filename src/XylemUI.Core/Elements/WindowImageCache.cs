using System.Runtime.InteropServices;
using XylemUI.Imaging;

namespace XylemUI;

/// <summary>
/// The decoded images of a window (design §5.0.4, "Cache"; row IM1, 3a): the elements that show the same source at the same decode
/// size share one decode and one <see cref="ImageHandle"/>, and an image no element shows stays, within two limits, for the next
/// element that asks for it. The tree owns it, since the handles and their textures are the window's.
/// </summary>
/// <remarks>
/// <para>
/// The index finds an entry by its key. An entry is in one or more of three sets: decoding, counted (within the limits, in the order
/// of use), and shown (an element shows it). The rules are the design's: an entry the limits take out of the counted set is released
/// at once when no element shows it, and lives while one does; an element that leaves takes one from the entry's count, and the entry
/// stays when it is counted.
/// </para>
/// <para>
/// It belongs to the UI thread. The index is a dictionary keyed on a struct and the order of use is linked through the entries, so
/// finding a key, moving an entry to the newest end, and taking one out of the counted set allocate nothing; an entry is made only
/// when a decode is asked for.
/// </para>
/// </remarks>
internal sealed class WindowImageCache
{
    private readonly TextureCache _images;
    private readonly Dictionary<ImageKey, ImageEntry> _index = [];

    // The entries Evict and Clear took out of the index that elements still show or wait for: the tree releases them with the rest.
    private readonly List<ImageEntry> _outside = [];
    private readonly Action<ImageRequest> _complete;
    private readonly UiTree _tree;

    // The animations a frame drew, which ask for their next frames when the frame ends.
    private readonly List<ImagePlayback> _drawn = [];
    private ImageEntry? _oldest;
    private ImageEntry? _newest;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowImageCache"/> class, with the limits of <see cref="ImageCache"/> on the
    /// calling thread, which later changes of them reach.
    /// </summary>
    /// <param name="images">The window's images, which the cache adds to and releases from.</param>
    /// <param name="tree">The window's tree, whose frame clock and timers the animated images play on.</param>
    public WindowImageCache(TextureCache images, UiTree tree)
    {
        _images = images;
        _tree = tree;
        _complete = Complete;
        ImageCache.Track(this, out int count, out long bytes);
        MaximumCount = count;
        MaximumBytes = bytes;
    }

    /// <summary>
    /// Gets the number of entries the index finds.
    /// </summary>
    public int Count => _index.Count;

    /// <summary>
    /// Gets the number of entries in the counted set; at most <see cref="MaximumCount"/>.
    /// </summary>
    public int CountedCount { get; private set; }

    /// <summary>
    /// Gets the bytes of the entries in the counted set; at most <see cref="MaximumBytes"/>.
    /// </summary>
    public long CountedBytes { get; private set; }

    /// <summary>
    /// Gets the most entries the counted set holds.
    /// </summary>
    public int MaximumCount { get; private set; }

    /// <summary>
    /// Gets the most bytes the counted set holds.
    /// </summary>
    public long MaximumBytes { get; private set; }

    /// <summary>
    /// Gets the number of decodes asked for.
    /// </summary>
    public int Decodes { get; private set; }

    /// <summary>
    /// Gets the number of times an element asked for a key the index had: it shared the entry's image or its decode.
    /// </summary>
    public int Hits { get; private set; }

    /// <summary>
    /// Gets the number of times an animation showed its next frame.
    /// </summary>
    public int Advances { get; private set; }

    /// <summary>
    /// Gets the number of elements the animations that play count as showing them; for the tests.
    /// </summary>
    public int ShowingElements
    {
        get
        {
            int count = 0;
            foreach (ImageEntry entry in _index.Values)
            {
                count += entry.Playback?.ShowingCount ?? 0;
            }

            foreach (ImageEntry entry in CollectionsMarshal.AsSpan(_outside))
            {
                count += entry.Playback?.ShowingCount ?? 0;
            }

            return count;
        }
    }

    /// <summary>
    /// Gets the number of entries a limit took out of the counted set.
    /// </summary>
    public int EvictedCount { get; private set; }

    /// <summary>
    /// Gets the number of times the counted set did not take an image: it was larger than <see cref="MaximumBytes"/>, or
    /// <see cref="MaximumCount"/> was 0.
    /// </summary>
    public int RefusedCount { get; private set; }

    /// <summary>
    /// Gets the frame clock the animated images join when their next frame is due.
    /// </summary>
    public AnimationClock Clock => _tree.Animations;

    /// <summary>
    /// Notes an animation that the frame running drew.
    /// </summary>
    /// <param name="playback">The animation's playing.</param>
    public void NoteDrawn(ImagePlayback playback) => _drawn.Add(playback);

    /// <summary>
    /// Ends a frame of the window that drew: each animation it drew asks for its next frame ("How it advances").
    /// </summary>
    public void FrameEnded()
    {
        if (_drawn.Count == 0)
        {
            return;
        }

        bool reduce = Clock.ReduceMotion;
        foreach (ImagePlayback playback in CollectionsMarshal.AsSpan(_drawn))
        {
            playback.AfterDrawn(_images, _tree.Time, reduce);
        }

        _drawn.Clear();
    }

    /// <summary>
    /// Tells the animations that the window's setting of less motion changed ("When it stops"): with the setting they stay on the
    /// frame they show, and without it they go on from that frame.
    /// </summary>
    public void MotionChanged()
    {
        foreach (ImageEntry entry in _index.Values)
        {
            entry.Playback?.MotionChanged();
            entry.Playback?.Repaint();
        }

        foreach (ImageEntry entry in CollectionsMarshal.AsSpan(_outside))
        {
            entry.Playback?.MotionChanged();
            entry.Playback?.Repaint();
        }
    }

    /// <summary>
    /// Shows an animation's next frame: the pixels its playing decoded go to the image's texture.
    /// </summary>
    /// <param name="entry">The animation's entry.</param>
    /// <param name="pixels">The frame.</param>
    public void Advance(ImageEntry entry, byte[] pixels)
    {
        _images.Rewrite(entry.Image, pixels);
        Advances++;
    }

    /// <summary>
    /// Ends the playing of an animation that played the plays its file asks for: the decoder goes, and the entry is the still
    /// image of its last frame, counted as one ("Animations in the cache").
    /// </summary>
    /// <param name="entry">The animation's entry.</param>
    public void EndPlaying(ImageEntry entry)
    {
        entry.Playback!.Close();
        entry.Playback = null;
        _images.DropPixels(entry.Image);
        long bytes = (long)entry.Width * entry.Height * 4;
        if (entry.Counted)
        {
            CountedBytes += bytes - entry.Bytes;
        }

        entry.Bytes = bytes;
    }

    /// <summary>
    /// Fails an animation whose next frame could not be decoded ("Untrusted input"): the decoder and the image go, the entry is
    /// forgotten as a decode that failed is, and the elements that showed it show their error parts.
    /// </summary>
    /// <param name="entry">The animation's entry.</param>
    /// <param name="showing">The elements that show it.</param>
    public void FailPlaying(ImageEntry entry, List<ImageBlock> showing)
    {
        entry.Playback!.Close();
        entry.Playback = null;
        if (entry.Counted)
        {
            Uncount(entry);
        }

        _images.Release(entry.Image);
        entry.Image = default;
        entry.State = ImageEntryState.Failed;
        Forget(entry);

        // From the end: an element's error part can run the app's code, which can dispose other elements, and those do not touch
        // the list of a playing that is closed.
        for (int i = showing.Count - 1; i >= 0; i--)
        {
            showing[i].Failed(entry);
        }
    }

    /// <summary>
    /// Gives an element the entry of a key and counts the element in it ("An element asks for a key"). An entry that is
    /// <see cref="ImageEntryState.Ready"/> is shown at once; for one that is <see cref="ImageEntryState.Decoding"/> the element
    /// waits, and <see cref="ImageBlock.Completed"/> tells it the result; one that is <see cref="ImageEntryState.Failed"/> is not
    /// kept, and the element holds nothing of it.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="block">The element.</param>
    /// <param name="context">The UI thread's context the element took when it was made, which a decode's result is posted to;
    /// <see langword="null"/> where no loop runs, and the decode then runs here.</param>
    /// <returns>The entry.</returns>
    public ImageEntry Acquire(ImageKey key, ImageBlock block, SynchronizationContext? context)
    {
        if (_index.TryGetValue(key, out ImageEntry? entry))
        {
            Hits++;
            entry.Elements++;
            if (entry.State == ImageEntryState.Decoding)
            {
                Wait(entry, block);
            }
            else if (entry.Counted)
            {
                MoveToNewest(entry);
            }
            else
            {
                // Out of the counted set and alive because an element shows it: back in, as a decode that ends.
                Check(entry);
            }

            return entry;
        }

        entry = new ImageEntry(key) { Elements = 1, Indexed = true };
        _index.Add(key, entry);
        Decodes++;
        if (context is null)
        {
            Finish(entry, ImageDecodeQueue.Decode(key.Source, key.Width, key.Height), null);
            return entry;
        }

        Wait(entry, block);
        var request = new ImageRequest(key.Source, key.Width, key.Height, context, _complete) { Entry = entry };
        entry.Request = request;
        ImageDecodeQueue.Enqueue(request);
        return entry;
    }

    /// <summary>
    /// Takes an element out of an entry's count ("An element leaves"): its scope was disposed, or its source changed. An entry with
    /// no element left stays when it is counted and is released when it is not; a decode nobody waits for is skipped when it has not
    /// started, and its entry goes.
    /// </summary>
    /// <param name="entry">The entry <see cref="Acquire"/> gave the element.</param>
    /// <param name="block">The element.</param>
    public void Leave(ImageEntry entry, ImageBlock block)
    {
        if (entry.State is ImageEntryState.Failed or ImageEntryState.Gone)
        {
            // The cache forgot the entry already, and nothing is left to release. Gone: the tree let every entry go while the
            // element lived on. Failed: an element that is told of the failure does not keep the entry (ImageBlock.Completed), so
            // this is an element that waited behind another and was disposed by that one's error part before its own turn.
            return;
        }

        entry.Elements--;
        if (entry.State == ImageEntryState.Decoding)
        {
            StopWaiting(entry, block);
            if (entry.Elements == 0 && entry.Request!.TryCancel())
            {
                entry.Request = null;
                entry.State = ImageEntryState.Gone;
                Forget(entry);
            }

            // A decode that runs ends as an entry no element shows (Finish).
            return;
        }

        if (entry.Elements == 0 && !entry.Counted)
        {
            Release(entry);
        }
    }

    /// <summary>
    /// Sets the limits and takes the oldest entries out of the counted set while it is over one.
    /// </summary>
    /// <param name="count">The most entries; 0 keeps no image that no element shows.</param>
    /// <param name="bytes">The most bytes.</param>
    public void Limit(int count, long bytes)
    {
        MaximumCount = count;
        MaximumBytes = bytes;
        Trim();
    }

    /// <summary>
    /// Takes the entries of a source out of the index, whatever their decode sizes (<see cref="ImageCache.Evict"/>).
    /// </summary>
    /// <param name="source">The source.</param>
    public void Evict(ImageSource source)
    {
        // Dictionary's Remove does not end an enumeration in progress (documented from .NET Core 3.0).
        foreach (KeyValuePair<ImageKey, ImageEntry> pair in _index)
        {
            if (pair.Key.Source.Equals(source))
            {
                Detach(pair.Value);
            }
        }
    }

    /// <summary>
    /// Takes every entry out of the index (<see cref="ImageCache.Clear"/>).
    /// </summary>
    public void Clear()
    {
        foreach (ImageEntry entry in _index.Values)
        {
            Detach(entry);
        }
    }

    /// <summary>
    /// Lets every entry go ("When the tree is thrown away"): the images are released, the decodes not started are skipped, and the
    /// result of one that runs is dropped when it arrives. The host calls it when its window closes.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (ImageEntry entry in _index.Values)
        {
            entry.Indexed = false;
            Drop(entry);
        }

        _index.Clear();
        foreach (ImageEntry entry in CollectionsMarshal.AsSpan(_outside))
        {
            Drop(entry);
        }

        _outside.Clear();
        _drawn.Clear();
        _oldest = null;
        _newest = null;
        CountedCount = 0;
        CountedBytes = 0;
    }

    // A decode's result on the UI thread. A request its entry no longer holds was dropped: by Evict or Clear with nobody waiting,
    // or by the tree.
    private void Complete(ImageRequest request)
    {
        if (request.Entry is not { } entry || !ReferenceEquals(entry.Request, request))
        {
            request.Result.Frames?.Dispose();
            return;
        }

        entry.Request = null;
        Finish(entry, request.Result, request.Context);
        ImageBlock? block = entry.FirstWaiting;
        entry.FirstWaiting = null;
        entry.LastWaiting = null;
        while (block is not null)
        {
            // The link is read first: an element's part can run the app's code, which can dispose the elements after it.
            ImageBlock? next = block.NextWaiting;
            block.NextWaiting = null;
            block.Completed(entry);
            block = next;
        }
    }

    // "A decode ends" and "A decode fails".
    private void Finish(ImageEntry entry, ImageResult result, SynchronizationContext? context)
    {
        if (result.Pixels is null)
        {
            entry.State = ImageEntryState.Failed;
            Forget(entry);
            return;
        }

        // An animation plays where a loop runs and the tree's images play; elsewhere its first frame is a still image.
        IAnimationFrames? frames = result.Frames;
        if (frames is not null && (context is null || !_tree.AnimatesImages))
        {
            frames.Dispose();
            frames = null;
        }

        // An animation counts for what it holds ("The bytes counted"): the texture and the buffer it uploads from, the decoder's
        // canvases, and the file's bytes when the decode read them.
        long bytes = (long)result.Width * result.Height * 4;
        if (frames is not null)
        {
            bytes = (bytes * 2) + frames.HeldBytes + result.SourceBytes;
        }

        if (entry.Elements == 0 && !(entry.Indexed && Fits(bytes)))
        {
            frames?.Dispose();

            // No element shows it and the counted set does not take it, so it would be released at once: the pixels are dropped.
            if (entry.Indexed)
            {
                RefusedCount++;
            }

            entry.State = ImageEntryState.Gone;
            Forget(entry);
            return;
        }

        entry.Image = _images.Add(result.Width, result.Height, result.Pixels);
        entry.Width = result.Width;
        entry.Height = result.Height;
        entry.Bytes = bytes;
        entry.State = ImageEntryState.Ready;
        if (frames is not null)
        {
            entry.Playback = new ImagePlayback(this, entry, frames, result.Pixels, result.Duration, context!);
        }

        if (entry.Indexed)
        {
            Check(entry);
        }
    }

    private bool Fits(long bytes) => bytes <= MaximumBytes && MaximumCount != 0;

    // The check of "A decode ends": into the counted set at the newest end when it fits, then the oldest out while over a limit.
    // An image larger than the byte limit is not counted and takes no other out.
    private void Check(ImageEntry entry)
    {
        if (Fits(entry.Bytes))
        {
            entry.Counted = true;
            entry.Older = _newest;
            if (_newest is null)
            {
                _oldest = entry;
            }
            else
            {
                _newest.Newer = entry;
            }

            _newest = entry;
            CountedCount++;
            CountedBytes += entry.Bytes;
        }
        else
        {
            RefusedCount++;
        }

        Trim();
    }

    // The oldest out of the counted set while it is over a limit, whether an element shows it or not ("An entry that leaves the
    // counted set"): released at once with no element, alive and still found through the index with one.
    private void Trim()
    {
        while (CountedCount > MaximumCount || CountedBytes > MaximumBytes)
        {
            ImageEntry oldest = _oldest!;
            Uncount(oldest);
            EvictedCount++;
            if (oldest.Elements == 0)
            {
                Release(oldest);
            }
        }
    }

    private void MoveToNewest(ImageEntry entry)
    {
        if (ReferenceEquals(entry, _newest))
        {
            return;
        }

        // Not the newest, so one is newer.
        ImageEntry newer = entry.Newer!;
        newer.Older = entry.Older;
        if (entry.Older is null)
        {
            _oldest = newer;
        }
        else
        {
            entry.Older.Newer = newer;
        }

        entry.Older = _newest;
        entry.Newer = null;
        _newest!.Newer = entry;
        _newest = entry;
    }

    private void Uncount(ImageEntry entry)
    {
        if (entry.Older is null)
        {
            _oldest = entry.Newer;
        }
        else
        {
            entry.Older.Newer = entry.Newer;
        }

        if (entry.Newer is null)
        {
            _newest = entry.Older;
        }
        else
        {
            entry.Newer.Older = entry.Older;
        }

        entry.Older = null;
        entry.Newer = null;
        entry.Counted = false;
        CountedCount--;
        CountedBytes -= entry.Bytes;
    }

    // Takes an entry out of the index (Evict, Clear). One that elements show or wait for lives on outside it; one that nobody does
    // goes at once, its decode skipped or its result dropped.
    private void Detach(ImageEntry entry)
    {
        _ = _index.Remove(entry.Key);
        entry.Indexed = false;
        if (entry.Counted)
        {
            Uncount(entry);
        }

        if (entry.Elements > 0)
        {
            _outside.Add(entry);
            return;
        }

        Drop(entry);
    }

    // Releases the image of an entry that is ready, and takes the entry out of the index or the list outside it.
    private void Release(ImageEntry entry)
    {
        entry.Playback?.Close();
        entry.Playback = null;
        _images.Release(entry.Image);
        entry.Image = default;
        entry.State = ImageEntryState.Gone;
        Forget(entry);
    }

    private void Forget(ImageEntry entry)
    {
        if (entry.Indexed)
        {
            _ = _index.Remove(entry.Key);
            entry.Indexed = false;
        }
        else
        {
            _ = _outside.Remove(entry);
        }
    }

    // Lets go of an entry that is in neither the index nor the list outside it any more.
    private void Drop(ImageEntry entry)
    {
        if (entry.State == ImageEntryState.Decoding)
        {
            _ = entry.Request?.TryCancel();
            entry.Request = null;
        }
        else if (entry.State == ImageEntryState.Ready)
        {
            entry.Playback?.Close();
            entry.Playback = null;
            _images.Release(entry.Image);
            entry.Image = default;
        }

        entry.State = ImageEntryState.Gone;
        entry.Counted = false;
        entry.Older = null;
        entry.Newer = null;

        // The elements that wait live on when the tree lets every entry go (ReleaseAll), and can ask for another source. One that
        // kept its link would bring the elements behind it into that entry's waiting list, and the entry's result would end the
        // waiting of elements that wait for another decode.
        ImageBlock? block = entry.FirstWaiting;
        entry.FirstWaiting = null;
        entry.LastWaiting = null;
        while (block is not null)
        {
            ImageBlock? next = block.NextWaiting;
            block.NextWaiting = null;
            block = next;
        }
    }

    private static void Wait(ImageEntry entry, ImageBlock block)
    {
        if (entry.LastWaiting is null)
        {
            entry.FirstWaiting = block;
        }
        else
        {
            entry.LastWaiting.NextWaiting = block;
        }

        entry.LastWaiting = block;
    }

    private static void StopWaiting(ImageEntry entry, ImageBlock block)
    {
        ImageBlock? previous = null;
        for (ImageBlock? waiting = entry.FirstWaiting; waiting is not null; previous = waiting, waiting = waiting.NextWaiting)
        {
            if (!ReferenceEquals(waiting, block))
            {
                continue;
            }

            if (previous is null)
            {
                entry.FirstWaiting = block.NextWaiting;
            }
            else
            {
                previous.NextWaiting = block.NextWaiting;
            }

            if (ReferenceEquals(entry.LastWaiting, block))
            {
                entry.LastWaiting = previous;
            }

            block.NextWaiting = null;
            return;
        }
    }
}
