namespace XylemUI;

/// <summary>
/// The size of a leaf's content box and its first baseline (design §3.2, §4.3).
/// </summary>
/// <param name="Size">The content's size.</param>
/// <param name="FirstBaseline">The distance from the top of the content box to the first baseline, or NaN when there is none.</param>
internal readonly record struct MeasureResult(Size Size, float FirstBaseline)
{
    /// <summary>
    /// Returns a size without a baseline.
    /// </summary>
    public static MeasureResult Of(float width, float height) => new(new Size(width, height), float.NaN);
}

/// <summary>
/// Measures the content of a leaf node (a text node, an image) for its content box (design §4.3). It reads only what is
/// already written on the node, never a signal.
/// </summary>
/// <param name="known">The content-box sizes already decided, or NaN where the content decides.</param>
/// <param name="available">The space for the content box on each axis.</param>
/// <returns>The size of the content, and its first baseline.</returns>
internal delegate MeasureResult MeasureFunc(OptionalSize known, AvailableSize available);
