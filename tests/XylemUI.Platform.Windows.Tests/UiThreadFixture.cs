using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace XylemUI.Platform.Windows.Tests;

/// <summary>
/// Owns one thread that is the UI thread for every test in <see cref="UiThreadGroup"/>, and runs work on it.
/// </summary>
/// <remarks>
/// Win32 windows belong to the thread that creates them, and <see cref="UiThread"/> is process-wide, so every window test runs on
/// this one thread. Messages sent with <c>SendMessageW</c> from that thread reach the window procedure without a message loop.
/// </remarks>
public sealed class UiThreadFixture : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;

    public UiThreadFixture()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "XylemUI test UI thread" };

        // A UI thread is in COM's single-threaded apartment, as an application's [STAThread] Main is: the first window initializes
        // OLE on it (design §5.0.3, "Color glyphs").
        _thread.SetApartmentState(ApartmentState.STA);
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

[CollectionDefinition(Name)]
public sealed class UiThreadGroup : ICollectionFixture<UiThreadFixture>
{
    public const string Name = "UI thread";
}
