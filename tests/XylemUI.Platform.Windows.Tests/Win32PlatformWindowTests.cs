using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The window tier of the Windows host (row PL1, design §6.0): the window paired with its renderer, whose swap chain is the loop's
// frame clock; the notices it binds to the window core; the synchronous resize; the settings it passes on until PR 5; what its
// disposal takes back; and the app tier's check for a second window. The windows are hidden, on WARP, and no loop runs: the
// messages are sent, so they run before SendMessageW returns.
[Collection(UiThreadGroup.Name)]
public sealed class Win32PlatformWindowTests(UiThreadFixture ui)
{
    private const uint ResizeFlags = (uint)(SWP.SWP_NOMOVE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE);

    /// <summary>
    /// Opening a window pairs it with a renderer and sets the renderer's swap chain as the loop's frame clock; the window's target is
    /// the renderer and its scheduler the loop's. Disposing it sets the clock back to the loop's timer (design §6.0, "The frame clock
    /// and the scheduler").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window core, not connected.
    ///
    /// Steps:
    /// - Open a hidden window on WARP for it; read the handle the loop's scheduler waits on. Dispose the window; read it again.
    ///
    /// Expected:
    /// - While the window is open, the loop waits on the renderer's frame latency waitable object; after, on another handle.
    /// - The window's target is its renderer, and its scheduler is the loop's.
    /// </remarks>
    [Fact]
    public void Open_SetsTheSwapChainAsTheClock_DisposeSetsTheTimerBack() =>
        ui.Invoke(() =>
        {
            using var core = new WindowCore(null, null, null, null);
            var window = new Win32PlatformWindow(core, "clock", 200, 100, show: false, useWarp: true);
            HANDLE clock = (HANDLE)window.Renderer.FrameLatencyWaitableObject;
            HANDLE during = Win32MessageLoop.Frames.WaitHandle;
            bool targetIsRenderer = ReferenceEquals(window.Renderer, window.Target);
            bool schedulerIsLoops = ReferenceEquals(Win32MessageLoop.FrameScheduler, window.FrameScheduler);

            window.Dispose();
            HANDLE after = Win32MessageLoop.Frames.WaitHandle;

            Assert.Equal(clock, during);
            Assert.NotEqual(clock, after);
            Assert.True(targetIsRenderer);
            Assert.True(schedulerIsLoops);
        });

    /// <summary>
    /// While a window of the host is open, a second <c>OpenWindow</c> throws, naming row MW1; once the first is disposed, another
    /// opens. The host reads the OS's languages from the open window, and has none while no window is open (Issue #928).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Windows host whose windows draw on WARP, and two window cores.
    ///
    /// Steps:
    /// - Open a hidden window for the first core; read the host's languages; open one for the second. Dispose the first window; read
    ///   the host's languages; open a window for the second core again.
    ///
    /// Expected:
    /// - The second open throws <see cref="InvalidOperationException"/> whose message names MW1.
    /// - While the first window is open, the host's languages are that window's; after it is disposed, none.
    /// - The open after the disposal gives a window.
    /// </remarks>
    [Fact]
    public void OpenWindow_WhileOneIsOpen_ThrowsNamingMw1_AndOpensAfterItIsDisposed() =>
        ui.Invoke(() =>
        {
            using var app = new Win32PlatformApp { UseWarp = true };
            using var first = new WindowCore(null, app.Clipboard, null, null);
            using var second = new WindowCore(null, app.Clipboard, null, null);
            IPlatformWindow opened = app.OpenWindow(first, "first", 200, 100, show: false);
            IReadOnlyList<string> whileOpen = app.UiLanguages;
            IReadOnlyList<string> windows = ((Win32PlatformWindow)opened).UiLanguages;
            Exception? thrown = Record.Exception(() => app.OpenWindow(second, "second", 200, 100, show: false));
            opened.Dispose();
            IReadOnlyList<string> afterDisposal = app.UiLanguages;

            IPlatformWindow again = app.OpenWindow(second, "second", 200, 100, show: false);
            again.Dispose();

            InvalidOperationException invalid = Assert.IsType<InvalidOperationException>(thrown);
            Assert.Contains("MW1", invalid.Message, StringComparison.Ordinal);
            Assert.Same(windows, whileOpen);
            Assert.Empty(afterDisposal);
        });

