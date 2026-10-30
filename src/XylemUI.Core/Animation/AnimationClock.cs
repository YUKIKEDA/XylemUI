namespace XylemUI;

/// <summary>
/// Something a window's animation list moves on each frame (design §2.14, "The frame clock"): a transition, an animation, or the
/// motion of a <c>Show</c>.
/// </summary>
internal abstract class Ticking
{
    /// <summary>
    /// Gets or sets where it is in the list, or −1 when it is not running.
    /// </summary>
    internal int Slot { get; set; } = -1;

    /// <summary>
    /// Moves it to a frame's time.
    /// </summary>
    /// <param name="now">The frame's time (<see cref="WindowFrames.FrameTime"/>).</param>
    /// <returns><see langword="true"/> while it still runs after this frame.</returns>
    internal abstract bool Tick(TimeSpan now);
}

/// <summary>
/// The animations of one window that are running (design §2.14, "The frame clock"). The frames move them at the start of each frame,
/// before the layout pass, with the frame's time; while the list is not empty, each frame asks for the next one, and when it is
/// empty no frame is asked for.
/// </summary>
/// <param name="tree">The window's tree, which asks for the frames.</param>
internal sealed class AnimationClock(UiTree tree)
{
    private readonly List<Ticking> _running = [];

    /// <summary>
    /// Gets the number of things running.
    /// </summary>
    public int Count => _running.Count;

    /// <summary>
    /// Gets or sets a value indicating whether the OS asks for less motion (design §2.14, "Reduced motion"): animations then jump to
    /// their end values and ask for no frame. The window's host sets it from the platform.
    /// </summary>
    public bool ReduceMotion { get; set; }

    /// <summary>
    /// Gets the time of the frame being run, or of the last one: where a thing that starts between frames starts from is the next
    /// frame's time instead.
    /// </summary>
    public TimeSpan Now { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the list is being moved now (inside <see cref="Tick"/>).
    /// </summary>
    public bool Ticking { get; private set; }

    /// <summary>
    /// Puts a thing in the list, if it is not there, and asks for a frame.
    /// </summary>
    /// <param name="item">The thing.</param>
    public void Add(Ticking item)
    {
        if (item.Slot >= 0)
        {
            return;
        }

        item.Slot = _running.Count;
        _running.Add(item);
        tree.NoteWork();
    }

    /// <summary>
    /// Takes a thing out of the list, if it is there.
    /// </summary>
    /// <param name="item">The thing.</param>
    public void Remove(Ticking item)
    {
        int slot = item.Slot;
        if (slot < 0)
        {
            return;
        }

        int last = _running.Count - 1;
        Ticking moved = _running[last];
        _running[slot] = moved;
        moved.Slot = slot;
        _running.RemoveAt(last);
        item.Slot = -1;
    }

    /// <summary>
    /// Moves every running thing to a frame's time, and takes out those that ended. A thing added while the list moves runs from
    /// the next frame.
    /// </summary>
    /// <param name="now">The frame's time.</param>
    public void Tick(TimeSpan now)
    {
        Now = now;
        if (_running.Count == 0)
        {
            return;
        }

        Ticking = true;
        try
        {
            // From the end, so that a removal (which moves the last into the gap) never skips one; the count is read once, so that one
            // added here waits for the next frame.
            for (int i = _running.Count - 1; i >= 0; i--)
            {
                if (i >= _running.Count)
                {
                    continue;
                }

                Ticking item = _running[i];
                if (!item.Tick(now))
                {
                    Remove(item);
                }
            }
        }
        finally
        {
            Ticking = false;
        }
    }
}
