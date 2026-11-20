namespace XylemUI;

/// <summary>
/// What a window's root wrapper tells the host about the window itself (design §13.2, "根の箱" and "題名の棒"): the color the host
/// paints behind the tree, and whether the title bar is dark. Each window has one, which it provides at its root under
/// <see cref="Current"/>; a part of the tree that provides another theme does not reach it.
/// </summary>
/// <remarks>
/// The headless app shows the values it was given (<c>HeadlessApp.Backdrop</c> and <c>HeadlessApp.DarkTitleBar</c>). A desktop
/// window does not use them yet: Windows paints them from row TH1's fifth pull request on (design §13.2, "行 TH1 の手順").
/// </remarks>
public sealed class WindowSurface
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowSurface"/> class, which has been given no value.
    /// </summary>
    internal WindowSurface() { }

    /// <summary>
    /// Gets the key of the window's surface (design §2.13): a window provides its own at its root. The key has no default, so a
    /// lookup outside a window throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static Context<WindowSurface> Current { get; } = new();

    /// <summary>
    /// Gets the color set last with <see cref="SetBackdrop"/>; <see langword="null"/> before the first.
    /// </summary>
    internal Color? Backdrop { get; private set; }

    /// <summary>
    /// Gets the value set last with <see cref="SetDarkTitleBar"/>; <see langword="null"/> before the first.
    /// </summary>
    internal bool? DarkTitleBar { get; private set; }

    /// <summary>
    /// Tells the host the color to paint behind the tree: the background of the theme's root box.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    public void SetBackdrop(Color color)
    {
        UiThread.VerifyAccess();
        Backdrop = color;
    }

    /// <summary>
    /// Tells the host whether the window's title bar is dark.
    /// </summary>
    /// <param name="dark"><see langword="true"/> for a dark title bar; <see langword="false"/> for a light one.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    public void SetDarkTitleBar(bool dark)
    {
        UiThread.VerifyAccess();
        DarkTitleBar = dark;
    }
}