    /// <summary>
    /// Attach binds the window's notices to the core (design §6.0, "The window and its notices"): the input and the
    /// accessibility adapter's actions to the core's input, the drops from the OS to it, the touchpad's frames to the core's
    /// frames, and the activation; the core's tree has the window's input method.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core mounted in a hidden window on WARP, with a box.
    ///
    /// Steps:
    /// - Read what the window's notices are bound to. Send <c>WM_ACTIVATE</c> with <c>WA_ACTIVE</c>, then with
    ///   <c>WA_INACTIVE</c>, reading the tree's activation after each. Cancel the loop's frame, then raise the touchpad's
    ///   <c>GestureFrameNeeded</c>.
    ///
    /// Expected:
    /// - The pointer's, the keys', and the text's handlers are the core's input's, the drops go to the core's input, and the
    ///   adapter's input is the core's. The tree's input method is the window's.
    /// - The tree is active after the first message and not after the second.
    /// - The loop's scheduler holds a frame after the touchpad's notice.
    /// </remarks>
    [Fact]
    public void Attach_BindsTheWindowsNoticesToTheCore() =>
        ui.Invoke(() =>
        {
            (WindowCore core, Win32PlatformWindow window) = Open(Elements.Box);
            using (core)
            {
                Win32Window native = window.Window;
                _ = SendMessageW(native.Hwnd, WM.WM_ACTIVATE, WA_ACTIVE, 0);
                bool activeAfterFirst = core.Tree.Focus.IsWindowActive;
                _ = SendMessageW(native.Hwnd, WM.WM_ACTIVATE, WA_INACTIVE, 0);
                bool activeAfterSecond = core.Tree.Focus.IsWindowActive;
                Win32MessageLoop.FrameScheduler.CancelFrame();
                native.GestureFrameNeeded!();
                bool frameAsked = Win32MessageLoop.Frames.HasPendingFrame;
                Win32MessageLoop.FrameScheduler.CancelFrame();

                Assert.Same(core.Input, native.PointerInputReceived!.Target);
                Assert.Same(core.Input, native.KeyInputReceived!.Target);
                Assert.Same(core.Input, native.TextInputReceived!.Target);
                Assert.Same(core.Input, native.ExternalDrops);
                Assert.Same(core.Input, native.Accessibility!.Input);
                Assert.Same(window.TextInput, core.Tree.TextInput);
                Assert.True(activeAfterFirst);
                Assert.False(activeAfterSecond);
                Assert.True(frameAsked);
            }
        });

    /// <summary>
    /// A size change of the window resizes the renderer, then runs the core's frame at once at the physical size divided by the
    /// scale (design §2.11.1; §6.0, "The renderer, Present, and the synchronous resize").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core mounted in a hidden window on WARP, with a box; no loop runs.
    ///
    /// Steps:
    /// - Resize the window to 360 x 240 with <c>SetWindowPos</c>, which sends <c>WM_SIZE</c> before it returns.
    ///
    /// Expected:
    /// - The renderer has the new client size; the frames presented again; the root node is the client size divided by the
    ///   window's scale.
    /// </remarks>
    [Fact]
    public void SizeChange_ResizesTheRenderer_ThenRunsTheFrameAtTheLogicalSize() =>
        ui.Invoke(() =>
        {
            (WindowCore core, Win32PlatformWindow window) = Open(Elements.Box);
            using (core)
            {
                int presents = core.Frames.PresentCount;

                Assert.True(SetWindowPos(window.Window.Hwnd, HWND.NULL, 0, 0, 360, 240, ResizeFlags));

                (int width, int height) = window.ClientSize;
                float scale = window.ScaleFactor;
                Rect root = core.Tree.Nodes.GetRef(core.RootNode).Layout.Rect;
                Assert.Equal((width, height), (window.Renderer.Width, window.Renderer.Height));
                Assert.True(core.Frames.PresentCount > presents);
                Assert.Equal((width / scale, height / scale), (root.Width, root.Height));
            }
        });

