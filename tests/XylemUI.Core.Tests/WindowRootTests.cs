namespace XylemUI.Tests;

// The root wrapper of a process's windows (row TH1, design §13.2, "根で置くもの" and "包みは 1 つ"): set before the first window
// reads it. The rules that need a wrapper set run in XylemUI.Testing.Tests (set, the same method again, another one before a read)
// and XylemUI.Desktop.Tests (after a read), each against its process's one value: no test of this project sets a wrapper.
public sealed class WindowRootTests
{
    /// <summary>
    /// A window that read the process's wrapper while none was set closes it: a wrapper set after the read is refused, as a
    /// null wrapper always is, and the windows stay unwrapped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No wrapper is set in the test process, and no test of this project sets one or reads it but this one, so the order of the
    ///   tests does not matter.
    ///
    /// Steps:
    /// - Read the wrapper, as a desktop window does when it mounts. Set a null wrapper. Set a wrapper. Read the wrapper again.
    ///
    /// Expected:
    /// - Both reads give none, and the wrapper is marked read after the first. The null wrapper throws
    ///   <see cref="ArgumentNullException"/>; the wrapper throws <see cref="InvalidOperationException"/> whose message says the
    ///   wrapper is set before the first window opens.
    /// </remarks>
    [Fact]
    public void SetWrapper_AfterAWindowReadNone_Throws()
    {
        Func<Func<Element>, Element> wrapper = static root => root();

        Func<Func<Element>, Element>? first = WindowRoot.ReadWrapper();
        bool read = WindowRoot.IsRead;
        _ = Assert.Throws<ArgumentNullException>(() => WindowRoot.SetWrapper(null!));
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => WindowRoot.SetWrapper(wrapper));
        Func<Func<Element>, Element>? second = WindowRoot.ReadWrapper();

        Assert.Null(first);
        Assert.True(read);
        Assert.Contains("before the first window opens", thrown.Message, StringComparison.Ordinal);
        Assert.Null(second);
    }
}
