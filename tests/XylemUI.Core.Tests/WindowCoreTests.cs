using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The window core (row PL1, design §6.0, "The window core"): the half of a window that a desktop window and the headless app
// share. It connects to a window tier (IPlatformWindow) and takes its values now and the OS's settings now, the root function runs in
// the root scope with the tree current and the locale provided, the first frame runs at the size given, the host's resize and text
// rendering reach the frames, a notice of the app tier's settings (IPlatformSettings) reaches the tree once, what the window's surface
// is told is painted, and closing and disposing cancel the frame, dispose the root, stop following the settings, and, for disposing,
// dispose the window and the atlas it owns.
[Collection(UiThreadGroup.Name)]
public sealed class WindowCoreTests(UiThreadFixture ui)
{
    private const PlatformSettingsChanges AllChanges =
        PlatformSettingsChanges.ReducesMotion
        | PlatformSettingsChanges.TextEditing
        | PlatformSettingsChanges.UiLanguages
        | PlatformSettingsChanges.Use24HourFormat;

    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// Mount runs the root function with the core's tree as the current tree and the locale resolved against the settings'
    /// languages provided, appends its node to the root node as the start of a route with no focus, puts the tree back as it was,
    /// and runs one first frame at the logical size and scale given on the window's target: the frame the root function's nodes
    /// asked for is cancelled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings whose OS languages are "ja-JP"; a core with them that supports en and ja, connected to a window whose target and
    ///   scheduler count.
    ///
    /// Steps:
    /// - Mount, with a size of 200 x 100 at a scale of 2, a root function that records the current tree and the locale and returns
    ///   a box.
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
            var settings = new FakeSettings { UiLanguages = ["ja-JP"] };
            (WindowCore core, FakeWindow window) = Connected(atlas, settings, [CldrLocales.En, CldrLocales.Ja]);
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
                null,
                default,
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
                    null,
                    default,
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
    /// and how its text is drawn; gives the tree the window's input method; and gives the tree the OS's settings now: less motion and
    /// the text fields' settings.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window that does not show, is active, draws text with the defaults but a gamma of 2.2, and has an input method. Settings
    ///   that ask for less motion and a caret 3 wide.
    ///
    /// Steps:
    /// - Make a core with the settings and connect it to the window. Give a new node of the tree a background, which asks for a
    ///   frame.
    ///
    /// Expected:
    /// - The tree's focus sees the window active; the frames draw text as the window does; the tree's input method is the window's.
    /// - The tree reduces motion, and its text fields' caret is 3 wide.
    /// - The scheduler holds no frame: a window that does not show only remembers the frame asked for.
    /// </remarks>
    [Fact]
    public void Connect_TakesTheWindowsValuesAndTheSettingsNow() =>
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
            var settings = new FakeSettings { ReducesMotion = true, TextEditing = TextEditingSettings.Default with { CaretWidth = 3 } };
            var core = new WindowCore(null, settings, null, null, null);

            core.Connect(window, atlas, Gray);
            NodeHandle node = core.Tree.CreateNode(new LayoutStyle());
            core.Tree.AppendChild(core.RootNode, node);
            core.Tree.SetBackground(node, new Paint(Red));

