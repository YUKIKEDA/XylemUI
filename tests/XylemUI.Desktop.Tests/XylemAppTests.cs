using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// What a desktop window provides at its root for a theme (row TH1, design §13.2, "根で置くもの" and "包みは 1 つ"): the process's
// root wrapper, run inside the locale, the OS's appearance, and the window's surface, and read by the window, after which no other
// wrapper is taken. The process's wrapper is TestRootWrapper's, set by a module initializer before any test runs. And the host
// XylemApp.Run makes: the one the module initializer registered, or none (row PL1, design §6.0, "ホストの選び方"). And when the window
// shows: after its first frame (Issue #907).
[Collection(UiThreadGroup.Name)]
public sealed class XylemAppTests(UiThreadFixture ui)
{
    private static readonly WindowOptions Options = new("Root wrapper test", 320, 200);

    /// <summary>
    /// The module initializer that the source generator writes for this test executable registers the Windows host, which this
    /// executable references: the host <c>XylemApp.Run</c> makes is <see cref="Win32PlatformApp"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test executable references <c>XylemUI.Platform.Windows</c>, whose <c>[assembly: PlatformHost]</c> names
    ///   <see cref="Win32Host"/>. The samples it references (Counter, Todo) are apps whose module initializers register the host
    ///   too, so the test runs theirs first, sets <see cref="PlatformHostRegistry.Factory"/> to <see langword="null"/>, and then
    ///   calls this executable's own generated module initializer, <c>XylemUI_Desktop_TestsActions.RegisterAll</c>, again: a
    ///   module constructor runs once per module, and the initializer's other calls (the action tables, the default key bindings)
    ///   take the same values twice. The registered factory is put back after.
    ///
    /// Steps:
    /// - Call the initializer; read <see cref="PlatformHostRegistry.Factory"/>; on the UI thread, make a host with it and dispose
    ///   it.
    ///
    /// Expected:
    /// - The factory is set, and the host it makes is a <see cref="Win32PlatformApp"/>. That this initializer calls the real
    ///   <see cref="Win32Host.Register"/> also checks the generator against the real host's shape, which the generator's tests
    ///   cannot reference.
    /// </remarks>
    [Fact]
    public void ModuleInitializer_RegistersTheWindowsHost() =>
        ui.Invoke(() =>
        {
            RuntimeHelpers.RunModuleConstructor(typeof(global::Counter.CounterApp).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(global::Todo.TodoApp).Module.ModuleHandle);
            Func<IPlatformApp>? registered = PlatformHostRegistry.Factory;
            PlatformHostRegistry.Factory = null;
            Func<IPlatformApp>? factory;
            try
            {
                global::XylemUI.Generated.XylemUI_Desktop_TestsActions.RegisterAll();
                factory = PlatformHostRegistry.Factory;
            }
            finally
            {
                PlatformHostRegistry.Factory = registered;
            }

            Assert.NotNull(factory);
            using IPlatformApp app = factory();
            Assert.IsType<Win32PlatformApp>(app);
        });

    /// <summary>
    /// With no host registered, <c>XylemApp.Run</c> throws <see cref="InvalidOperationException"/> whose message names the
    /// runtime identifier, and opens no window, changes no dispatcher, and leaves no loop counted as running (design §6.0,
    /// "ホストの選び方").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The module initializers of this executable and of the samples it references have run (the samples' are run here first,
    ///   so that one run later cannot register the host again while the test runs). The test sets
    ///   <see cref="PlatformHostRegistry.Factory"/> to <see langword="null"/> and puts the registered one back after.
    ///
    /// Steps:
    /// - Run the app with a root function that counts its runs; then run it again, still with no host.
    ///
    /// Expected:
    /// - The first run throws <see cref="InvalidOperationException"/> whose message contains
    ///   <see cref="RuntimeInformation.RuntimeIdentifier"/> and "No platform host is registered". The second throws the same
    ///   message, not "it already runs": the first left no loop counted as running. The root function did not run, and
    ///   <see cref="UiDispatcher.Current"/> is the one before the runs.
    /// </remarks>
    [Fact]
    public void Run_WithNoHostRegistered_ThrowsNamingTheRuntimeIdentifier() =>
        ui.Invoke(() =>
        {
            RuntimeHelpers.RunModuleConstructor(typeof(global::Counter.CounterApp).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(global::Todo.TodoApp).Module.ModuleHandle);
            IUiDispatcher before = UiDispatcher.Current;
            int runs = 0;
            Func<IPlatformApp>? registered = PlatformHostRegistry.Factory;
            PlatformHostRegistry.Factory = null;
            Func<Element> root = () =>
            {
                runs++;
                return Box();
            };
            var options = new WindowOptions("No host test", 100, 80);
            InvalidOperationException thrown;
            InvalidOperationException again;
            try
            {
                thrown = Assert.Throws<InvalidOperationException>(() => XylemApp.Run(root, options));
                again = Assert.Throws<InvalidOperationException>(() => XylemApp.Run(root, options));
            }
            finally
            {
                PlatformHostRegistry.Factory = registered;
            }

            Assert.Contains(RuntimeInformation.RuntimeIdentifier, thrown.Message, StringComparison.Ordinal);
            Assert.Contains("No platform host is registered", thrown.Message, StringComparison.Ordinal);
            Assert.Contains("No platform host is registered", again.Message, StringComparison.Ordinal);
            Assert.Equal(0, runs);
            Assert.Same(before, UiDispatcher.Current);
        });

