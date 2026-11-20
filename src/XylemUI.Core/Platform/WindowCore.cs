using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The half of a window that does not depend on the OS (design §6.0, "The window core"): the tree with a root node that takes the
/// window's size, the frames and the input, the locale provided around the root function, the root scope, the first frame, and what
/// closing the window does. A desktop window and the headless app each hold one, and give it their window tier
/// (<see cref="IPlatformWindow"/>).
/// </summary>
/// <remarks>
/// It belongs to the UI thread. A host makes it, then calls <see cref="Connect"/> once, then <see cref="Mount"/> once; the order
/// in which the host makes its own parts around these calls is its own (design §6.0, "The order of creation"). From
/// <see cref="Connect"/> on it owns the window and the atlas, and disposes them with itself.
/// </remarks>
internal sealed class WindowCore : IDisposable
{
    private readonly IReadOnlyList<Locale>? _supportedLocales;
    private readonly Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? _resolveLocale;
    private IPlatformWindow? _window;
    private GlyphAtlas? _atlas;
    private IFrameScheduler? _scheduler;
    private WindowLocale? _locale;
    private ScopeHandle _rootScope;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowCore"/> class: makes the tree and its root node.
    /// </summary>
    /// <param name="text">The text tools of the UI thread; <see langword="null"/> for a tree that holds no text.</param>
    /// <param name="clipboard">The clipboard the text fields copy to and paste from; <see langword="null"/> for none.</param>
    /// <param name="supportedLocales">The locales the app supports; <see langword="null"/> for none.</param>
    /// <param name="resolveLocale">What resolves the app's locale; <see langword="null"/> for <see cref="Localization.Resolve"/>.</param>
    public WindowCore(
        TextContext? text,
        IClipboardBackend? clipboard,
        IReadOnlyList<Locale>? supportedLocales,
        Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? resolveLocale
    )
    {
        _supportedLocales = supportedLocales;
        _resolveLocale = resolveLocale;
        Tree = new UiTree(text) { Clipboard = clipboard };
        RootNode = Tree.CreateNode(new LayoutStyle());
    }

    /// <summary>
    /// Occurs on the UI thread after the window closed and the root scope was disposed.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Gets the window's tree.
    /// </summary>
    public UiTree Tree { get; }

    /// <summary>
    /// Gets the node the root function's node is appended to, which takes the window's size.
    /// </summary>
    public NodeHandle RootNode { get; }

    /// <summary>
    /// Gets the window's frames; made by <see cref="Connect"/>.
    /// </summary>
    public WindowFrames Frames { get; private set; } = null!;

    /// <summary>
    /// Gets the window's input; made by <see cref="Connect"/>.
    /// </summary>
    public WindowInput Input { get; private set; } = null!;

    /// <summary>
    /// Gets the root scope, which owns what the root function made; none before <see cref="Mount"/> and after
    /// <see cref="DisposeRoot"/>.
    /// </summary>
    public ScopeHandle RootScope => _rootScope;

    /// <summary>
    /// Makes the window's frames and input from the window's target, scheduler, and pointer platform, and gives them the window's
    /// values now: whether it shows, whether it is active, and how its text is drawn; then gives the tree the window's input method.
    /// The frames ask the window's scheduler for a frame whenever the tree queues work. The core owns the window and the atlas from
    /// this call on, also when it throws.
    /// </summary>
    /// <param name="window">The window tier.</param>
    /// <param name="atlas">The glyph atlas the target draws glyphs from.</param>
    /// <param name="background">The color behind the tree.</param>
    /// <param name="time">The clock a frame reads its time from; the system's when not given.</param>
    /// <exception cref="ArgumentException">The window's text rendering is bilevel with ClearType coverage.</exception>
    public void Connect(IPlatformWindow window, GlyphAtlas atlas, Color background, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(atlas);
        _window = window;
        _atlas = atlas;
        IFrameScheduler scheduler = window.FrameScheduler;
        Frames = new WindowFrames(Tree, RootNode, window.Target, atlas, scheduler, background, window.TextRendering, time);
        Input = new WindowInput(Tree, Frames, window.Pointer);
        _scheduler = scheduler;

        // The values now: a window shown, minimized, or activated before there were frames and input told nobody, and its later
        // notices tell only a change (design §2.11, "Frames of hidden and minimized windows").
        Frames.SetVisible(window.IsShowing);
        Input.SetActive(window.IsActive);
        Tree.SetTextInput(window.TextInput);
    }

