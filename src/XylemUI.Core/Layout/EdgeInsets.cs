namespace XylemUI;

/// <summary>
/// Distances from the four edges of a box: a padding or a margin.
/// </summary>
/// <param name="Left">The left distance.</param>
/// <param name="Top">The top distance.</param>
/// <param name="Right">The right distance.</param>
/// <param name="Bottom">The bottom distance.</param>
internal readonly record struct EdgeInsets(float Left, float Top, float Right, float Bottom)
{
    /// <summary>
    /// Gets the sum of the left and right distances.
    /// </summary>
    public float Horizontal => Left + Right;

    /// <summary>
    /// Gets the sum of the top and bottom distances.
    /// </summary>
    public float Vertical => Top + Bottom;

    /// <summary>
    /// Returns insets with the same distance on all four edges.
    /// </summary>
    public static EdgeInsets All(float value) => new(value, value, value, value);
}
