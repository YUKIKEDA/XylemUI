using System.Collections.Concurrent;
using XylemUI.Imaging;

namespace XylemUI;

/// <summary>
/// The decodes of the image elements (design §5.0.4, "Decoding threads", IM1's Grill, Q3): on the .NET thread pool, at most
/// <see cref="Limit"/> at once, the waiting ones in the order they were asked for, so that many large photos opened at once do not
/// hold their decode buffers at once. A decode reads its source's bytes and decodes them to the size asked for on its thread, then
/// posts its result to the UI thread's <see cref="SynchronizationContext"/> as the framework's own work (design §2.12, D3's Grill;
/// IM1's Grill, Q22): the worker holds no tree.
/// </summary>
internal static class ImageDecodeQueue
{
    private static readonly ConcurrentQueue<DecodeWork> Waiting = new();
    private static int _running;
    private static int _mostRunning;
    private static int _inFlight;

    [ThreadStatic]
    private static ImageDecoder? _decoder;

    /// <summary>
    /// Gets the most decodes that run at once: half the processors, at least one.
    /// </summary>
    public static int Limit { get; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// Gets the most decodes that ran at once since the process started; for the tests of <see cref="Limit"/>.
    /// </summary>
    public static int MostRunning => Volatile.Read(ref _mostRunning);

    /// <summary>
    /// Gets the decodes queued whose results are not posted yet: a test waits for 0 before it runs the UI queue, since a headless
    /// app's <c>PumpAndSettle</c> does not wait for work on other threads (as Flutter's does not).
    /// </summary>
    public static int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>
    /// Gets or sets the decode a test puts in place of the decoders, which a test of the elements runs without the native shim; null
    /// for the decoders.
    /// </summary>
    internal static Func<ImageSource, int, int, ImageResult>? DecodeForTests { get; set; }

    /// <summary>
    /// Queues a decode, which starts at once when fewer than <see cref="Limit"/> run, and otherwise when one of those ends.
    /// </summary>
    /// <param name="work">The decode: an image's (<see cref="ImageRequest"/>), or the next frame of an animation's.</param>
    public static void Enqueue(DecodeWork work)
    {
        _ = Interlocked.Increment(ref _inFlight);
        Waiting.Enqueue(work);
        TryStart();
    }

    /// <summary>
    /// Reads and decodes an image on the calling thread.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="width">The width asked for, in pixels; 0 for the image's.</param>
    /// <param name="height">The height asked for, in pixels; 0 for the image's.</param>
    /// <returns>The result.</returns>
    public static ImageResult Decode(ImageSource source, int width, int height)
    {
        if (DecodeForTests is { } decode)
        {
            return decode(source, width, height);
        }

        if (source.Read() is not { } bytes)
        {
            return default;
        }

        if (ImageAnimation.IsAnimation(bytes.Span))
        {
            return DecodeAnimation(source, bytes, width, height);
        }

        _decoder ??= new ImageDecoder();
        return _decoder.TryDecodeTo(bytes.Span, width, height, TextureCache.MaxSide, out byte[]? pixels, out int w, out int h)
            ? new ImageResult(pixels, w, h)
            : default;
    }

    // An animation's decoder and its first frame (design §5.0.4, "Animations"): the cache plays the rest, or closes the decoder
    // where images do not play.
    private static ImageResult DecodeAnimation(ImageSource source, ReadOnlyMemory<byte> bytes, int width, int height)
    {
        // The decoder pins the bytes for as long as the animation plays. Bytes read here go to the pinned object heap first, so
        // that a small array does not stay pinned among the objects the GC moves; bytes the app gave stay where the app put them.
        if (source.OwnsBytes)
        {
            byte[] pinned = GC.AllocateUninitializedArray<byte>(bytes.Length, pinned: true);
            bytes.CopyTo(pinned);
            bytes = pinned;
        }

        ImageAnimation? animation = ImageAnimation.TryOpen(bytes, width, height, TextureCache.MaxSide);
        if (animation is null)
        {
            return default;
        }

        byte[] pixels = new byte[animation.Width * animation.Height * 4];
        if (animation.Next(pixels, out TimeSpan duration) != AnimationStep.Frame)
        {
            animation.Dispose();
            return default;
        }

        return new ImageResult(pixels, animation.Width, animation.Height)
        {
            Frames = animation,
            Duration = duration,
            SourceBytes = source.OwnsBytes ? bytes.Length : 0,
        };
    }

    // Starts one worker when fewer than the limit run. The count of the workers is not compared with the decodes that wait: a worker
    // that decodes took its decode out of the queue, so a decode queued behind it would count as one that a worker is coming for,
    // and would wait for that decode to end. A worker started for a decode that another took finds the queue empty and stops.
    private static void TryStart()
    {
        int running = Volatile.Read(ref _running);
        while (true)
        {
            if (running >= Limit)
            {
                return;
            }

            int seen = Interlocked.CompareExchange(ref _running, running + 1, running);
            if (seen == running)
            {
                break;
            }

            running = seen;
        }

        int most = Volatile.Read(ref _mostRunning);
        while (running + 1 > most && Interlocked.CompareExchange(ref _mostRunning, running + 1, most) != most)
        {
            most = Volatile.Read(ref _mostRunning);
        }

        // A work item that is made once: queueing a delegate makes an object each time, on the UI thread, once for each frame of
        // an animation that plays (design §5.0.4, "Allocation").
        _ = ThreadPool.UnsafeQueueUserWorkItem(Worker.Instance, preferLocal: false);
    }

    /// <summary>
    /// Runs the decodes that wait, on a worker of the thread pool, until none waits.
    /// </summary>
    internal static void Work()
    {
        while (Waiting.TryDequeue(out DecodeWork? work))
        {
            try
            {
                work.Run();
            }
            finally
            {
                _ = Interlocked.Decrement(ref _inFlight);
            }
        }

        // The worker stops, then looks again: a decode queued between its last look and its stop found the limit's workers running
        // and started none. It starts a worker only when a decode waits: one started each time a worker stops would stop and start
        // the next without end.
        _ = Interlocked.Decrement(ref _running);
        if (!Waiting.IsEmpty)
        {
            TryStart();
        }
    }
}

/// <summary>
/// The decode queue's worker as the thread pool's work item.
/// </summary>
internal sealed class Worker : IThreadPoolWorkItem
{
    private Worker() { }

