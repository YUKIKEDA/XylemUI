using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The half of a window that does not depend on the OS (design §6.0, "The window core"): the tree with a root node that takes the
/// window's size, the frames and the input, the locale provided around the root function, the root scope, and the first frame. A
/// desktop window and the headless app each hold one, and give it what they draw on, their frame scheduler, and their pointer
/// platform.
/// </summary>
/// <remarks>
/// It belongs to the UI thread. A host makes it, then calls <see cref="Connect"/> once, then <see cref="Mount"/> once; the order
/// in which the host makes its own parts around these calls is its own (design §6.0, "The order of creation").
/// </remarks>
internal sealed class WindowCore
{
    private readonly IReadOnlyList<Locale>? _supportedLocales;
    private readonly Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? _resolveLocale;
    private IFrameScheduler _scheduler = null!;
    private WindowLocale? _locale;
    private ScopeHandle _rootScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowCore"/> class: makes the tree and its root node.
    /// </summary>
    /// <param name="text">The text tools of the UI thread; <see langword="null"/> for a tree that holds no text.</param>
    /// <param name="supportedLocales">The locales the app supports; <see langword="null"/> for none.</param>
    /// <param name="resolveLocale">What resolves the app's locale; <see langword="null"/> for <see cref="Localization.Resolve"/>.</param>
    public WindowCore(
        TextContext? text,
        IReadOnlyList<Locale>? supportedLocales,
        Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? resolveLocale
    )
    {
        _supportedLocales = supportedLocales;
        _resolveLocale = resolveLocale;
        Tree = new UiTree(text);
        RootNode = Tree.CreateNode(new LayoutStyle());
    }

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
    /// Makes the window's frames and input. The frames ask <paramref name="scheduler"/> for a frame whenever the tree queues work.
    /// </summary>
    /// <param name="target">What the frames draw on.</param>
    /// <param name="atlas">The glyph atlas the target draws glyphs from; the host disposes it.</param>
    /// <param name="scheduler">The frame scheduler of the window.</param>
    /// <param name="pointer">The pointer platform of the window: the capture, the cursor, and the drags handed to the OS.</param>
    /// <param name="background">The color behind the tree.</param>
    /// <param name="textRendering">How the window draws text; the OS's defaults when not given.</param>
    /// <param name="time">The clock a frame reads its time from; the system's when not given.</param>
    /// <exception cref="ArgumentException"><paramref name="textRendering"/> is bilevel with ClearType coverage.</exception>
    public void Connect(
        IRenderTarget target,
        GlyphAtlas atlas,
        IFrameScheduler scheduler,
        IPointerPlatform pointer,
        Color background,
        TextRenderingParameters? textRendering = null,
        TimeProvider? time = null
    )
    {
        Frames = new WindowFrames(Tree, RootNode, target, atlas, scheduler, background, textRendering, time);
        Input = new WindowInput(Tree, Frames, pointer);
        _scheduler = scheduler;
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

        _scheduler.CancelFrame();
        Frames.Resize(size, scale);
    }

    /// <summary>
    /// Resolves the app's locale again against other languages of the OS (design §4.11, "A change of the OS"). It is called after
    /// <see cref="Mount"/>.
    /// </summary>
    /// <param name="tags">The tags of the OS's languages, most preferred first.</param>
    public void SetSystemLocales(IReadOnlyList<string> tags) => _locale!.Update(tags);

    /// <summary>
    /// Stops the tree from asking for frames, disposes the root scope, and lets go of the images the tree's cache kept. It may be
    /// called more than once, and before <see cref="Mount"/>. It does not cancel a frame already asked for: the host does that
    /// where it needs to.
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
}
