namespace XylemUI;

/// <summary>
/// What a window's root wrapper tells the host about the window itself (design §13.2, "根の箱" and "題名の棒"): the color the host
/// paints behind the tree, and whether the title bar is dark. Each window has one, which it provides at its root under
/// <see cref="Current"/>; a part of the tree that provides another theme does not reach it.
/// </summary>
/// <remarks>
/// The window core paints a value that differs from the last one: the color behind the tree from the next frame on, and the title
/// bar through the window tier (<see cref="IPlatformWindow.SetDarkTitleBar"/>). The headless app also shows the values it was given
/// (<c>HeadlessApp.Backdrop</c> and <c>HeadlessApp.DarkTitleBar</c>).
/// </remarks>
public sealed class WindowSurface
{
    private Action<Color>? _paintBackdrop;
    private Action<bool>? _paintTitleBar;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowSurface"/> class, which has been given no value and paints nothing.
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
        if (Backdrop != color)
        {
            Backdrop = color;
            _paintBackdrop?.Invoke(color);
        }
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
        if (DarkTitleBar != dark)
        {
            DarkTitleBar = dark;
            _paintTitleBar?.Invoke(dark);
        }
    }

    /// <summary>
    /// Sets what paints a value that differs from the last one: the window core binds its frames and its window when it connects,
    /// and takes them back when its root is disposed.
    /// </summary>
    /// <param name="paintBackdrop">What paints the color behind the tree; <see langword="null"/> for nothing.</param>
    /// <param name="paintTitleBar">What makes the title bar dark or light; <see langword="null"/> for nothing.</param>
    internal void Bind(Action<Color>? paintBackdrop, Action<bool>? paintTitleBar)
    {
        _paintBackdrop = paintBackdrop;
        _paintTitleBar = paintTitleBar;
    }
}
