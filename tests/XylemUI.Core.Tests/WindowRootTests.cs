namespace XylemUI.Tests;

// The root wrapper of a process's windows (row TH1, design §13.2, "根で置くもの"): set once in a process.
public sealed class WindowRootTests
{
    /// <summary>
    /// The wrapper is set once in a process: the first call sets it, a second throws and leaves the first, and a null wrapper is
    /// refused without being set.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No wrapper is set in the test process, and no other test of this project sets one. The process's wrapper cannot be put
    ///   back, so the first call and the second are checked in one test: two tests would depend on the order they run in (PR #882's
    ///   review).
    ///
    /// Steps:
    /// - Set a null wrapper. Set a wrapper that returns its root's element. Set a second wrapper.
    ///
    /// Expected:
    /// - The null wrapper throws <see cref="ArgumentNullException"/>, and no wrapper is set after it. The first wrapper is set. The
    ///   second throws <see cref="InvalidOperationException"/>, and the first is still the one set.
    /// </remarks>
    [Fact]
    public void SetWrapper_TheSecondTime_ThrowsAndKeepsTheFirst()
    {
        Assert.Null(WindowRoot.Wrapper);
        Func<Func<Element>, Element> first = static root => root();
        Func<Func<Element>, Element> second = static root => root();

        _ = Assert.Throws<ArgumentNullException>(() => WindowRoot.SetWrapper(null!));
        Func<Func<Element>, Element>? afterNull = WindowRoot.Wrapper;
        WindowRoot.SetWrapper(first);
        Func<Func<Element>, Element>? afterFirst = WindowRoot.Wrapper;
        _ = Assert.Throws<InvalidOperationException>(() => WindowRoot.SetWrapper(second));

        Assert.Null(afterNull);
        Assert.Same(first, afterFirst);
        Assert.Same(first, WindowRoot.Wrapper);
    }
}
