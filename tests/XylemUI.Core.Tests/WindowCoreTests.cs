using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The window core (row PL1, design §6.0, "The window core"): the half of a window that a desktop window and the headless app
// share. The root function runs in the root scope with the tree current and the locale provided, the first frame runs at the size
// given, and disposing the root stops the tree's frame requests without cancelling a frame already asked for.
[Collection(UiThreadGroup.Name)]
public sealed class WindowCoreTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// Mount runs the root function with the core's tree as the current tree and the resolved locale provided, appends its node to
    /// the root node as the start of a route with no focus, puts the tree back as it was, and runs one first frame at the logical
    /// size and scale given: the frame the root function's nodes asked for is cancelled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core that supports en and ja, connected to a counting target and scheduler.
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
            (WindowCore core, CountingTarget target, CountingScheduler scheduler) = Connected(atlas, [Locale.Parse("en"), Locale.Parse("ja")]);
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
            Assert.Equal(1, target.Presents);
            Assert.False(scheduler.Pending);
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
            (WindowCore core, _, _) = Connected(atlas);
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
            (WindowCore core, _, CountingScheduler scheduler) = Connected(atlas);
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
            bool askedFor = scheduler.Pending;

            core.DisposeRoot();
            core.DisposeRoot();

            Assert.True(askedFor);
            Assert.Equal(1, cleanups);
            Assert.True(core.RootScope.IsNone);
            Assert.False(core.Tree.Nodes.IsAlive(box.Node));
            Assert.Null(core.Tree.FrameNeeded);
            Assert.True(scheduler.Pending);
        });

    /// <summary>
    /// A core that never connected or mounted can dispose its root: a host that fails while it makes its window does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core, not connected.
    ///
    /// Steps:
    /// - Dispose the root.
    ///
    /// Expected:
    /// - It does not throw, and the root scope is none.
    /// </remarks>
    [Fact]
    public void DisposeRoot_BeforeConnect_DoesNothing() =>
        ui.Invoke(() =>
        {
            var core = new WindowCore(null, null, null);

            core.DisposeRoot();

            Assert.True(core.RootScope.IsNone);
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
            (WindowCore core, _, _) = Connected(atlas, [Locale.Parse("en"), Locale.Parse("ja")]);
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

    // A core with no text, connected to a counting target and scheduler.
    private static (WindowCore Core, CountingTarget Target, CountingScheduler Scheduler) Connected(
        GlyphAtlas atlas,
        IReadOnlyList<Locale>? supported = null
    )
    {
        var core = new WindowCore(null, supported, null);
        var target = new CountingTarget();
        var scheduler = new CountingScheduler();
        core.Connect(target, atlas, scheduler, new NoPlatform(), Gray);
        return (core, target, scheduler);
    }

    // A box that a column holds: a node with a parent.
    private static Element Child()
    {
        Element child = Box();
        _ = Column(child);
        return child;
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
