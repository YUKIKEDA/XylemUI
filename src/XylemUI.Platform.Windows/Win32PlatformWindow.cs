using XylemUI.Rendering.D3D11;
using XylemUI.Text;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The window tier of the Windows host (design §6.0): a <see cref="Win32Window"/>, its UIA provider (<see cref="Win32Accessibility"/>),
/// the <see cref="D3D11Renderer"/> that draws into it, whose swap chain is the loop's frame clock, and the OS's input method
/// (<see cref="TsfTextInput"/>). The window core sees the renderer as its target and the loop's scheduler as its scheduler, and
/// nothing of the clock.
/// </summary>
/// <remarks>
/// It belongs to the UI thread. It makes its parts in the order the design gives (window, provider, show, renderer, clock, TSF), and
/// disposes them in the order the desktop window had: TSF lets go of the window before it is destroyed, the provider goes before
/// the window, and the loop stops waiting on the clock before the renderer closes it. Until the OS's settings become one object
/// of the app tier (row PL1, PR 5), the window passes its own settings to the core in <see cref="Attach"/> (Issue #928).
/// </remarks>
internal sealed class Win32PlatformWindow : IPlatformWindow
{
    private readonly WindowCore _core;
    private readonly Action? _onDisposed;
    private TsfTextInput? _textInput;
    private Win32Accessibility? _accessibility;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32PlatformWindow"/> class: makes the window, its UIA provider, shows it when
    /// asked to, then makes the renderer at the client size, sets the swap chain as the loop's frame clock, and makes the input
    /// method. What was made is disposed when a later step throws.
    /// </summary>
    /// <remarks>
    /// A window that is shown is shown before the renderer is made, as the startup scene of <c>XylemUI.E2E</c> (the baseline of row
    /// M1a), Avalonia, and GPUI show theirs. Shown after its first present, as Flutter's Windows runner does to show no empty
    /// window, the counter's first frame reached the display 40 to 60 ms later; the renderer, whose shaders compile at run time until
    /// row C0, takes most of the time before it (<c>docs/benchmarks/v1d-components.md</c>). The UIA provider answers the window's
    /// <c>WM_GETOBJECT</c> from before the window shows, and the first answer makes the tree's accessibility tree (design §4.4.1).
    /// </remarks>
    /// <param name="core">The window core, whose tree the provider reads.</param>
    /// <param name="title">The title bar's text.</param>
    /// <param name="width">The width of the client area, in logical pixels; rounded up.</param>
    /// <param name="height">The height of the client area, in logical pixels; rounded up.</param>
    /// <param name="show"><see langword="true"/> to show the window; the tests keep it hidden.</param>
    /// <param name="useWarp"><see langword="true"/> to draw with the WARP software rasterizer, for the tests.</param>
    /// <param name="onDisposed">Called once when the window is disposed; <see langword="null"/> for nothing.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread, or the renderer could not be
    /// made.</exception>
    [UiThreadOnly]
    public Win32PlatformWindow(WindowCore core, string title, float width, float height, bool show, bool useWarp, Action? onDisposed = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(title);
        _core = core;
        _onDisposed = onDisposed;
        try
        {
            Window = new Win32Window(title, (int)MathF.Ceiling(width), (int)MathF.Ceiling(height));
            _accessibility = new Win32Accessibility(Window, core.Tree);
            Window.Accessibility = _accessibility;
            if (show)
            {
                Window.Show();
            }

            (int pixelWidth, int pixelHeight) = Window.ClientSize;
            Renderer = D3D11Renderer.Create(Window.Handle, pixelWidth, pixelHeight, useWarp);
            Renderer.TextRendering = Window.TextRendering;

            // The frames are paced by the swap chain, and each starts late (design §2.11, "Frames that start late").
            Win32MessageLoop.SetFrameClock(Renderer.FrameLatencyWaitableObject, Renderer.TryReadLastRefresh, Renderer.RefreshRate);

            // The OS's input method through TSF (design §4.7); without it, the fields take WM_CHAR's committed text (§4.9).
            _textInput = TsfTextInput.TryCreate(Window);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the Win32 window.
    /// </summary>
    public Win32Window Window { get; } = null!;

    /// <summary>
    /// Gets the renderer.
    /// </summary>
    public D3D11Renderer Renderer { get; } = null!;

    /// <summary>
    /// Gets the tags of the OS's languages as the window read them, for the first mount until the OS's settings become one object
    /// of the app tier (row PL1, PR 5).
    /// </summary>
    public IReadOnlyList<string> UiLanguages => Window.UiLanguages;

    /// <inheritdoc/>
    public (int Width, int Height) ClientSize => Window.ClientSize;

    /// <inheritdoc/>
    public float ScaleFactor => Window.ScaleFactor;

    /// <inheritdoc/>
    public bool IsShowing => Window.IsShowing;

    /// <inheritdoc/>
    public bool IsActive => Window.IsActive;

    /// <inheritdoc/>
    public TextRenderingParameters TextRendering => Window.TextRendering;

    /// <inheritdoc/>
    public IRenderTarget Target => Renderer;

    /// <inheritdoc/>
    public IFrameScheduler FrameScheduler => Win32MessageLoop.FrameScheduler;

    /// <inheritdoc/>
    public IPointerPlatform Pointer => Window;

    /// <inheritdoc/>
    public ITextInputPlatform? TextInput => _textInput;

    /// <inheritdoc/>
    public void Attach()
    {
        WindowInput input = _core.Input;
        WindowFrames frames = _core.Frames;
        UiTree tree = _core.Tree;
        _accessibility!.Input = input;
        Window.PointerInputReceived = input.Handle;
        Window.KeyInputReceived = input.Handle;
        Window.TextInputReceived = input.Handle;
        Window.ActivationChanged = input.SetActive;

        // No frames while the window is hidden or minimized (design §2.11); the core gave the frames the value now.
        Window.VisibilityChanged = frames.SetVisible;

        // Drags from the OS come in as drags of the app (design §7, "Drag and drop").
        Window.ExternalDrops = input;

        // A touchpad's interaction begins: the frames poll its viewport while it goes on (design §7, "Touch and pen", I1c).
        Window.GestureFrameNeeded = frames.RequestFrame;
        Window.SynchronousRender = Render;
        Window.TextRenderingChanged += OnTextRenderingChanged;

        // The window's settings until PR 5 of row PL1: text fields draw their caret and selection with Windows' settings (design
        // §4.9), and less motion when Windows asks for it (design §2.14, "Reduced motion").
        tree.SetTextEditing(Window.TextEditing);
        Window.TextEditingChanged += OnTextEditingChanged;
        tree.Animations.ReduceMotion = Window.ReducesMotion;
        Window.ReducesMotionChanged += OnReducesMotionChanged;
        Window.UiLanguagesChanged += OnUiLanguagesChanged;
        Window.Closed += OnClosed;
    }

    /// <inheritdoc/>
    public void RootMounted() => _accessibility?.StartIfAsked();

    /// <inheritdoc/>
    public void RequestClose() => Window.RequestClose();

    /// <summary>
    /// Takes the notices back from the core, lets TSF go of the window, takes the provider out, destroys the window, sets the loop's
    /// frame clock back to its timer, and releases the renderer. The callback given to the constructor runs also when a step
    /// throws. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The callback runs whatever a step throws: the app counts the window as open until it runs, and would refuse every later
        // window (Win32PlatformApp.OpenWindow).
        try
        {
            DisposeParts();
        }
        finally
        {
            _onDisposed?.Invoke();
        }
    }

    // The order of the design (§6.0, "What the host owns"), as the desktop window before row PL1 disposed its parts.
    private void DisposeParts()
    {
        if (Window is not null)
        {
            Window.Closed -= OnClosed;
            Window.TextRenderingChanged -= OnTextRenderingChanged;
            Window.ReducesMotionChanged -= OnReducesMotionChanged;
            Window.UiLanguagesChanged -= OnUiLanguagesChanged;
            Window.TextEditingChanged -= OnTextEditingChanged;
            Window.SynchronousRender = null;
            Window.PointerInputReceived = null;
            Window.KeyInputReceived = null;
            Window.ExternalDrops = null;
            Window.TextInputReceived = null;
            Window.ActivationChanged = null;
            Window.VisibilityChanged = null;
            Window.GestureFrameNeeded = null;

            // TSF lets go of the window before it is destroyed: an AssociateFocus on a destroyed window fails (spike #510).
            _core.Tree.SetTextInput(null);
            _textInput?.Dispose();
            _textInput = null;

            // The provider goes first: a WM_GETOBJECT while the window is destroyed gets Windows' default.
            Window.Accessibility = null;
            _accessibility?.Dispose();
            _accessibility = null;
            Window.Dispose();
        }

        // The frame clock is the swap chain's object, which the renderer closes: the loop stops waiting on it first.
        if (Renderer is not null)
        {
            Win32MessageLoop.SetFrameClock(0);
            Renderer.Dispose();
        }
    }

    // The synchronous resize path (design §2.11.1): the swap chain takes the new size, then the core runs the frame at once at the
    // size in logical pixels. The division is the host's: the core does not read the window's scale.
    private void Render(Win32Window window, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Renderer.Resize(width, height);
        float scale = window.ScaleFactor;
        _core.Resize(new Size(width / scale, height / scale), scale);
    }

    private void OnTextRenderingChanged(object? sender, EventArgs e)
    {
        Renderer.TextRendering = Window.TextRendering;
        _core.TextRenderingChanged(Window.TextRendering);
    }

    private void OnTextEditingChanged(object? sender, EventArgs e) => _core.Tree.SetTextEditing(Window.TextEditing);

    private void OnReducesMotionChanged(object? sender, EventArgs e) => _core.Tree.Animations.ReduceMotion = Window.ReducesMotion;

    private void OnUiLanguagesChanged(object? sender, EventArgs e) => _core.SetSystemLocales(Window.UiLanguages);

    // The window is gone: no frame may draw into its swap chain, and what the root function made goes with it.
    private void OnClosed(object? sender, EventArgs e) => _core.WindowClosed();
}
