using System.Runtime.ExceptionServices;

namespace XylemUI.Tests;

// The value behind UiDispatcher.Current (row PL1, design §2.12): a host sets its dispatcher and gives back the one it replaced, and
// any thread reads it. Each test makes its own CurrentDispatcher, since the process's keeps the headless session's dispatcher set.
[Collection(UiThreadGroup.Name)]
public sealed class CurrentDispatcherTests(UiThreadFixture ui)
{
    private static readonly Action<Exception> Rethrow = static exception => ExceptionDispatchInfo.Throw(exception);

    /// <summary>
    /// Reading the value before a host set one throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A new <see cref="CurrentDispatcher"/>, on which nothing called <see cref="CurrentDispatcher.Exchange"/>.
    ///
    /// Steps:
    /// - Read <see cref="CurrentDispatcher.Value"/>.
    ///
    /// Expected:
    /// - It throws <see cref="InvalidOperationException"/> whose message names <c>UiDispatcher.Current</c>.
    /// </remarks>
    [Fact]
    public void Value_BeforeAHostSetsIt_ThrowsInvalidOperationException()
    {
        var current = new CurrentDispatcher();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => current.Value);
        Assert.Contains("UiDispatcher.Current", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host sets its dispatcher: the value is that dispatcher, and the exchange returns what was set before, none at first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A new <see cref="CurrentDispatcher"/> and a dispatcher <c>host</c>.
    ///
    /// Steps:
    /// - Exchange <c>host</c> in; read the value.
    ///
    /// Expected:
    /// - The exchange returns <see langword="null"/>, and the value is <c>host</c>.
    /// </remarks>
    [Fact]
    public void Exchange_AHostSetsItsDispatcher_TheValueIsItAndNoneIsReturned()
    {
        var current = new CurrentDispatcher();
        var host = new UiDispatcher(static () => { });

        IUiDispatcher? previous = current.Exchange(host);

        Assert.Null(previous);
        Assert.Same(host, current.Value);
    }

    /// <summary>
    /// Hosts started one inside the other and disposed in the reverse order each give back the value they replaced: the outer
    /// host's dispatcher after the inner one ends, and none after the outer one ends.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A new <see cref="CurrentDispatcher"/>; dispatchers <c>outer</c> (the headless session's UI thread) and <c>inner</c> (a
    ///   real window's loop run inside it).
    ///
    /// Steps:
    /// - Exchange <c>outer</c> in, then <c>inner</c>, keeping what each returned; read the value.
    /// - Exchange back what <c>inner</c>'s exchange returned; read the value.
    /// - Exchange back what <c>outer</c>'s exchange returned; read the value.
    ///
    /// Expected:
    /// - <c>outer</c>'s exchange returned <see langword="null"/> and <c>inner</c>'s returned <c>outer</c>; the value was
    ///   <c>inner</c>.
    /// - Giving back returned <c>inner</c>, and the value was <c>outer</c> again.
    /// - Giving back returned <c>outer</c>, and reading the value then throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Exchange_NestedHostsDisposedInReverseOrder_GiveBackEachPreviousValue()
    {
        var current = new CurrentDispatcher();
        var outer = new UiDispatcher(static () => { });
        var inner = new UiDispatcher(static () => { });

        IUiDispatcher? beforeOuter = current.Exchange(outer);
        IUiDispatcher? beforeInner = current.Exchange(inner);
        Assert.Null(beforeOuter);
        Assert.Same(outer, beforeInner);
        Assert.Same(inner, current.Value);

        Assert.Same(inner, current.Exchange(beforeInner));
        Assert.Same(outer, current.Value);

        Assert.Same(outer, current.Exchange(beforeOuter));
        Assert.Throws<InvalidOperationException>(() => current.Value);
    }

    /// <summary>
    /// A worker thread reads the dispatcher that another thread set, and the one that replaced it after.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A new <see cref="CurrentDispatcher"/>; dispatchers <c>first</c> and <c>second</c>.
    ///
    /// Steps:
    /// - On the test's thread, exchange <c>first</c> in; a worker thread reads the value; join it.
    /// - Exchange <c>second</c> in; a second worker thread reads the value; join it.
    ///
    /// Expected:
    /// - The first worker read <c>first</c>, and the second read <c>second</c>; neither threw.
    /// </remarks>
    [Fact]
    public void Value_ReadOnAWorkerThread_IsTheDispatcherSetOnAnotherThread()
    {
        var current = new CurrentDispatcher();
        var first = new UiDispatcher(static () => { });
        var second = new UiDispatcher(static () => { });

        _ = current.Exchange(first);
        IUiDispatcher readFirst = ReadOnAWorker(current);
        _ = current.Exchange(second);
        IUiDispatcher readSecond = ReadOnAWorker(current);

        Assert.Same(first, readFirst);
        Assert.Same(second, readSecond);
    }

    /// <summary>
    /// A message a worker posts to the dispatcher it read from the value reaches its receiver on the UI thread when the host's
    /// dispatcher drains.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A new <see cref="CurrentDispatcher"/> holding a dispatcher <c>host</c> whose wake does nothing. A receiver of <c>Ping</c>,
    ///   added in a scope on the UI thread, that records the number and whether it runs on the UI thread.
    ///
    /// Steps:
    /// - A worker thread reads the value and posts <c>Ping</c> 7 to it; the test joins it.
    /// - Drain <c>host</c> on the UI thread.
    ///
    /// Expected:
    /// - The drain ran 1 item; the receiver got 7, on the UI thread.
    /// </remarks>
    [Fact]
    public void Value_AWorkerPostsToIt_TheMessageReachesTheReceiverOnTheUiThread() =>
        ui.Invoke(() =>
        {
            var current = new CurrentDispatcher();
            var host = new UiDispatcher(static () => { });
            _ = current.Exchange(host);
            var received = new List<int>();
            bool onUiThread = false;
            ScopeHandle scope = Scope.Create(() =>
                UiMessage.On<Ping>(ping =>
                {
                    received.Add(ping.Number);
                    onUiThread = UiThread.CheckAccess();
                })
            );
            try
            {
                var worker = new Thread(() => current.Value.Post(new Ping(7)));
                worker.Start();
                worker.Join();

                Assert.Equal(1, host.Drain(Rethrow));
            }
            finally
            {
                Scope.Dispose(scope);
            }

            Assert.Equal([7], received);
            Assert.True(onUiThread);
        });

    // Reads the value on a new thread and returns what it read, rethrowing what the read threw.
    private static IUiDispatcher ReadOnAWorker(CurrentDispatcher current)
    {
        IUiDispatcher? read = null;
        ExceptionDispatchInfo? error = null;
        var worker = new Thread(() =>
        {
            try
            {
                read = current.Value;
            }
            catch (Exception exception)
            {
                error = ExceptionDispatchInfo.Capture(exception);
            }
        });
        worker.Start();
        worker.Join();
        error?.Throw();
        return read!;
    }

    private sealed record Ping(int Number) : UiMessage;
}
