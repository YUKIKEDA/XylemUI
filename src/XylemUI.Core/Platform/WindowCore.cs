using System.Runtime.ExceptionServices;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The half of a window that does not depend on the OS (design §6.0, "The window core"): the tree with a root node that takes the
/// window's size, the frames and the input, the locale keys, the OS's appearance, and the window's surface provided around the root
/// function and its wrapper, the root scope, the first frame, the OS's settings given to the tree, and what closing the window
/// does. A desktop window and the headless app each hold one, and give it their window tier (<see cref="IPlatformWindow"/>)
/// and the app tier's settings (<see cref="IPlatformSettings"/>).
/// </summary>
/// <remarks>
/// It belongs to the UI thread. A host makes it, then calls <see cref="Connect"/> once, then <see cref="Mount"/> once; the order
/// in which the host makes its own parts around these calls is its own (design §6.0, "The order of creation"). From
/// <see cref="Connect"/> on it owns the window and the atlas, and disposes them with itself. It follows the settings' changes from
/// <see cref="Connect"/> until the root is disposed; the settings are the app's, and outlive it.
/// </remarks>
internal sealed class WindowCore : IDisposable
{
    private readonly IPlatformSettings _settings;
    private readonly Action<PlatformSettingsChanges> _onSettingsChanged;
    private readonly IReadOnlyList<LocaleData>? _supportedLocales;
    private readonly Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? _resolveLocale;
    private IPlatformWindow? _window;
    private GlyphAtlas? _atlas;
    private IFrameScheduler? _scheduler;
    private WindowLocale? _locale;
    private WindowAppearance? _appearance;
    private ScopeHandle _rootScope;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowCore"/> class: makes the tree and its root node.
    /// </summary>
    /// <param name="text">The text tools of the UI thread; <see langword="null"/> for a tree that holds no text.</param>
    /// <param name="settings">The OS's settings of the app tier.</param>
    /// <param name="clipboard">The clipboard the text fields copy to and paste from; <see langword="null"/> for none.</param>
    /// <param name="supportedLocales">The tables of the locales the app supports; <see langword="null"/> for none.</param>
    /// <param name="resolveLocale">What resolves the app's locale; <see langword="null"/> for <see cref="Localization.Resolve"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
    public WindowCore(
        TextContext? text,
        IPlatformSettings settings,
        IClipboardBackend? clipboard,
        IReadOnlyList<LocaleData>? supportedLocales,
        Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? resolveLocale
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        // Made once: the core adds it to the settings' event in Connect and takes it out when the root is disposed.
        _onSettingsChanged = OnSettingsChanged;
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
    /// Gets the window's surface, which the core provides at the root under <see cref="WindowSurface.Current"/>: what the root
    /// wrapper set last of the color behind the tree and of the title bar. From <see cref="Connect"/> until the root is disposed, a
    /// value that differs from the last one is painted: the color as the frames' <see cref="WindowFrames.ClearColor"/>, and the
    /// title bar through <see cref="IPlatformWindow.SetDarkTitleBar"/> (design §13.2, "根の箱" and "題名の棒").
    /// </summary>
    public WindowSurface Surface { get; } = new();

    /// <summary>
    /// Makes the window's frames and input from the window's target, scheduler, and pointer platform, and gives them the window's
    /// values now: whether it shows, whether it is active, and how its text is drawn; then gives the tree the window's input method
    /// and the OS's settings now, less motion and the text fields' settings, follows the settings' changes from here on, and paints
    /// what the window's surface is told from here on. The frames ask the window's scheduler for a frame whenever the tree queues
    /// work. The core owns the window and the atlas from this call on, also when it throws.
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

        // The settings now and their changes (design §6.0, "The order of creation", step 6). The languages and the 24-hour form
        // reach the locale, which Mount makes from the values at that time.
        Tree.Animations.ReduceMotion = _settings.ReducesMotion;
        Tree.SetTextEditing(_settings.TextEditing);
        _settings.Changed += _onSettingsChanged;

        // The root wrapper tells the surface its values from Mount on (design §13.2, "根の箱" and "題名の棒").
        Surface.Bind(Frames.SetClearColor, window.SetDarkTitleBar);
    }

