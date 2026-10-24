using System.Runtime.InteropServices;

namespace XylemUI.Text;

/// <summary>
/// The CPU side of the glyph atlas: the coverage of rasterized glyphs on pages of a fixed size, each split into plots, with the
/// least recently used plot evicted when there is no room (design §5.0.2, §5.0.3; the same scheme as Skia's GrDrawOpAtlas). Each
/// kind of coverage (<see cref="GlyphCoverage"/>) has its own pages: one byte per pixel for grayscale, four for ClearType and for
/// color glyphs. A ClearType or color page has half the side of a grayscale page, so the kinds take the same bytes, and a kind may
/// hold four times as many
/// (T2 review: a 1024 px RGBA page for one line of text added 9.5 MB to the idle private memory).
/// </summary>
/// <remarks>
/// <para>
/// A glyph is rasterized by the atlas's <see cref="IGlyphRasterizer"/> the first time it is asked for, at one size, subpixel step,
/// and kind of coverage (<see cref="GlyphKey"/>), and kept: the next request returns the same region without rasterizing, and
/// marks its plot used in the current frame. Glyphs are packed on shelves inside a plot.
/// </para>
/// <para>
/// Each kind's pages are counted, evicted, and bounded on their own, as GPUI keeps a list of textures per kind in one atlas (Zed
/// v1.24.3, <c>crates/gpui_windows/src/directx_atlas.rs</c>, lines 21 to 23). When no plot of the kind has room, a page is added
/// while the kind has fewer than its maximum (<see cref="GetMaxPages"/>); after that, the plot used longest ago, and not in the
/// current frame, is emptied: its glyphs are forgotten and will be rasterized again when they are next asked for. When every plot
/// was used in the current frame, a page is added beyond the maximum; <see cref="BeginFrame"/> drops such extra pages once a frame
/// no longer uses them. So the memory is bounded by the maximum pages of each kind in steady state (the same bytes and plots for
/// both kinds), and a frame only re-rasterizes the glyphs of the plots it evicts. The pages of a kind never exceed twice its
/// maximum (<see cref="GetPageLimit"/>): a frame that needs
/// more (more new glyphs than the limit holds, or a caller that never calls <see cref="BeginFrame"/>) gets <see langword="false"/>
/// for the glyphs that do not fit, and they are not drawn. A kind no glyph asked for has no page.
/// </para>
/// <para>
/// The regions changed since the last <see cref="ClearDirtyRegions"/> are listed in <see cref="DirtyRegions"/>, for the GPU upload
/// after the display list is built (design §5.0.3). Each page is a pinned array: the rasterizer writes the coverage straight in,
/// and the upload reads it in place. One atlas belongs to one thread, and its changes go to one renderer: the renderer clears the dirty
/// regions after uploading them, so a second renderer of the same atlas would miss them (sharing is decided in roadmap row P10).
/// </para>
/// <para>
/// <see cref="GetMany"/> rasterizes the new glyphs of a frame together: on worker threads when the atlas was made with a function
/// that creates rasterizers and there are at least <see cref="ParallelThreshold"/> of them, else on the calling thread (design
/// §5.0.3, "Glyphs seen for the first time", roadmap row T5). The regions are taken on the calling thread in the order of the
/// requests, so the pages are the same whatever the number of threads. Such an atlas owns its rasterizers and its workers until
/// <see cref="Dispose"/>.
/// </para>
/// </remarks>
public sealed class GlyphAtlas : IDisposable
{
    /// <summary>
    /// The number of new glyphs from which <see cref="GetMany"/> hands them to the worker threads (design §5.0.3, "Glyphs seen for
    /// the first time"): one glyph cannot be split, and from 2 the workers were faster (Yu Gothic at 16 px, 4 threads, median of 21
    /// processes: 2 glyphs 130 µs against 146 µs on the calling thread alone, 3 glyphs 188 against 222, 4 glyphs 247 against 308;
    /// roadmap row T5, <c>docs/benchmarks/t5-glyph-workers.md</c>).
    /// </summary>
    public const int ParallelThreshold = 2;

    private const int Padding = 1;

    // The plot of a cached key that stands for a color glyph, without and with a layer in the text's color; -1 is a glyph with no
    // ink.
    private const int ColorGlyph = -2;
    private const int ColorGlyphInTextColor = -3;
    private const uint OpaqueBlack = 0xFF000000;

    private static long _lastPageId;

    private readonly IGlyphRasterizer _rasterizer;
    private readonly Dictionary<GlyphKey, (AtlasGlyph Glyph, int Plot)> _glyphs = [];
    private readonly List<AtlasRegion> _dirty = [];
    private readonly Pages _gray;
    private readonly Pages _rgb;
    private readonly Pages _color;

