namespace XylemUI;

/// <summary>
/// Carries what a leaf's measurement threw, with the leaf, up to the layout pass, which reports the inner exception (design
/// §2.4.1, the exceptions of a layout). It never leaves the pass.
/// </summary>
internal sealed class LeafMeasureException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LeafMeasureException"/> class.
    /// </summary>
    /// <param name="node">The leaf.</param>
    /// <param name="inner">What its measurement threw.</param>
    public LeafMeasureException(NodeHandle node, Exception inner)
        : base("A leaf's measurement threw.", inner) => Node = node;

    /// <summary>
    /// Gets the leaf.
    /// </summary>
    public NodeHandle Node { get; }
}
