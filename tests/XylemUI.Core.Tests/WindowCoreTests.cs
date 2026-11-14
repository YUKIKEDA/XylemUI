using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The window core (row PL1, design §6.0, "The window core"): the half of a window that a desktop window and the headless app
// share. It connects to a window tier (IPlatformWindow) and takes its values now, the root function runs in the root scope with the
// tree current and the locale provided, the first frame runs at the size given, the host's resize and text rendering reach the
// frames, and closing and disposing cancel the frame, dispose the root, and, for disposing, the window and the atlas it owns.
[Collection(UiThreadGroup.Name)]
public sealed class WindowCoreTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// Mount runs the root function with the core's tree as the current tree and the resolved locale provided, appends its node to
    /// the root node as the start of a route with no focus, puts the tree back as it was, and runs one first frame at the logical
    /// size and scale given on the window's target: the frame the root function's nodes asked for is cancelled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core that supports en and ja, connected to a window whose target and scheduler count.
    ///
    /// Steps:
    /// - Mount, with the OS's languages "ja-JP" and a size of 200 x 100 at a scale of 2, a root function that records the current
    ///   tree and the locale and returns a box.
    ///
    /// Expected:
    /// - The root function saw the core's tree and "ja". The box's parent is the root node, and it is the tree's key root. The
    ///   current tree after Mount is the one before it. The root scope is alive. The target presented once; no frame is pending;
    ///   the root node is 200 x 100 and the frames' scale is 2.
    /// </remarks>
    [Fact]
    public void Mount_RunsTheRootWithTheTreeAndLocale_ThenOneFirstFrame() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, FakeWindow window) = Connected(atlas, [Locale.Parse("en"), Locale.Parse("ja")]);
            UiTree? before = UiTree.Current;
            UiTree? seenTree = null;
            string? seenLocale = null;
            Element box = default;

            core.Mount(
                () =>
                {
                    seenTree = UiTree.Current;
                    seenLocale = Localization.Locale.Use().Get().ToLanguageTag();
                    return box = Box();
                },
                ["ja-JP"],
                "WindowCoreTests",
                new Size(200, 100),
                2
            );

            Assert.Same(core.Tree, seenTree);
            Assert.Equal("ja", seenLocale);
            Assert.Equal(core.RootNode, core.Tree.Nodes.GetRef(box.Node).Parent);
            Assert.Equal(box.Node, core.Tree.KeyRoot);
            Assert.Same(before, UiTree.Current);
            Assert.True(Scope.IsAlive(core.RootScope));
            Assert.Equal(1, window.Target.Presents);
            Assert.False(window.Scheduler.Pending);
            Rect rect = core.Tree.Nodes.GetRef(core.RootNode).Layout.Rect;
            Assert.Equal((200f, 100f), (rect.Width, rect.Height));
            Assert.Equal(2f, core.Frames.Scale);
            core.DisposeRoot();
        });

    /// <summary>
    /// A root function that returns no node, a node of another tree, or a node with a parent makes Mount throw, and the current
    /// tree is put back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, one for each case.
    ///
    /// Steps:
    /// - Mount a root function that returns: no node; a node of another tree; a box that a column holds.
    ///
    /// Expected:
    /// - Each Mount throws <see cref="InvalidOperationException"/>; the current tree after it is the one before it; the root scope
    ///   is none.
    /// </remarks>
    /// <param name="kind">0: no node; 1: a node of another tree; 2: a node with a parent.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Mount_RootReturnsANodeItMayNot_Throws(int kind) =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas);
            UiTree? before = UiTree.Current;
            var other = new UiTree();

            _ = Assert.Throws<InvalidOperationException>(() =>
                core.Mount(
                    () =>
                        kind switch
                        {
                            0 => default,
                            1 => new Element(other, other.CreateNode(new LayoutStyle())),
                            _ => Child(),
                        },
                    [],
                    "WindowCoreTests",
                    new Size(200, 100),
                    1
                )
            );

            Assert.Same(before, UiTree.Current);
            Assert.True(core.RootScope.IsNone);
        });

    /// <summary>
    /// Connect takes the window's values now (design §6.0, "The order of creation", step 6): whether it shows, whether it is active,
    /// and how its text is drawn; and gives the tree the window's input method.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window that does not show, is active, draws text with the defaults but a gamma of 2.2, and has an input method.
    ///
    /// Steps:
    /// - Connect a core to it. Give a new node of the tree a background, which asks for a frame.
    ///
    /// Expected:
    /// - The tree's focus sees the window active; the frames draw text as the window does; the tree's input method is the window's.
    /// - The scheduler holds no frame: a window that does not show only remembers the frame asked for.
    /// </remarks>
    [Fact]
    public void Connect_TakesTheWindowsValuesNow() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            TextRenderingParameters rendering = TextRenderingParameters.Default with { Gamma = 2.2f };
            var window = new FakeWindow
            {
                IsShowing = false,
                IsActive = true,
                TextRendering = rendering,
                TextInput = new NoInputMethod(),
            };
            var core = new WindowCore(null, null, null, null);

            core.Connect(window, atlas, Gray);
            NodeHandle node = core.Tree.CreateNode(new LayoutStyle());
            core.Tree.AppendChild(core.RootNode, node);
            core.Tree.SetBackground(node, new Paint(Red));

            Assert.True(core.Tree.Focus.IsWindowActive);
            Assert.Equal(rendering, core.Frames.TextRendering);
            Assert.Same(window.TextInput, core.Tree.TextInput);
            Assert.False(window.Scheduler.Pending);
            core.DisposeRoot();
        });

    /// <summary>
    /// The clipboard given to the core is the one its tree's text fields copy to and paste from (design §6.0, "Headless").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A clipboard.
    ///
    /// Steps:
    /// - Make a core with it.
    ///
    /// Expected:
    /// - The tree's clipboard is that clipboard.
    /// </remarks>
    [Fact]
    public void Constructor_GivesTheTreeTheClipboard() =>
        ui.Invoke(() =>
        {
            var clipboard = new NoClipboard();

            var core = new WindowCore(null, clipboard, null, null);

            Assert.Same(clipboard, core.Tree.Clipboard);
        });

    /// <summary>
    /// The host's resize runs a frame at once at the logical size and scale it gives (design §2.11.1, §6.0, "The renderer, Present,
    /// and the synchronous resize").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, mounted with a box at 200 x 100 and a scale of 1.
    ///
    /// Steps:
    /// - Resize to 150 x 50 at a scale of 2.
    ///
    /// Expected:
    /// - The target presented once more, and no frame is pending; the root node is 150 x 50 and the frames' scale is 2.
    /// </remarks>
    [Fact]
    public void Resize_RunsAFrameAtOnceAtTheSizeGiven() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, FakeWindow window) = Connected(atlas);
            core.Mount(Box, [], "WindowCoreTests", new Size(200, 100), 1);
            int presents = window.Target.Presents;

            core.Resize(new Size(150, 50), 2);

            Assert.Equal(presents + 1, window.Target.Presents);
            Assert.False(window.Scheduler.Pending);
            Rect rect = core.Tree.Nodes.GetRef(core.RootNode).Layout.Rect;
            Assert.Equal((150f, 50f), (rect.Width, rect.Height));
            Assert.Equal(2f, core.Frames.Scale);
            core.DisposeRoot();
        });

    /// <summary>
    /// The text rendering the host tells the core reaches the frames (design §6.0, "OS settings").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, whose window draws text with the defaults.
    ///
    /// Steps:
    /// - Tell it that the text is drawn bilevel.
    ///
    /// Expected:
    /// - The frames draw text bilevel.
    /// </remarks>
    [Fact]
    public void TextRenderingChanged_GivesTheFramesTheValue() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas);
            TextRenderingParameters aliased = TextRenderingParameters.Default with { IsAliased = true };

            core.TextRenderingChanged(aliased);

            Assert.Equal(aliased, core.Frames.TextRendering);
            core.DisposeRoot();
        });

    /// <summary>
    /// The OS window's close cancels the frame asked for, disposes the root, and raises <see cref="WindowCore.Closed"/> once; the
    /// window itself stays until the core is disposed (design §2.11, "Frames of hidden and minimized windows"; §6.0).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, mounted with a root function that adds a cleanup to the root scope and returns a box; a handler counts
    ///   <see cref="WindowCore.Closed"/> and records whether the root scope was none when it ran.
    ///
    /// Steps:
    /// - Set the box's background red, which asks for a frame. Tell the core the window closed.
    ///
    /// Expected:
    /// - A frame was asked for, and none is pending after. The cleanup ran once, and the root scope is none. The handler ran once,
    ///   after the root was disposed. The window was not disposed.
    /// </remarks>
    [Fact]
    public void WindowClosed_CancelsTheFrame_DisposesTheRoot_ThenRaisesClosed() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, FakeWindow window) = Connected(atlas);
            int cleanups = 0;
            int closed = 0;
            bool rootGoneWhenClosed = false;
            Element box = default;
            core.Mount(
                () =>
                {
                    Scope.OnCleanup(() => cleanups++);
                    return box = Box();
                },
                [],
                "WindowCoreTests",
                new Size(200, 100),
                1
            );
            core.Closed += (_, _) =>
            {
                closed++;
                rootGoneWhenClosed = core.RootScope.IsNone;
            };
            core.Tree.SetBackground(box.Node, new Paint(Red));
            bool askedFor = window.Scheduler.Pending;

            core.WindowClosed();

            Assert.True(askedFor);
            Assert.False(window.Scheduler.Pending);
            Assert.Equal(1, cleanups);
            Assert.True(core.RootScope.IsNone);
            Assert.Equal(1, closed);
            Assert.True(rootGoneWhenClosed);
            Assert.Empty(window.Disposals);
        });

    /// <summary>
    /// Disposing the core cancels the frame asked for, disposes the root, then disposes the window and then the atlas it owns, and
    /// raises no <see cref="WindowCore.Closed"/>; a second disposal does nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core connected to a window and an atlas that each record their disposal in one list, mounted with a box; a handler counts
    ///   <see cref="WindowCore.Closed"/>.
    ///
    /// Steps:
    /// - Set the box's background red, which asks for a frame. Dispose the core twice.
    ///
    /// Expected:
    /// - No frame is pending, the root scope is none, the list is "window", "atlas", and the handler did not run.
    /// </remarks>
    [Fact]
    public void Dispose_CancelsTheFrame_DisposesTheRoot_ThenTheWindowAndTheAtlasOnce() =>
        ui.Invoke(() =>
        {
            var order = new List<string>();
            var atlas = new GlyphAtlas(() => new DisposalGlyphs(order), threads: 1);
            var window = new FakeWindow(order);
            var core = new WindowCore(null, null, null, null);
            core.Connect(window, atlas, Gray);
            int closed = 0;
            core.Closed += (_, _) => closed++;
            Element box = default;
            core.Mount(() => box = Box(), [], "WindowCoreTests", new Size(200, 100), 1);
            core.Tree.SetBackground(box.Node, new Paint(Red));

            core.Dispose();
            core.Dispose();

            Assert.False(window.Scheduler.Pending);
            Assert.True(core.RootScope.IsNone);
            Assert.Equal(["window", "atlas"], order);
            Assert.Equal(0, closed);
        });

    /// <summary>
    /// The core owns the window and the atlas from Connect on, also when Connect throws: disposing it then disposes both.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window that draws text bilevel with ClearType coverage, which the frames refuse, and an atlas; both record their
    ///   disposal in one list.
    ///
    /// Steps:
    /// - Connect a core to them; dispose the core.
    ///
    /// Expected:
    /// - Connect throws <see cref="ArgumentException"/>; the list is "window", "atlas".
    /// </remarks>
    [Fact]
    public void Connect_Throws_TheCoreStillDisposesTheWindowAndTheAtlas() =>
        ui.Invoke(() =>
        {
            var order = new List<string>();
            var atlas = new GlyphAtlas(() => new DisposalGlyphs(order), threads: 1);
            var window = new FakeWindow(order)
            {
                TextRendering = TextRenderingParameters.Default with { Coverage = GlyphCoverage.Rgb, IsAliased = true },
            };
            var core = new WindowCore(null, null, null, null);

            _ = Assert.Throws<ArgumentException>(() => core.Connect(window, atlas, Gray));
            core.Dispose();

            Assert.Equal(["window", "atlas"], order);
        });

    /// <summary>
    /// Disposing the root disposes the root scope and what it made, and stops the tree from asking for frames, but leaves a frame
    /// already asked for to the host; a second call does nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, mounted with a root function that adds a cleanup to the root scope and returns a box.
    ///
    /// Steps:
    /// - Set the box's background red, which asks for a frame. Dispose the root; dispose it again.
    ///
    /// Expected:
    /// - The cleanup ran once. The root scope is none, and the box is freed. The tree has no frame request to call, and the frame
    ///   asked for is still pending.
    /// </remarks>
    [Fact]
    public void DisposeRoot_DisposesTheScope_AndKeepsTheFrameAskedFor() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, FakeWindow window) = Connected(atlas);
            int cleanups = 0;
            Element box = default;
            core.Mount(
                () =>
                {
                    Scope.OnCleanup(() => cleanups++);
                    return box = Box();
                },
                [],
                "WindowCoreTests",
                new Size(200, 100),
                1
            );
            core.Tree.SetBackground(box.Node, new Paint(Red));
            bool askedFor = window.Scheduler.Pending;

            core.DisposeRoot();
            core.DisposeRoot();

            Assert.True(askedFor);
            Assert.Equal(1, cleanups);
            Assert.True(core.RootScope.IsNone);
            Assert.False(core.Tree.Nodes.IsAlive(box.Node));
            Assert.Null(core.Tree.FrameNeeded);
            Assert.True(window.Scheduler.Pending);
        });

    /// <summary>
    /// A core that never connected or mounted can dispose its root and itself: a host that fails while it makes its window does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core, not connected.
    ///
    /// Steps:
    /// - Dispose the root, then the core.
    ///
    /// Expected:
    /// - Neither throws, and the root scope is none.
    /// </remarks>
    [Fact]
    public void DisposeRoot_BeforeConnect_DoesNothing() =>
        ui.Invoke(() =>
        {
            var core = new WindowCore(null, null, null, null);

            core.DisposeRoot();
            core.Dispose();

            Assert.True(core.RootScope.IsNone);
        });

    /// <summary>
    /// Languages of the OS told before the root is mounted are not kept and throw nothing: the mount takes the ones it is given.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core that supports en and ja, not mounted.
    ///
    /// Steps:
    /// - Set the OS's languages to "en-GB". Mount with the OS's languages "ja-JP" and a root function that reads the locale.
    ///
    /// Expected:
    /// - Nothing is thrown, and the root function read ja.
    /// </remarks>
    [Fact]
    public void SetSystemLocales_BeforeMount_DoesNothing() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas, [Locale.Parse("en"), Locale.Parse("ja")]);
            string? read = null;

            core.SetSystemLocales(["en-GB"]);
            core.Mount(
                () =>
                {
                    read = Localization.Locale.Use().Get().ToLanguageTag();
                    return Box();
                },
                ["ja-JP"],
                "WindowCoreTests",
                new Size(200, 100),
                1
            );
            core.DisposeRoot();

            Assert.Equal("ja", read);
        });

    /// <summary>
    /// Other languages of the OS resolve the locale again, and a reader of it runs again only when the resolved locale changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core that supports en and ja, mounted with the OS's languages "ja-JP" and a root function whose effect records
    ///   the locale.
    ///
    /// Steps:
    /// - Set the OS's languages to "en-GB", then "en-US", then "ko-KR, ja-JP".
    ///
    /// Expected:
    /// - The effect recorded ja, en, ja: "en-US" resolves to en again and does not run it.
    /// </remarks>
    [Fact]
    public void SetSystemLocales_ResolvesTheLocaleAgain() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas, [Locale.Parse("en"), Locale.Parse("ja")]);
            var seen = new List<string>();
            core.Mount(
                () =>
                {
                    IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                    _ = Effect.Create(() => seen.Add(locale.Get().ToLanguageTag()));
                    return Box();
                },
                ["ja-JP"],
                "WindowCoreTests",
                new Size(200, 100),
                1
            );

            core.SetSystemLocales(["en-GB"]);
            core.SetSystemLocales(["en-US"]);
            core.SetSystemLocales(["ko-KR", "ja-JP"]);
            core.DisposeRoot();

            Assert.Equal(["ja", "en", "ja"], seen);
        });

    // A core with no text, connected to a window that shows, whose target and scheduler count. The atlas is the test's: the core
    // is not disposed here.
    private static (WindowCore Core, FakeWindow Window) Connected(GlyphAtlas atlas, IReadOnlyList<Locale>? supported = null)
    {
        var core = new WindowCore(null, null, supported, null);
        var window = new FakeWindow();
        core.Connect(window, atlas, Gray);
        return (core, window);
    }

    // A box that a column holds: a node with a parent.
    private static Element Child()
    {
        Element child = Box();
        _ = Column(child);
        return child;
    }

    // A window tier whose target and scheduler count, whose values the test sets, and that records its disposal.
    private sealed class FakeWindow(List<string>? disposals = null) : IPlatformWindow
    {
        public List<string> Disposals { get; } = disposals ?? [];

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public (int Width, int Height) ClientSize => (200, 100);

        public float ScaleFactor => 1;

        public bool IsShowing { get; init; } = true;

        public bool IsActive { get; init; }

        public TextRenderingParameters TextRendering { get; init; } = TextRenderingParameters.Default;

        public ITextInputPlatform? TextInput { get; init; }

        IRenderTarget IPlatformWindow.Target => Target;

        public IFrameScheduler FrameScheduler => Scheduler;

        public IPointerPlatform Pointer { get; } = new NoPlatform();

        public void Attach() { }

        public void RootMounted() { }

        public void RequestClose() { }

        public void Dispose() => Disposals.Add("window");
    }

    // A rasterizer for trees that draw no text, which records its disposal: the atlas disposes the rasterizers it made.
    private sealed class DisposalGlyphs(List<string> disposals) : IGlyphRasterizer, IDisposable
    {
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn in these tests.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) =>
            throw new InvalidOperationException("No glyph is drawn in these tests.");

        public void Dispose() => disposals.Add("atlas");
    }

    private sealed class NoInputMethod : ITextInputPlatform
    {
        public void Attach(ITextInputClient client, bool showKeyboard) { }

        public void Detach() { }

        public void TextChanged(int start, int oldEnd, int newEnd) { }

        public void SelectionChanged() { }

        public void LayoutChanged() { }

        public void CommitComposition() { }
    }

    private sealed class NoClipboard : IClipboardBackend
    {
        public string? GetText() => null;

        public bool HasText() => false;

        public bool SetText(string text) => false;
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