    /// <summary>
    /// Gets the one worker item.
    /// </summary>
    public static Worker Instance { get; } = new();

    /// <inheritdoc/>
    public void Execute() => ImageDecodeQueue.Work();
}

/// <summary>
/// A decoded image: its pixels, premultiplied BGRA in an array of their own, and its size; no pixels when it could not be read.
/// </summary>
/// <param name="Pixels">The pixels, or <see langword="null"/>.</param>
/// <param name="Width">The width, in pixels.</param>
/// <param name="Height">The height, in pixels.</param>
internal readonly record struct ImageResult(byte[]? Pixels, int Width, int Height)
{
    /// <summary>
    /// Gets the frames after the first, when the image is an animation: <see cref="Pixels"/> is its first frame. Whoever takes the
    /// result plays them or disposes them.
    /// </summary>
    public IAnimationFrames? Frames { get; init; }

    /// <summary>
    /// Gets how long an animation's first frame shows.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the bytes of an animation's file that the decode read itself and the frames keep; 0 for bytes the app gave.
    /// </summary>
    public long SourceBytes { get; init; }
}

/// <summary>
/// Work of the decode queue, run on a worker.
/// </summary>
internal abstract class DecodeWork
{
    /// <summary>
    /// Runs the work on the worker.
    /// </summary>
    public abstract void Run();
}

/// <summary>
/// One decode of a window's image cache: what to read, at what size, and where its result goes. It is run on a worker, then
/// completed on the UI thread. A request nobody waits for is skipped while it has not started (<see cref="TryCancel"/>); one that
/// started always completes, and the cache decides what its result is for.
/// </summary>
/// <param name="source">The source.</param>
/// <param name="width">The width asked for; 0 for the image's.</param>
/// <param name="height">The height asked for; 0 for the image's.</param>
/// <param name="context">The UI thread's context, which the result is posted to.</param>
/// <param name="complete">The completion, run on the UI thread.</param>
internal sealed class ImageRequest(ImageSource source, int width, int height, SynchronizationContext context, Action<ImageRequest> complete)
    : DecodeWork
{
    private const int Waiting = 0;
    private const int Running = 1;
    private const int Cancelled = 2;

    // Waiting, then Running on the worker or Cancelled on the UI thread: whichever comes first wins.
    private int _state;

    /// <summary>
    /// Gets the source the request reads.
    /// </summary>
    public ImageSource Source => source;

    /// <summary>
    /// Gets the UI thread's context the result is posted to.
    /// </summary>
    public SynchronizationContext Context => context;

    /// <summary>
    /// Gets the cache's entry the decode is for; <see langword="null"/> for a request made outside a cache.
    /// </summary>
    public ImageEntry? Entry { get; init; }

    /// <summary>
    /// Gets the result, once the decode ran.
    /// </summary>
    public ImageResult Result { get; private set; }

    /// <summary>
    /// Skips the decode when it has not started.
    /// </summary>
    /// <returns><see langword="true"/> when the decode will not run; <see langword="false"/> when it runs or ran, and its
    /// completion follows.</returns>
    public bool TryCancel() => Interlocked.CompareExchange(ref _state, Cancelled, Waiting) == Waiting;

    /// <summary>
    /// Reads and decodes the image on the worker, then posts the completion to the UI thread.
    /// </summary>
    public override void Run()
    {
        if (Interlocked.CompareExchange(ref _state, Running, Waiting) != Waiting)
        {
            return;
        }

        Result = ImageDecodeQueue.Decode(source, width, height);
        context.Post(static state => ((ImageRequest)state!).Complete(), this);
    }

    private void Complete() => complete(this);
}
