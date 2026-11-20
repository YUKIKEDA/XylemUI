using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// What a desktop window provides at its root for a theme (row TH1, design §13.2, "根で置くもの"): the process's root wrapper, run
// inside the locale, the OS's appearance, and the window's surface.
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
    /// - The process's wrapper is set to one that, for this test's root function alone, records what it finds and that it runs
    ///   the root function, and otherwise only runs the root function, so that the other tests' windows are as before. No other
    ///   test of this project sets it, since it cannot be put back.
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
            WindowRoot.SetWrapper(given =>
            {
                if (!ReferenceEquals(given, root))
                {
                    return given();
                }

                log.Add("wrap");
                locale = Localization.Locale.TryUse(out _);
                appearance = SystemAppearance.Current.Use().Get();
                surface = WindowSurface.Current.Use();
                Element made = given();
                log.Add("wrapped");
                return made;
            });

            using var window = new TestWindow(root, Options);

            string[] expected = ["wrap", "root", "wrapped"];
            Assert.Equal(expected, log);
            Assert.True(locale);
            Assert.Equal(new SystemAppearance(ColorScheme.Light, false, null, null), appearance);
            Assert.Same(window.Core.Surface, surface);
            Assert.Equal(window.Core.RootNode, window.Tree.Nodes.GetRef(box.Node).Parent);
        });
}
