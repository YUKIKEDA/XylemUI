namespace XylemUI;

/// <summary>
/// What a parent last read from a leaf whose content changed (design §2.4.1, row L5): kept when the leaf turns from clean to
/// dirty, and compared with the leaf's new values in the layout pass. The parent stays clean when they are the same.
/// </summary>
internal struct ContentSnapshot
{
    /// <summary>
    /// The leaf.
    /// </summary>
    public NodeHandle Node;

    /// <summary>
    /// The stages of the changes since the snapshot, for the path to the parent when a value differs.
    /// </summary>
    public DirtyFlags Flags;

    /// <summary>
    /// The size of the leaf's last layout run.
    /// </summary>
    public Size Size;

    /// <summary>
    /// The leaf's first baseline of the last layout run, or NaN.
    /// </summary>
    public float FirstBaseline;

    /// <summary>
    /// The leaf's measurements and content widths, with the parent's results of the base-size step.
    /// </summary>
    public MeasureCache Measures;
}