            Assert.True(core.Tree.Focus.IsWindowActive);
            Assert.Equal(rendering, core.Frames.TextRendering);
            Assert.Same(window.TextInput, core.Tree.TextInput);
            Assert.True(core.Tree.Animations.ReduceMotion);
            Assert.Equal(3f, core.Tree.TextEditing.CaretWidth);
            Assert.False(window.Scheduler.Pending);
            core.DisposeRoot();
        });

    /// <summary>
    /// The clipboard given to the core is the one its tree's text fields copy to and paste from (design §6.0, "Headless"); a core
    /// without the app tier's settings is refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A clipboard and settings.
    ///
    /// Steps:
    /// - Make a core with them. Make a core with no settings.
    ///
    /// Expected:
    /// - The first tree's clipboard is that clipboard. The second throws <see cref="ArgumentNullException"/> for the settings.
    /// </remarks>
    [Fact]
    public void Constructor_GivesTheTreeTheClipboard_AndNeedsTheSettings() =>
        ui.Invoke(() =>
        {
            var clipboard = new NoClipboard();

            var core = new WindowCore(null, new FakeSettings(), clipboard, null, null);
            ArgumentNullException thrown = Assert.Throws<ArgumentNullException>(() => new WindowCore(null, null!, clipboard, null, null));

            Assert.Same(clipboard, core.Tree.Clipboard);
            Assert.Equal("settings", thrown.ParamName);
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
            core.Mount(Box, null, default, "WindowCoreTests", new Size(200, 100), 1);
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
                null,
                default,
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
            var core = new WindowCore(null, new FakeSettings(), null, null, null);
            core.Connect(window, atlas, Gray);
            int closed = 0;
            core.Closed += (_, _) => closed++;
            Element box = default;
            core.Mount(() => box = Box(), null, default, "WindowCoreTests", new Size(200, 100), 1);
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
            var core = new WindowCore(null, new FakeSettings(), null, null, null);

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
                null,
                default,
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
            var core = new WindowCore(null, new FakeSettings(), null, null, null);

            core.DisposeRoot();
            core.Dispose();

            Assert.True(core.RootScope.IsNone);
        });

    /// <summary>
    /// One notice of the settings changes, once, each value it marks, and no value it does not mark (design §6.0, "OS settings"):
    /// less motion and the text fields' settings in the tree, the languages through the locale, and the 24-hour form.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with no less motion, the default text fields' settings, the languages "ja-JP", and the 12-hour form. A connected
    ///   core with them that supports en and ja, mounted with a root function whose effects record the locale and the 24-hour form.
    ///
    /// Steps:
    /// - Change all four values of the settings: less motion, a caret 3 wide, "en-US", and the 24-hour form. Tell a notice that
    ///   marks less motion only; read the tree. Tell one that marks the other three.
    ///
    /// Expected:
    /// - After the first notice: the tree reduces motion; its caret is 1 wide; the effects recorded only ja and off.
    /// - After the second: the caret is 3 wide; the effects recorded ja then en, and off then on, each once more.
    /// </remarks>
    [Fact]
    public void SettingsChanged_OneNotice_ChangesEachMarkedValueOnce() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings { UiLanguages = ["ja-JP"] };
            (WindowCore core, _) = Connected(atlas, settings, [CldrLocales.En, CldrLocales.Ja]);
            var locales = new List<string>();
            var forms = new List<bool>();
            core.Mount(
                () =>
                {
                    IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                    IReadOnlySignal<bool> use24HourFormat = Localization.Use24HourFormat.Use();
                    _ = Effect.Create(() => locales.Add(locale.Get().ToLanguageTag()));
                    _ = Effect.Create(() => forms.Add(use24HourFormat.Get()));
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );
            settings.ReducesMotion = true;
            settings.TextEditing = TextEditingSettings.Default with { CaretWidth = 3 };
            settings.UiLanguages = ["en-US"];
            settings.Use24HourFormat = true;

            settings.Tell(PlatformSettingsChanges.ReducesMotion);
            bool reducedAfterFirst = core.Tree.Animations.ReduceMotion;
            float caretAfterFirst = core.Tree.TextEditing.CaretWidth;
            string[] localesAfterFirst = [.. locales];
            bool[] formsAfterFirst = [.. forms];
            settings.Tell(PlatformSettingsChanges.TextEditing | PlatformSettingsChanges.UiLanguages | PlatformSettingsChanges.Use24HourFormat);
            core.DisposeRoot();

            Assert.True(reducedAfterFirst);
            Assert.Equal(1f, caretAfterFirst);
            Assert.Equal(["ja"], localesAfterFirst);
            Assert.Equal([false], formsAfterFirst);
            Assert.Equal(3f, core.Tree.TextEditing.CaretWidth);
            Assert.Equal(["ja", "en"], locales);
            Assert.Equal([false, true], forms);
        });

    /// <summary>
    /// A marked value whose giving throws does not stop the others: every marked value is given, and the notice then throws that
    /// exception itself.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with no less motion, the languages "ja-JP", and the 12-hour form. A connected core with them that supports en and
    ///   ja, mounted with a root function whose effects record the locale and the 24-hour form.
    ///
    /// Steps:
    /// - Make the settings ask for less motion, "en-US", and the 24-hour form, and make reading their text fields' settings throw
    ///   a <see cref="FormatException"/>. Tell one notice that marks all four values.
    ///
    /// Expected:
    /// - The notice throws that exception itself. The tree reduces motion (given before the throw); the effects recorded ja then
    ///   en, and off then on (given after it).
    /// </remarks>
    [Fact]
    public void SettingsChanged_OneValueThrows_GivesTheOthers_ThenThrowsIt() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings { UiLanguages = ["ja-JP"] };
            (WindowCore core, _) = Connected(atlas, settings, [CldrLocales.En, CldrLocales.Ja]);
            var locales = new List<string>();
            var forms = new List<bool>();
            MountRecording(core, locales, forms);
            var error = new FormatException("The text fields' settings throw.");
            settings.ReducesMotion = true;
            settings.UiLanguages = ["en-US"];
            settings.Use24HourFormat = true;
            settings.TextEditingError = error;

            FormatException thrown = Assert.Throws<FormatException>(() => settings.Tell(AllChanges));
            settings.TextEditingError = null;
            bool reduced = core.Tree.Animations.ReduceMotion;
            core.DisposeRoot();

            Assert.Same(error, thrown);
            Assert.True(reduced);
            Assert.Equal(["ja", "en"], locales);
            Assert.Equal([false, true], forms);
        });

    /// <summary>
    /// Two marked values whose giving throws do not stop the others either, and the notice then throws an
    /// <see cref="AggregateException"/> with both exceptions, in the order the values are given.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with no less motion and the 12-hour form. A connected core with them that supports en, mounted with a root
    ///   function whose effect records the 24-hour form.
    ///
    /// Steps:
    /// - Make the settings ask for less motion and the 24-hour form, and make reading their text fields' settings throw a
    ///   <see cref="FormatException"/> and reading their languages throw an <see cref="ArgumentException"/>. Tell one notice that
    ///   marks all four values.
    ///
    /// Expected:
    /// - The notice throws an <see cref="AggregateException"/> whose inner exceptions are the format exception, then the argument
    ///   exception. The tree reduces motion; the effect recorded off then on.
    /// </remarks>
    [Fact]
    public void SettingsChanged_TwoValuesThrow_GivesTheOthers_ThenThrowsBoth() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings();
            (WindowCore core, _) = Connected(atlas, settings, [CldrLocales.En]);
            var locales = new List<string>();
            var forms = new List<bool>();
            MountRecording(core, locales, forms);
            var textEditingError = new FormatException("The text fields' settings throw.");
            var languagesError = new ArgumentException("The languages throw.");
            settings.ReducesMotion = true;
            settings.Use24HourFormat = true;
            settings.TextEditingError = textEditingError;
            settings.UiLanguagesError = languagesError;

            AggregateException thrown = Assert.Throws<AggregateException>(() => settings.Tell(AllChanges));
            settings.TextEditingError = null;
            settings.UiLanguagesError = null;
            bool reduced = core.Tree.Animations.ReduceMotion;
            core.DisposeRoot();

            Assert.Equal<Exception>([textEditingError, languagesError], thrown.InnerExceptions);
            Assert.True(reduced);
            Assert.Equal([false, true], forms);
        });

    /// <summary>
    /// The settings are the app's and outlive the window: once the window closed, the core no longer follows them, and a notice
    /// reaches nothing of it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with no less motion; a connected core with them, mounted with a box.
    ///
    /// Steps:
    /// - Read whether the settings have a handler. Tell the core the window closed. Make the settings ask for less motion, and tell
    ///   a notice that marks it.
    ///
    /// Expected:
    /// - The settings had a handler before the close and have none after; the tree does not reduce motion.
    /// </remarks>
    [Fact]
    public void SettingsChanged_AfterTheWindowClosed_ReachesNothing() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings();
            (WindowCore core, _) = Connected(atlas, settings);
            core.Mount(Box, null, default, "WindowCoreTests", new Size(200, 100), 1);
            bool followedBefore = settings.HasHandlers;

            core.WindowClosed();
            settings.ReducesMotion = true;
            settings.Tell(PlatformSettingsChanges.ReducesMotion);

            Assert.True(followedBefore);
            Assert.False(settings.HasHandlers);
            Assert.False(core.Tree.Animations.ReduceMotion);
        });

    /// <summary>
    /// Notices of the languages and the 24-hour form before the root is mounted throw nothing, and Mount takes the settings' values
    /// at that time.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with the languages "en-GB" and the 24-hour form; a connected core with them that supports en and ja, not mounted.
    ///
    /// Steps:
    /// - Tell a notice that marks the languages and the 24-hour form. Make the settings' languages "ja-JP" and the form 12-hour,
    ///   with no notice. Mount with a root function that reads the locale and the 24-hour form.
    ///
    /// Expected:
    /// - Nothing is thrown; the root function read ja and the 12-hour form.
    /// </remarks>
    [Fact]
    public void SettingsChanged_BeforeMount_MountTakesTheValuesThen() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings { UiLanguages = ["en-GB"], Use24HourFormat = true };
            (WindowCore core, _) = Connected(atlas, settings, [CldrLocales.En, CldrLocales.Ja]);
            string? locale = null;
            bool? use24HourFormat = null;

            settings.Tell(PlatformSettingsChanges.UiLanguages | PlatformSettingsChanges.Use24HourFormat);
            settings.UiLanguages = ["ja-JP"];
            settings.Use24HourFormat = false;
            core.Mount(
                () =>
                {
                    locale = Localization.Locale.Use().Get().ToLanguageTag();
                    use24HourFormat = Localization.Use24HourFormat.Use().Get();
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );
            core.DisposeRoot();

            Assert.Equal("ja", locale);
            Assert.False(use24HourFormat);
        });

    /// <summary>
    /// Other languages of the OS resolve the locale again, and a reader of it runs again only when the resolved locale changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with the languages "ja-JP"; a connected core with them that supports en and ja, mounted with a root function
    ///   whose effect records the locale.
    ///
    /// Steps:
    /// - Make the languages "en-GB", then "en-US", then "ko-KR, ja-JP", telling a notice marked with the languages after each.
    ///
    /// Expected:
    /// - The effect recorded ja, en, ja: "en-US" resolves to en again and does not run it.
    /// </remarks>
    [Fact]
    public void SettingsChanged_Languages_ResolveTheLocaleAgain() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings { UiLanguages = ["ja-JP"] };
            (WindowCore core, _) = Connected(atlas, settings, [CldrLocales.En, CldrLocales.Ja]);
            var seen = new List<string>();
            core.Mount(
                () =>
                {
                    IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                    _ = Effect.Create(() => seen.Add(locale.Get().ToLanguageTag()));
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );

            settings.ChangeLanguages(["en-GB"]);
            settings.ChangeLanguages(["en-US"]);
            settings.ChangeLanguages(["ko-KR", "ja-JP"]);
            core.DisposeRoot();

            Assert.Equal(["ja", "en", "ja"], seen);
        });

    /// <summary>
    /// Mount runs the wrapper inside the locale, the OS's appearance, and the core's surface, gives it the root function, and
    /// appends the node it returns; the root function runs inside the wrapper and finds the same values (design §13.2, "根で置く
    /// もの").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core that supports en and ja. A wrapper that records the locale, the scheme, and whether the surface found is
    ///   the core's, calls the root function, and returns a box that holds the root's node; a root function that records the same
    ///   and returns a box.
    ///
    /// Steps:
    /// - Mount with the wrapper, the OS's languages "ja-JP", and a dark appearance.
    ///
    /// Expected:
    /// - The log is "wrap ja dark core's surface", "root ja dark core's surface", "wrapped". The wrapper's box is the root node's
    ///   child and the tree's key root, and the root's box is the wrapper's box's child.
    /// </remarks>
    [Fact]
    public void Mount_RunsTheWrapperInsideTheRootValues_AroundTheRoot() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas, new FakeSettings { UiLanguages = ["ja-JP"] }, [CldrLocales.En, CldrLocales.Ja]);
            var log = new List<string>();
            Element outer = default;
            Element inner = default;
            Func<Element> root = () =>
            {
                log.Add("root " + Seen());
                return inner = Box();
            };
            Func<Func<Element>, Element> wrapper = given =>
            {
                log.Add("wrap " + Seen());
                outer = Box(given());
                log.Add("wrapped");
                return outer;
            };
            var dark = new SystemAppearance(ColorScheme.Dark, false, null, null);

            core.Mount(root, wrapper, dark, "WindowCoreTests", new Size(200, 100), 1);
            NodeHandle outerParent = core.Tree.Nodes.GetRef(outer.Node).Parent;
            NodeHandle innerParent = core.Tree.Nodes.GetRef(inner.Node).Parent;
            NodeHandle keyRoot = core.Tree.KeyRoot;
            core.DisposeRoot();

            string[] expected = ["wrap ja dark core's surface", "root ja dark core's surface", "wrapped"];
            Assert.Equal(expected, log);
            Assert.Equal(core.RootNode, outerParent);
            Assert.Equal(outer.Node, keyRoot);
            Assert.Equal(outer.Node, innerParent);

            string Seen() =>
                Localization.Locale.Use().Get().ToLanguageTag()
                + (SystemAppearance.Current.Use().Get().Scheme == ColorScheme.Dark ? " dark" : " light")
                + (ReferenceEquals(WindowSurface.Current.Use(), core.Surface) ? " core's surface" : " another surface");
        });

    /// <summary>
    /// A wrapper that returns a node of another tree, or one with a parent, makes Mount throw as a root function that returns one
    /// does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, one for each case.
    ///
    /// Steps:
    /// - Mount a root function that returns a box, with a wrapper that calls it and returns: a node of another tree; a box that a
    ///   column holds.
    ///
    /// Expected:
    /// - Each Mount throws <see cref="InvalidOperationException"/>, and the root scope is none.
    /// </remarks>
    /// <param name="kind">0: a node of another tree; 1: a node with a parent.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Mount_WrapperReturnsANodeItMayNot_Throws(int kind) =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas);
            var other = new UiTree();
            Func<Func<Element>, Element> wrapper = root =>
            {
                _ = root();
                return kind == 0 ? new Element(other, other.CreateNode(new LayoutStyle())) : Child();
            };

            _ = Assert.Throws<InvalidOperationException>(() => core.Mount(Box, wrapper, default, "WindowCoreTests", new Size(200, 100), 1));

            Assert.True(core.RootScope.IsNone);
        });

    /// <summary>
    /// A wrapper that throws, before or after it calls the root function, makes Mount throw its exception, and leaves nothing of
    /// the mount: the root scope and the three scopes that provide the locale, the OS's appearance, and the surface are disposed,
    /// the nodes the root function made are freed, and the current tree is put back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, one for each case; the number of live scopes and the current tree before the mount.
    ///
    /// Steps:
    /// - Mount a root function that returns a box, with a wrapper that records the current scope and its three ancestors, then:
    ///   throws a <see cref="FormatException"/> at once; or calls the root function and then throws it.
    ///
    /// Expected:
    /// - Mount throws that exception. The four recorded scopes are not alive, the number of live scopes is the one before, the root
    ///   scope is none, the current tree is the one before, and, when the root function ran, its box is freed.
    /// </remarks>
    /// <param name="afterRoot"><see langword="false"/>: the wrapper throws before it calls the root function; <see langword="true"/>:
    /// after.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mount_WrapperThrows_LeavesNothingOfTheMount(bool afterRoot) =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas);
            var error = new FormatException("The wrapper throws.");
            var recorded = new List<Handle<ScopeData>>();
            Element box = default;
            Func<Func<Element>, Element> wrapper = root =>
            {
                // The surface's scope, then the appearance's, the locale's, and the root scope.
                Handle<ScopeData> scope = Scope.CurrentRef;
                for (int i = 0; i < 4; i++)
                {
                    recorded.Add(scope);
                    scope = Scope.Get(scope).Parent;
                }

                if (afterRoot)
                {
                    box = root();
                }

                throw error;
            };
            int live = Scope.LiveCount;
            UiTree? before = UiTree.Current;

            FormatException thrown = Assert.Throws<FormatException>(() =>
                core.Mount(Box, wrapper, default, "WindowCoreTests", new Size(200, 100), 1)
            );

            Assert.Same(error, thrown);
            Assert.Equal(4, recorded.Count);
            Assert.All(recorded, scope => Assert.False(Scope.IsAlive(scope)));
            Assert.Equal(live, Scope.LiveCount);
            Assert.True(core.RootScope.IsNone);
            Assert.Same(before, UiTree.Current);
            Assert.Equal(afterRoot, !box.IsNone);
            Assert.True(box.IsNone || !core.Tree.Nodes.IsAlive(box.Node));
        });

    /// <summary>
    /// A wrapper calls the root function exactly once (design §13.2, "根で置くもの"): a second call throws before the root function
    /// runs again, and a wrapper that returns without calling it makes Mount throw; either way nothing of the mount is left.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, one for each case; a root function that counts its runs and returns a box; the number of live scopes and
    ///   the current tree before the mount.
    ///
    /// Steps:
    /// - Mount with a wrapper that: calls the root function twice and returns the first box; or returns a box of its own without
    ///   calling it.
    ///
    /// Expected:
    /// - Mount throws <see cref="InvalidOperationException"/>. The root function ran once in the first case and never in the
    ///   second. The number of live scopes is the one before, the root scope is none, the current tree is the one before, and the
    ///   tree holds as many nodes as before: the boxes made are freed.
    /// </remarks>
    /// <param name="twice"><see langword="true"/>: the wrapper calls the root function twice; <see langword="false"/>: never.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mount_WrapperCallsTheRootOtherThanOnce_Throws(bool twice) =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas);
            int runs = 0;
            Func<Element> root = () =>
            {
                runs++;
                return Box();
            };
            Func<Func<Element>, Element> wrapper = given =>
            {
                if (!twice)
                {
                    return Box();
                }

                Element first = given();
                _ = given();
                return first;
            };
            int live = Scope.LiveCount;
            int nodes = core.Tree.Nodes.Count;
            UiTree? before = UiTree.Current;

            _ = Assert.Throws<InvalidOperationException>(() => core.Mount(root, wrapper, default, "WindowCoreTests", new Size(200, 100), 1));

            Assert.Equal(twice ? 1 : 0, runs);
            Assert.Equal(live, Scope.LiveCount);
            Assert.True(core.RootScope.IsNone);
            Assert.Same(before, UiTree.Current);
            Assert.Equal(nodes, core.Tree.Nodes.Count);
        });

    /// <summary>
    /// An appearance whose high contrast and high-contrast colors disagree makes Mount throw before the root function runs, and
    /// SetSystemAppearance throw before and after Mount.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core; high contrast on with no colors.
    ///
    /// Steps:
    /// - Set the value before Mount. Mount with it and a root function that counts its runs. Mount with the default value; set the
    ///   value after it.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentException"/>, naming the parameter of the call: <c>value</c>, <c>appearance</c>, and
    ///   <c>value</c>. The root function ran once, in the second Mount.
    /// </remarks>
    [Fact]
    public void ContrastColors_DisagreeingWithHighContrast_AreRefused() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas);
            var wrong = new SystemAppearance(ColorScheme.Dark, true, null, null);
            int runs = 0;
            Element Root()
            {
                runs++;
                return Box();
            }

            ArgumentException beforeMount = Assert.Throws<ArgumentException>(() => core.SetSystemAppearance(wrong));
            ArgumentException mount = Assert.Throws<ArgumentException>(() => core.Mount(Root, null, wrong, "WindowCoreTests", new Size(200, 100), 1));
            core.Mount(Root, null, default, "WindowCoreTests", new Size(200, 100), 1);
            ArgumentException afterMount = Assert.Throws<ArgumentException>(() => core.SetSystemAppearance(wrong));
            core.DisposeRoot();

            Assert.Equal(("value", "appearance", "value"), (beforeMount.ParamName, mount.ParamName, afterMount.ParamName));
            Assert.Equal(1, runs);
        });

    /// <summary>
    /// An appearance of the OS told before the root is mounted is not kept: the mount takes the one it is given.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, not mounted.
    ///
    /// Steps:
    /// - Set the OS's appearance to dark. Mount with the default appearance and a root function that reads the scheme.
    ///
    /// Expected:
    /// - Nothing is thrown, and the root function read light.
    /// </remarks>
    [Fact]
    public void SetSystemAppearance_BeforeMount_DoesNothing() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas);
            ColorScheme? read = null;

            core.SetSystemAppearance(new SystemAppearance(ColorScheme.Dark, false, null, null));
            core.Mount(
                () =>
                {
                    read = SystemAppearance.Current.Use().Get().Scheme;
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );
            core.DisposeRoot();

            Assert.Equal(ColorScheme.Light, read);
        });

    /// <summary>
    /// Another appearance of the OS runs a reader of it again, only when the value changed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A connected core, mounted with the default appearance and a root function whose effect records the scheme.
    ///
    /// Steps:
    /// - Set the OS's appearance to dark, dark again, then light.
    ///
    /// Expected:
    /// - The effect recorded light, dark, light.
    /// </remarks>
    [Fact]
    public void SetSystemAppearance_RunsTheReadersAgain_WhenTheValueChanges() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, _) = Connected(atlas);
            var dark = new SystemAppearance(ColorScheme.Dark, false, null, null);
            var seen = new List<ColorScheme>();
            core.Mount(
                () =>
                {
                    IReadOnlySignal<SystemAppearance> appearance = SystemAppearance.Current.Use();
                    _ = Effect.Create(() => seen.Add(appearance.Get().Scheme));
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );

            core.SetSystemAppearance(dark);
            core.SetSystemAppearance(dark);
            core.SetSystemAppearance(default);
            core.DisposeRoot();

            ColorScheme[] expected = [ColorScheme.Light, ColorScheme.Dark, ColorScheme.Light];
            Assert.Equal(expected, seen);
        });

    /// <summary>
    /// Mount provides the tables of the resolved locale under <see cref="Localization.Data"/>, their locale under
    /// <see cref="Localization.Locale"/>, and the settings' 24-hour form under <see cref="Localization.Use24HourFormat"/> (design
    /// §4.11, "Distribution").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with the languages "ja-JP" and the 24-hour form; a connected core with them that supports the tables of en and ja.
    ///
    /// Steps:
    /// - Mount with a root function that reads the three keys.
    ///
    /// Expected:
    /// - The tables are <see cref="CldrLocales.Ja"/> itself, the locale is ja, and the 24-hour form is on.
    /// </remarks>
    [Fact]
    public void Mount_ProvidesTheTablesTheirLocaleAndThe24HourForm() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings { UiLanguages = ["ja-JP"], Use24HourFormat = true };
            (WindowCore core, _) = Connected(atlas, settings, [CldrLocales.En, CldrLocales.Ja]);
            LocaleData? tables = null;
            string? locale = null;
            bool? use24HourFormat = null;

            core.Mount(
                () =>
                {
                    tables = Localization.Data.Use().Get();
                    locale = Localization.Locale.Use().Get().ToLanguageTag();
                    use24HourFormat = Localization.Use24HourFormat.Use().Get();
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );
            core.DisposeRoot();

            Assert.Same(CldrLocales.Ja, tables);
            Assert.Equal("ja", locale);
            Assert.True(use24HourFormat);
        });

    /// <summary>
    /// When the resolution returns a locale whose tables the app did not declare, the tables are the declared ones of that
    /// locale, else the first declared ones of its language and likely script, else the root's, under the returned locale's tag
    /// (design §4.11, "When the replacing function returns a locale not declared").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - For each case a new core whose resolving function returns a fixed locale: "ja" with en and ja declared; "zh-TW" with
    ///   zh-Hans and zh-Hant declared, in that order; "sr-ME" (likely script Latn) with sr (Cyrl) declared; "fr-CA" with en and ja
    ///   declared; "xx-YY" with the en tables declared under the tag "xx" (a language the likely subtags do not have, so neither
    ///   script can be inferred).
    ///
    /// Steps:
    /// - Mount each core with a root function that reads <see cref="Localization.Data"/> and <see cref="Localization.Locale"/>.
    ///
    /// Expected:
    /// - "ja": <see cref="CldrLocales.Ja"/> itself. "zh-TW": the plural rules of <see cref="CldrLocales.ZhHant"/>. "sr-ME",
    ///   "fr-CA", and "xx-YY": the plural rules of <see cref="LocaleData.Root"/>; two scripts that cannot be inferred are not the
    ///   same script (Issue #906). In every case the tables' locale and the locale key are the
    ///   returned locale.
    /// </remarks>
    [Fact]
    public void Mount_ALocaleNotDeclared_TakesTheTablesOfItsLanguageAndScriptOrTheRoot() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());

            Assert.Same(CldrLocales.Ja, TablesFor("ja", [CldrLocales.En, CldrLocales.Ja]));
            Assert.Same(CldrLocales.ZhHant.Plurals, TablesFor("zh-TW", [CldrLocales.ZhHans, CldrLocales.ZhHant]).Plurals);
            Assert.Same(LocaleData.Root.Plurals, TablesFor("sr-ME", [CldrLocales.Sr]).Plurals);
            Assert.Same(LocaleData.Root.Plurals, TablesFor("fr-CA", [CldrLocales.En, CldrLocales.Ja]).Plurals);
            Assert.Same(LocaleData.Root.Plurals, TablesFor("xx-YY", [CldrLocales.En.WithLocale(Locale.Parse("xx"))]).Plurals);

            LocaleData TablesFor(string tag, LocaleData[] supported)
            {
                Locale returned = Locale.Parse(tag);
                (WindowCore core, _) = Connected(atlas, null, supported, (_, _) => returned);
                LocaleData? tables = null;
                Locale locale = default;
                core.Mount(
                    () =>
                    {
                        tables = Localization.Data.Use().Get();
                        locale = Localization.Locale.Use().Get();
                        return Box();
                    },
                    null,
                    default,
                    "WindowCoreTests",
                    new Size(200, 100),
                    1
                );
                core.DisposeRoot();
                Assert.Equal(returned, tables!.Locale);
                Assert.Equal(returned, locale);
                return tables;
            }
        });

    /// <summary>
    /// Languages of the OS that resolve to the same locale keep the same tables, also when they are the root's under the OS's
    /// tag: a reader of <see cref="Localization.Data"/> runs again only when the resolved locale changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with the languages "ja-JP"; a connected core with them that supports no locale, mounted with a root function whose
    ///   effect records the locale of <see cref="Localization.Data"/>.
    ///
    /// Steps:
    /// - Make the languages "ja-JP, en-US", then "fr-FR", telling a notice marked with the languages after each.
    ///
    /// Expected:
    /// - The effect recorded ja-JP and fr-FR: "ja-JP, en-US" resolves to ja-JP again and does not run it. The last tables have the
    ///   plural rules of <see cref="LocaleData.Root"/>.
    /// </remarks>
    [Fact]
    public void SettingsChanged_TheSameResolvedLocale_KeepsTheTables() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings { UiLanguages = ["ja-JP"] };
            (WindowCore core, _) = Connected(atlas, settings);
            var seen = new List<string>();
            IReadOnlySignal<LocaleData>? data = null;
            core.Mount(
                () =>
                {
                    IReadOnlySignal<LocaleData> tables = Localization.Data.Use();
                    data = tables;
                    _ = Effect.Create(() => seen.Add(tables.Get().Locale.ToLanguageTag()));
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );

            settings.ChangeLanguages(["ja-JP", "en-US"]);
            settings.ChangeLanguages(["fr-FR"]);
            LocaleData last = data!.Get();
            core.DisposeRoot();

            Assert.Equal(["ja-JP", "fr-FR"], seen);
            Assert.Same(LocaleData.Root.Plurals, last.Plurals);
        });

    /// <summary>
    /// A notice of the 24-hour form changes <see cref="Localization.Use24HourFormat"/>, and its readers run again only when the
    /// value changes; the readers of the tables do not run.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with the 12-hour form; a connected core with them that supports en, mounted with a root function with two
    ///   effects: one records the 24-hour form, the other counts its reads of <see cref="Localization.Data"/>.
    ///
    /// Steps:
    /// - Make the settings' form 12-hour, 24-hour, 24-hour, and 12-hour, telling a notice marked with the 24-hour form after each.
    ///
    /// Expected:
    /// - The first effect recorded off, on, off; the second ran once.
    /// </remarks>
    [Fact]
    public void SettingsChanged_24HourForm_RunsItsReadersOnlyWhenTheValueChanges() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings();
            (WindowCore core, _) = Connected(atlas, settings, [CldrLocales.En]);
            var seen = new List<bool>();
            int tableReads = 0;
            core.Mount(
                () =>
                {
                    IReadOnlySignal<bool> use24HourFormat = Localization.Use24HourFormat.Use();
                    IReadOnlySignal<LocaleData> data = Localization.Data.Use();
                    _ = Effect.Create(() => seen.Add(use24HourFormat.Get()));
                    _ = Effect.Create(() =>
                    {
                        _ = data.Get();
                        tableReads++;
                    });
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );

            settings.Change24HourFormat(false);
            settings.Change24HourFormat(true);
            settings.Change24HourFormat(true);
            settings.Change24HourFormat(false);
            core.DisposeRoot();

            Assert.Equal([false, true, false], seen);
            Assert.Equal(1, tableReads);
        });

    /// <summary>
    /// What the root wrapper tells the window's surface is painted (design §13.2, "根の箱" and "題名の棒"): the backdrop as the
    /// color the frames draw behind the tree, from the first frame on, and the title bar through the window tier; a value equal to
    /// the last paints nothing, and once the root is disposed nothing more is painted.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core connected to a window whose target records the color behind the tree and whose title bar records each value it is
    ///   given; the frames' color is gray. A root function that keeps the surface and tells it the backdrop #292929 and a dark title
    ///   bar.
    ///
    /// Steps:
    /// - Mount, and read the frames' color, the color the first frame drew with, and the title bars. Tell the surface #292929 and
    ///   dark again; read whether a frame is pending. Tell it #FAFAFA, and run the frame; tell it a light title bar. Dispose the
    ///   root; tell it black and dark.
    ///
    /// Expected:
    /// - After the mount: #292929 and #292929; the title bars are dark.
    /// - The same values ask for no frame and add no title bar.
    /// - #FAFAFA asks for a frame, which draws with #FAFAFA; the title bars are dark, then light.
    /// - After the root is disposed: the frames' color is still #FAFAFA, and the title bars are still dark, then light.
    /// </remarks>
    [Fact]
    public void Surface_PaintsTheBackdropAndTheTitleBar_OnlyChangedValues_UntilTheRootIsDisposed() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            (WindowCore core, FakeWindow window) = Connected(atlas);
            Color dark = Color.FromRgb(0x292929);
            Color light = Color.FromRgb(0xFAFAFA);
            WindowSurface? surface = null;
            core.Mount(
                () =>
                {
                    surface = WindowSurface.Current.Use();
                    surface.SetBackdrop(dark);
                    surface.SetDarkTitleBar(true);
                    return Box();
                },
                null,
                default,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );
            (Color Frames, Color Drawn) mounted = (core.Frames.ClearColor, window.Target.ClearColor);
            bool[] barsAfterMount = [.. window.TitleBars];

            surface!.SetBackdrop(dark);
            surface.SetDarkTitleBar(true);
            bool pendingAfterSame = window.Scheduler.Pending;
            bool[] barsAfterSame = [.. window.TitleBars];
            surface.SetBackdrop(light);
            bool pendingAfterLight = window.Scheduler.Pending;
            window.Scheduler.Run();
            Color drawnAfterLight = window.Target.ClearColor;
            surface.SetDarkTitleBar(false);
            core.DisposeRoot();
            surface.SetBackdrop(Color.FromRgb(0x000000));
            surface.SetDarkTitleBar(true);

            Assert.Equal((dark, dark), mounted);
            Assert.Equal([true], barsAfterMount);
            Assert.False(pendingAfterSame);
            Assert.Equal([true], barsAfterSame);
            Assert.True(pendingAfterLight);
            Assert.Equal(light, drawnAfterLight);
            Assert.Equal(light, core.Frames.ClearColor);
            Assert.Equal([true, false], window.TitleBars);
        });

    /// <summary>
    /// A notice of the settings marked with the OS's appearance gives the tree the settings' appearance, and its readers run again
    /// only when it changed; a notice that does not mark it changes nothing of it (design §13.2, "置き方").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings with the default appearance; a connected core with them, mounted with that appearance and a root function whose
    ///   effect records the scheme.
    ///
    /// Steps:
    /// - Make the settings' appearance dark and tell a notice marked with less motion only. Tell one marked with the appearance;
    ///   tell it again. Make the appearance light and tell one marked with less motion and the appearance.
    ///
    /// Expected:
    /// - The effect recorded light, dark, light: the first notice did not give the appearance, and the second dark ran nothing.
    /// </remarks>
    [Fact]
    public void SettingsChanged_Appearance_GivesTheSettingsAppearanceWhenMarked() =>
        ui.Invoke(() =>
        {
            using var atlas = new GlyphAtlas(new NoGlyphs());
            var settings = new FakeSettings();
            (WindowCore core, _) = Connected(atlas, settings);
            var seen = new List<ColorScheme>();
            core.Mount(
                () =>
                {
                    IReadOnlySignal<SystemAppearance> appearance = SystemAppearance.Current.Use();
                    _ = Effect.Create(() => seen.Add(appearance.Get().Scheme));
                    return Box();
                },
                null,
                settings.Appearance,
                "WindowCoreTests",
                new Size(200, 100),
                1
            );

            settings.Appearance = new SystemAppearance(ColorScheme.Dark, false, null, null);
            settings.Tell(PlatformSettingsChanges.ReducesMotion);
            settings.Tell(PlatformSettingsChanges.Appearance);
            settings.Tell(PlatformSettingsChanges.Appearance);
            settings.Appearance = default;
            settings.Tell(PlatformSettingsChanges.ReducesMotion | PlatformSettingsChanges.Appearance);
            core.DisposeRoot();

            ColorScheme[] expected = [ColorScheme.Light, ColorScheme.Dark, ColorScheme.Light];
            Assert.Equal(expected, seen);
        });

    // A core with no text, made with the settings given or settings of its own, and connected to a window that shows, whose target
    // and scheduler count. The atlas is the test's: the core is not disposed here.
    private static (WindowCore Core, FakeWindow Window) Connected(
        GlyphAtlas atlas,
        FakeSettings? settings = null,
        IReadOnlyList<LocaleData>? supported = null,
        Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? resolve = null
    )
    {
        var core = new WindowCore(null, settings ?? new FakeSettings(), null, supported, resolve);
        var window = new FakeWindow();
        core.Connect(window, atlas, Gray);
        return (core, window);
    }

    // Mounts a root function whose effects record the locale's tag and the 24-hour form each time they change.
    private static void MountRecording(WindowCore core, List<string> locales, List<bool> forms) =>
        core.Mount(
            () =>
            {
                IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                IReadOnlySignal<bool> use24HourFormat = Localization.Use24HourFormat.Use();
                _ = Effect.Create(() => locales.Add(locale.Get().ToLanguageTag()));
                _ = Effect.Create(() => forms.Add(use24HourFormat.Get()));
                return Box();
            },
            null,
            default,
            "WindowCoreTests",
            new Size(200, 100),
            1
        );

    // A box that a column holds: a node with a parent.
    private static Element Child()
    {
        Element child = Box();
        _ = Column(child);
        return child;
    }

    // The app tier's settings, whose values the test sets and whose notices the test tells, marked as the test says: a host tells a
    // change only, which Change* do.
    // A value whose error is set throws it when read, as a setter of the tree that throws would.
    private sealed class FakeSettings : IPlatformSettings
    {
        private TextEditingSettings _textEditing = TextEditingSettings.Default;
        private IReadOnlyList<string> _uiLanguages = [];

        public event Action<PlatformSettingsChanges>? Changed;

        public bool ReducesMotion { get; set; }

        public TextEditingSettings TextEditing
        {
            get => TextEditingError is null ? _textEditing : throw TextEditingError;
            set => _textEditing = value;
        }

        public IReadOnlyList<string> UiLanguages
        {
            get => UiLanguagesError is null ? _uiLanguages : throw UiLanguagesError;
            set => _uiLanguages = value;
        }

        public bool Use24HourFormat { get; set; }

        public SystemAppearance Appearance { get; set; }

        public Exception? TextEditingError { get; set; }

        public Exception? UiLanguagesError { get; set; }

        public bool HasHandlers => Changed is not null;

        public void Tell(PlatformSettingsChanges changes) => Changed?.Invoke(changes);

        public void ChangeLanguages(IReadOnlyList<string> tags)
        {
            UiLanguages = tags;
            Tell(PlatformSettingsChanges.UiLanguages);
        }

        public void Change24HourFormat(bool use24HourFormat)
        {
            Use24HourFormat = use24HourFormat;
            Tell(PlatformSettingsChanges.Use24HourFormat);
        }
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

        public List<bool> TitleBars { get; } = [];

        public void Attach() { }

        public void RootMounted() { }

        public void Show() { }

        public void RequestClose() { }

        public void SetDarkTitleBar(bool dark) => TitleBars.Add(dark);

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
