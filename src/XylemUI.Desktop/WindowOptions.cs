namespace XylemUI;

/// <summary>
/// The window <see cref="XylemApp.Run"/> opens (design §2.13).
/// </summary>
/// <param name="Title">The title bar text.</param>
/// <param name="Width">The width of the client area, in logical pixels: times the display's scale on the screen.</param>
/// <param name="Height">The height of the client area, in logical pixels.</param>
public sealed record WindowOptions(string Title = "XylemUI", float Width = 800, float Height = 600);
