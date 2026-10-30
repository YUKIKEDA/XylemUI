using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Text;
using XylemUI.Text;

namespace XylemUI.Testing;

/// <summary>
/// Runs an app's root function in a window that is not there (design §12): no OS window, no swap chain, and no GPU. The frames lay
/// the tree out and build its display list on a virtual clock, input goes into the same entry points the OS's input does, and the
/// tests find nodes by test ID or text.
/// </summary>
/// <remarks>
/// Every member runs on the UI thread of <see cref="HeadlessSession"/>: in a test's body that
/// <see cref="HeadlessSession.Run(Func{Task})"/> or <c>[UiFact]</c> runs. Text is measured with the test font
/// (<see cref="TestFontProvider"/>).
/// </remarks>
public sealed class HeadlessApp : IAsyncDisposable
{
    // Exceptions of work on the UI thread that no test awaited (HeadlessSession's loop); the next app that pumps rethrows them.
    private static readonly ConcurrentQueue<Exception> Unobserved = new();

    private readonly HeadlessOptions _options;
    private readonly TestFontProvider _fonts = new();
    private readonly TextContext _text;
    private readonly GlyphAtlas _atlas;
    private readonly HeadlessScheduler _scheduler = new();
    private readonly HeadlessPlatform _platform = new();
    private readonly HeadlessTarget _target;
    private readonly HeadlessRenderer? _renderer;
    private readonly UiDispatcher _dispatcher;
    private readonly NodeHandle _rootNode;
    private readonly bool _trackerWasEnabled;
    private readonly HashSet<int> _scopesBefore = [];

    // Keeps what the work on the UI queue throws in _errors; made once, so that a pump in a Zero-Alloc test allocates nothing.
    private readonly Action<Exception> _report;
    private ScopeHandle _rootScope;
    private bool _disposed;
    private List<Exception>? _errors;

    private HeadlessApp(Func<Element> root, HeadlessOptions options)
    {
        _options = options;
        _report = exception => (_errors ??= []).Add(exception);
        _dispatcher = HeadlessSession.Dispatcher;
        _text = new TextContext(_fonts);
        _renderer = options.Renderer?.Invoke();

        // A renderer that fails to size, or a part made after it that throws, does not leave its device behind (PR #469's review).
        try
        {
            _renderer?.Resize((int)MathF.Ceiling(options.Width * options.Scale), (int)MathF.Ceiling(options.Height * options.Scale));
            _target = new HeadlessTarget(_renderer?.Target);
            _atlas = new GlyphAtlas(_renderer?.CreateRasterizer() ?? new HeadlessGlyphs());
            Tree = new UiTree(_text);
            Tree.Animations.ReduceMotion = options.ReduceMotion;
            Tree.Clipboard = Clipboard;
            Tree.SetTextEditing(options.CaretBlink ? TextEditingSettings.Default : TextEditingSettings.Default with { CaretBlink = null });
            _rootNode = Tree.CreateNode(new LayoutStyle());
            Frames = new WindowFrames(Tree, _rootNode, _target, _atlas, _scheduler, Color.FromRgb(0xFFFFFF), time: Time);
            Input = new WindowInput(Tree, Frames, _platform);
            Pointer = new HeadlessPointer(this);
            Keyboard = new HeadlessKeyboard(this);
        }
        catch
        {
            _atlas?.Dispose();
            _renderer?.Dispose();
            _text.Dispose();
            _fonts.Dispose();
            throw;
        }

        // Scopes made before the app are not its leaks (AssertNoLeakedScopes).
        _trackerWasEnabled = ScopeTracker.IsEnabled;
        if (_trackerWasEnabled)
        {
            ScopeTracker.ForEachActiveScope(scope => _scopesBefore.Add(scope.Id));
        }

        ScopeTracker.IsEnabled = true;

        // A root function that throws, or returns a node it may not, leaves the process as it found it: the tracker as it was, and
        // nothing of the app held.
        try
        {
            Input.SetActive(true);
            Mount(root);

            // The frame the root function's nodes asked for is the first one, which the size runs at once; a frame that one asks
            // for stays.
            _scheduler.CancelFrame();
            Frames.Resize(new Size(options.Width, options.Height), options.Scale);
        }
        catch
        {
            Free();
            throw;
        }
    }

