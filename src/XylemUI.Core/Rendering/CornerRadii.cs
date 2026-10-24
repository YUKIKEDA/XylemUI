namespace XylemUI;

/// <summary>
/// The radius of each corner of a rounded rectangle, in logical pixels (design §5.0).
/// </summary>
/// <param name="TopLeft">The top-left radius.</param>
/// <param name="TopRight">The top-right radius.</param>
/// <param name="BottomRight">The bottom-right radius.</param>
/// <param name="BottomLeft">The bottom-left radius.</param>
public readonly record struct CornerRadii(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CornerRadii"/> struct with one radius for every corner.
    /// </summary>
    /// <param name="all">The radius of every corner.</param>
    public CornerRadii(float all)
        : this(all, all, all, all) { }
}