    /// <summary>
    /// Runs the root function once, with the tree as the current tree and the resolved locale provided around it, in a new scope
    /// that becomes the root scope; appends its node to the root node; then runs the first frame at the window's size. It is
    /// called after <see cref="Connect"/>.
    /// </summary>
    /// <remarks>
    /// The frame the root function's nodes asked for is the first one, which the size runs at once: without the cancel, an empty
    /// frame ran after it (found by the headless app's check at its end, row P02a). A frame that one asks for stays.
    /// </remarks>
    /// <param name="root">The root function.</param>
    /// <param name="systemLocales">The tags of the OS's languages, most preferred first, which the app's locale is resolved against.</param>
    /// <param name="scopeName">The owner the root scope is tracked under (<see cref="ScopeTracker"/>).</param>
    /// <param name="size">The window's client size, in logical pixels.</param>
    /// <param name="scale">The window's scale, from logical to physical pixels.</param>
    /// <exception cref="InvalidOperationException">The root function returned no node of the window's tree, or one that has a
    /// parent.</exception>
    public void Mount(Func<Element> root, IReadOnlyList<string> systemLocales, string scopeName, Size size, float scale)
    {
        var locale = new WindowLocale(_supportedLocales, _resolveLocale, systemLocales);
        _locale = locale;
        UiTree? previous = UiTree.Current;
        UiTree.Current = Tree;
        try
        {
            _rootScope = Scope.Create(
                () =>
                {
                    Element content = locale.Provide(Tree, root);
                    if (!ReferenceEquals(content.Tree, Tree))
                    {
                        throw new InvalidOperationException("The root function returns a node of the window's tree.");
                    }

                    if (!Tree.Nodes.GetRef(content.Node).Parent.IsNone)
                    {
                        throw new InvalidOperationException("The root function returns a node with no parent.");
                    }

                    // One change for the readers of what an action can do: the node is in the tree and is where a route starts
                    // with no focus.
                    ReactiveRuntime.Batch(() =>
                    {
                        Tree.AppendChild(RootNode, content.Node);
                        Tree.KeyRoot = content.Node;
                    });
                },
                scopeName
            );
        }
        finally
        {
            UiTree.Current = previous;
        }

        _scheduler!.CancelFrame();
        Frames.Resize(size, scale);
    }

    /// <summary>
    /// Gives the tree the window's new size and runs a frame at once: the synchronous resize path (design §2.11.1). The host calls
    /// it from the OS's size notice after it resized its renderer, with the physical size divided by the scale.
    /// </summary>
    /// <param name="size">The client size, in logical pixels.</param>
    /// <param name="scale">The window's scale, from logical to physical pixels.</param>
    public void Resize(Size size, float scale) => Frames.Resize(size, scale);

    /// <summary>
    /// Tells the frames that the window's text is drawn otherwise (design §5.0.3): the host gave its renderer the value first, and
    /// the next frame draws the whole tree again with it.
    /// </summary>
    /// <param name="value">How the text of the window's monitor is drawn now.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is bilevel with ClearType coverage.</exception>
    public void TextRenderingChanged(TextRenderingParameters value) => Frames.TextRendering = value;

    /// <summary>
    /// Resolves the app's locale again against other languages of the OS (design §4.11, "A change of the OS"). Before
    /// <see cref="Mount"/> it does nothing: <see cref="Mount"/> takes the languages of the OS at that time, so a change told before it
    /// is not lost.
    /// </summary>
    /// <param name="tags">The tags of the OS's languages, most preferred first.</param>
    public void SetSystemLocales(IReadOnlyList<string> tags) => _locale?.Update(tags);

    /// <summary>
    /// Tells the core that the OS window closed: the frame asked for is cancelled, the root is disposed (<see cref="DisposeRoot"/>),
    /// and <see cref="Closed"/> is raised. The window and the atlas stay until <see cref="Dispose"/>.
    /// </summary>
    public void WindowClosed()
    {
        Close();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops the tree from asking for frames, disposes the root scope, and lets go of the images the tree's cache kept. It may be
    /// called more than once, and before <see cref="Mount"/>. It does not cancel a frame already asked for, so that the headless
    /// app can run the frames left after it (<c>AssertNoLeakedScopes</c>); <see cref="WindowClosed"/> and <see cref="Dispose"/>
    /// cancel it.
    /// </summary>
    public void DisposeRoot()
    {
        Tree.FrameNeeded = null;
        if (!_rootScope.IsNone && Scope.IsAlive(_rootScope))
        {
            Scope.Dispose(_rootScope);
        }

        _rootScope = default;

        // The tree is thrown away with its window: the images its cache kept for a later element go too (design §5.0.4, "Cache").
        Tree.CachedImages.ReleaseAll();
    }

    /// <summary>
    /// Cancels the frame asked for, disposes the root, then disposes the window and the atlas; it raises no <see cref="Closed"/>. A
    /// second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();

        // The window's renderer draws glyphs from the atlas: the window goes first, as the desktop window always disposed them.
        _window?.Dispose();
        _atlas?.Dispose();
    }

    // No frame of the window's scheduler runs for a root that is gone. The frame is cancelled before the root scope is disposed;
    // DisposeRoot's first step, which stops the tree from asking for frames, only clears a delegate, so it may come after.
    private void Close()
    {
        _scheduler?.CancelFrame();
        DisposeRoot();
    }
}