    /// <summary>
    /// Gets the virtual clock the frames read (design §12.2).
    /// </summary>
    public TestTime Time { get; } = new();

    /// <summary>
    /// Gets the mouse of the window.
    /// </summary>
    public HeadlessPointer Pointer { get; }

    /// <summary>
    /// Gets the keyboard of the window.
    /// </summary>
    public HeadlessKeyboard Keyboard { get; }

    /// <summary>
    /// Gets the clipboard the app's text fields copy to and paste from: one in memory, empty at the start (design §4.9, "The
    /// clipboard").
    /// </summary>
    public HeadlessClipboard Clipboard { get; } = new();

    /// <summary>
    /// Gets the number of frames that presented: a frame that changed nothing presents nothing (design §2.11).
    /// </summary>
    public int PresentCount => _target.Presents;

    /// <summary>
    /// Gets the cursor the input set last over the pointer.
    /// </summary>
    public CursorKind Cursor => _platform.Cursor;

    /// <summary>
    /// Gets the tree.
    /// </summary>
    internal UiTree Tree { get; }

    /// <summary>
    /// Gets the frames.
    /// </summary>
    internal WindowFrames Frames { get; }

    /// <summary>
    /// Gets the input.
    /// </summary>
    internal WindowInput Input { get; }

    /// <summary>
    /// Gets the node the root function's node is appended to.
    /// </summary>
    internal NodeHandle RootNode => _rootNode;

    /// <summary>
    /// Starts an app: runs its root function, lays the tree out, and runs the first frame.
    /// </summary>
    /// <param name="root">The app's root function, which returns a node with no parent.</param>
    /// <param name="options">The window and the clock; the defaults when <see langword="null"/>.</param>
    /// <returns>The app.</returns>
    /// <exception cref="InvalidOperationException">The caller is not on <see cref="HeadlessSession"/>'s UI thread.</exception>
    public static Task<HeadlessApp> StartAsync(Func<Element> root, HeadlessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        options ??= new HeadlessOptions();
        if (!(options.Width > 0 && options.Height > 0 && float.IsFinite(options.Width) && float.IsFinite(options.Height)))
        {
            throw new ArgumentException("The window's width and height are finite numbers above 0.", nameof(options));
        }

        if (!(options.Scale > 0 && float.IsFinite(options.Scale)) || options.FrameInterval <= TimeSpan.Zero || options.MaxIterations < 1)
        {
            throw new ArgumentException("The scale and the frame interval are above 0, and the iterations at least 1.", nameof(options));
        }

        _ = HeadlessSession.Dispatcher;
        if (!UiThread.CheckAccess())
        {
            throw new InvalidOperationException("A headless app runs on HeadlessSession's UI thread: in HeadlessSession.Run or a [UiFact] test.");
        }

        return Task.FromResult(new HeadlessApp(root, options));
    }

