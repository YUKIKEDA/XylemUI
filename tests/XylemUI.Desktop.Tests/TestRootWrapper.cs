using System.Runtime.CompilerServices;

namespace XylemUI.Desktop.Tests;

// The process's root wrapper of the desktop tests, set in a module initializer as the source generator sets an app's (design
// §13.2, "根で置くもの"), so that it is set before any test opens a window, whatever order the tests run in. It runs each window's
// root function as it is, unless a test gives it a wrapper of its own (Inner) for the windows it opens.
internal static class TestRootWrapper
{
    // The wrapper the windows opened now go through; null for the root function alone. The windows open on the UI thread.
    public static Func<Func<Element>, Element>? Inner { get; set; }

    [ModuleInitializer]
    public static void Register() => WindowRoot.SetWrapper(Wrap);

    public static Element Wrap(Func<Element> root) => Inner is { } inner ? inner(root) : root();
}
