using XylemUI.Imaging;

namespace XylemUI;

/// <summary>
/// The playing of an animated image of a window's image cache (design §5.0.4, "Animations", "How it advances"): the entry's
/// decoder, the one buffer its frames are decoded into and uploaded from, the timer of the frame that shows, and the work that
/// decodes the next frame on a worker.
/// </summary>
/// <remarks>
/// <para>
/// After a frame of the window drew the image, the next frame of the animation is asked of a worker and the timer of the frame that
/// shows is started. When the timer rang and the pixels arrived, the playing joins the frame clock's list (design §2.14), which asks
/// for a frame of the window; in that frame it gives the pixels to the texture and marks the elements that show the image for
/// paint, and leaves the list. The next step waits for that frame to draw the image: an image that is not drawn does not advance,
/// and a window whose frames do not run keeps the playing in the list.
/// </para>
/// <para>
/// Everything here runs on the UI thread but <see cref="Run"/>, which a worker runs while <see cref="_decoding"/> is set; the UI
/// thread does not touch the decoder or the buffer meanwhile. Playing allocates nothing on the UI thread: the work, the timer, and
/// the buffer are used again for each frame.
/// </para>
/// </remarks>
internal sealed class ImagePlayback : Ticking
{
    private readonly WindowImageCache _cache;
    private readonly ImageEntry _entry;
    private readonly IAnimationFrames _frames;
    private readonly byte[] _buffer;
    private readonly SynchronizationContext _ui;
    private readonly FrameWork _work;
    private readonly List<ImageBlock> _showing = [];
    private ITimer? _timer;

    // How long the frame that shows stays, and what the worker gave for the next.
    private TimeSpan _duration;
    private TimeSpan _nextDuration;
    private AnimationStep _nextStep;

    // The next frame was asked for; its timer rang; its pixels arrived.
    private bool _waiting;
    private bool _due;
    private bool _arrived;

    // A worker is in the decoder; the playing was closed meanwhile and the decoder goes when the worker is back.
    private bool _decoding;
    private bool _closed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImagePlayback"/> class, for an entry whose first frame is decoded.
    /// </summary>
    /// <param name="cache">The window's cache.</param>
    /// <param name="entry">The animation's entry.</param>
    /// <param name="frames">The animation's decoder, which the playing owns from now on.</param>
    /// <param name="buffer">The first frame's pixels: the buffer each next frame is decoded into and uploaded from.</param>
    /// <param name="duration">How long the first frame shows.</param>
    /// <param name="ui">The UI thread's context.</param>
    public ImagePlayback(
        WindowImageCache cache,
        ImageEntry entry,
        IAnimationFrames frames,
        byte[] buffer,
        TimeSpan duration,
        SynchronizationContext ui
    )
    {
        _cache = cache;
        _entry = entry;
        _frames = frames;
        _buffer = buffer;
        _duration = duration;
        _ui = ui;
        _work = new FrameWork(this);
    }

    /// <summary>
    /// Gets a value indicating whether a frame of the window drew the image since the cache last looked.
    /// </summary>
    public bool Drawn { get; private set; }

    /// <summary>
    /// Gets the number of elements counted as showing the image.
    /// </summary>
    public int ShowingCount => _showing.Count;

    /// <summary>
    /// Gets the bytes the decoder holds besides the texture and the buffer.
    /// </summary>
    public long HeldBytes => _frames.HeldBytes;

    /// <summary>
    /// Counts an element that shows the image, whose node is marked for paint when the image advances.
    /// </summary>
    /// <param name="block">The element.</param>
    public void Add(ImageBlock block)
    {
        block.ShowingIndex = _showing.Count;
        _showing.Add(block);
    }

    /// <summary>
    /// Takes out an element that <see cref="Add"/> counted.
    /// </summary>
    /// <param name="block">The element.</param>
    public void Remove(ImageBlock block)
    {
        int last = _showing.Count - 1;
        ImageBlock moved = _showing[last];
        _showing[block.ShowingIndex] = moved;
        moved.ShowingIndex = block.ShowingIndex;
        _showing.RemoveAt(last);
        block.ShowingIndex = -1;
    }

