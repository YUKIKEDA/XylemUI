using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// One window of an app (design §2.13, "The window"): the Win32 window, its D3D11 renderer, its frames and input, and its tree with
/// a root box that takes the window's size. The root function runs once in the window's root scope, with the tree as the current
/// tree, and its element goes into the root box. Closing the window disposes the root scope.
/// </summary>
/// <remarks>
/// It belongs to the UI thread. The frames are paced by the swap chain (<see cref="Win32MessageLoop.SetFrameClock"/>).
/// </remarks>
internal sealed class DesktopWindow : IDisposable
{
    // The color behind the tree. The text that sets no color is black (WindowFrames), so the window is white, as Avalonia's light
    // theme draws a window.
    private static readonly Color Background = Color.FromRgb(0xFFFFFF);

    private readonly DirectWriteFontProvider _fonts;
    private readonly TextContext _text;
    private readonly GlyphAtlas? _atlas;
    private readonly NodeHandle _rootNode;
    private ScopeHandle _rootScope;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopWindow"/> class: makes the window, shown or hidden, runs the root function,
    /// and lays the tree out and draws it once at the window's size.
    /// </summary>
    /// <remarks>
    /// A window that is shown is shown before the renderer is made, as the startup scene of <c>XylemUI.E2E</c> (the baseline of row
    /// M1a), Avalonia, and GPUI show theirs. Shown after its first present, as Flutter's Windows runner does to show no empty
    /// window, the counter's first frame reached the display 40 to 60 ms later; the renderer, whose shaders compile at run time until
    /// row C0, takes most of the time before it (<c>docs/benchmarks/v1d-components.md</c>).
    /// </remarks>
    /// <param name="root">The root function.</param>
    /// <param name="options">The window.</param>
    /// <param name="show"><see langword="true"/> to show the window; the tests keep it hidden.</param>
    /// <param name="useWarp"><see langword="true"/> to draw with the WARP software rasterizer, for the tests.</param>
    /// <exception cref="ArgumentException">The options' size is not a positive number.</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread, or the root function returned no node of
    /// the window's tree or one that has a parent.</exception>
    public DesktopWindow(Func<Element> root, WindowOptions options, bool show = false, bool useWarp = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Title);
        if (!(options.Width > 0 && options.Height > 0 && float.IsFinite(options.Width) && float.IsFinite(options.Height)))
        {
            throw new ArgumentException("The window's width and height are finite numbers above 0.", nameof(options));
        }

        UiThread.VerifyAccess();
        _fonts = new DirectWriteFontProvider();
        _text = new TextContext(_fonts);
        Tree = new UiTree(_text) { UiLanguage = TextLanguage.FromTag(Win32UiLanguage.Read()) };
        _rootNode = Tree.CreateNode(new LayoutStyle());