    /// <summary>
    /// Runs the root function once, in a new scope that becomes the root scope, with the tree as the current tree and, around it,
    /// the resolved locale, its tables, the OS's 24-hour form (design §4.11, "Distribution"), the OS's appearance, and the
    /// window's <see cref="Surface"/> provided, and inside them the root wrapper when there is one (design §13.2, "根で置くもの");
    /// appends the node it returned to the root node; then runs the first frame at the window's size. It is called after
    /// <see cref="Connect"/>. The locale is resolved against the OS's languages of the settings at this time, and the 24-hour
    /// form is the settings' at this time.
    /// </summary>
    /// <remarks>
    /// The frame the root function's nodes asked for is the first one, which the size runs at once: without the cancel, an empty
    /// frame ran after it (found by the headless app's check at its end, row P02a). A frame that one asks for stays.
    /// </remarks>
    /// <param name="root">The root function.</param>
    /// <param name="wrapper">
    /// The root wrapper, which is given a function that runs the root function, calls it exactly once, and returns the window's
    /// node; <see langword="null"/> to run the root function alone. The desktop host passes <see cref="WindowRoot.ReadWrapper"/>,
    /// and the headless app its options' wrapper.
    /// </param>
    /// <param name="appearance">The OS's appearance now.</param>
    /// <param name="scopeName">The owner the root scope is tracked under (<see cref="ScopeTracker"/>).</param>
    /// <param name="size">The window's client size, in logical pixels.</param>
    /// <param name="scale">The window's scale, from logical to physical pixels.</param>
    /// <exception cref="ArgumentException">Only one of <see cref="SystemAppearance.HighContrast"/> and
    /// <see cref="SystemAppearance.Contrast"/> of <paramref name="appearance"/> says high contrast is on.</exception>
    /// <exception cref="InvalidOperationException">The root function, or the wrapper, returned no node of the window's tree, or one
    /// that has a parent; or the wrapper called the root function a second time (the second call throws, and the root function
    /// does not run again), or returned without calling it (design §13.2, "根で置くもの").</exception>
    public void Mount(
        Func<Element> root,
        Func<Func<Element>, Element>? wrapper,
        SystemAppearance appearance,
        string scopeName,
        Size size,
        float scale
    )
    {
        var windowAppearance = new WindowAppearance(WindowAppearance.Check(appearance, nameof(appearance)));
        var locale = new WindowLocale(_supportedLocales, _resolveLocale, _settings.UiLanguages, _settings.Use24HourFormat);
        _locale = locale;
        _appearance = windowAppearance;
        Func<Element> body = wrapper is null ? root : () => Wrap(wrapper, root);
        UiTree? previous = UiTree.Current;
        UiTree.Current = Tree;
        try
        {
            _rootScope = Scope.Create(
                () =>
                {
                    Element content = locale.Provide(Tree, () => windowAppearance.Provide(() => WindowSurface.Current.Provide(Surface, body)));
                    if (!ReferenceEquals(content.Tree, Tree))
                    {
                        throw new InvalidOperationException("The root function, or the wrapper, returns a node of the window's tree.");
                    }

                    if (!Tree.Nodes.GetRef(content.Node).Parent.IsNone)
                    {
                        throw new InvalidOperationException("The root function, or the wrapper, returns a node with no parent.");
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
    /// Takes another appearance of the OS (design §13.2, "置き方"): the readers of <see cref="SystemAppearance.Current"/> run again
    /// when the value changed, and only then. Before <see cref="Mount"/> it does nothing: <see cref="Mount"/> takes the appearance of
    /// that time.
    /// </summary>
    /// <param name="value">The OS's appearance now.</param>
    /// <exception cref="ArgumentException">Only one of <see cref="SystemAppearance.HighContrast"/> and
    /// <see cref="SystemAppearance.Contrast"/> says high contrast is on.</exception>
    public void SetSystemAppearance(SystemAppearance value)
    {
        // Checked before Mount too, where nothing else reads it: `_appearance?.Update(...)` would not evaluate its argument.
        SystemAppearance checkedValue = WindowAppearance.Check(value, nameof(value));
        _appearance?.Update(checkedValue);
    }

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
    /// Stops the tree from asking for frames, from following the OS's settings, and from painting what the window's surface is
    /// told, disposes the root scope, and lets go of the images the tree's cache kept. It may be called more than once, and before
    /// <see cref="Connect"/>. It does not cancel a frame already asked for, so that the headless app can run the frames left after
    /// it (<c>AssertNoLeakedScopes</c>); <see cref="WindowClosed"/> and <see cref="Dispose"/> cancel it.
    /// </summary>
    public void DisposeRoot()
    {
        Tree.FrameNeeded = null;

        // The settings are the app's and outlive the window: a change after it closed reaches no tree of it.
        _settings.Changed -= _onSettingsChanged;

        // An app may keep the surface past its window: what it is told then reaches no window that closed.
        Surface.Bind(null, null);
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

    // Runs the wrapper with a function that runs the root function at most once (Issue #907): a second call throws before the root
    // function makes its nodes again, which would stay in the tree with no parent, and a wrapper that never called it is refused.
    private static Element Wrap(Func<Func<Element>, Element> wrapper, Func<Element> root)
    {
        bool called = false;
        Element content = wrapper(() =>
        {
            if (called)
            {
                ThrowHelper.ThrowRootCalledTwice();
            }

            called = true;
            return root();
        });

        if (!called)
        {
            ThrowHelper.ThrowRootNotCalled();
        }

        return content;
    }

    // No frame of the window's scheduler runs for a root that is gone. The frame is cancelled before the root scope is disposed;
    // DisposeRoot's first step, which stops the tree from asking for frames, only clears a delegate, so it may come after.
    private void Close()
    {
        _scheduler?.CancelFrame();
        DisposeRoot();
    }

    // A reading of the OS's settings changed the values it marks (design §6.0, "OS settings"): each goes where the tree reads it, and
    // the values it does not mark stay. Before Mount there is no locale or appearance yet: Mount takes the languages and the 24-hour
    // form then, and the appearance its caller gives.
    // Every marked value is given, also after one throws (an effect the locale runs, say); the exceptions are thrown together at
    // the end, as SynchronousRenderGuard runs its deferred callbacks.
    private void OnSettingsChanged(PlatformSettingsChanges changes)
    {
        List<Exception>? errors = null;
        if ((changes & PlatformSettingsChanges.ReducesMotion) != 0)
        {
            try
            {
                Tree.Animations.ReduceMotion = _settings.ReducesMotion;
            }
            catch (Exception motionError)
            {
                (errors ??= []).Add(motionError);
            }
        }

        if ((changes & PlatformSettingsChanges.TextEditing) != 0)
        {
            try
            {
                Tree.SetTextEditing(_settings.TextEditing);
            }
            catch (Exception textEditingError)
            {
                (errors ??= []).Add(textEditingError);
            }
        }

        if ((changes & PlatformSettingsChanges.UiLanguages) != 0)
        {
            try
            {
                _locale?.Update(_settings.UiLanguages);
            }
            catch (Exception languagesError)
            {
                (errors ??= []).Add(languagesError);
            }
        }

        if ((changes & PlatformSettingsChanges.Use24HourFormat) != 0)
        {
            try
            {
                _locale?.SetUse24HourFormat(_settings.Use24HourFormat);
            }
            catch (Exception formError)
            {
                (errors ??= []).Add(formError);
            }
        }

        if ((changes & PlatformSettingsChanges.Appearance) != 0)
        {
            try
            {
                SetSystemAppearance(_settings.Appearance);
            }
            catch (Exception appearanceError)
            {
                (errors ??= []).Add(appearanceError);
            }
        }

        if (errors is not null)
        {
            ThrowSettingsErrors(errors);
        }
    }

    private static void ThrowSettingsErrors(List<Exception> errors)
    {
        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Throw(errors[0]);
        }

        throw new AggregateException("Giving the tree the changed OS settings threw.", errors);
    }
}