    /// <summary>
    /// Finds the one node whose test ID is <paramref name="key"/>, or, when no node has that ID, the one text leaf whose text is
    /// <paramref name="key"/> (design §12.5, "The keys").
    /// </summary>
    /// <param name="key">The test ID or the text.</param>
    /// <returns>The node.</returns>
    /// <exception cref="InvalidOperationException">No node, or more than one node, matches.</exception>
    public Element Find(string key)
    {
        IReadOnlyList<Element> found = FindAll(key);
        return found.Count switch
        {
            1 => found[0],
            0 => throw new InvalidOperationException($"No node has the test ID or the text \"{key}\"."),
            _ => throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"{found.Count} nodes have the test ID or the text \"{key}\"; Find wants one.")
            ),
        };
    }

    /// <summary>
    /// Finds the nodes whose test ID is <paramref name="key"/>, or, when no node has that ID, the text leaves whose text is
    /// <paramref name="key"/>, in preorder, the order of the a11y tree (design §12.5).
    /// </summary>
    /// <param name="key">The test ID or the text.</param>
    /// <returns>The nodes; empty when none matches.</returns>
    public IReadOnlyList<Element> FindAll(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        VerifyRunning();
        var found = new List<Element>();
        if (Tree.TestIdCount > 0)
        {
            Collect(_rootNode, key, byText: false, found);
        }

        if (found.Count == 0)
        {
            Collect(_rootNode, key, byText: true, found);
        }

        return found;
    }

    /// <summary>
    /// Writes the frame drawn last to a PNG file (design §12.3): what an agent looks at to judge the UI.
    /// </summary>
    /// <param name="path">The file; its folder is made when missing.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="InvalidOperationException">The app has no renderer (<see cref="HeadlessOptions.Renderer"/>).</exception>
    public async Task SaveScreenshotAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Color[] pixels = Capture(out int width, out int height);
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null)
        {
            _ = Directory.CreateDirectory(folder);
        }

        await File.WriteAllBytesAsync(path, Png.Encode(pixels, width, height)).ConfigureAwait(true);
    }

    /// <summary>
    /// Compares the frame drawn last with a golden of the test project's <c>Goldens/</c> folder (design §12.3, "Comparison"). With
    /// the environment variable <c>XYLEMUI_UPDATE_GOLDENS=1</c>, it writes the golden instead.
    /// </summary>
    /// <param name="name">The golden's file name, with <c>.png</c>; it may name a subfolder.</param>
    /// <param name="tolerance">How far the frame may be; <see cref="GoldenTolerance.Default"/> when <see langword="null"/>.</param>
    /// <returns>A task that completes when the comparison or the update is done.</returns>
    /// <exception cref="InvalidOperationException">The app has no renderer (<see cref="HeadlessOptions.Renderer"/>).</exception>
    /// <exception cref="GoldenMismatchException">The golden is missing, or differs by more than the tolerance; the frame and the
    /// image of the difference are written to the test's output folder's <c>GoldenFailures</c>.</exception>
    public Task MatchesGoldenAsync(string name, GoldenTolerance? tolerance = null)
    {
        Color[] pixels = Capture(out int width, out int height);
        return Goldens.MatchAsync(name, pixels, width, height, tolerance ?? GoldenTolerance.Default);
    }

    /// <summary>
    /// Moves the focus to a node, as <see cref="ElementExtensions.Focus"/> does, then runs the work that queued.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <returns><see langword="true"/> when the node took the focus; <see langword="false"/> when it cannot take it.</returns>
    public bool Focus(Element element)
    {
        VerifyRunning();
        bool focused = element.Focus();
        _ = DrainQueue();
        return focused;
    }

    /// <summary>
    /// Makes the window active or inactive, as the OS does when another window takes the keyboard (design §7, "Keys and the
    /// focus"); it is active from the start.
    /// </summary>
    /// <param name="active">Whether the window is active.</param>
    public void SetActive(bool active)
    {
        VerifyRunning();
        Input.SetActive(active);
        _ = DrainQueue();
    }

    /// <summary>
    /// Runs frames and timers on the virtual clock until nothing is left to run (design §12.2). Each iteration runs the UI queue;
    /// then, if a frame is asked for, advances the clock by <see cref="HeadlessOptions.FrameInterval"/> and runs it; otherwise, if
    /// a timer is due, advances the clock to it and fires it. It returns when the queue is empty and no frame or timer is left.
    /// </summary>
    /// <returns>The number of iterations that ran something.</returns>
    /// <exception cref="InvalidOperationException">Something was left after <see cref="HeadlessOptions.MaxIterations"/>
    /// iterations, as an animation that never ends leaves a frame; the message names what was left.</exception>
    public int PumpAndSettle()
    {
        VerifyRunning();
        int iterations = 0;
        while (true)
        {
            bool queued = DrainQueue();
            bool frame = _scheduler.Pending;
            TimeSpan? timer = Time.UntilNextTimer();
            if (!queued && !frame && timer is null)
            {
                return iterations;
            }

            if (iterations == _options.MaxIterations)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PumpAndSettle ran {iterations} iterations and did not settle: {Left(frame, queued)} was still left."
                    )
                );
            }

            iterations++;
            if (frame)
            {
                Time.Advance(_options.FrameInterval);
                _scheduler.Run();
            }
            else if (timer is { } until)
            {
                Time.Advance(until);
            }
        }
    }

    /// <summary>
    /// Advances the clock by <paramref name="duration"/>, firing the timers that fall due on the way, then runs the UI queue and
    /// one frame if a frame is asked for (Flutter's <c>pump(duration)</c>).
    /// </summary>
    /// <param name="duration">How far; 0 or more.</param>
    public void Pump(TimeSpan duration)
    {
        VerifyRunning();
        Time.Advance(duration);
        _ = DrainQueue();
        if (_scheduler.Pending)
        {
            _scheduler.Run();
            _ = DrainQueue();
        }
    }

    /// <summary>
    /// Closes the window, as the OS's close button does, runs what is left, and throws when a scope that the app made is still
    /// alive (design §12.5): an effect or a component that nothing disposes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Scopes are alive; the message lists each with its owner and, when
    /// <see cref="ScopeTracker.CaptureStackTrace"/> is on, the stack that made it.</exception>
    public void AssertNoLeakedScopes()
    {
        VerifyRunning();
        DisposeRoot();
        _ = PumpAndSettle();
        var leaked = new List<TrackedScope>();
        ScopeTracker.ForEachActiveScope(scope =>
        {
            if (!_scopesBefore.Contains(scope.Id))
            {
                leaked.Add(scope);
            }
        });
        if (leaked.Count == 0)
        {
            return;
        }

        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"{leaked.Count} scopes are alive after the window closed:");
        foreach (TrackedScope scope in leaked)
        {
            message.Append(CultureInfo.InvariantCulture, $"\n  scope {scope.Id} of {scope.Owner}");
            if (scope.IsEffect)
            {
                message.Append(" (effect)");
            }

            if (scope.StackTrace is { } stack)
            {
                message.Append('\n').Append(stack);
            }
        }

        throw new InvalidOperationException(message.ToString());
    }

    /// <summary>
    /// Closes the window and frees what the app holds. A frame or a timer that is still due fails the test (design §12.2, "The
    /// check at the end"): a timer left running moves another test's clock.
    /// </summary>
    /// <returns>A task that has completed.</returns>
    /// <exception cref="InvalidOperationException">A frame or a timer was left; what the app held is freed first.</exception>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        bool frame = _scheduler.Pending;
        int timers = Time.PendingTimers;
        Free();

        // Work on the UI thread that no test awaited threw since the last pump: the test it ran in sees it here, not the next one.
        if (!Unobserved.IsEmpty)
        {
            var errors = new List<Exception>();
            while (Unobserved.TryDequeue(out Exception? exception))
            {
                errors.Add(exception);
            }

            if (errors.Count == 1)
            {
                ExceptionDispatchInfo.Throw(errors[0]);
            }

            throw new AggregateException("Work on the UI queue threw.", errors);
        }

        if (frame || timers > 0)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The app ended with {(frame ? "a frame" : "no frame")} and {timers} timers still due; call PumpAndSettle first."
                )
            );
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Keeps an exception of work on the UI thread that no test awaited, for the app that pumps or is disposed next to rethrow.
    /// </summary>
    /// <param name="exception">The exception.</param>
    internal static void ReportUnobserved(Exception exception) => Unobserved.Enqueue(exception);

    /// <summary>
    /// Runs the UI queue until it is empty, and rethrows what its work threw.
    /// </summary>
    /// <returns><see langword="true"/> when it ran something.</returns>
    internal bool DrainQueue()
    {
        bool ran = false;
        while (_dispatcher.Drain(_report) > 0)
        {
            ran = true;
        }

        while (Unobserved.TryDequeue(out Exception? exception))
        {
            (_errors ??= []).Add(exception);
        }

        if (_errors is { } errors)
        {
            _errors = null;
            if (errors.Count == 1)
            {
                ExceptionDispatchInfo.Throw(errors[0]);
            }

            throw new AggregateException("Work on the UI queue threw.", errors);
        }

        return ran;
    }

    /// <summary>
    /// Returns the center of a node's box in the window's physical pixels: its layout box, moved by the scroll positions of the
    /// scroll containers it is in. Transforms are not applied.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <returns>The point.</returns>
    internal (int X, int Y) CenterOf(Element element)
    {
        VerifyRunning();
        UiTree tree = element.TreeOrThrow();
        if (!ReferenceEquals(tree, Tree))
        {
            throw new ArgumentException("The node is not in this app's tree.", nameof(element));
        }

        Rect rect = tree.Nodes.GetRef(element.Node).Layout.Rect;
        var point = new Vector2(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        for (NodeHandle parent = tree.Nodes.GetRef(element.Node).Parent; !parent.IsNone; parent = tree.Nodes.GetRef(parent).Parent)
        {
            Rect box = tree.Nodes.GetRef(parent).Layout.Rect;
            point -= tree.ScrollOffsetOf(parent);
            point += new Vector2(box.X, box.Y);
        }

        float scale = _options.Scale;
        return ((int)MathF.Round(point.X * scale), (int)MathF.Round(point.Y * scale));
    }

    /// <summary>
    /// Reads back the frame drawn last.
    /// </summary>
    /// <param name="width">Its width, in physical pixels.</param>
    /// <param name="height">Its height.</param>
    /// <returns>Its colors.</returns>
    /// <exception cref="InvalidOperationException">The app has no renderer.</exception>
    internal Color[] Capture(out int width, out int height)
    {
        VerifyRunning();
        if (_renderer is null)
        {
            throw new InvalidOperationException(
                "The app draws no pixels: give HeadlessOptions.Renderer a renderer, such as XylemUI.Testing.D3D11's WARP renderer."
            );
        }

        return _renderer.Capture(out width, out height);
    }

    /// <summary>
    /// Throws when the app is disposed, or when the caller is not on the UI thread.
    /// </summary>
    internal void VerifyRunning()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UiThread.VerifyAccess();
    }

    private static string Left(bool frame, bool queued) =>
        (frame, queued) switch
        {
            (true, true) => "a frame and work on the UI queue",
            (true, false) => "a frame",
            (false, true) => "work on the UI queue",
            _ => "a timer",
        };

    // The nodes under a node, in preorder, whose test ID (byText false) or text (byText true) is the key.
    private void Collect(NodeHandle node, string key, bool byText, List<Element> found)
    {
        ref UiNode n = ref Tree.Nodes.GetRef(node);
        bool match = byText ? n.Text is { } leaf && leaf.Text.SequenceEqual(key) : Tree.TestIdOf(node) == key;
        if (match)
        {
            found.Add(new Element(Tree, node));
        }

        for (NodeHandle child = n.FirstChild; !child.IsNone; child = Tree.Nodes.GetRef(child).NextSibling)
        {
            Collect(child, key, byText, found);
        }
    }

    // The root function, with the tree as the current tree, in a new scope that becomes the root scope (as DesktopWindow does).
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
                        throw new InvalidOperationException("The root function returns a node of the app's tree.");
                    }

                    if (!Tree.Nodes.GetRef(content.Node).Parent.IsNone)
                    {
                        throw new InvalidOperationException("The root function returns a node with no parent.");
                    }

                    Tree.AppendChild(_rootNode, content.Node);
                },
                "HeadlessApp.StartAsync"
            );
        }
        finally
        {
            UiTree.Current = previous;
        }
    }

    // Frees what the app holds and gives the tracker back the state it had; the app is disposed after it.
    private void Free()
    {
        _disposed = true;
        DisposeRoot();
        _scheduler.CancelFrame();
        ScopeTracker.IsEnabled = _trackerWasEnabled;
        _atlas.Dispose();
        _renderer?.Dispose();
        _text.Dispose();
        _fonts.Dispose();
    }

    private void DisposeRoot()
    {
        Tree.FrameNeeded = null;
        if (!_rootScope.IsNone && Scope.IsAlive(_rootScope))
        {
            Scope.Dispose(_rootScope);
        }

        _rootScope = default;
    }
}