    // What only GetMany, the refusals, and Dispose use, so that a frame whose glyphs are all in the atlas reads the same fields as
    // before (row T5).
    private readonly Batch _batch;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphAtlas"/> class.
    /// </summary>
    /// <param name="rasterizer">Draws the glyphs the atlas does not hold yet.</param>
    /// <param name="pageSize">The width and height of a grayscale page, in pixels; a ClearType page is half as wide and high.</param>
    /// <param name="plotSize">The width and height of a plot, in pixels; it divides half of <paramref name="pageSize"/>.</param>
    /// <param name="maxPages">The number of grayscale pages kept in steady state; four times as many ClearType pages.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive, or the plot does not divide half the page.</exception>
    public GlyphAtlas(IGlyphRasterizer rasterizer, int pageSize = 1024, int plotSize = 256, int maxPages = 4)
        : this(rasterizer, null, 1, pageSize, plotSize, maxPages) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphAtlas"/> class that rasterizes the new glyphs of a frame on worker threads,
    /// each with its own rasterizer (design §5.0.3, "Glyphs seen for the first time"). The atlas owns the rasterizers it creates and
    /// disposes them in <see cref="Dispose"/>.
    /// </summary>
    /// <param name="createRasterizer">
    /// Creates a rasterizer for one thread: each instance is used on one thread, and distinct instances on distinct threads at once.
    /// When it throws <see cref="InvalidOperationException"/> or <see cref="OutOfMemoryException"/> for a worker, or a worker
    /// thread cannot be started, the atlas disposes the rasterizers it made for the workers and draws on the calling thread.
    /// </param>
    /// <param name="pageSize">The width and height of a grayscale page, in pixels; a ClearType page is half as wide and high.</param>
    /// <param name="plotSize">The width and height of a plot, in pixels; it divides half of <paramref name="pageSize"/>.</param>
    /// <param name="maxPages">The number of grayscale pages kept in steady state; four times as many ClearType pages.</param>
    /// <param name="threads">
    /// The number of threads that rasterize, the calling thread included; 0 takes half the logical processors, kept from 1 to 4.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive, the plot does not divide half the page, or <paramref name="threads"/> is negative.</exception>
    public GlyphAtlas(Func<IGlyphRasterizer> createRasterizer, int pageSize = 1024, int plotSize = 256, int maxPages = 4, int threads = 0)
        : this(NewRasterizer(createRasterizer, threads), createRasterizer, ThreadsOf(threads), pageSize, plotSize, maxPages) { }

