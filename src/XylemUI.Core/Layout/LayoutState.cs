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
    /// The style uses no percentage, so <see cref="MeasureCache.Box"/> holds its lengths in pixels for every parent (row L4).
    /// </summary>
    public bool HasBox;

    /// <summary>
    /// The style sets an inset (<see cref="LayoutStyle.Inset"/>), kept when the style is set: the final layout of every item
    /// reads it to skip the relative shift of the many that have none (row L2).
    /// </summary>
    public bool HasInset;

    /// <summary>
    /// A minimum or a maximum of the style is a sizing keyword (<see cref="LayoutStyle.HasSizingKeywordLimit"/>, row L3), kept
    /// when the style is set: a container reads it for each child without reading the child's style. Reading the style there,
    /// with a copy of the four limits, made a change in row L2's scene of absolutely positioned children 5.2 % slower than main;
    /// with this flag, 0.7 % (<c>layout-loop absolute change</c>, main and the branch alternated 12 times each).
    /// </summary>
    public bool HasKeywordLimits;

    /// <summary>
    /// The number of children that are hidden or absolutely positioned (row L2), kept by <see cref="UiTree.AppendChild"/> and
    /// <see cref="UiTree.SetStyle"/>. Where it is 0, the usual case, a container's algorithm reads no child's style to find its
    /// items: reading it cost the relayout after one change of row L1a's scene about 5 % (another part of each child's node).
    /// </summary>
    public int OutOfFlowChildren;

    /// <summary>
    /// The index of the node's <see cref="ContentSnapshot"/> in <see cref="UiTree.ContentSnapshots"/> plus 1; 0 when it has
    /// none (row L5). Next to the flags, in the struct's padding, so that finding the snapshot takes no search.
    /// </summary>
    public int ContentSnapshotSlot;

    /// <summary>
    /// Measures the content of a leaf; <see langword="null"/> for a leaf without content and for a container.
    /// </summary>
    public MeasureFunc? Measure;

    /// <summary>
    /// Returns the widths of a leaf's content, which answer its min-content and max-content width queries without a measuring
    /// run (row L4); <see langword="null"/> where <see cref="Measure"/> answers them.
    /// </summary>
    public IntrinsicWidthsFunc? IntrinsicWidths;

    /// <summary>
    /// The type of the last exception the leaf's measurement threw and the pass reported; <see langword="null"/> when none. The
    /// same type again is counted, not reported, until a change of the node clears it (design §2.4.1).
    /// </summary>
    public Type? ReportedFailure;

    /// <summary>
    /// The border box of the last layout run, relative to the parent's border box.
    /// </summary>
    public Rect Rect;

    /// <summary>
    /// A leaf's first baseline of the last layout run, from the top of the border box, or NaN when it has none (a parent
    /// synthesizes one from the border box, CSS Flexbox §9.4). A container's comes from <see cref="UiNode.BaselineChild"/>.
    /// </summary>
    public float FirstBaseline;

    /// <summary>
    /// The node is a block: it takes the baseline of its first child that has one, from <see cref="UiNode.BaselineChild"/>
    /// on. A flex container takes <see cref="UiNode.BaselineChild"/>'s, synthesized from its border box when it has none.
    /// </summary>
    public bool BaselineFromFirstChildWithOne;

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