    /// <summary>
    /// Notes that an element drew the image in the frame running; the cache looks at the end of the frame
    /// (<see cref="WindowImageCache.FrameEnded"/>).
    /// </summary>
    public void NoteDrawn()
    {
        if (!Drawn)
        {
            Drawn = true;
            _cache.NoteDrawn(this);
        }
    }

    /// <summary>
    /// Asks for the next frame after a frame of the window drew the image: the decode on a worker, and the timer of the frame that
    /// shows. Nothing is asked while one is asked for already, or while the window shows less motion.
    /// </summary>
    /// <param name="images">The window's images.</param>
    /// <param name="time">The clock the timer runs on.</param>
    /// <param name="reduceMotion">Whether the window shows less motion (design §2.14).</param>
    public void AfterDrawn(TextureCache images, TimeProvider time, bool reduceMotion)
    {
        Drawn = false;
        if (_waiting || _closed || reduceMotion)
        {
            return;
        }

        // The worker writes the buffer: an upload of it that no renderer took is withdrawn, and the texture keeps the frame before.
        _ = images.Withdraw(_entry.Image);
        _waiting = true;
        _due = false;
        _arrived = false;
        _decoding = true;
        ImageDecodeQueue.Enqueue(_work);
        if (_timer is null)
        {
            _timer = time.CreateTimer(static state => ((ImagePlayback)state!).TimerRang(), this, _duration, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _ = _timer.Change(_duration, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Looks again at whether the next frame can show, after the window's setting of less motion changed: a frame that waited for
    /// the setting to go joins the frame clock's list.
    /// </summary>
    public void MotionChanged() => TryJoin();

    /// <summary>
    /// Marks the elements that show the image for paint.
    /// </summary>
    public void Repaint()
    {
        foreach (ImageBlock block in _showing)
        {
            block.Repaint();
        }
    }

    /// <summary>
    /// Ends the playing: the timer stops, the playing leaves the frame clock's list, and the decoder is closed, once the worker that
    /// is in it is back.
    /// </summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _timer?.Dispose();
        _timer = null;
        _cache.Clock.Remove(this);
        if (!_decoding)
        {
            _frames.Dispose();
        }
    }

    /// <inheritdoc/>
    internal override bool Tick(TimeSpan now)
    {
        _waiting = false;
        switch (_nextStep)
        {
            case AnimationStep.Frame:
                _duration = _nextDuration;
                _cache.Advance(_entry, _buffer);
                Repaint();
                break;
            case AnimationStep.End:
                _cache.EndPlaying(_entry);
                break;
            default:
                _cache.FailPlaying(_entry, _showing);
                break;
        }

        return false;
    }

    // On the timer's thread, or on the UI thread under a test clock.
    private void TimerRang()
    {
        if (SynchronizationContext.Current == _ui)
        {
            TimerElapsed();
        }
        else
        {
            _ui.Post(static state => ((ImagePlayback)state!).TimerElapsed(), this);
        }
    }

    private void TimerElapsed()
    {
        // The timer rings once for each frame asked for, so a frame waits here. A callback that was posted before the playing was
        // closed finds it closed in TryJoin.
        _due = true;
        TryJoin();
    }

    // On a worker: the next frame into the buffer.
    private void Run()
    {
        _nextStep = _frames.Next(_buffer, out _nextDuration);
        _ui.Post(static state => ((ImagePlayback)state!).Decoded(), this);
    }

    private void Decoded()
    {
        _decoding = false;
        if (_closed)
        {
            _frames.Dispose();
            return;
        }

        _arrived = true;
        TryJoin();
    }

    private void TryJoin()
    {
        if (_waiting && _due && _arrived && !_closed && !_cache.Clock.ReduceMotion)
        {
            _cache.Clock.Add(this);
        }
    }

    private sealed class FrameWork(ImagePlayback playback) : DecodeWork
    {
        public override void Run() => playback.Run();
    }
}