    /// <summary>
    /// A desktop window runs its root function in the process's root wrapper (<see cref="WindowRoot.SetWrapper"/>), which runs
    /// inside the locale, the OS's appearance the host's settings read, and the window's surface (design §13.2, "OS の見た目").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The process's wrapper is <c>TestRootWrapper.Wrap</c>, set by the test assembly's module initializer; it runs the root
    ///   function as it is unless a test gives it a wrapper of its own. This test gives one that records what it finds and that it
    ///   runs the root function, and takes it back after the window opened.
    /// - An appearance reader set by the test, put back after it, that returns dark with the accent #0078D4.
    ///
    /// Steps:
    /// - Open a hidden window whose root function records what it finds and returns a box.
    ///
    /// Expected:
    /// - The log is "wrap", "root", "wrapped". The wrapper found a locale, the appearance the reader returned, and the window's own
    ///   surface: the same object as its core's. The box is the window's node.
    /// </remarks>
    [Fact]
    public void OpenWindow_TheProcessWrapper_RunsInsideTheRootValues() =>
        ui.Invoke(() =>
        {
            var log = new List<string>();
            SystemAppearance? appearance = null;
            WindowSurface? surface = null;
            bool locale = false;
            Element box = default;
            Func<Element> root = () =>
            {
                log.Add("root");
                return box = Box();
            };
            TestRootWrapper.Inner = given =>
            {
                log.Add("wrap");
                locale = Localization.Locale.TryUse(out _);
                appearance = SystemAppearance.Current.Use().Get();
                surface = WindowSurface.Current.Use();
                Element made = given();
                log.Add("wrapped");
                return made;
            };
            var dark = new SystemAppearance(ColorScheme.Dark, false, Color.FromRgb(0x0078D4), null);
            Func<SystemAppearance, SystemAppearance> previousReader = Win32PlatformSettings.AppearanceReader;
            Win32PlatformSettings.AppearanceReader = _ => dark;
            TestWindow window;
            try
            {
                window = new TestWindow(root, Options);
            }
            finally
            {
                TestRootWrapper.Inner = null;
                Win32PlatformSettings.AppearanceReader = previousReader;
            }

            using (window)
            {
                string[] expected = ["wrap", "root", "wrapped"];
                Assert.Equal(expected, log);
                Assert.True(locale);
                Assert.Equal(dark, appearance);
                Assert.Same(window.Core.Surface, surface);
                Assert.Equal(window.Core.RootNode, window.Tree.Nodes.GetRef(box.Node).Parent);
            }
        });

