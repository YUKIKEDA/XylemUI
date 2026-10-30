using System.Runtime.ExceptionServices;

namespace XylemUI.Testing;

/// <summary>
/// Owns the one UI thread of the process that the headless tests run on (design §12.5, "Threads"): Core allows one UI thread per
/// process, and a test runner runs tests on threads of its own. A test's body is handed to <see cref="Run(Func{Task})"/>, and every
/// call of <see cref="HeadlessApp"/> in it runs on that thread, so a test reads as plain synchronous calls.
/// </summary>
/// <remarks>
/// The thread starts on the first call and lives as long as the process. It is a single-threaded apartment on Windows, as an app's
/// <c>Main</c> with <c>[STAThread]</c> is, so a test may also open a real window on it. Its synchronization context is its
/// dispatcher's: an <see langword="await"/> in a test's body comes back to the UI thread. The items it queues run with no batch
/// around each, so a signal that a test sets runs its effects at once; in an app, a continuation is one batch (design §2.2.1).
/// </remarks>
public static class HeadlessSession
{
    private static readonly Lock Gate = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private static UiDispatcher? _dispatcher;

    /// <summary>
    /// Gets the dispatcher of the UI thread, starting the thread if it has not started.
    /// </summary>
    internal static UiDispatcher Dispatcher
    {
        get
        {
            EnsureStarted();
            return _dispatcher!;
        }
    }

    /// <summary>
    /// Runs a test's body on the UI thread and returns when it has ended, with its exception if it threw. Called on the UI thread,
    /// it runs the body there at once.
    /// </summary>
    /// <remarks>
    /// Bodies handed over from other threads run one at a time: a body that awaits keeps the next one waiting until it ends, so two
    /// tests never interleave on the UI thread, where the scope tracker and the queue are the process's. A body therefore does not
    /// wait, on another thread, for a second body it hands over.
    /// </remarks>
    /// <param name="body">The test's body.</param>
    /// <returns>A task that completes when the body's task has completed.</returns>
    public static Task Run(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        UiDispatcher dispatcher = Dispatcher;
        if (UiThread.CheckAccess())
        {
            return body();
        }

        return RunOneAtATime(dispatcher, body);
    }

    private static async Task RunOneAtATime(UiDispatcher dispatcher, Func<Task> body)
    {
        await OneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            await Post(dispatcher, body).ConfigureAwait(false);
        }
        finally
        {
            _ = OneAtATime.Release();
        }
    }

    private static Task Post(UiDispatcher dispatcher, Func<Task> body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.PostCallback(
            static state =>
            {
                var (body, done) = ((Func<Task>, TaskCompletionSource))state!;
                Task task;
                try
                {
                    task = body();
                }
                catch (Exception exception)
                {
                    done.SetException(exception);
                    return;
                }

                _ = task.ContinueWith(
                    static (ended, state) =>
                    {
                        var done = (TaskCompletionSource)state!;
                        if (ended.IsCanceled)
                        {
                            done.SetCanceled();
                        }
                        else if (ended.Exception is { } error)
                        {
                            done.SetException(error.InnerExceptions);
                        }
                        else
                        {
                            done.SetResult();
                        }
                    },
                    done,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            },
            (body, done),
            batched: false
        );
        return done.Task;
    }

    /// <summary>
    /// Runs a synchronous test's body on the UI thread and returns when it has ended, with its exception if it threw.
    /// </summary>
    /// <param name="body">The test's body.</param>
    /// <returns>A task that completes when the body has returned.</returns>
    public static Task Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Run(() =>
        {
            body();
            return Task.CompletedTask;
        });
    }

    private static void EnsureStarted()
    {
        if (Volatile.Read(ref _dispatcher) is not null)
        {
            return;
        }

        lock (Gate)
        {
            if (_dispatcher is not null)
            {
                return;
            }

            var startup = new Startup();
            var thread = new Thread(Loop) { IsBackground = true, Name = "XylemUI headless UI thread" };
            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.STA);
            }

            thread.Start(startup);
            startup.Started.Wait();
            startup.Started.Dispose();
            startup.Error?.Throw();
        }
    }

    private static void Loop(object? state)
    {
        var startup = (Startup)state!;
        UiDispatcher dispatcher;
        try
        {
            UiThread.Initialize();
            dispatcher = new UiDispatcher(static () => Wake.Set());

            // Not in a batch: the test's body is one item of the queue, and a batch around it would hold every effect back until it
            // ended. Each signal a test sets flushes its effects at once; an app's continuation is not a batch here as it is in an app.
            SynchronizationContext.SetSynchronizationContext(dispatcher.UnbatchedSynchronizationContext);
        }
        catch (Exception exception)
        {
            startup.Error = ExceptionDispatchInfo.Capture(exception);
            startup.Started.Set();
            return;
        }

        Volatile.Write(ref _dispatcher, dispatcher);
        startup.Started.Set();
        while (true)
        {
            _ = Wake.WaitOne();
            while (dispatcher.Drain(ReportException) > 0) { }
        }
    }

    // A test's own exceptions go to its task; one here came from work that no test awaits, which a test cannot observe either. It
    // is kept for the next HeadlessApp to rethrow, so that it fails a test instead of passing unseen.
    private static void ReportException(Exception exception) => HeadlessApp.ReportUnobserved(exception);

    // What the UI thread tells the thread that started it: that it runs, or why it could not.
    private sealed class Startup
    {
        public ManualResetEventSlim Started { get; } = new();

        public ExceptionDispatchInfo? Error { get; set; }
    }
}
