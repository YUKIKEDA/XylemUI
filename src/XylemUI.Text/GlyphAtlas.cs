using System.Runtime.InteropServices;

namespace XylemUI.Text;

/// <summary>
/// The CPU side of the glyph atlas: 8-bit coverage of rasterized glyphs on pages of a fixed size, each split into plots, with the
/// least recently used plot evicted when there is no room (design §5.0.2, §5.0.3; the same scheme as Skia's GrDrawOpAtlas).
/// </summary>
/// <remarks>
/// <para>
/// A glyph is rasterized by the atlas's <see cref="IGlyphRasterizer"/> the first time it is asked for, at one size and subpixel
/// step (<see cref="GlyphKey"/>), and kept: the next request returns the
/// same region without rasterizing, and marks its plot used in the current frame. Glyphs are packed on shelves inside a plot.
/// </para>
/// <para>
/// When no plot has room, a page is added while there are fewer than <see cref="MaxPages"/>; after that, the plot used longest
/// ago, and not in the current frame, is emptied: its glyphs are forgotten and will be rasterized again when they are next asked
/// for. When every plot was used in the current frame, a page is added beyond <see cref="MaxPages"/>; <see cref="BeginFrame"/>
/// drops such extra pages once a frame no longer uses them. So the memory is bounded by <see cref="MaxPages"/> in steady state, and
/// a frame only re-rasterizes the glyphs of the plots it evicts. The pages never exceed <see cref="PageLimit"/>: a frame that needs
/// more (more new glyphs than the limit holds, or a caller that never calls <see cref="BeginFrame"/>) gets <see langword="false"/>
/// for the glyphs that do not fit, and they are not drawn.
/// </para>
/// <para>
/// The regions changed since the last <see cref="ClearDirtyRegions"/> are listed in <see cref="DirtyRegions"/>, for the GPU upload
/// after the display list is built (design §5.0.3). Each page is a pinned array: the rasterizer writes the coverage straight in,
/// and the upload reads it in place. One atlas belongs to one thread, and its changes go to one renderer: the renderer clears the dirty
/// regions after uploading them, so a second renderer of the same atlas would miss them (sharing is decided in roadmap row P10).
/// </para>
/// </remarks>
public sealed class GlyphAtlas
{
    private const int Padding = 1;

    private static long _lastPageId;