    /// <summary>
    /// A <c>WM_SETTINGCHANGE</c> that changes the window's values reaches the renderer and the core: the text rendering, and, until
    /// the OS's settings become one object (row PL1, PR 5), less motion, the text fields' settings, and the OS's languages. The
    /// first values are the ones the window read when it was made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Readers set by the test, put back after it: the default text rendering, less motion, a caret 3 wide, and the languages
    ///   "ja-JP". A core that supports en and ja, mounted in a hidden window on WARP with a root function whose effect records the
    ///   locale.
    ///
    /// Steps:
    /// - Read the tree's motion and text fields' settings. Set the readers to a gamma of 2.2, no less motion, a caret 4 wide, and
    ///   "en-US"; send <c>WM_SETTINGCHANGE</c>.
    ///
    /// Expected:
    /// - Before: less motion and the caret 3 wide. After: the renderer and the frames draw text with the gamma of 2.2, no less
    ///   motion, the caret 4 wide, and the effect recorded ja, then en.
    /// </remarks>
    [Fact]
    public void SettingChange_ReachesTheRendererAndTheCore() =>
        ui.Invoke(() =>
        {
            Func<HMONITOR, TextRenderingParameters> previousRendering = Win32Window.TextRenderingReader;
            Func<bool, bool> previousMotion = Win32Window.ReducesMotionReader;
            Func<TextEditingSettings, TextEditingSettings> previousEditing = Win32Window.TextEditingReader;
            Func<string[]> previousLanguages = Win32Window.UiLanguagesReader;
            try
            {
                TextRenderingParameters rendering = TextRenderingParameters.Default;
                bool reduces = true;
                TextEditingSettings editing = TextEditingSettings.Default with { CaretWidth = 3 };
                string[] languages = ["ja-JP"];
                Win32Window.TextRenderingReader = _ => rendering;
                Win32Window.ReducesMotionReader = _ => reduces;
                Win32Window.TextEditingReader = _ => editing;
                Win32Window.UiLanguagesReader = () => languages;
                var seen = new List<string>();
                (WindowCore core, Win32PlatformWindow window) = Open(
                    () =>
                    {
                        IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                        _ = Effect.Create(() => seen.Add(locale.Get().ToLanguageTag()));
                        return Elements.Box();
                    },
                    [Locale.Parse("en"), Locale.Parse("ja")]
                );
                using (core)
                {
                    bool reducedBefore = core.Tree.Animations.ReduceMotion;
                    TextEditingSettings editingBefore = core.Tree.TextEditing;
                    rendering = TextRenderingParameters.Default with { Gamma = 2.2f };
                    reduces = false;
                    editing = TextEditingSettings.Default with { CaretWidth = 4 };
                    languages = ["en-US"];

                    _ = SendMessageW(window.Window.Hwnd, WM.WM_SETTINGCHANGE, default, default);

                    Assert.True(reducedBefore);
                    Assert.Equal(3f, editingBefore.CaretWidth);
                    Assert.Equal(rendering, window.Renderer.TextRendering);
                    Assert.Equal(rendering, core.Frames.TextRendering);
                    Assert.False(core.Tree.Animations.ReduceMotion);
                    Assert.Equal(4f, core.Tree.TextEditing.CaretWidth);
                    Assert.Equal(["ja", "en"], seen);
                }
            }
            finally
            {
                Win32Window.TextRenderingReader = previousRendering;
                Win32Window.ReducesMotionReader = previousMotion;
                Win32Window.TextEditingReader = previousEditing;
                Win32Window.UiLanguagesReader = previousLanguages;
            }
        });

    /// <summary>
    /// Disposing the core disposes its window in the host's order (design §6.0, "What the host owns"): the notices are taken back,
    /// the tree loses the input method before the window is destroyed, the provider is taken out, and the window is destroyed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core mounted in a hidden window on WARP, with a box.
    ///
    /// Steps:
    /// - Dispose the core.
    ///
    /// Expected:
    /// - The window has no synchronous render, no input handlers, no drops, no provider, and is not alive; the tree has no input
    ///   method.
    /// </remarks>
    [Fact]
    public void Dispose_TakesTheNoticesBack_AndDestroysTheWindow() =>
        ui.Invoke(() =>
        {
            (WindowCore core, Win32PlatformWindow window) = Open(Elements.Box);

            core.Dispose();

            Win32Window native = window.Window;
            Assert.Null(native.SynchronousRender);
            Assert.Null(native.PointerInputReceived);
            Assert.Null(native.KeyInputReceived);
            Assert.Null(native.TextInputReceived);
            Assert.Null(native.ActivationChanged);
            Assert.Null(native.VisibilityChanged);
            Assert.Null(native.ExternalDrops);
            Assert.Null(native.Accessibility);
            Assert.False(native.IsAlive);
            Assert.Null(core.Tree.TextInput);
        });

    // Opens a core's window as XylemApp does (design §6.0, "The order of creation", steps 4 to 7): hidden, on WARP, with a tree that
    // holds no text. The core owns the window and the atlas; the caller disposes it.
    private static (WindowCore Core, Win32PlatformWindow Window) Open(Func<Element> root, IReadOnlyList<Locale>? supported = null)
    {
        var core = new WindowCore(null, null, supported, null);
        try
        {
            var window = new Win32PlatformWindow(core, "host window", 200, 100, show: false, useWarp: true);
            var atlas = new GlyphAtlas(static () => new DirectWriteGlyphRasterizer(), threads: 1);
            core.Connect(window, atlas, Color.FromRgb(0xFFFFFF));
            window.Attach();
            (int width, int height) = window.ClientSize;
            float scale = window.ScaleFactor;
            core.Mount(root, window.UiLanguages, "Win32PlatformWindowTests", new Size(width / scale, height / scale), scale);
            window.RootMounted();
            return (core, window);
        }
        catch
        {
            core.Dispose();
            throw;
        }
    }
}