    /// <summary>
    /// A desktop window marks the process's wrapper read; after it, the same method set again does nothing, and another one is
    /// refused with a message that says the wrapper is set before the first window opens (design §13.2, "包みは 1 つ").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The process's wrapper is <c>TestRootWrapper.Wrap</c>, set by the module initializer before any test, so whether another
    ///   test has opened a window first does not matter: this test opens one itself.
    ///
    /// Steps:
    /// - Open a hidden window of a box and close it. Set a new delegate of <c>TestRootWrapper.Wrap</c>. Set a delegate of another
    ///   method.
    ///
    /// Expected:
    /// - The wrapper is marked read. The same method throws nothing; the other one throws <see cref="InvalidOperationException"/>
    ///   whose message says the wrapper is set before the first window opens. A window opened after both still goes through
    ///   <c>TestRootWrapper.Wrap</c>.
    /// </remarks>
    [Fact]
    public void SetWrapper_AfterAWindowRead_TakesOnlyTheSameMethod() =>
        ui.Invoke(() =>
        {
            using (new TestWindow(Box, Options)) { }

            bool read = WindowRoot.IsRead;
            WindowRoot.SetWrapper(new Func<Func<Element>, Element>(TestRootWrapper.Wrap));
            InvalidOperationException other = Assert.Throws<InvalidOperationException>(() => WindowRoot.SetWrapper(static root => root()));
            int wraps = 0;
            TestRootWrapper.Inner = given =>
            {
                wraps++;
                return given();
            };
            try
            {
                using (new TestWindow(Box, Options)) { }
            }
            finally
            {
                TestRootWrapper.Inner = null;
            }

            Assert.True(read);
            Assert.Contains("before the first window opens", other.Message, StringComparison.Ordinal);
            Assert.Equal(1, wraps);
        });

    /// <summary>
    /// <c>XylemApp.OpenWindow</c>, as <c>XylemApp.Run</c> calls it, opens the window hidden and shows it only after its first frame
    /// was presented, with the title bar and the backdrop the root wrapper told already painted (design §6.0, "The order of
    /// creation"; Issue #907). Each frame is drawn at the client size and the scale the window has at that frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The process's wrapper is given a wrapper of the test that tells the window's surface #292929 and a dark title bar, and is
    ///   taken back after the window opened.
    /// - A host of the tests on WARP whose renderer is wrapped in a target that records, at each draw, whether the window is visible
    ///   (<c>IsWindowVisible</c>) and whether it clears with #292929, and checks that the renderer's size is the client rectangle's
    ///   and that the list's scale is the window's DPI over 96; and at each present, whether the window is visible and whether its
    ///   <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> is set. The host's message hook records the first message the window gets while it is
    ///   visible, with the attribute then. The host gives the core its window wrapped in one that records the calls of
    ///   <c>RootMounted</c> and <c>Show</c> before it passes them on.
    ///
    /// Steps:
    /// - Open the window of a box as <c>XylemApp.Run</c> does: shown after its first frame.
    ///
    /// Expected:
    /// - The first five records are the first frame's draw, hidden and with #292929; its present, hidden and with the dark title
    ///   bar; <c>RootMounted</c>; <c>Show</c>; and then the window's first visible message, with the dark title bar. After the open
    ///   the window is visible.
    /// - Every draw found the renderer at the client size and the list at the window's scale, and the root box has the client size
    ///   divided by the scale.
    /// - A show that also changes the window's size (the first <c>ShowWindow</c> of a process takes the show command its starter
    ///   gave) or activates it may draw more frames after these; the test does not count them, and checks each one.
    /// </remarks>
    [Fact]
    public void OpenWindow_ShowsTheWindowOnlyAfterItsFirstFrame_WithTheTitleBarAndTheBackdropPainted() =>
        ui.Invoke(() =>
        {
            Color backdrop = Color.FromRgb(0x292929);
            var log = new List<string>();
            var mismatches = new List<string>();

            // The target reads the host's window, which the host keeps once the window's constructor, which wraps the renderer,
            // has returned: before the core connects and draws.
            var host = new StrongBox<TestHost>();
            bool seenVisible = false;
            host.Value = new TestHost(
                useWarp: true,
                wrapTarget: inner => new WatchingTarget(inner, () => host.Value!.Window!, backdrop, log, mismatches),
                messageHook: (native, _) =>
                {
                    if (!seenVisible && IsWindowVisible(native.Hwnd) != BOOL.FALSE)
                    {
                        seenVisible = true;
                        log.Add("visible " + TitleBar(native.Hwnd));
                    }

                    return false;
                }
            )
            {
                WrapWindow = inner => new RecordingWindow(inner, log),
            };
            TestRootWrapper.Inner = given =>
            {
                WindowSurface surface = WindowSurface.Current.Use();
                surface.SetBackdrop(backdrop);
                surface.SetDarkTitleBar(true);
                return given();
            };
            TestWindow window;
            try
            {
                window = new TestWindow(Box, Options, show: true, host: host.Value);
            }
            finally
            {
                TestRootWrapper.Inner = null;
            }

            using (window)
            {
                bool visible = IsWindowVisible(window.Window.Hwnd) != BOOL.FALSE;
                (int width, int height) = window.Window.ClientSize;
                float scale = window.Window.ScaleFactor;
                Rect root = window.Tree.Nodes.GetRef(window.Core.RootNode).Layout.Rect;

                string[] expected = ["draw hidden backdrop", "present hidden dark", "root mounted", "show", "visible dark"];
                Assert.Equal(expected, log.Take(5));
                Assert.True(visible);
                Assert.Empty(mismatches);
                Assert.Equal((width / scale, height / scale), (root.Width, root.Height));
            }
        });