        // What was made is disposed when a later step throws, so that no window, swap chain, or scope outlives the constructor.
        try
        {
            Window = new Win32Window(options.Title, (int)MathF.Ceiling(options.Width), (int)MathF.Ceiling(options.Height));
            if (show)
            {
                Window.Show();
            }

            (int width, int height) = Window.ClientSize;
            Renderer = D3D11Renderer.Create(Window.Handle, width, height, useWarp);
            Renderer.TextRendering = Window.TextRendering;
            _atlas = new GlyphAtlas(static () => new DirectWriteGlyphRasterizer());
            Frames = new WindowFrames(Tree, _rootNode, Renderer, _atlas, Win32MessageLoop.FrameScheduler, Background, Window.TextRendering);
            Input = new WindowInput(Tree, Frames, Window);
            Window.PointerInputReceived = Input.Handle;
            Window.KeyInputReceived = Input.Handle;
            Window.ActivationChanged = Input.SetActive;

            // A touchpad's interaction begins: the frames poll its viewport while it goes on (design §7, "Touch and pen", I1c).
            Window.GestureFrameNeeded = Frames.RequestFrame;

            // A window shown above was activated before the input listened.
            Input.SetActive(Window.IsActive);
            Window.SynchronousRender = Render;
            Window.TextRenderingChanged += OnTextRenderingChanged;
            Window.Closed += OnClosed;
            Win32MessageLoop.SetFrameClock(Renderer.FrameLatencyWaitableObject);
            Mount(root);

            // The frame the root function's nodes asked for is the first one, which the size runs at once: without this, an empty
            // frame ran after it (found by the headless app's check at its end, row P02a). A frame that one asks for stays.
            Win32MessageLoop.FrameScheduler.CancelFrame();
            float scale = Window.ScaleFactor;
            Frames.Resize(new Size(width / scale, height / scale), scale);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Occurs on the UI thread after the window closed and its root scope was disposed.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Gets the window's tree.
    /// </summary>
    public UiTree Tree { get; }

    /// <summary>
    /// Gets the Win32 window.
    /// </summary>
    public Win32Window Window { get; } = null!;

    /// <summary>
    /// Gets the renderer.
    /// </summary>
    public D3D11Renderer Renderer { get; } = null!;

    /// <summary>
    /// Gets the window's frames.
    /// </summary>
    public WindowFrames Frames { get; } = null!;

    /// <summary>
    /// Gets the window's input.
    /// </summary>
    public WindowInput Input { get; } = null!;

    /// <summary>
    /// Gets the root scope, which owns what the root function made; disposed when the window closes.
    /// </summary>
    public ScopeHandle RootScope => _rootScope;

    /// <summary>
    /// Disposes the root scope, destroys the window, and releases the renderer and the text tools.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeRoot();
        if (Window is not null)
        {
            Window.Closed -= OnClosed;
            Window.TextRenderingChanged -= OnTextRenderingChanged;
            Window.SynchronousRender = null;
            Window.PointerInputReceived = null;
            Window.KeyInputReceived = null;
            Window.ActivationChanged = null;
            Window.Dispose();
        }

        // The frame clock is the swap chain's object, which the renderer closes: the loop stops waiting on it first.
        if (Renderer is not null)
        {
            Win32MessageLoop.SetFrameClock(0);
            Renderer.Dispose();
        }

        _atlas?.Dispose();
        _text.Dispose();
        _fonts.Dispose();
    }

    // The root function, with the tree as the current tree, in a new scope that becomes the root scope.
    private void Mount(Func<Element> root)
    {
        UiTree? previous = UiTree.Current;
        UiTree.Current = Tree;
        try
        {
            _rootScope = Scope.Create(
                () =>
                {
                    Element content = root();
                    if (content.IsNone || !ReferenceEquals(content.Tree, Tree))
                    {
                        throw new InvalidOperationException("The root function returns a node of the window's tree.");
                    }

                    if (!Tree.Nodes.GetRef(content.Node).Parent.IsNone)
                    {
                        throw new InvalidOperationException("The root function returns a node with no parent.");
                    }

                    Tree.AppendChild(_rootNode, content.Node);
                },
                "XylemApp.Run"
            );
        }
        finally
        {
            UiTree.Current = previous;
        }
    }

    // The synchronous resize path (design §2.11.1): the swap chain takes the new size, then the frame runs at once.
    private void Render(Win32Window window, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Renderer.Resize(width, height);
        float scale = window.ScaleFactor;
        Frames.Resize(new Size(width / scale, height / scale), scale);
    }

    private void OnTextRenderingChanged(object? sender, EventArgs e)
    {
        Renderer.TextRendering = Window.TextRendering;
        Frames.TextRendering = Window.TextRendering;
    }

    // The window is gone: no frame may draw into its swap chain, and what the root function made goes with it.
    private void OnClosed(object? sender, EventArgs e)
    {
        DisposeRoot();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeRoot()
    {
        Tree.FrameNeeded = null;
        Win32MessageLoop.FrameScheduler.CancelFrame();
        if (!_rootScope.IsNone && Scope.IsAlive(_rootScope))
        {
            Scope.Dispose(_rootScope);
        }

        _rootScope = default;
    }
}
