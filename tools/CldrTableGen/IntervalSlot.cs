namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// How a skeleton writes a range whose dates first differ in one field (design §4.11, "Interval formats").
/// </summary>
/// <param name="Kind">The way the range is written.</param>
/// <param name="First">For <see cref="IntervalKind.Split"/>, the part of the first date; for <see cref="IntervalKind.Fallback"/>, the
/// pattern that writes each date; empty otherwise.</param>
/// <param name="Second">For <see cref="IntervalKind.Split"/>, the part of the second date, which may be empty; empty otherwise.</param>
/// <param name="LaterFirst">For <see cref="IntervalKind.Split"/>, whether the first date is the later one.</param>
internal sealed record IntervalSlot(IntervalKind Kind, string First, string Second, bool LaterFirst)
{
    /// <summary>
    /// Gets the slot that writes the earlier date alone with the skeleton's pattern.
    /// </summary>
    public static IntervalSlot Single { get; } = new(IntervalKind.Single, string.Empty, string.Empty, false);

    /// <summary>
    /// Gets the slot that writes both dates with the skeleton's pattern and joins them with <c>intervalFormatFallback</c>.
    /// </summary>
    public static IntervalSlot OwnFallback { get; } = new(IntervalKind.OwnFallback, string.Empty, string.Empty, false);
}