    private readonly List<byte[]> _pages = [];
    private readonly List<long> _pageIds = [];
    private readonly List<Plot> _plots = [];
    private readonly IGlyphRasterizer _rasterizer;
    private readonly Dictionary<GlyphKey, (AtlasGlyph Glyph, int Plot)> _glyphs = [];
    private readonly List<AtlasRegion> _dirty = [];
    private readonly int _plotsPerPage;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphAtlas"/> class.
    /// </summary>
    /// <param name="rasterizer">Draws the glyphs the atlas does not hold yet.</param>
    /// <param name="pageSize">The width and height of a page, in pixels.</param>
    /// <param name="plotSize">The width and height of a plot, in pixels; it divides <paramref name="pageSize"/>.</param>
    /// <param name="maxPages">The number of pages kept in steady state.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive, or the plot does not divide the page.</exception>
    public GlyphAtlas(IGlyphRasterizer rasterizer, int pageSize = 1024, int plotSize = 256, int maxPages = 4)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plotSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);
        if (plotSize > pageSize || pageSize % plotSize != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plotSize), "The plot size must divide the page size.");
        }

        _rasterizer = rasterizer;
        PageSize = pageSize;
        PlotSize = plotSize;
        MaxPages = maxPages;
        _plotsPerPage = pageSize / plotSize * (pageSize / plotSize);
    }

    /// <summary>
    /// Gets the width and height of a page, in pixels.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the width and height of a plot, in pixels. A glyph larger than a plot (less its padding) does not fit the atlas.
    /// </summary>
    public int PlotSize { get; }

    /// <summary>
    /// Gets the number of pages kept in steady state.
    /// </summary>
    public int MaxPages { get; }

    /// <summary>
    /// Gets the number of pages now; it exceeds <see cref="MaxPages"/> only while frames need the extra pages.
    /// </summary>
    public int PageCount => _pages.Count;

    /// <summary>
    /// Gets the most pages the atlas ever holds: twice <see cref="MaxPages"/>.
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
    /// Gets the number of requests refused so far because the atlas had <see cref="PageLimit"/> pages and no plot to evict. A
    /// refused glyph is not cached, so each request for it rasterizes it again; a rising count means a frame needs more than the
    /// limit, or <see cref="BeginFrame"/> is not called.
    /// </summary>
    public int RefusedCount { get; private set; }

    /// <summary>
    /// Returns the coverage of a page, one byte per pixel, row after row (the stride is <see cref="PageSize"/>).
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The pixels.</returns>
    public ReadOnlySpan<byte> GetPagePixels(int page) => _pages[page];

    /// <summary>
    /// Returns the id of a page: unique in the process, so a page dropped and added again, or a page of another atlas, has
    /// another id. A renderer uploads the whole page when the id at an index changes, and only the dirty regions otherwise.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The id.</returns>
    public long GetPageId(int page) => _pageIds[page];

    /// <summary>
    /// Starts a new frame: glyphs asked for from now on protect their plots from eviction until the next call. Pages beyond
    /// <see cref="MaxPages"/> that the frame just ended did not use are dropped, last first.
    /// </summary>
    public void BeginFrame()
    {
        while (_pages.Count > MaxPages && !PageUsedIn(_pages.Count - 1, Frame))
        {
            DropLastPage();
        }

        Frame++;
    }

    /// <summary>
    /// Forgets the dirty regions, after they were uploaded.
    /// </summary>
    public void ClearDirtyRegions() => _dirty.Clear();

    /// <summary>
    /// Returns the region of a glyph, rasterizing it into the atlas the first time, and marks it used in the current frame.
    /// </summary>
    /// <param name="key">The glyph, its size, and its subpixel step.</param>
    /// <param name="glyph">The glyph's region and bearings.</param>
    /// <returns>
    /// <see langword="false"/> when the glyph is larger than a plot, or when there is no room: every plot was used in the current
    /// frame and the atlas has <see cref="PageLimit"/> pages. A glyph with no ink always fits.
    /// </returns>
    /// <exception cref="ArgumentException">The key has no font, a size that is not positive, or a subpixel step out of range.</exception>
    /// <exception cref="InvalidOperationException">The rasterizer could not draw the glyph.</exception>
    /// <exception cref="ObjectDisposedException">The font was disposed and the glyph is not in the atlas.</exception>
    public bool TryGet(in GlyphKey key, out AtlasGlyph glyph)
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

        if (_glyphs.TryGetValue(key, out (AtlasGlyph Glyph, int Plot) cached))
        {
            if (cached.Plot >= 0)
            {
                CollectionsMarshal.AsSpan(_plots)[cached.Plot].LastUsedFrame = Frame;
            }

            glyph = cached.Glyph;
            return true;
        }

        return TryRasterize(key, out glyph);
    }

    private bool TryRasterize(in GlyphKey key, out AtlasGlyph glyph)
    {
        GlyphBox box = _rasterizer.Measure(key);
        RasterizeCount++;
        int width = box.Width;
        int height = box.Height;

        // A glyph with no ink (a space) needs no room, so it fits even when every plot is full.
        if (width == 0 || height == 0)
        {
            glyph = new AtlasGlyph(0, 0, 0, 0, 0, box.Left, box.Top);
            _glyphs.Add(key, (glyph, -1));
            return true;
        }

        if (width + (2 * Padding) > PlotSize || height + (2 * Padding) > PlotSize)
        {
            glyph = default;
            return false;
        }

        int plotIndex = FindRoom(width, height, out int x, out int y);
        if (plotIndex < 0)
        {
            RefusedCount++;
            glyph = default;
            return false;
        }

        ref Plot plot = ref CollectionsMarshal.AsSpan(_plots)[plotIndex];
        byte[] page = _pages[plot.Page];

        // FindRoom placed the region inside the plot, so its rows lie inside the page, PageSize bytes apart.
        int start = (y * PageSize) + x;
        _rasterizer.Rasterize(key, page.AsSpan(start, ((height - 1) * PageSize) + width), PageSize);

        plot.LastUsedFrame = Frame;
        plot.Keys.Add(key);
        glyph = new AtlasGlyph(plot.Page, x, y, width, height, box.Left, box.Top);
        _glyphs.Add(key, (glyph, plotIndex));
        _dirty.Add(new AtlasRegion(plot.Page, x, y, width, height));
        return true;
    }

    // A plot with room; else a new page while there are fewer than MaxPages; else the least recently used plot not used this
    // frame, emptied; else (every plot used this frame) a new page beyond MaxPages, up to PageLimit; else -1.
    private int FindRoom(int width, int height, out int x, out int y)
    {
        Span<Plot> plots = CollectionsMarshal.AsSpan(_plots);
        for (int i = 0; i < plots.Length; i++)
        {
            if (plots[i].TryPlace(width, height, out x, out y))
            {
                return i;
            }
        }

        int target = -1;
        if (_pages.Count >= MaxPages)
        {
            for (int i = 0; i < plots.Length; i++)
            {
                if (plots[i].LastUsedFrame < Frame && (target < 0 || plots[i].LastUsedFrame < plots[target].LastUsedFrame))
                {
                    target = i;
                }
            }
        }

        if (target >= 0)
        {
            Evict(target);
        }
        else if (_pages.Count >= PageLimit)
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

        _ = CollectionsMarshal.AsSpan(_plots)[target].TryPlace(width, height, out x, out y);
        return target;
    }

    private void AddPage()
    {
        int page = _pages.Count;
        _pages.Add(GC.AllocateArray<byte>(PageSize * PageSize, pinned: true));
        _pageIds.Add(Interlocked.Increment(ref _lastPageId));
        for (int y = 0; y < PageSize; y += PlotSize)
        {
            for (int x = 0; x < PageSize; x += PlotSize)
            {
                _plots.Add(new Plot(page, x, y, PlotSize));
            }
        }
    }

    // Forgets the plot's glyphs and clears its pixels, so that sampling near a new glyph never sees an old one.
    private void Evict(int plotIndex)
    {
        ref Plot plot = ref CollectionsMarshal.AsSpan(_plots)[plotIndex];
        foreach (GlyphKey key in CollectionsMarshal.AsSpan(plot.Keys))
        {
            _ = _glyphs.Remove(key);
        }

        byte[] page = _pages[plot.Page];
        for (int row = 0; row < PlotSize; row++)
        {
            page.AsSpan(((plot.Y + row) * PageSize) + plot.X, PlotSize).Clear();
        }

        plot.Reset();
        _dirty.Add(new AtlasRegion(plot.Page, plot.X, plot.Y, PlotSize, PlotSize));
        EvictionCount++;
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
        int page = _pages.Count - 1;
        for (int i = _plots.Count - 1; i >= page * _plotsPerPage; i--)
        {
            foreach (GlyphKey key in CollectionsMarshal.AsSpan(_plots[i].Keys))
            {
                _ = _glyphs.Remove(key);
            }

            _plots.RemoveAt(i);
        }

        _pages.RemoveAt(page);
        _pageIds.RemoveAt(page);

        // A loop rather than RemoveAll with a lambda, which would allocate a closure over `page`.
        for (int i = _dirty.Count - 1; i >= 0; i--)
        {
            if (_dirty[i].Page == page)
            {
                _dirty.RemoveAt(i);
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