    // Whether the window's DWMWA_USE_IMMERSIVE_DARK_MODE is set, as "dark" or "light"; "unread" when DWM gives it back with an error.
    private static unsafe string TitleBar(HWND hwnd)
    {
        BOOL value;
        int result = DwmGetWindowAttribute(hwnd, (uint)DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &value, (uint)sizeof(BOOL));
        return result != 0 ? "unread"
            : value != BOOL.FALSE ? "dark"
            : "light";
    }

    private static string VisibilityOf(HWND hwnd) => IsWindowVisible(hwnd) != BOOL.FALSE ? "visible" : "hidden";

    // Passes the draws and presents on to the renderer, and records the window's state at each against the OS's: whether the window
    // is visible, the color the frame clears with, the title bar, and whether the frame's size and scale are the window's then.
    private sealed unsafe class WatchingTarget(
        IRenderTarget inner,
        Func<Win32PlatformWindow> window,
        Color backdrop,
        List<string> log,
        List<string> mismatches
    ) : IRenderTarget
    {
        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas, TextureCache images)
        {
            Win32PlatformWindow host = window();
            HWND hwnd = host.Window.Hwnd;
            log.Add("draw " + VisibilityOf(hwnd) + (clearColor == backdrop ? " backdrop" : " other"));
            RECT client;
            _ = GetClientRect(hwnd, &client);
            (int Width, int Height) clientSize = (client.right - client.left, client.bottom - client.top);
            if (clientSize != (host.Renderer.Width, host.Renderer.Height))
            {
                mismatches.Add($"renderer {host.Renderer.Width} x {host.Renderer.Height}, client {clientSize.Width} x {clientSize.Height}");
            }

            float scale = GetDpiForWindow(hwnd) / 96f;
            if (lists[0].Scale != scale)
            {
                mismatches.Add($"list scale {lists[0].Scale}, window's {scale}");
            }

            inner.Draw(lists, clearColor, atlas, images);
        }

        public void Present()
        {
            HWND hwnd = window().Window.Hwnd;
            log.Add("present " + VisibilityOf(hwnd) + " " + TitleBar(hwnd));
            inner.Present();
        }
    }

    // Passes every member on to the host's window, and records the two calls the order of creation puts after the first frame,
    // before it passes them on.
    private sealed class RecordingWindow(IPlatformWindow inner, List<string> log) : IPlatformWindow
    {
        public (int Width, int Height) ClientSize => inner.ClientSize;

        public float ScaleFactor => inner.ScaleFactor;

        public bool IsShowing => inner.IsShowing;

        public bool IsActive => inner.IsActive;

        public TextRenderingParameters TextRendering => inner.TextRendering;

        public IRenderTarget Target => inner.Target;

        public IFrameScheduler FrameScheduler => inner.FrameScheduler;

        public IPointerPlatform Pointer => inner.Pointer;

        public ITextInputPlatform? TextInput => inner.TextInput;

        public void Attach() => inner.Attach();

        public void RootMounted()
        {
            log.Add("root mounted");
            inner.RootMounted();
        }

        public void Show()
        {
            log.Add("show");
            inner.Show();
        }

        public void RequestClose() => inner.RequestClose();

        public void SetDarkTitleBar(bool dark) => inner.SetDarkTitleBar(dark);

        public void Dispose() => inner.Dispose();
    }
}
