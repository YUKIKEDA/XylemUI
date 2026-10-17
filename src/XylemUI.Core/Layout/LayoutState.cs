namespace XylemUI;

/// <summary>
/// The layout part of a node: its style, its last result, and the caches that let a clean node skip the work (design §2.4,
/// §3.2).
/// </summary>
internal struct LayoutState
{
    /// <summary>
    /// The layout style.
    /// </summary>
    public LayoutStyle Style;

    /// <summary>
    /// The style uses a percentage of the parent's width (<see cref="LayoutStyle.UsesPercentOfParentWidth"/>), kept when the
    /// style is set: every cache key reads it.
    /// </summary>
    public bool UsesPercentOfParentWidth;

    /// <summary>
    /// The style uses a percentage of the parent's height (<see cref="LayoutStyle.UsesPercentOfParentHeight"/>).
    /// </summary>
    public bool UsesPercentOfParentHeight;

    /// <summary>
    /// Measures the content of a leaf; <see langword="null"/> for a leaf without content and for a container.
    /// </summary>
    public MeasureFunc? Measure;

    /// <summary>
    /// The border box of the last layout run, relative to the parent's border box.
    /// </summary>
    public Rect Rect;

    /// <summary>
    /// The parent decided both sides in the last layout run (design §2.4.1, a computed boundary unless the parent read the
    /// content).
    /// </summary>
    public bool CachedComputedBoundary;

    /// <summary>
    /// The input of the last layout run: the source of the constraint when the node is laid out again from the worklist.
    /// </summary>
    public LayoutInput LastInput;

    /// <summary>
    /// The node was laid out at least once, so <see cref="LastInput"/> holds.
    /// </summary>
    public bool WasLaidOut;

    /// <summary>
    /// The key of the last layout run, when <see cref="WasLaidOut"/>. A dirty node does not read it.
    /// </summary>
    public LayoutCacheKey CachedKey;

    /// <summary>
    /// Advanced each time the node's caches are dropped. A child read by this node keeps the generation of the read. It starts
    /// at 1, so that 0 means "never read"; after 2³² drops it would wrap to 0 and a read in that generation would look like
    /// none.
    /// </summary>
    public uint CacheGeneration;

    /// <summary>
    /// The parent's <see cref="CacheGeneration"/> when it last read this node's content (a measuring run or a baseline);
    /// 0 when it never did. While it matches, a cached result of the parent depends on this node's content, so a change inside
    /// goes on to the parent (design §2.4.1).
    /// </summary>
    public uint ContentReadGeneration;
}
