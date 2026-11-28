using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XylemUI.Platform.Windows;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// What a desktop window provides at its root for a theme (row TH1, design §13.2, "根で置くもの" and "包みは 1 つ"): the process's
// root wrapper, run inside the locale, the OS's appearance, and the window's surface, and read by the window, after which no other
// wrapper is taken. The process's wrapper is TestRootWrapper's, set by a module initializer before any test runs. And the host
// XylemApp.Run makes: the one the module initializer registered, or none (row PL1, design §6.0, "ホストの選び方").
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
}
