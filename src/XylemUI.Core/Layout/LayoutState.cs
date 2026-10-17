namespace XylemUI;

/// <summary>
/// The layout part of a node (design §2.4.1).
/// </summary>
internal struct LayoutState
{
    /// <summary>
    /// The layout properties.
    /// </summary>
    public LayoutStyle Style;

    /// <summary>
    /// Measures the content of a leaf; <see langword="null"/> for a node whose size comes from its children.
    /// </summary>
    public MeasureFunc? Measure;

    /// <summary>
    /// The result of the last layout.
    /// </summary>
    public Rect Rect;

    /// <summary>
    /// The last layout's constraint was tight, so the content cannot change the size (the computed boundary). Read by
    /// invalidation, which runs before this frame's constraint exists.
    /// </summary>
    public bool CachedComputedBoundary;

    /// <summary>
    /// The constraint of the last layout: the constraint of a boundary laid out again for a change inside it.
    /// </summary>
    public AvailableSpace LastAvailableSpace;

    /// <summary>
    /// The key of the last layout; <see langword="null"/> before the first.
    /// </summary>
    public LayoutCacheKey? CachedKey;

    /// <summary>
    /// The key of the last measurement: a run that returned a size and wrote nothing else (a flex container measuring its
    /// children); <see langword="null"/> before the first.
    /// </summary>
    public LayoutCacheKey? MeasureKey;

    /// <summary>
    /// The size of the last measurement.
    /// </summary>
    public Size MeasuredSize;
}
