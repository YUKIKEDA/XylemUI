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
    /// Gets what makes the renderer that draws the app's frames into pixels (design §12.3, "The plug-in"), such as
    /// <c>XylemUI.Testing.D3D11</c>'s WARP renderer; <see langword="null"/> by default, for an app that draws nothing. The app
    /// disposes the renderer.
    /// </summary>
    public Func<HeadlessRenderer>? Renderer { get; init; }

    /// <summary>
    /// Gets a value indicating whether the app acts as if the OS asked for less motion: animations jump to their end values (design
    /// §2.14, "Reduced motion"); <see langword="false"/> by default.
    /// </summary>
    public bool ReduceMotion { get; init; }

    /// <summary>
    /// Gets a value indicating whether a text field's caret blinks, every 530 ms of the virtual clock (design §4.9, "The caret");
    /// <see langword="false"/> by default, where the caret is always drawn: a blinking caret keeps a timer that
    /// <see cref="HeadlessApp.PumpAndSettle"/> waits for and that fails the end of the test (design §12.2). The caret's width and the
    /// selection's colors are always Windows' defaults (1, and the selection's colors of Windows 11's default theme).
    /// </summary>
    public bool CaretBlink { get; init; }

    /// <summary>
    /// Gets the most iterations <see cref="HeadlessApp.PumpAndSettle"/> runs before it throws; 1,000 by default.
    /// </summary>
    public int MaxIterations { get; init; } = 1_000;
}
