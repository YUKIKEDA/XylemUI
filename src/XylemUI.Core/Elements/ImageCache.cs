using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The limits and the contents of the image caches of the UI thread's windows (design §5.0.4, "Cache", "API"). Each window keeps
/// the images it decoded: the elements that show the same source at the same decode size share one decode, and an image no element
/// shows stays for the next one that asks for it, until a limit takes it out.
/// </summary>
/// <remarks>
/// <para>
/// There is one pair of limits for the UI thread. Each window's cache holds at most <see cref="MaximumCount"/> images and
/// <see cref="MaximumBytes"/> bytes, the images on screen among them; an image a limit takes out while an element shows it stays
/// until the last such element goes. A window opened later uses the limits set before.
/// </para>
/// <para>
/// A still image counts for 4 bytes a pixel of its decoded size. The mipmaps the GPU keeps and the array given to
/// <see cref="ImageSource.FromBytes"/> are not counted.
/// </para>
/// </remarks>
public static class ImageCache
{
    private const int DefaultMaximumCount = 1000;

    // 100 MiB: 100 images of 512 × 512 (IM1's Grill, Q33).
    private const long DefaultMaximumBytes = 104_857_600;

    // The limits of the calling thread and its caches, held weakly: a closed window's cache goes when nothing else holds it.
    [ThreadStatic]
    private static Limits? _limits;

    /// <summary>
    /// Gets or sets the most images a window's cache holds; 1,000 unless set. Setting it takes the least recently used images out
    /// of each open window's cache at once while the cache holds more. With 0, no image stays that no element shows.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread (design §2.12).</exception>
    public static int MaximumCount
    {
        get => OnUiThread().MaximumCount;
        set => SetMaximumCount(value);
    }

    /// <summary>
    /// Gets or sets the most bytes the images of a window's cache take; 104,857,600 (100 MiB) unless set. Setting it takes the
    /// least recently used images out of each open window's cache at once while the cache holds more. An image larger than the
    /// limit is shown and not kept.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread (design §2.12).</exception>
    public static long MaximumBytes
    {
        get => OnUiThread().MaximumBytes;
        set => SetMaximumBytes(value);
    }

    private static Limits Current => _limits ??= new Limits();

    /// <summary>
    /// Takes the images of a source out of every open window's cache, whatever their decode sizes: an element made afterwards
    /// decodes the source again. An image no element shows is released at once; one that elements show stays on screen and is
    /// released when the last of them goes. The elements that wait for a decode of the source still show its result.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread (design §2.12).</exception>
    public static void Evict(ImageSource source)
    {
        List<WeakReference<WindowImageCache>> caches = OnUiThread().Caches;
        ArgumentNullException.ThrowIfNull(source);
        for (int index = caches.Count - 1; index >= 0; index--)
        {
            if (caches[index].TryGetTarget(out WindowImageCache? cache))
            {
                cache.Evict(source);
            }
            else
            {
                caches.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Takes every image out of every open window's cache, as <see cref="Evict"/> does for one source.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread (design §2.12).</exception>
    public static void Clear()
    {
        List<WeakReference<WindowImageCache>> caches = OnUiThread().Caches;
        for (int index = caches.Count - 1; index >= 0; index--)
        {
            if (caches[index].TryGetTarget(out WindowImageCache? cache))
            {
                cache.Clear();
            }
            else
            {
                caches.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Keeps a window's cache, so that the limits set later and <see cref="Evict"/> and <see cref="Clear"/> reach it, and gives it
    /// the limits of the calling thread. A tree made on another thread than the UI thread, as a test's, gets the defaults.
    /// </summary>
    /// <param name="cache">The cache.</param>
    /// <param name="maximumCount">The most images.</param>
    /// <param name="maximumBytes">The most bytes.</param>
    internal static void Track(WindowImageCache cache, out int maximumCount, out long maximumBytes)
    {
        Limits limits = Current;
        List<WeakReference<WindowImageCache>> caches = limits.Caches;

        // The references of collected caches go before the list grows, so that it holds about the caches alive.
        if (caches.Count == caches.Capacity)
        {
            _ = caches.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        caches.Add(new WeakReference<WindowImageCache>(cache));
        maximumCount = limits.MaximumCount;
        maximumBytes = limits.MaximumBytes;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetMaximumCount(int value)
    {
        Limits limits = OnUiThread();
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        limits.MaximumCount = value;
        Apply(limits);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetMaximumBytes(long value)
    {
        Limits limits = OnUiThread();
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        limits.MaximumBytes = value;
        Apply(limits);
    }

    // The limits of the UI thread; throws on another thread.
    private static Limits OnUiThread()
    {
        UiThread.VerifyAccess();
        return Current;
    }

    private static void Apply(Limits limits)
    {
        List<WeakReference<WindowImageCache>> caches = limits.Caches;
        for (int index = caches.Count - 1; index >= 0; index--)
        {
            if (caches[index].TryGetTarget(out WindowImageCache? cache))
            {
                cache.Limit(limits.MaximumCount, limits.MaximumBytes);
            }
            else
            {
                caches.RemoveAt(index);
            }
        }
    }

    private sealed class Limits
    {
        public List<WeakReference<WindowImageCache>> Caches { get; } = [];

        public int MaximumCount { get; set; } = DefaultMaximumCount;

        public long MaximumBytes { get; set; } = DefaultMaximumBytes;
    }
}
