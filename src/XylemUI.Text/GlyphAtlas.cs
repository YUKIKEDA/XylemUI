using System.Runtime.InteropServices;

namespace XylemUI.Text;

/// <summary>
/// The CPU side of the glyph atlas: the coverage of rasterized glyphs on pages of a fixed size, each split into plots, with the
/// least recently used plot evicted when there is no room (design §5.0.2, §5.0.3; the same scheme as Skia's GrDrawOpAtlas). Each
/// kind of coverage (<see cref="GlyphCoverage"/>) has its own pages: one byte per pixel for grayscale, four for ClearType. A
/// ClearType page has half the side of a grayscale page, so the two take the same bytes, and a kind may hold four times as many
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
/// </remarks>
public sealed class GlyphAtlas
{
    private const int Padding = 1;

    private static long _lastPageId;

    private readonly IGlyphRasterizer _rasterizer;
    private readonly Dictionary<GlyphKey, (AtlasGlyph Glyph, int Plot)> _glyphs = [];
    private readonly List<AtlasRegion> _dirty = [];
    private readonly Pages _gray;
    private readonly Pages _rgb;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphAtlas"/> class.
    /// </summary>
    /// <param name="rasterizer">Draws the glyphs the atlas does not hold yet.</param>
    /// <param name="pageSize">The width and height of a grayscale page, in pixels; a ClearType page is half as wide and high.</param>
    /// <param name="plotSize">The width and height of a plot, in pixels; it divides half of <paramref name="pageSize"/>.</param>
    /// <param name="maxPages">The number of grayscale pages kept in steady state; four times as many ClearType pages.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive, or the plot does not divide half the page.</exception>
    public GlyphAtlas(IGlyphRasterizer rasterizer, int pageSize = 1024, int plotSize = 256, int maxPages = 4)
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
        PageSize = pageSize;
        PlotSize = plotSize;
        MaxPages = maxPages;
        _gray = new Pages(this, GlyphCoverage.Gray, pageSize, maxPages);
        _rgb = new Pages(this, GlyphCoverage.Rgb, pageSize / 2, 4 * maxPages);
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
    /// Gets the number of requests refused so far because the kind had its page limit and no plot to evict. A
    /// refused glyph is not cached, so each request for it rasterizes it again; a rising count means a frame needs more than the
    /// limit, or <see cref="BeginFrame"/> is not called.
    /// </summary>
    public int RefusedCount { get; private set; }

    /// <summary>
    /// Returns the bytes of one pixel of a kind's pages: 1 for <see cref="GlyphCoverage.Gray"/>, 4 for
    /// <see cref="GlyphCoverage.Rgb"/>.
    /// </summary>
    /// <param name="coverage">The kind.</param>
    /// <returns>The bytes per pixel.</returns>
    public static int BytesPerPixel(GlyphCoverage coverage) => coverage == GlyphCoverage.Rgb ? 4 : 1;

    /// <summary>
    /// Returns the width and height of a kind's pages, in pixels: <see cref="PageSize"/> for grayscale, half of it for ClearType.
    /// </summary>
    /// <param name="coverage">The kind.</param>
    /// <returns>The size.</returns>
    public int GetPageSize(GlyphCoverage coverage) => PagesOf(coverage).Side;

    /// <summary>
    /// Returns the number of a kind's pages kept in steady state: <see cref="MaxPages"/> for grayscale, four times as many for
    /// ClearType.
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
        Frame++;
    }

    /// <summary>
    /// Forgets the dirty regions, after they were uploaded.
    /// </summary>
    public void ClearDirtyRegions() => _dirty.Clear();

    /// <summary>
    /// Returns the region of a glyph, rasterizing it into the atlas the first time, and marks it used in the current frame.
    /// </summary>
    /// <param name="key">The glyph, its size, its subpixel step, and its kind of coverage.</param>
    /// <param name="glyph">The glyph's region and bearings, on a page of the key's kind.</param>
    /// <returns>
    /// <see langword="false"/> when the glyph is larger than a plot, or when there is no room: every plot of the kind was used in
    /// the current frame and the kind has its page limit (<see cref="GetPageLimit"/>). A glyph with no ink always fits.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The key has no font, a size that is not positive, a subpixel step out of range, or a coverage that is not defined.
    /// </exception>
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

        if (key.Coverage is not (GlyphCoverage.Gray or GlyphCoverage.Rgb))
        {
            throw new ArgumentException($"The glyph coverage {key.Coverage} is not defined.", nameof(key));
        }

        if (_glyphs.TryGetValue(key, out (AtlasGlyph Glyph, int Plot) cached))
        {
            if (cached.Plot >= 0)
            {
                PagesOf(key.Coverage).PlotAt(cached.Plot).LastUsedFrame = Frame;
            }

            glyph = cached.Glyph;
            return true;
        }

        return TryRasterize(key, out glyph);
    }

    private Pages PagesOf(GlyphCoverage coverage) => coverage == GlyphCoverage.Rgb ? _rgb : _gray;

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

        Pages pages = PagesOf(key.Coverage);
        int plotIndex = pages.FindRoom(width, height, out int x, out int y);
        if (plotIndex < 0)
        {
            RefusedCount++;
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
        glyph = new AtlasGlyph(plot.Page, x, y, width, height, box.Left, box.Top);
        _glyphs.Add(key, (glyph, plotIndex));
        _dirty.Add(new AtlasRegion(plot.Page, x, y, width, height, key.Coverage));
        return true;
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
