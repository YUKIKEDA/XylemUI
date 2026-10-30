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
    /// <param name="width">The content's width.</param>
    /// <param name="height">The content's height.</param>
    /// <returns>The result with the size and a NaN baseline.</returns>
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

/// <summary>
/// The widths of a leaf's content box with nothing to break against: the narrowest it can be, its longest unbreakable run
/// (min-content), and the widest it is, with no line broken (max-content). One call returns both, as Chromium's
/// <c>ComputeMinMaxSizes</c> does (design §3.2, row L4).
/// </summary>
/// <param name="Min">The min-content width.</param>
/// <param name="Max">The max-content width.</param>
/// <param name="DependsOnHeight">
/// The widths change with a decided content height (an aspect ratio, a vertical writing mode); otherwise one answer serves every
/// height.
/// </param>
internal readonly record struct IntrinsicWidths(float Min, float Max, bool DependsOnHeight);

/// <summary>
/// Returns the widths of a leaf's content (design §3.2, row L4). It reads only what is already written on the node, never a
/// signal, and agrees with the leaf's <see cref="MeasureFunc"/> under a min-content and a max-content width.
/// </summary>
/// <param name="knownHeight">The content-box height already decided, or NaN where the content decides.</param>
/// <returns>The min-content and max-content widths of the content.</returns>
internal delegate IntrinsicWidths IntrinsicWidthsFunc(float knownHeight);
