using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace XylemUI.Text.Tests;

/// <summary>
/// Owns one thread that is the UI thread for every test in <see cref="UiThreadGroup"/>, and runs work on it.
/// </summary>
/// <remarks>
/// <see cref="UiThread"/> is process-wide, so every test that needs the UI thread runs on this one thread. Only this fixture
/// initializes it in this test assembly.
/// </remarks>
public sealed class UiThreadFixture : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;

    public UiThreadFixture()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "XylemUI test UI thread" };
        _thread.Start();
        Invoke(UiThread.Initialize);
    }

    public void Invoke(Action action)
    {
        ExceptionDispatchInfo? error = null;
        using var done = new ManualResetEventSlim();
        _work.Add(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        error?.Throw();
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join();
        _work.Dispose();
    }

    private void Loop()
    {
        foreach (Action action in _work.GetConsumingEnumerable())
        {
            action();
        }
    }
}

/// <summary>
/// The tests that need the UI thread: the text leaf's layout (row V1a). They run apart from every other test of this project, as
/// <see cref="HeavyAllocationGroup"/> does (<c>.cursor/rules/testing.mdc</c>): together they allocate about 32 MB (the process
/// allocated 271.1 MB running them and 239.4 MB running one small test of them). Among them are 90 trees of one text leaf and a
/// tree of 100 leaves laid out hundreds of times (Issue #302).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiThreadGroup : ICollectionFixture<UiThreadFixture>
{
    public const string Name = "UI thread";
}
