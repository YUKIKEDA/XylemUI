namespace XylemUI.Testing;

/// <summary>
/// The window a <see cref="HeadlessApp"/> stands for, and how its virtual time moves (design §12.2, §12.4).
/// </summary>
public sealed record HeadlessOptions
{
    /// <summary>
    /// Gets the width of the window's client area in logical pixels; 800 by default.
    /// </summary>
    public float Width { get; init; } = 800;

    /// <summary>
    /// Gets the height of the window's client area in logical pixels; 600 by default.
    /// </summary>
    public float Height { get; init; } = 600;

    /// <summary>
    /// Gets the number of physical pixels to a logical one; 1 by default.
    /// </summary>
    public float Scale { get; init; } = 1;

    /// <summary>
    /// Gets how far one frame of <see cref="HeadlessApp.PumpAndSettle"/> advances the clock; 1/60 of a second by default.
    /// </summary>
    public TimeSpan FrameInterval { get; init; } = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    /// <summary>
    /// Gets the most iterations <see cref="HeadlessApp.PumpAndSettle"/> runs before it throws; 1,000 by default.
    /// </summary>
    public int MaxIterations { get; init; } = 1_000;
}
