using XylemUI.Testing;

namespace XylemUI.TestSupport;

/// <summary>
/// Runs a test's code on the UI thread of <see cref="HeadlessSession"/>, the one UI thread of the process (design §12.5,
/// "Threads"), for the tests of a <see cref="UiThreadGroup"/>. Every test project links this file, so that each has the same
/// thread: two would throw in <see cref="UiThread.Initialize"/>.
/// </summary>
public sealed class UiThreadFixture
{
    // Where a test's code goes: the session's UI thread.
    private readonly Func<Action, Task> _run = HeadlessSession.Run;

    /// <summary>
    /// Runs an action on the UI thread and returns when it has ended, rethrowing what it threw.
    /// </summary>
    /// <param name="action">The action.</param>
    public void Invoke(Action action) => _run(action).GetAwaiter().GetResult();
}

/// <summary>
/// The tests that share the UI thread; they run one after another.
/// </summary>
[CollectionDefinition(Name)]
public sealed class UiThreadGroup : ICollectionFixture<UiThreadFixture>
{
    /// <summary>
    /// The collection's name.
    /// </summary>
    public const string Name = "UI thread";
}
