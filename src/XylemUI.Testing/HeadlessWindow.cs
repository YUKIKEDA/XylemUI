using XylemUI.Text;

namespace XylemUI.Testing;

/// <summary>
/// The window tier of a headless app (design §6.0, "Headless"): the headless parts gathered into the window the window core
/// connects to, as Avalonia and GPUI give their tests half a window. It shows and is active from the start, and draws text with the
/// defaults; the app changes the first two through the core's frames and input (<see cref="HeadlessApp.SetVisible"/>,
/// <see cref="HeadlessApp.SetActive"/>), as an OS's notices would. The loop stays headless: no <see cref="IPlatformApp"/>.
/// </summary>
/// <param name="target">What the frames draw on.</param>
/// <param name="scheduler">The frame scheduler that <see cref="HeadlessApp.PumpAndSettle"/> runs.</param>
/// <param name="pointer">The capture, the cursor, and the drags handed to the OS.</param>
/// <param name="ime">The OS's input method.</param>
/// <param name="options">The options whose size and scale the window has.</param>
internal sealed class HeadlessWindow(
    HeadlessTarget target,
    HeadlessScheduler scheduler,
    HeadlessPlatform pointer,
    HeadlessIme ime,
    HeadlessOptions options
) : IPlatformWindow
{
    /// <inheritdoc/>
    public (int Width, int Height) ClientSize =>
        ((int)MathF.Ceiling(options.Width * options.Scale), (int)MathF.Ceiling(options.Height * options.Scale));

    /// <inheritdoc/>
    public float ScaleFactor => options.Scale;

    /// <inheritdoc/>
    public bool IsShowing => true;

    /// <inheritdoc/>
    public bool IsActive => true;

    /// <inheritdoc/>
    public TextRenderingParameters TextRendering => TextRenderingParameters.Default;

    /// <inheritdoc/>
    public IRenderTarget Target => target;

    /// <inheritdoc/>
    public IFrameScheduler FrameScheduler => scheduler;

    /// <inheritdoc/>
    public IPointerPlatform Pointer => pointer;

    /// <inheritdoc/>
    public ITextInputPlatform? TextInput => ime;

    /// <inheritdoc/>
    /// <remarks>
    /// No OS sends notices: the tests call the core's input and frames through the app.
    /// </remarks>
    public void Attach() { }

    /// <inheritdoc/>
    /// <remarks>
    /// The app makes its accessibility tree when a test first asks for it (<see cref="HeadlessApp.FindByRole"/>).
    /// </remarks>
    public void RootMounted() { }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing: a headless window closes when the app is disposed.
    /// </remarks>
    public void RequestClose() { }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing: the app owns the renderer behind the target, and disposes it after the core.
    /// </remarks>
    public void Dispose() { }
}
