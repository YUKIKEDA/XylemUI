using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The window tier of the Windows host (row PL1, design §6.0): the window paired with its renderer, whose swap chain is the loop's
// frame clock; the notices it binds to the window core; the synchronous resize; the setting messages, which read the app tier's
// settings again; what its disposal takes back; the request to close; and the app tier's check for a second window. The windows are
// hidden, on WARP, and no loop runs: the messages are sent, so they run before SendMessageW returns, and the test dispatches the one
// RequestClose posts.
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
            var settings = new Win32PlatformSettings();
            using var core = new WindowCore(null, settings, null, null, null);
            var window = new Win32PlatformWindow(core, settings, "clock", 200, 100, useWarp: true);
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
    /// A host given a function that wraps the renderer gives the core what the function made from the window's renderer, and the
    /// core's first frame and the frame of a size change draw and present through it (Issue #933, the seam of the end-to-end
    /// benchmark).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Windows host whose windows draw on WARP, given a function that wraps the renderer in a target that counts its draws and
    ///   presents and passes them on.
    ///
    /// Steps:
    /// - Open a hidden window for a core; connect, attach, and mount a box. Read the counts. Resize the window to 360 x 240 with
    ///   <c>SetWindowPos</c>; read the counts again.
    ///
    /// Expected:
    /// - The function was called once, with the window's renderer, and the window's target is what it made.
    /// - After the mount, one draw and one present; after the size change, two of each.
    /// </remarks>
    [Fact]
    public void OpenWindow_WithAWrapper_TheCoreDrawsAndPresentsThroughIt() =>
        ui.Invoke(() =>
        {
            var wrapped = new List<IRenderTarget>();
            CountingTarget? counting = null;
            using var app = new Win32PlatformApp
            {
                UseWarp = true,
                WrapTarget = target =>
                {
                    wrapped.Add(target);
                    return counting = new CountingTarget(target);
                },
            };
            using var core = new WindowCore(null, app.Settings, app.Clipboard, null, null);
            var window = (Win32PlatformWindow)app.OpenWindow(core, "wrapped", 200, 100);
            core.Connect(window, new GlyphAtlas(app.CreateRasterizer, threads: 1), Color.FromRgb(0xFFFFFF));
            window.Attach();
            (int width, int height) = window.ClientSize;
            float scale = window.ScaleFactor;
            core.Mount(Elements.Box, null, default, "Win32PlatformWindowTests", new Size(width / scale, height / scale), scale);
            (int Draws, int Presents) mounted = (counting!.Draws, counting.Presents);

            Assert.True(SetWindowPos(window.Window.Hwnd, HWND.NULL, 0, 0, 360, 240, ResizeFlags));

            IRenderTarget only = Assert.Single(wrapped);
            Assert.Same(window.Renderer, only);
            Assert.Same(counting, window.Target);
            Assert.Equal((1, 1), mounted);
            Assert.Equal((2, 2), (counting.Draws, counting.Presents));
        });

    /// <summary>
    /// While a window of the host is open, a second <c>OpenWindow</c> throws, naming row MW1; once the first is disposed, another
    /// opens (Issue #928). Every window the host opens reads the host's one settings object again on its setting messages (design
    /// §6.0, "OS settings").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Windows host whose windows draw on WARP, and two window cores.
    ///
    /// Steps:
    /// - Open a hidden window for the first core; read what its setting messages call; open one for the second. Dispose the first
    ///   window; open a window for the second core again, and read what its setting messages call.
    ///
    /// Expected:
    /// - The second open throws <see cref="InvalidOperationException"/> whose message names MW1.
    /// - The open after the disposal gives a window.
    /// - The setting messages of both windows read the host's settings again.
    /// </remarks>
    [Fact]
    public void OpenWindow_WhileOneIsOpen_ThrowsNamingMw1_AndOpensAfterItIsDisposed() =>
        ui.Invoke(() =>
        {
            using var app = new Win32PlatformApp { UseWarp = true };
            using var first = new WindowCore(null, app.Settings, app.Clipboard, null, null);
            using var second = new WindowCore(null, app.Settings, app.Clipboard, null, null);
            IPlatformWindow opened = app.OpenWindow(first, "first", 200, 100);
            object? firstReads = ((Win32PlatformWindow)opened).Window.SettingsMessageReceived?.Target;
            Exception? thrown = Record.Exception(() => app.OpenWindow(second, "second", 200, 100));
            opened.Dispose();

            IPlatformWindow again = app.OpenWindow(second, "second", 200, 100);
            object? againReads = ((Win32PlatformWindow)again).Window.SettingsMessageReceived?.Target;
            again.Dispose();

            InvalidOperationException invalid = Assert.IsType<InvalidOperationException>(thrown);
            Assert.Contains("MW1", invalid.Message, StringComparison.Ordinal);
            Assert.Same(app.Settings, firstReads);
            Assert.Same(app.Settings, againReads);
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
    /// A <c>WM_SETTINGCHANGE</c> to the window reaches the renderer and the core: the window's text rendering, and, through the app
    /// tier's settings it reads again, less motion, the text fields' settings, the OS's languages, and the 24-hour form (design §6.0,
    /// "OS settings"; §4.11, "The key of the 24-hour form"). The first values are the ones the settings read when they were made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Readers set by the test, put back after it: the default text rendering, less motion, a caret 3 wide, the languages
    ///   "ja-JP", and the 24-hour form. Settings made with them; a core that supports the tables of en and ja, mounted in a hidden
    ///   window on WARP with the settings and a root function whose effects record the locale and the 24-hour form.
    ///
    /// Steps:
    /// - Read the tree's motion and text fields' settings. Set the readers to a gamma of 2.2, no less motion, a caret 4 wide,
    ///   "en-US", and the 12-hour form; send <c>WM_SETTINGCHANGE</c>.
    ///
    /// Expected:
    /// - Before: less motion and the caret 3 wide. After: the renderer and the frames draw text with the gamma of 2.2, no less
    ///   motion, the caret 4 wide; the locale effect recorded ja, then en; the 24-hour effect recorded on, then off.
    /// </remarks>
    [Fact]
    public void SettingChange_ReachesTheRendererAndTheCore() =>
        ui.Invoke(() =>
        {
            Func<HMONITOR, TextRenderingParameters> previousRendering = Win32Window.TextRenderingReader;
            Func<bool, bool> previousMotion = Win32PlatformSettings.ReducesMotionReader;
            Func<TextEditingSettings, TextEditingSettings> previousEditing = Win32PlatformSettings.TextEditingReader;
            Func<string[]> previousLanguages = Win32PlatformSettings.UiLanguagesReader;
            Func<bool, bool> previousTimeFormat = Win32PlatformSettings.Use24HourFormatReader;
            try
            {
                TextRenderingParameters rendering = TextRenderingParameters.Default;
                bool reduces = true;
                TextEditingSettings editing = TextEditingSettings.Default with { CaretWidth = 3 };
                string[] languages = ["ja-JP"];
                bool use24HourForm = true;
                Win32Window.TextRenderingReader = _ => rendering;
                Win32PlatformSettings.ReducesMotionReader = _ => reduces;
                Win32PlatformSettings.TextEditingReader = _ => editing;
                Win32PlatformSettings.UiLanguagesReader = () => languages;
                Win32PlatformSettings.Use24HourFormatReader = _ => use24HourForm;
                var seen = new List<string>();
                var forms = new List<bool>();
                (WindowCore core, Win32PlatformWindow window) = Open(
                    () =>
                    {
                        IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                        IReadOnlySignal<bool> use24HourFormat = Localization.Use24HourFormat.Use();
                        _ = Effect.Create(() => seen.Add(locale.Get().ToLanguageTag()));
                        _ = Effect.Create(() => forms.Add(use24HourFormat.Get()));
                        return Elements.Box();
                    },
                    [CldrLocales.En, CldrLocales.Ja]
                );
                using (core)
                {
                    bool reducedBefore = core.Tree.Animations.ReduceMotion;
                    TextEditingSettings editingBefore = core.Tree.TextEditing;
                    rendering = TextRenderingParameters.Default with { Gamma = 2.2f };
                    reduces = false;
                    editing = TextEditingSettings.Default with { CaretWidth = 4 };
                    languages = ["en-US"];
                    use24HourForm = false;

                    _ = SendMessageW(window.Window.Hwnd, WM.WM_SETTINGCHANGE, default, default);

                    Assert.True(reducedBefore);
                    Assert.Equal(3f, editingBefore.CaretWidth);
                    Assert.Equal(rendering, window.Renderer.TextRendering);
                    Assert.Equal(rendering, core.Frames.TextRendering);
                    Assert.False(core.Tree.Animations.ReduceMotion);
                    Assert.Equal(4f, core.Tree.TextEditing.CaretWidth);
                    Assert.Equal(["ja", "en"], seen);
                    Assert.Equal([true, false], forms);
                }
            }
            finally
            {
                Win32Window.TextRenderingReader = previousRendering;
                Win32PlatformSettings.ReducesMotionReader = previousMotion;
                Win32PlatformSettings.TextEditingReader = previousEditing;
                Win32PlatformSettings.UiLanguagesReader = previousLanguages;
                Win32PlatformSettings.Use24HourFormatReader = previousTimeFormat;
            }
        });

    /// <summary>
    /// A setting that changes after the host is made and before its window opens is read when the window opens, and told once:
    /// a <c>WM_SETTINGCHANGE</c> in between reached no window of the host (design §6.0, "OS settings").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Readers set by the test, put back after it: no less motion, the default text fields' settings, the languages "ja-JP", and
    ///   the 12-hour form. A Windows host whose windows draw on WARP, made with them; a handler that records its settings' notices;
    ///   a window core, not connected.
    ///
    /// Steps:
    /// - Make the readers return "en-US" and the 24-hour form. Read the notices. Open a hidden window for the core; dispose it.
    ///
    /// Expected:
    /// - No notice before the window opens. After: one notice, marked with the languages and the 24-hour form; the settings hold
    ///   "en-US" and the 24-hour form.
    /// </remarks>
    [Fact]
    public void OpenWindow_ASettingChangedAfterTheHostWasMade_IsReadAndToldOnce() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previousMotion = Win32PlatformSettings.ReducesMotionReader;
            Func<TextEditingSettings, TextEditingSettings> previousEditing = Win32PlatformSettings.TextEditingReader;
            Func<string[]> previousLanguages = Win32PlatformSettings.UiLanguagesReader;
            Func<bool, bool> previousTimeFormat = Win32PlatformSettings.Use24HourFormatReader;
            try
            {
                string[] languages = ["ja-JP"];
                bool use24HourForm = false;
                Win32PlatformSettings.ReducesMotionReader = static _ => false;
                Win32PlatformSettings.TextEditingReader = static _ => TextEditingSettings.Default;
                Win32PlatformSettings.UiLanguagesReader = () => [.. languages];
                Win32PlatformSettings.Use24HourFormatReader = _ => use24HourForm;
                using var app = new Win32PlatformApp { UseWarp = true };
                var notices = new List<PlatformSettingsChanges>();
                app.Settings.Changed += notices.Add;
                using var core = new WindowCore(null, app.Settings, app.Clipboard, null, null);
                languages = ["en-US"];
                use24HourForm = true;
                int beforeOpen = notices.Count;

                IPlatformWindow window = app.OpenWindow(core, "late setting", 200, 100);
                window.Dispose();

                Assert.Equal(0, beforeOpen);
                Assert.Equal([PlatformSettingsChanges.UiLanguages | PlatformSettingsChanges.Use24HourFormat], notices);
                Assert.Equal(["en-US"], app.Settings.UiLanguages);
                Assert.True(app.Settings.Use24HourFormat);
            }
            finally
            {
                Win32PlatformSettings.ReducesMotionReader = previousMotion;
                Win32PlatformSettings.TextEditingReader = previousEditing;
                Win32PlatformSettings.UiLanguagesReader = previousLanguages;
                Win32PlatformSettings.Use24HourFormatReader = previousTimeFormat;
            }
        });

    /// <summary>
    /// <c>RequestClose</c> asks the window to close as its close button does (design §6.0, the window tier): once the request is
    /// handled, the window is destroyed, and the core disposes the root and raises its <c>Closed</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core mounted in a hidden window on WARP, with a box; a handler that counts the core's <c>Closed</c>. No loop runs.
    ///
    /// Steps:
    /// - Call <c>RequestClose</c>; read whether the window is alive and the count. Dispatch the messages posted to the window.
    ///
    /// Expected:
    /// - Before the messages are dispatched, the window is alive and the core has not closed: the request is posted.
    /// - After, the window is not alive, the core raised <c>Closed</c> once, and it has no root scope.
    /// </remarks>
    [Fact]
    public void RequestClose_ClosesTheWindowWhenTheRequestIsHandled() =>
        ui.Invoke(() =>
        {
            (WindowCore core, Win32PlatformWindow window) = Open(Elements.Box);
            using (core)
            {
                int closed = 0;
                core.Closed += (_, _) => closed++;

                window.RequestClose();
                bool aliveBefore = window.Window.IsAlive;
                int closedBefore = closed;
                Pump(window.Window);

                Assert.True(aliveBefore);
                Assert.Equal(0, closedBefore);
                Assert.False(window.Window.IsAlive);
                Assert.Equal(1, closed);
                Assert.True(core.RootScope.IsNone);
            }
        });

    /// <summary>
    /// Disposing the core disposes its window in the host's order (design §6.0, "What the host owns"): the notices are taken back,
    /// the tree loses the input method and the provider is taken out before the window is destroyed, and the loop waits on the
    /// swap chain until the window is destroyed and on its timer after.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core mounted in a hidden window on WARP, with a box; a handler of the test on the Win32 window's <c>Closed</c>, raised
    ///   by <c>WM_DESTROY</c> while the window is destroyed, records the tree's input method, the window's provider, and the
    ///   handle the loop waits on.
    ///
    /// Steps:
    /// - Dispose the core.
    ///
    /// Expected:
    /// - Before the disposal, the tree has the window's input method and the window its provider.
    /// - The handler ran once; when it ran, the tree had no input method, the window had no provider, and the loop still waited on
    ///   the renderer's frame latency waitable object.
    /// - After: the window has no synchronous render, no reader of its setting messages, no input handlers, no drops, no provider,
    ///   and is not alive; the tree has no input method; the loop waits on another handle.
    /// </remarks>
    [Fact]
    public void Dispose_TakesTheNoticesBack_AndDestroysTheWindowAfterTheInputMethodAndTheProvider() =>
        ui.Invoke(() =>
        {
            (WindowCore core, Win32PlatformWindow window) = Open(Elements.Box);
            Win32Window native = window.Window;
            HANDLE clock = (HANDLE)window.Renderer.FrameLatencyWaitableObject;
            int destroyed = 0;
            ITextInputPlatform? inputMethodAtDestroy = null;
            Win32Accessibility? providerAtDestroy = null;
            HANDLE waitedAtDestroy = default;
            native.Closed += (_, _) =>
            {
                destroyed++;
                inputMethodAtDestroy = core.Tree.TextInput;
                providerAtDestroy = native.Accessibility;
                waitedAtDestroy = Win32MessageLoop.Frames.WaitHandle;
            };

            bool hadBoth = core.Tree.TextInput is not null && native.Accessibility is not null;

            core.Dispose();

            Assert.True(hadBoth);
            Assert.Equal(1, destroyed);
            Assert.Null(inputMethodAtDestroy);
            Assert.Null(providerAtDestroy);
            Assert.Equal(clock, waitedAtDestroy);
            Assert.NotEqual(clock, Win32MessageLoop.Frames.WaitHandle);
            Assert.Null(native.SynchronousRender);
            Assert.Null(native.SettingsMessageReceived);
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

    /// <summary>
    /// <c>SetDarkTitleBar</c> sets the window's <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> (design §13.2, "題名の棒"), which DWM gives
    /// back as it was set.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A core mounted in a hidden window on WARP, with a box.
    ///
    /// Steps:
    /// - Make the title bar dark and read the attribute with <c>DwmGetWindowAttribute</c>; make it light and read it again.
    ///
    /// Expected:
    /// - Both reads succeed and give TRUE, then FALSE.
    /// </remarks>
    [Fact]
    public void SetDarkTitleBar_SetsTheImmersiveDarkModeAttribute() =>
        ui.Invoke(() =>
        {
            (WindowCore core, Win32PlatformWindow window) = Open(Elements.Box);
            using (core)
            {
                window.SetDarkTitleBar(true);
                (int Result, bool Dark) afterDark = ReadDarkTitleBar(window.Window);
                window.SetDarkTitleBar(false);
                (int Result, bool Dark) afterLight = ReadDarkTitleBar(window.Window);

                Assert.Equal((0, true), afterDark);
                Assert.Equal((0, false), afterLight);
            }
        });

    /// <summary>
    /// A change of Windows' appearance reaches the window's paint through the whole host (design §13.2): the message reads the
    /// settings again, the core gives the tree the appearance, the root wrapper's effect tells the window's surface, and the host
    /// paints the backdrop behind the tree and sets the title bar; the window's creation paints the values of that time.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An appearance reader set by the test, put back after it, that returns light. A core mounted in a hidden window on WARP with
    ///   a wrapper whose effect tells the surface #292929 and a dark title bar when the OS's appearance is dark, and #FAFAFA and a
    ///   light one when it is light.
    ///
    /// Steps:
    /// - Read the frames' color and the title bar's attribute. Make the reader return dark, send
    ///   <c>WM_DWMCOLORIZATIONCOLORCHANGED</c>, and read them again.
    ///
    /// Expected:
    /// - At the creation: #FAFAFA and FALSE. After the message: #292929 and TRUE.
    /// </remarks>
    [Fact]
    public void AppearanceMessage_ReachesTheBackdropAndTheTitleBar() =>
        ui.Invoke(() =>
        {
            Func<SystemAppearance, SystemAppearance> previous = Win32PlatformSettings.AppearanceReader;
            try
            {
                SystemAppearance current = default;
                Win32PlatformSettings.AppearanceReader = _ => current;
                Color darkBackdrop = Color.FromRgb(0x292929);
                Color lightBackdrop = Color.FromRgb(0xFAFAFA);
                (WindowCore core, Win32PlatformWindow window) = Open(Elements.Box, wrapper: Wrap);
                using (core)
                {
                    (Color Backdrop, (int Result, bool Dark) Bar) created = (core.Frames.ClearColor, ReadDarkTitleBar(window.Window));
                    current = new SystemAppearance(ColorScheme.Dark, false, null, null);

                    _ = SendMessageW(window.Window.Hwnd, WM.WM_DWMCOLORIZATIONCOLORCHANGED, default, default);

                    Assert.Equal((lightBackdrop, (0, false)), created);
                    Assert.Equal((darkBackdrop, (0, true)), (core.Frames.ClearColor, ReadDarkTitleBar(window.Window)));
                }

                Element Wrap(Func<Element> root)
                {
                    IReadOnlySignal<SystemAppearance> appearance = SystemAppearance.Current.Use();
                    WindowSurface surface = WindowSurface.Current.Use();
                    _ = Effect.Create(() =>
                    {
                        bool dark = appearance.Get().Scheme == ColorScheme.Dark;
                        surface.SetBackdrop(dark ? darkBackdrop : lightBackdrop);
                        surface.SetDarkTitleBar(dark);
                    });
                    return root();
                }
            }
            finally
            {
                Win32PlatformSettings.AppearanceReader = previous;
            }
        });

    // Reads the window's DWMWA_USE_IMMERSIVE_DARK_MODE back: the HRESULT, and whether it is TRUE.
    private static unsafe (int Result, bool Dark) ReadDarkTitleBar(Win32Window window)
    {
        BOOL value;
        int result = DwmGetWindowAttribute(window.Hwnd, (uint)DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &value, (uint)sizeof(BOOL));
        return (result, value != BOOL.FALSE);
    }

    // Opens a core's window as XylemApp does (design §6.0, "The order of creation", steps 4 to 7): hidden, on WARP, with a tree that
    // holds no text, settings of its own, read now, and their appearance; the root function in the wrapper given, if any. The core
    // owns the window and the atlas; the caller disposes it.
    private static (WindowCore Core, Win32PlatformWindow Window) Open(
        Func<Element> root,
        IReadOnlyList<LocaleData>? supported = null,
        Func<Func<Element>, Element>? wrapper = null
    )
    {
        var settings = new Win32PlatformSettings();
        var core = new WindowCore(null, settings, null, supported, null);
        try
        {
            var window = new Win32PlatformWindow(core, settings, "host window", 200, 100, useWarp: true);
            GlyphAtlas atlas;
            try
            {
                atlas = new GlyphAtlas(static () => new DirectWriteGlyphRasterizer(), threads: 1);
            }
            catch
            {
                // Not yet the core's: it owns the window from Connect on, as in XylemApp.OpenWindow.
                window.Dispose();
                throw;
            }

            core.Connect(window, atlas, Color.FromRgb(0xFFFFFF));
            window.Attach();
            (int width, int height) = window.ClientSize;
            float scale = window.ScaleFactor;
            var size = new Size(width / scale, height / scale);
            core.Mount(root, wrapper, settings.Appearance, "Win32PlatformWindowTests", size, scale);
            window.RootMounted();
            return (core, window);
        }
        catch
        {
            core.Dispose();
            throw;
        }
    }

    // Counts the draws and presents it passes on to the renderer.
    private sealed class CountingTarget(IRenderTarget inner) : IRenderTarget
    {
        public int Draws { get; private set; }

        public int Presents { get; private set; }

        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas, TextureCache images)
        {
            Draws++;
            inner.Draw(lists, clearColor, atlas, images);
        }

        public void Present()
        {
            Presents++;
            inner.Present();
        }
    }

    // Dispatches the messages posted to the window, such as the WM_CLOSE that RequestClose posts: no loop runs in these tests.
    private static unsafe void Pump(Win32Window window)
    {
        MSG message;
        while (window.IsAlive && PeekMessageW(&message, window.Hwnd, 0, 0, PM.PM_REMOVE) != BOOL.FALSE)
        {
            _ = DispatchMessageW(&message);
        }
    }
}
