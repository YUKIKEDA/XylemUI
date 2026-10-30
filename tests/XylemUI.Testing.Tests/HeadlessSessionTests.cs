using XylemUI.Testing.XUnit;

namespace XylemUI.Testing.Tests;

// The UI thread of the headless tests (design §12.5, "Threads"): HeadlessSession runs a test's body there, and [UiFact] and
// [UiTheory] run a whole test there.
public sealed class HeadlessSessionTests
{
    private readonly int _constructedOn = Environment.CurrentManagedThreadId;

    /// <summary>
    /// A body handed over from another thread runs on the UI thread, and the call returns once it has ended.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A test thread of xUnit's.
    ///
    /// Steps:
    /// - Run a body that records whether it is on the UI thread, and wait for the task.
    ///
    /// Expected:
    /// - The body ran on the UI thread, and the caller is not on it.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Run_FromAnotherThread_RunsTheBodyOnTheUiThread()
    {
        bool onUiThread = false;
        await HeadlessSession.Run(() => onUiThread = UiThread.CheckAccess());
        Assert.True(onUiThread);
        Assert.False(UiThread.CheckAccess());
    }

    /// <summary>
    /// An <see langword="await"/> in a body comes back to the UI thread, through the dispatcher's synchronization context.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A test thread of xUnit's.
    ///
    /// Steps:
    /// - Run a body that awaits a task completed on a pool thread, then records whether it is on the UI thread.
    ///
    /// Expected:
    /// - The part after the await ran on the UI thread.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Run_AwaitInTheBody_ComesBackToTheUiThread()
    {
        bool onUiThread = false;
        await HeadlessSession.Run(async () =>
        {
            await Task.Run(() => { }, TestContext.Current.CancellationToken);
            onUiThread = UiThread.CheckAccess();
        });
        Assert.True(onUiThread);
    }

    /// <summary>
    /// A body that throws faults the task with the same exception.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A test thread of xUnit's.
    ///
    /// Steps:
    /// - Run a body that throws an <see cref="InvalidOperationException"/>, and await the task.
    ///
    /// Expected:
    /// - The await throws that exception.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Run_BodyThrows_TheTaskThrowsTheSameException()
    {
        var thrown = new InvalidOperationException("the body's");
        InvalidOperationException caught = await Assert.ThrowsAsync<InvalidOperationException>(() => HeadlessSession.Run(() => throw thrown));
        Assert.Same(thrown, caught);
    }

    /// <summary>
    /// A <see cref="UiFactAttribute"/> test's class is made, and its body runs, on the UI thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - This class records the thread its constructor ran on.
    ///
    /// Steps:
    /// - Read the thread in the body.
    ///
    /// Expected:
    /// - The body is on the UI thread, and the constructor ran on the same thread.
    /// </remarks>
    [UiFact]
    public void UiFact_RunsTheClassAndTheBodyOnTheUiThread()
    {
        Assert.True(UiThread.CheckAccess());
        Assert.Equal(_constructedOn, Environment.CurrentManagedThreadId);
    }

    /// <summary>
    /// An asynchronous <see cref="UiFactAttribute"/> test comes back to the UI thread after an <see langword="await"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Await a task completed on a pool thread.
    ///
    /// Expected:
    /// - The body is on the UI thread before and after it.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task UiFact_Async_StaysOnTheUiThread()
    {
        Assert.True(UiThread.CheckAccess());
        await Task.Run(() => { }, TestContext.Current.CancellationToken);
        Assert.True(UiThread.CheckAccess());
    }

    /// <summary>
    /// Each row of a <see cref="UiTheoryAttribute"/> test runs on the UI thread with its own argument.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Rows 1 and 2.
    ///
    /// Steps:
    /// - Read the thread and the argument in the body.
    ///
    /// Expected:
    /// - The body is on the UI thread, with an argument of 1 or 2.
    /// </remarks>
    /// <param name="row">The row's argument.</param>
    [UiTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void UiTheory_RunsEachRowOnTheUiThread(int row)
    {
        Assert.True(UiThread.CheckAccess());
        Assert.InRange(row, 1, 2);
    }
}
