using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// What a desktop window provides at its root for a theme (row TH1, design §13.2, "根で置くもの" and "包みは 1 つ"): the process's
// root wrapper, run inside the locale, the OS's appearance, and the window's surface, and read by the window, after which no other
// wrapper is taken. The process's wrapper is TestRootWrapper's, set by a module initializer before any test runs.
[Collection(UiThreadGroup.Name)]
public sealed class XylemAppTests(UiThreadFixture ui)
{
    private static readonly WindowOptions Options = new("Root wrapper test", 320, 200);

    /// <summary>
    /// A desktop window runs its root function in the process's root wrapper (<see cref="WindowRoot.SetWrapper"/>), which runs
    /// inside the locale, the OS's appearance, and the window's surface; until row TH1's fifth pull request reads Windows, the
    /// appearance is the one of a host that does not read the OS.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The process's wrapper is <c>TestRootWrapper.Wrap</c>, set by the test assembly's module initializer; it runs the root
    ///   function as it is unless a test gives it a wrapper of its own. This test gives one that records what it finds and that it
    ///   runs the root function, and takes it back after the window opened.
    ///
    /// Steps:
    /// - Open a hidden window whose root function records what it finds and returns a box.
    ///
    /// Expected:
    /// - The log is "wrap", "root", "wrapped". The wrapper found a locale, the default appearance (light, high contrast off, no
    ///   accent color), and the window's own surface: the same object as its core's. The box is the window's node.
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
            TestWindow window;
            try
            {
                window = new TestWindow(root, Options);
            }
            finally
            {
                TestRootWrapper.Inner = null;
            }

            using (window)
            {
                string[] expected = ["wrap", "root", "wrapped"];
                Assert.Equal(expected, log);
                Assert.True(locale);
                Assert.Equal(new SystemAppearance(ColorScheme.Light, false, null, null), appearance);
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
