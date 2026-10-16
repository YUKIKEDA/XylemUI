namespace XylemUI.Tests;

// UiThread is process-wide state. Only this class initializes it in this test assembly, on a thread it owns.
public sealed class UiThreadTests
{
    /// <summary>
    /// The UI-thread check accepts the initializing thread and rejects every other thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dedicated thread calls <see cref="UiThread.Initialize"/>.
    ///
    /// Steps:
    /// - Call <see cref="UiThread.VerifyAccess"/> on that thread.
    /// - Call it on the test thread and on a thread-pool thread.
    /// - Call <see cref="UiThread.Initialize"/> again on the test thread.
    ///
    /// Expected:
    /// - The dedicated thread passes, and a second <see cref="UiThread.Initialize"/> on it does nothing.
    /// - The test thread and the thread-pool thread get <see cref="InvalidOperationException"/>.
    /// - <see cref="UiThread.Initialize"/> from the test thread throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public async Task VerifyAccess_OffTheInitializingThread_Throws()
    {
        Exception? onUiThread = null;
        var uiThread = new Thread(() =>
        {
            try
            {
                UiThread.Initialize();
                UiThread.VerifyAccess();
                UiThread.Initialize();
            }
            catch (Exception exception)
            {
                onUiThread = exception;
            }
        });
        uiThread.Start();
        uiThread.Join();

        Assert.Null(onUiThread);
        Assert.True(UiThread.IsInitialized);
        Assert.False(UiThread.CheckAccess());
        Assert.Throws<InvalidOperationException>(UiThread.VerifyAccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(UiThread.VerifyAccess, TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(UiThread.Initialize);
    }
}