    private GlyphAtlas(IGlyphRasterizer rasterizer, Func<IGlyphRasterizer>? createRasterizer, int threads, int pageSize, int plotSize, int maxPages)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plotSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);
        if (pageSize % 2 != 0 || plotSize > pageSize / 2 || (pageSize / 2) % plotSize != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plotSize), "The plot size must divide half the page size.");
        }

        _rasterizer = rasterizer;
        _batch = new Batch(rasterizer, createRasterizer, Math.Max(1, threads));
        _batch.MeasureStep = MeasureWork;
        _batch.RasterizeStep = RasterizeWork;
        PageSize = pageSize;
        PlotSize = plotSize;
        MaxPages = maxPages;
        _gray = new Pages(this, GlyphCoverage.Gray, pageSize, maxPages);
        _rgb = new Pages(this, GlyphCoverage.Rgb, pageSize / 2, 4 * maxPages);
        _color = new Pages(this, GlyphCoverage.Color, pageSize / 2, 4 * maxPages);
    }

    /// <summary>
    /// Gets the width and height of a grayscale page, in pixels (<see cref="GetPageSize"/> gives each kind's).
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the width and height of a plot, in pixels. A glyph larger than a plot (less its padding) does not fit the atlas.
    /// </summary>
    public int PlotSize { get; }

    /// <summary>
    /// Gets the number of grayscale pages kept in steady state (<see cref="GetMaxPages"/> gives each kind's).
    /// </summary>
    public int MaxPages { get; }

    /// <summary>
    /// Gets the most grayscale pages the atlas ever holds: twice <see cref="MaxPages"/> (<see cref="GetPageLimit"/> gives each
    /// kind's).
    /// </summary>
    public int PageLimit => 2 * MaxPages;

    /// <summary>
    /// Gets the current frame, advanced by <see cref="BeginFrame"/>.
    /// </summary>
    public long Frame { get; private set; } = 1;

    /// <summary>
    /// Gets the regions changed since the last <see cref="ClearDirtyRegions"/>.
    /// </summary>
    public ReadOnlySpan<AtlasRegion> DirtyRegions => CollectionsMarshal.AsSpan(_dirty);

    /// <summary>
    /// Gets the number of glyphs rasterized so far; a cached glyph does not add to it.
    /// </summary>
    public int RasterizeCount { get; private set; }

    /// <summary>
    /// Gets the number of plots emptied to make room so far.
    /// </summary>
    public int EvictionCount { get; private set; }

    /// <summary>
    /// Gets the number of glyphs refused so far because the kind had its page limit and no plot to evict. The atlas remembers a
    /// refused glyph until the next <see cref="BeginFrame"/> and refuses it again without rasterizing it, so a glyph counts once
    /// per frame; a rising count means frames need more than the limit, or <see cref="BeginFrame"/> is not called.
    /// </summary>
    public int RefusedCount { get; private set; }

    /// <summary>
    /// Returns the bytes of one pixel of a kind's pages: 1 for <see cref="GlyphCoverage.Gray"/>, 4 for
    /// <see cref="GlyphCoverage.Rgb"/> and <see cref="GlyphCoverage.Color"/>.
    /// </summary>
    /// <param name="coverage">The kind.</param>
    /// <returns>The bytes per pixel.</returns>
    public static int BytesPerPixel(GlyphCoverage coverage) => coverage == GlyphCoverage.Gray ? 1 : 4;

    /// <summary>
    /// Returns the width and height of a kind's pages, in pixels: <see cref="PageSize"/> for grayscale, half of it for ClearType
    /// and color.
    /// </summary>
    /// <param name="coverage">The kind.</param>
    /// <returns>The size.</returns>
    public int GetPageSize(GlyphCoverage coverage) => PagesOf(coverage).Side;

    /// <summary>
    /// Returns the number of a kind's pages kept in steady state: <see cref="MaxPages"/> for grayscale, four times as many for
    /// ClearType and color.
    /// </summary>
    /// <param name="coverage">The kind.</param>
    /// <returns>The number of pages.</returns>
    public int GetMaxPages(GlyphCoverage coverage) => PagesOf(coverage).MaxPages;

    /// <summary>
    /// Returns the most pages of a kind the atlas ever holds: twice <see cref="GetMaxPages"/>.
    /// </summary>
    /// <param name="coverage">The kind.</param>
    /// <returns>The number of pages.</returns>
    public int GetPageLimit(GlyphCoverage coverage) => 2 * PagesOf(coverage).MaxPages;

    /// <summary>
    /// Returns the number of pages of a kind now; it exceeds the kind's maximum only while frames need the extra pages.
    /// </summary>
    /// <param name="coverage">The kind.</param>
    /// <returns>The number of pages.</returns>
    public int GetPageCount(GlyphCoverage coverage) => PagesOf(coverage).Pixels.Count;

    /// <summary>
    /// Returns the coverage of a page, row after row, each row <see cref="GetPageSize"/> times <see cref="BytesPerPixel"/> bytes.
    /// </summary>
    /// <param name="coverage">The kind of the page.</param>
    /// <param name="page">The page, among the pages of <paramref name="coverage"/>.</param>
    /// <returns>The pixels.</returns>
    public ReadOnlySpan<byte> GetPagePixels(GlyphCoverage coverage, int page) => PagesOf(coverage).Pixels[page];

    /// <summary>
    /// Returns the id of a page: unique in the process, so a page dropped and added again, or a page of another atlas or kind, has
    /// another id. A renderer uploads the whole page when the id at an index changes, and only the dirty regions otherwise.
    /// </summary>
    /// <param name="coverage">The kind of the page.</param>
    /// <param name="page">The page, among the pages of <paramref name="coverage"/>.</param>
    /// <returns>The id.</returns>
    public long GetPageId(GlyphCoverage coverage, int page) => PagesOf(coverage).Ids[page];

    /// <summary>
    /// Starts a new frame: glyphs asked for from now on protect their plots from eviction until the next call. Pages beyond
    /// the kind's maximum that the frame just ended did not use are dropped, last first.
    /// </summary>
    public void BeginFrame()
    {
        _gray.DropUnusedExtraPages(Frame);
        _rgb.DropUnusedExtraPages(Frame);
        _color.DropUnusedExtraPages(Frame);
        Frame++;
    }

    /// <summary>
    /// Forgets the dirty regions, after they were uploaded.
    /// </summary>
    public void ClearDirtyRegions() => _dirty.Clear();

    /// <summary>
    /// Returns the region of a glyph, rasterizing it into the atlas the first time, and marks it used in the current frame, for
    /// text in opaque black (see the overload that takes the text's color).
    /// </summary>
    /// <param name="key">The glyph, its size, its subpixel step, and its kind of coverage.</param>
    /// <param name="glyph">The glyph's region and bearings, on a page of the key's kind, or of the color kind for a color glyph.</param>
    /// <returns>
    /// <see langword="false"/> when the glyph is larger than a plot, or when there is no room: every plot of the kind was used in
    /// the current frame and the kind has its page limit (<see cref="GetPageLimit"/>). A glyph with no ink always fits.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The key has no font, a size that is not positive, a subpixel step out of range, or a coverage that is not defined; or it is
    /// bilevel (<see cref="GlyphKey.Aliased"/>) with ClearType coverage or a subpixel step other than 0.
    /// </exception>
    /// <exception cref="InvalidOperationException">The rasterizer could not draw the glyph.</exception>
    /// <exception cref="ObjectDisposedException">The font was disposed and the glyph is not in the atlas.</exception>
    public bool TryGet(in GlyphKey key, out AtlasGlyph glyph) => TryGet(key, OpaqueBlack, out glyph);

    /// <summary>
    /// Returns the region of a glyph, rasterizing it into the atlas the first time, and marks it used in the current frame. A color
    /// glyph asked for as grayscale or ClearType is kept once as <see cref="GlyphCoverage.Color"/>, on a whole pixel (subpixel step
    /// 0), and with the text's color in its key only when it has a layer in the text's color (design §5.0.3, "Color glyphs"). A
    /// bilevel glyph is kept on the pages of its kind, grayscale or color, as a glyph of its own (design §5.0.3, "When fonts are
    /// not smoothed").
    /// </summary>
    /// <param name="key">The glyph, its size, its subpixel step, and its kind of coverage.</param>
    /// <param name="textColor">
    /// The text's color, packed as red in the low byte, then green, blue, and alpha; its alpha is not used here.
    /// </param>
    /// <param name="glyph">The glyph's region and bearings, on a page of the key's kind, or of the color kind for a color glyph.</param>
    /// <returns>
    /// <see langword="false"/> when the glyph is larger than a plot, or when there is no room: every plot of the kind was used in
    /// the current frame and the kind has its page limit (<see cref="GetPageLimit"/>). A glyph with no ink always fits.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The key has no font, a size that is not positive, a subpixel step out of range, or a coverage that is not defined; or it is
    /// bilevel (<see cref="GlyphKey.Aliased"/>) with ClearType coverage or a subpixel step other than 0.
    /// </exception>
    /// <exception cref="InvalidOperationException">The rasterizer could not draw the glyph.</exception>
    /// <exception cref="ObjectDisposedException">The font was disposed and the glyph is not in the atlas.</exception>
    public bool TryGet(in GlyphKey key, uint textColor, out AtlasGlyph glyph)
    {
        Validate(key);
        if (_glyphs.TryGetValue(key, out (AtlasGlyph Glyph, int Plot) cached))
        {
            if (cached.Plot >= 0)
            {
                PagesOf(key.Coverage).PlotAt(cached.Plot).LastUsedFrame = Frame;
            }
            else if (cached.Plot <= ColorGlyph)
            {
                return TryGet(ColorKeyOf(key, cached.Plot == ColorGlyphInTextColor, textColor), textColor, out glyph);
            }

            glyph = cached.Glyph;
            return true;
        }

        // A glyph refused in this frame is refused again without rasterizing it: rasterizing it again took 6.4 µs per request
        // (design §11.3, roadmap row T5).
        if (IsRefused(key))
        {
            glyph = default;
            return false;
        }

        return TryRasterize(key, textColor, out glyph);
    }

    /// <summary>
    /// Returns whether a glyph is in the atlas without rasterizing it, and if so its region, marking it used in the current frame
    /// (design §5.0.3, "Glyphs seen for the first time": <c>DisplayList.DrawText</c> writes the glyphs found here and leaves the
    /// others to <see cref="GetMany"/>).
    /// </summary>
    /// <param name="key">The glyph, its size, its subpixel step, and its kind of coverage.</param>
    /// <param name="textColor">The text's color, packed as red in the low byte, then green, blue, and alpha.</param>
    /// <param name="glyph">The glyph's region and bearings when it is found.</param>
    /// <returns>Whether it was found, missing, or refused in the current frame (no room, or larger than a plot).</returns>
    /// <exception cref="ArgumentException">The key is not valid (see <see cref="TryGet(in GlyphKey, uint, out AtlasGlyph)"/>).</exception>
    public GlyphLookup Lookup(in GlyphKey key, uint textColor, out AtlasGlyph glyph)
    {
        Validate(key);
        if (_glyphs.TryGetValue(key, out (AtlasGlyph Glyph, int Plot) cached))
        {
            if (cached.Plot >= 0)
            {
                PagesOf(key.Coverage).PlotAt(cached.Plot).LastUsedFrame = Frame;
            }
            else if (cached.Plot <= ColorGlyph)
            {
                return Lookup(ColorKeyOf(key, cached.Plot == ColorGlyphInTextColor, textColor), textColor, out glyph);
            }

            glyph = cached.Glyph;
            return GlyphLookup.Found;
        }

        glyph = default;
        return IsRefused(key) ? GlyphLookup.Refused : GlyphLookup.Missing;
    }

    /// <summary>
    /// Returns the regions of many glyphs, rasterizing together the ones not in the atlas: (1) their boxes are measured, (2) their
    /// regions are taken on the calling thread in the order of <paramref name="requests"/>, (3) they are rasterized into their
    /// regions. Steps 1 and 3 run on the worker threads when the atlas has them and at least <see cref="ParallelThreshold"/> glyphs
    /// are new (design §5.0.3, "Glyphs seen for the first time").
    /// </summary>
    /// <param name="requests">The glyphs and the text colors they are drawn in.</param>
    /// <param name="glyphs">The regions, one for each request; the default value for a glyph that is not found.</param>
    /// <param name="found">
    /// Whether each glyph is in the atlas: <see langword="false"/> for one larger than a plot or with no room, as
    /// <see cref="TryGet(in GlyphKey, uint, out AtlasGlyph)"/> returns.
    /// </param>
    /// <exception cref="ArgumentException">A key is not valid, or a span is shorter than <paramref name="requests"/>.</exception>
    /// <exception cref="InvalidOperationException">A rasterizer could not draw a glyph.</exception>
    /// <exception cref="ObjectDisposedException">A font was disposed and its glyph is not in the atlas, or the atlas was disposed.</exception>
    public void GetMany(ReadOnlySpan<GlyphRequest> requests, Span<AtlasGlyph> glyphs, Span<bool> found)
    {
        ObjectDisposedException.ThrowIf(_batch.Disposed, this);
        if (glyphs.Length < requests.Length || found.Length < requests.Length)
        {
            throw new ArgumentException("The spans for the results are shorter than the requests.", nameof(glyphs));
        }

        _batch.ItemCount = 0;
        for (int i = 0; i < requests.Length; i++)
        {
            GlyphLookup lookup = Lookup(requests[i].Key, requests[i].TextColor, out glyphs[i]);
            found[i] = lookup == GlyphLookup.Found;
            if (lookup == GlyphLookup.Missing)
            {
                if (_batch.ItemCount == _batch.Items.Length)
                {
                    Array.Resize(ref _batch.Items, _batch.Items.Length * 2);
                }

                _batch.Items[_batch.ItemCount++] = new Item
                {
                    Request = i,
                    Key = requests[i].Key,
                    TextColor = requests[i].TextColor,
                };
            }
        }

        if (_batch.ItemCount == 0)
        {
            return;
        }

        GlyphWorkers? workers = _batch.ItemCount >= _batch.ParallelFrom ? Workers() : null;
        _batch.ActiveThreads = workers?.Count ?? 1;
        Run(workers, _batch.MeasureStep);
        bool rasterize = false;
        for (int i = 0; i < _batch.ItemCount; i++)
        {
            rasterize |= Place(ref _batch.Items[i], out glyphs[_batch.Items[i].Request], out found[_batch.Items[i].Request]);
        }

        if (!rasterize)
        {
            return;
        }

        try
        {
            Run(workers, _batch.RasterizeStep);
        }
        catch
        {
            // A glyph that was not rasterized must not stay cached over the old pixels of its region, so every glyph this call
            // placed is forgotten, in the cache and in its plot; its room is taken until the plot is evicted.
            for (int i = 0; i < _batch.ItemCount; i++)
            {
                ref Item item = ref _batch.Items[i];
                if (item.Page is not null)
                {
                    _ = _glyphs.Remove(item.Final);
                    _ = PagesOf(item.Final.Coverage).PlotAt(item.Plot).Keys.Remove(item.Final);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Gets or sets the number of new glyphs from which <see cref="GetMany"/> uses the workers: <see cref="ParallelThreshold"/>,
    /// which the glyph-resolve scenario of <c>benchmarks/XylemUI.E2E</c> moves to measure it.
    /// </summary>
    internal int ParallelFrom
    {
        get => _batch.ParallelFrom;
        set => _batch.ParallelFrom = value;
    }

    /// <summary>
    /// Gets or sets how the atlas starts its worker threads: the workers, or <see langword="null"/> when a thread could not be
    /// started. A test replaces it to make the start fail.
    /// </summary>
    internal Func<int, GlyphWorkers?> StartWorkers
    {
        get => _batch.StartWorkers;
        set => _batch.StartWorkers = value;
    }

    /// <summary>
    /// Gets the worker threads, or <see langword="null"/> before they are started, when they could not be, and after
    /// <see cref="Dispose"/>.
    /// </summary>
    internal GlyphWorkers? RunningWorkers => _batch.Running;

    /// <summary>
    /// Stops the worker threads and disposes the rasterizers the atlas created. An atlas made with one rasterizer leaves it to the
    /// caller.
    /// </summary>
    public void Dispose()
    {
        if (_batch.Disposed)
        {
            return;
        }

        _batch.Disposed = true;
        _batch.Running?.Dispose();
        _batch.Running = null;
        if (_batch.CreateRasterizer is null)
        {
            return;
        }

        foreach (IGlyphRasterizer rasterizer in _batch.Rasterizers)
        {
            (rasterizer as IDisposable)?.Dispose();
        }
    }

    // Checks the arguments before the first rasterizer is made, so that a wrong argument leaves nothing to dispose.
    private static IGlyphRasterizer NewRasterizer(Func<IGlyphRasterizer> createRasterizer, int threads)
    {
        ArgumentNullException.ThrowIfNull(createRasterizer);
        ArgumentOutOfRangeException.ThrowIfNegative(threads);
        return createRasterizer();
    }

    // Half the logical processors, kept from 1 to 4, as Chromium sizes its raster workers (design §5.0.3).
    private static int ThreadsOf(int threads) => threads == 0 ? Math.Clamp(Environment.ProcessorCount / 2, 1, 4) : threads;

    private static void Run(GlyphWorkers? workers, Action<int> work)
    {
        if (workers is null)
        {
            work(0);
        }
        else
        {
            workers.Run(work);
        }
    }

    // Remembers a refused glyph until the next frame. The set is emptied here and not in BeginFrame, which does not read it: a frame
    // starts with cold caches, where reading it cost 100 to 600 ns (the draw-text --cold scenario of benchmarks/XylemUI.E2E, row
    // T5).
    private void Refuse(in GlyphKey key)
    {
        if (_batch.RefusedFrame != Frame)
        {
            _batch.Refused.Clear();
            _batch.RefusedFrame = Frame;
        }

        _ = _batch.Refused.Add(key);
    }

    private bool IsRefused(in GlyphKey key) => _batch.RefusedFrame == Frame && _batch.Refused.Contains(key);

    // The worker threads and their rasterizers, made the first time a frame has enough new glyphs; null when the atlas has one
    // rasterizer, or when a worker's rasterizer could not be made or a thread could not be started (the glyphs are then rasterized
    // on the calling thread, and the rasterizers made for the workers are disposed). It is tried once.
    private GlyphWorkers? Workers()
    {
        if (_batch.Running is not null || _batch.NoWorkers || _batch.CreateRasterizer is null || _batch.Threads < 2)
        {
            return _batch.Running;
        }

        _batch.NoWorkers = true;
        var rasterizers = new IGlyphRasterizer[_batch.Threads];
        rasterizers[0] = _rasterizer;
        int made = 1;
        try
        {
            for (; made < _batch.Threads; made++)
            {
                rasterizers[made] = _batch.CreateRasterizer();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or OutOfMemoryException)
        {
            // A rasterizer for a worker could not be made (DirectWrite's factory or loader): the frame is drawn on the calling
            // thread, as when a thread cannot be started (design §5.0.3).
            DisposeExtra(rasterizers, made);
            return null;
        }

        _batch.Running = _batch.StartWorkers(_batch.Threads);
        if (_batch.Running is null)
        {
            DisposeExtra(rasterizers, made);
            return null;
        }

        _batch.Rasterizers = rasterizers;
        _batch.NoWorkers = false;
        return _batch.Running;
    }

    // Disposes the rasterizers made for workers that will not run, all but the atlas's own.
    private static void DisposeExtra(IGlyphRasterizer[] rasterizers, int made)
    {
        for (int i = 1; i < made; i++)
        {
            (rasterizers[i] as IDisposable)?.Dispose();
        }
    }

    // The workers, or null when a thread could not be started (GlyphWorkers stops the ones it started).
    private static GlyphWorkers? TryStartWorkers(int count)
    {
        try
        {
            return new GlyphWorkers(count);
        }
        catch (Exception exception) when (exception is OutOfMemoryException or ThreadStartException)
        {
            return null;
        }
    }

    // Step 1 on one thread: the boxes of the items with index worker, worker + threads, and so on. A color glyph asked for as
    // grayscale or ClearType is measured again as its color key.
    private void MeasureWork(int worker)
    {
        IGlyphRasterizer rasterizer = _batch.Rasterizers[worker];
        for (int i = worker; i < _batch.ItemCount; i += _batch.ActiveThreads)
        {
            ref Item item = ref _batch.Items[i];
            GlyphBox box = rasterizer.Measure(item.Key);
            if (box.IsColor && item.Key.Coverage != GlyphCoverage.Color)
            {
                item.Alias = box.UsesTextColor ? ColorGlyphInTextColor : ColorGlyph;
                item.Final = ColorKeyOf(item.Key, box.UsesTextColor, item.TextColor);
                item.Box = rasterizer.Measure(item.Final);
            }
            else
            {
                item.Alias = 0;
                item.Final = item.Key;
                item.Box = box;
            }
        }
    }

    // Step 3 on one thread: the items of step 1's partition that took a region are rasterized into it.
    private void RasterizeWork(int worker)
    {
        IGlyphRasterizer rasterizer = _batch.Rasterizers[worker];
        for (int i = worker; i < _batch.ItemCount; i += _batch.ActiveThreads)
        {
            ref Item item = ref _batch.Items[i];
            if (item.Page is not null)
            {
                rasterizer.Rasterize(item.Final, item.Page.AsSpan(item.Start, item.Length), item.Stride);
            }
        }
    }

    // Step 2 for one item, on the calling thread: the region of its final key, taken as TryRasterize takes it. Returns whether the
    // item is to be rasterized in step 3.
    private bool Place(ref Item item, out AtlasGlyph glyph, out bool found)
    {
        item.Page = null;
        if (item.Alias != 0)
        {
            _ = _glyphs.TryAdd(item.Key, (default, item.Alias));
        }

        GlyphKey key = item.Final;
        if (_glyphs.TryGetValue(key, out (AtlasGlyph Glyph, int Plot) cached))
        {
            if (cached.Plot >= 0)
            {
                PagesOf(key.Coverage).PlotAt(cached.Plot).LastUsedFrame = Frame;
            }

            glyph = cached.Glyph;
            found = true;
            return false;
        }

        if (IsRefused(key))
        {
            glyph = default;
            found = false;
            return false;
        }

        RasterizeCount++;
        GlyphBox box = item.Box;
        if (box.Width == 0 || box.Height == 0)
        {
            glyph = new AtlasGlyph(0, 0, 0, 0, 0, box.Left, box.Top, key.Coverage);
            _glyphs.Add(key, (glyph, -1));
            found = true;
            return false;
        }

        if (box.Width + (2 * Padding) > PlotSize || box.Height + (2 * Padding) > PlotSize)
        {
            Refuse(key);
            glyph = default;
            found = false;
            return false;
        }

        Pages pages = PagesOf(key.Coverage);
        int plotIndex = pages.FindRoom(box.Width, box.Height, out int x, out int y);
        if (plotIndex < 0)
        {
            RefusedCount++;
            Refuse(key);
            glyph = default;
            found = false;
            return false;
        }

        ref Plot plot = ref pages.PlotAt(plotIndex);
        int bytes = pages.BytesPerPixel;
        item.Stride = pages.Side * bytes;
        item.Start = (y * item.Stride) + (x * bytes);
        item.Length = ((box.Height - 1) * item.Stride) + (box.Width * bytes);
        item.Page = pages.Pixels[plot.Page];
        item.Plot = plotIndex;
        plot.LastUsedFrame = Frame;
        plot.Keys.Add(key);
        glyph = new AtlasGlyph(plot.Page, x, y, box.Width, box.Height, box.Left, box.Top, key.Coverage);
        _glyphs.Add(key, (glyph, plotIndex));
        _dirty.Add(new AtlasRegion(plot.Page, x, y, box.Width, box.Height, key.Coverage));
        found = true;
        return true;
    }

    private static void Validate(in GlyphKey key)
    {
        if (key.Font is null)
        {
            throw new ArgumentException("The glyph key has no font.", nameof(key));
        }

        if (!(key.PixelSize > 0) || !float.IsFinite(key.PixelSize))
        {
            throw new ArgumentException($"The glyph size {key.PixelSize} is not a positive number.", nameof(key));
        }

        if ((uint)key.SubpixelX >= GlyphPosition.SubpixelSteps)
        {
            throw new ArgumentException($"The subpixel step {key.SubpixelX} is not from 0 to {GlyphPosition.SubpixelSteps - 1}.", nameof(key));
        }

        if (key.Coverage is not (GlyphCoverage.Gray or GlyphCoverage.Rgb or GlyphCoverage.Color))
        {
            throw new ArgumentException($"The glyph coverage {key.Coverage} is not defined.", nameof(key));
        }

        if (key.Aliased && key.Coverage == GlyphCoverage.Rgb)
        {
            throw new ArgumentException("A bilevel glyph has no ClearType coverage.", nameof(key));
        }

        if (key.Aliased && key.SubpixelX != 0)
        {
            throw new ArgumentException($"The subpixel step {key.SubpixelX} of a bilevel glyph is not 0.", nameof(key));
        }
    }

    // The key of the color glyph that a grayscale or ClearType key stands for: subpixel step 0, the text's color, opaque, only
    // when the glyph paints a layer in it, and bilevel when the key is.
    private static GlyphKey ColorKeyOf(in GlyphKey key, bool usesTextColor, uint textColor) =>
        new(key.Font, key.GlyphId, key.PixelSize, 0, GlyphCoverage.Color, usesTextColor ? textColor | OpaqueBlack : 0, key.Aliased);

    private Pages PagesOf(GlyphCoverage coverage) =>
        coverage switch
        {
            GlyphCoverage.Rgb => _rgb,
            GlyphCoverage.Color => _color,
            _ => _gray,
        };

    private bool TryRasterize(in GlyphKey key, uint textColor, out AtlasGlyph glyph)
    {
        GlyphBox box = _rasterizer.Measure(key);

        // A color glyph asked for as grayscale or ClearType: this key only stands for the color glyph from now on.
        if (box.IsColor && key.Coverage != GlyphCoverage.Color)
        {
            _glyphs.Add(key, (default, box.UsesTextColor ? ColorGlyphInTextColor : ColorGlyph));
            return TryGet(ColorKeyOf(key, box.UsesTextColor, textColor), textColor, out glyph);
        }

        RasterizeCount++;
        int width = box.Width;
        int height = box.Height;

        // A glyph with no ink (a space) needs no room, so it fits even when every plot is full.
        if (width == 0 || height == 0)
        {
            glyph = new AtlasGlyph(0, 0, 0, 0, 0, box.Left, box.Top, key.Coverage);
            _glyphs.Add(key, (glyph, -1));
            return true;
        }

        if (width + (2 * Padding) > PlotSize || height + (2 * Padding) > PlotSize)
        {
            Refuse(key);
            glyph = default;
            return false;
        }

        Pages pages = PagesOf(key.Coverage);
        int plotIndex = pages.FindRoom(width, height, out int x, out int y);
        if (plotIndex < 0)
        {
            RefusedCount++;
            Refuse(key);
            glyph = default;
            return false;
        }

        ref Plot plot = ref pages.PlotAt(plotIndex);
        byte[] page = pages.Pixels[plot.Page];

        // FindRoom placed the region inside the plot, so its rows lie inside the page, one page row apart.
        int bytes = pages.BytesPerPixel;
        int stride = pages.Side * bytes;
        int start = (y * stride) + (x * bytes);
        _rasterizer.Rasterize(key, page.AsSpan(start, ((height - 1) * stride) + (width * bytes)), stride);

        plot.LastUsedFrame = Frame;
        plot.Keys.Add(key);
        glyph = new AtlasGlyph(plot.Page, x, y, width, height, box.Left, box.Top, key.Coverage);
        _glyphs.Add(key, (glyph, plotIndex));
        _dirty.Add(new AtlasRegion(plot.Page, x, y, width, height, key.Coverage));
        return true;
    }

    // The rasterizers (the first is the atlas's own; the others are made with the workers), the worker threads, the refusals of
    // frame RefusedFrame, and the new glyphs of the current GetMany call, partitioned over ActiveThreads by index.
    private sealed class Batch(IGlyphRasterizer rasterizer, Func<IGlyphRasterizer>? createRasterizer, int threads)
    {
        public readonly Func<IGlyphRasterizer>? CreateRasterizer = createRasterizer;
        public readonly int Threads = threads;
        public readonly HashSet<GlyphKey> Refused = [];
        public long RefusedFrame;
        public int ParallelFrom = ParallelThreshold;
        public Func<int, GlyphWorkers?> StartWorkers = TryStartWorkers;
        public Action<int> MeasureStep = static _ => { };
        public Action<int> RasterizeStep = static _ => { };
        public IGlyphRasterizer[] Rasterizers = [rasterizer];
        public GlyphWorkers? Running;
        public bool NoWorkers;
        public bool Disposed;
        public Item[] Items = new Item[64];
        public int ItemCount;
        public int ActiveThreads;
    }

    // A new glyph of a GetMany call: its request, its key and the color key it stands for, its box, and the region step 2 took.
    private struct Item
    {
        public int Request;
        public GlyphKey Key;
        public uint TextColor;
        public GlyphKey Final;
        public GlyphBox Box;
        public int Alias;
        public byte[]? Page;
        public int Plot;
        public int Start;
        public int Length;
        public int Stride;
    }

    // The pages of one kind of coverage, their ids, and their plots.
    private sealed class Pages(GlyphAtlas atlas, GlyphCoverage coverage, int side, int maxPages)
    {
        public readonly List<byte[]> Pixels = [];
        public readonly List<long> Ids = [];
        public readonly int BytesPerPixel = GlyphAtlas.BytesPerPixel(coverage);
        public readonly int Side = side;
        public readonly int MaxPages = maxPages;
        private readonly int _plotsPerPage = side / atlas.PlotSize * (side / atlas.PlotSize);
        private readonly List<Plot> _plots = [];

        public ref Plot PlotAt(int index) => ref CollectionsMarshal.AsSpan(_plots)[index];

        public void DropUnusedExtraPages(long frame)
        {
            while (Pixels.Count > MaxPages && !PageUsedIn(Pixels.Count - 1, frame))
            {
                DropLastPage();
            }
        }

        // A plot with room; else a new page while there are fewer than MaxPages; else the least recently used plot not used this
        // frame, emptied; else (every plot used this frame) a new page beyond MaxPages, up to PageLimit; else -1.
        public int FindRoom(int width, int height, out int x, out int y)
        {
            Span<Plot> plots = CollectionsMarshal.AsSpan(_plots);
            for (int i = 0; i < plots.Length; i++)
            {
                if (plots[i].TryPlace(width, height, out x, out y))
                {
                    return i;
                }
            }

            long frame = atlas.Frame;
            int target = -1;
            if (Pixels.Count >= MaxPages)
            {
                for (int i = 0; i < plots.Length; i++)
                {
                    if (plots[i].LastUsedFrame < frame && (target < 0 || plots[i].LastUsedFrame < plots[target].LastUsedFrame))
                    {
                        target = i;
                    }
                }
            }

            if (target >= 0)
            {
                Evict(target);
            }
            else if (Pixels.Count >= 2 * MaxPages)
            {
                x = 0;
                y = 0;
                return -1;
            }
            else
            {
                AddPage();
                target = _plots.Count - _plotsPerPage;
            }

            _ = PlotAt(target).TryPlace(width, height, out x, out y);
            return target;
        }

        private void AddPage()
        {
            int page = Pixels.Count;
            int size = Side;
            Pixels.Add(GC.AllocateArray<byte>(size * size * BytesPerPixel, pinned: true));
            Ids.Add(Interlocked.Increment(ref _lastPageId));
            for (int y = 0; y < size; y += atlas.PlotSize)
            {
                for (int x = 0; x < size; x += atlas.PlotSize)
                {
                    _plots.Add(new Plot(page, x, y, atlas.PlotSize));
                }
            }
        }

        // Forgets the plot's glyphs and clears its pixels, so that sampling near a new glyph never sees an old one.
        private void Evict(int plotIndex)
        {
            ref Plot plot = ref PlotAt(plotIndex);
            foreach (GlyphKey key in CollectionsMarshal.AsSpan(plot.Keys))
            {
                _ = atlas._glyphs.Remove(key);
            }

            byte[] page = Pixels[plot.Page];
            int stride = Side * BytesPerPixel;
            int plotSize = atlas.PlotSize;
            for (int row = 0; row < plotSize; row++)
            {
                page.AsSpan(((plot.Y + row) * stride) + (plot.X * BytesPerPixel), plotSize * BytesPerPixel).Clear();
            }

            plot.Reset();
            atlas._dirty.Add(new AtlasRegion(plot.Page, plot.X, plot.Y, plotSize, plotSize, coverage));
            atlas.EvictionCount++;
        }

        private bool PageUsedIn(int page, long frame)
        {
            int first = page * _plotsPerPage;
            for (int i = first; i < first + _plotsPerPage; i++)
            {
                if (_plots[i].LastUsedFrame >= frame)
                {
                    return true;
                }
            }

            return false;
        }

        private void DropLastPage()
        {
            int page = Pixels.Count - 1;
            for (int i = _plots.Count - 1; i >= page * _plotsPerPage; i--)
            {
                foreach (GlyphKey key in CollectionsMarshal.AsSpan(_plots[i].Keys))
                {
                    _ = atlas._glyphs.Remove(key);
                }

                _plots.RemoveAt(i);
            }

            Pixels.RemoveAt(page);
            Ids.RemoveAt(page);

            // A loop rather than RemoveAll with a lambda, which would allocate a closure over `page`.
            List<AtlasRegion> dirty = atlas._dirty;
            for (int i = dirty.Count - 1; i >= 0; i--)
            {
                if (dirty[i].Page == page && dirty[i].Coverage == coverage)
                {
                    dirty.RemoveAt(i);
                }
            }
        }
    }

    // A plot: shelves of glyphs inside a square of a page, and the last frame that used one of them.
    private struct Plot(int page, int x, int y, int size)
    {
        public readonly int Page = page;
        public readonly int X = x;
        public readonly int Y = y;
        public readonly List<GlyphKey> Keys = [];
        public long LastUsedFrame;
        private readonly int _size = size;
        private int _shelfX = Padding;
        private int _shelfY = Padding;
        private int _shelfHeight;

        public void Reset()
        {
            Keys.Clear();
            LastUsedFrame = 0;
            _shelfX = Padding;
            _shelfY = Padding;
            _shelfHeight = 0;
        }

        // The next spot on the current shelf, or the start of a new shelf below it, with one pixel of padding around the glyph.
        public bool TryPlace(int width, int height, out int x, out int y)
        {
            int shelfX = _shelfX;
            int shelfY = _shelfY;
            int shelfHeight = _shelfHeight;
            if (shelfX + width + Padding > _size)
            {
                shelfX = Padding;
                shelfY += shelfHeight + Padding;
                shelfHeight = 0;
            }

            if (shelfY + height + Padding > _size)
            {
                x = 0;
                y = 0;
                return false;
            }

            x = X + shelfX;
            y = Y + shelfY;
            _shelfX = shelfX + width + Padding;
            _shelfY = shelfY;
            _shelfHeight = Math.Max(shelfHeight, height);
            return true;
        }
    }
}
