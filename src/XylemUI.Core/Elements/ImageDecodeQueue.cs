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
    private static readonly ConcurrentQueue<ImageRequest> Waiting = new();
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
    /// Queues a decode, which runs when fewer than <see cref="Limit"/> run.
    /// </summary>
    /// <param name="request">The decode.</param>
    public static void Enqueue(ImageRequest request)
    {
        _ = Interlocked.Increment(ref _inFlight);
        Waiting.Enqueue(request);
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

        _decoder ??= new ImageDecoder();
        return _decoder.TryDecodeTo(bytes.Span, width, height, TextureCache.MaxSide, out byte[]? pixels, out int w, out int h)
            ? new ImageResult(pixels, w, h)
            : default;
    }

    // Starts a worker for each waiting decode while fewer than the limit run. A worker that finds the queue empty stops, then
    // looks again: a decode queued between its last look and its stop would otherwise wait for the next one.
    private static void TryStart()
    {
        while (!Waiting.IsEmpty)
        {
            int running = Volatile.Read(ref _running);
            if (running >= Limit)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _running, running + 1, running) != running)
            {
                continue;
            }

            int most = Volatile.Read(ref _mostRunning);
            while (running + 1 > most && Interlocked.CompareExchange(ref _mostRunning, running + 1, most) != most)
            {
                most = Volatile.Read(ref _mostRunning);
            }

            _ = ThreadPool.UnsafeQueueUserWorkItem(static _ => Work(), null);
        }
    }

    private static void Work()
    {
        while (Waiting.TryDequeue(out ImageRequest? request))
        {
            try
            {
                request.Run();
            }
            finally
            {
                _ = Interlocked.Decrement(ref _inFlight);
            }
        }

        _ = Interlocked.Decrement(ref _running);
        TryStart();
    }
}

/// <summary>
/// A decoded image: its pixels, premultiplied BGRA in an array of their own, and its size; no pixels when it could not be read.
/// </summary>
/// <param name="Pixels">The pixels, or <see langword="null"/>.</param>
/// <param name="Width">The width, in pixels.</param>
/// <param name="Height">The height, in pixels.</param>
internal readonly record struct ImageResult(byte[]? Pixels, int Width, int Height);

/// <summary>
/// One decode of an image element: what to read, at what size, and where its result goes. It is run on a worker, then completed on
/// the UI thread; a request the element let go of (its source changed, or it was disposed) is skipped or its result dropped.
/// </summary>
/// <param name="source">The source.</param>
/// <param name="width">The width asked for; 0 for the image's.</param>
/// <param name="height">The height asked for; 0 for the image's.</param>
/// <param name="context">The UI thread's context, which the result is posted to.</param>
/// <param name="complete">The element's completion, run on the UI thread.</param>
internal sealed class ImageRequest(ImageSource source, int width, int height, SynchronizationContext context, Action<ImageRequest> complete)
{
    private volatile bool _cancelled;

    /// <summary>
    /// Gets the source the request reads.
    /// </summary>
    public ImageSource Source => source;

    /// <summary>
    /// Gets the result, once the decode ran.
    /// </summary>
    public ImageResult Result { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the element let the request go.
    /// </summary>
    public bool IsCancelled => _cancelled;

    /// <summary>
    /// Lets the request go: a decode not started is skipped, and a result not yet completed is dropped.
    /// </summary>
    public void Cancel() => _cancelled = true;

    /// <summary>
    /// Reads and decodes the image on the worker, then posts the completion to the UI thread.
    /// </summary>
    public void Run()
    {
        if (_cancelled)
        {
            return;
        }

        Result = ImageDecodeQueue.Decode(source, width, height);
        context.Post(static state => ((ImageRequest)state!).Complete(), this);
    }

    private void Complete()
    {
        if (!_cancelled)
        {
            complete(this);
        }
    }
}
