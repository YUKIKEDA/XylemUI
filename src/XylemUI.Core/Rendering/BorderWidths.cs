namespace XylemUI;

/// <summary>
/// The width of each edge of a border, in logical pixels (design §5.0, "Shapes, brushes, and shadows").
/// </summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Bottom">The bottom edge.</param>
public readonly record struct BorderWidths(float Left, float Top, float Right, float Bottom)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BorderWidths"/> struct with one width for every edge.
    /// </summary>
    /// <param name="all">The width of every edge.</param>
    public BorderWidths(float all)
        : this(all, all, all, all) { }
}
