namespace XylemUI.Tests;

// The static entry to the UI thread's dispatcher (row PL1, design §2.12): UiDispatcher.Current and UiDispatcher.Exchange use the
// process's one CurrentDispatcher. The process's value is the headless session's dispatcher, which every test project's UI thread
// sets, so the test puts back what it found.
[Collection(UiThreadGroup.Name)]
public sealed class UiDispatcherTests(UiThreadFixture ui)
{
    /// <summary>
    /// A host's exchange replaces what <see cref="UiDispatcher.Current"/> returns on every thread, and giving back the value it
    /// returned restores the one before.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the headless session, which set <see cref="UiDispatcher.Current"/> when it started. A dispatcher
    ///   <c>host</c>.
    ///
    /// Steps:
    /// - Read <see cref="UiDispatcher.Current"/> as <c>before</c>; exchange <c>host</c> in; read it on the UI thread and on a
    ///   worker thread.
    /// - Exchange back what the first exchange returned; read it again.
    ///
    /// Expected:
    /// - The first exchange returned <c>before</c>; reading <c>before</c> did not throw.
    /// - Both reads after it returned <c>host</c>.
    /// - Giving back returned <c>host</c>, and <see cref="UiDispatcher.Current"/> is <c>before</c> again.
    /// </remarks>
    [Fact]
    public void Current_AHostExchangesItsDispatcherAndGivesTheOldOneBack_ReturnsTheHostsThenTheOldOne() =>
        ui.Invoke(() =>
        {
            IUiDispatcher before = UiDispatcher.Current;
            var host = new UiDispatcher(static () => { });
            IUiDispatcher? returned = UiDispatcher.Exchange(host);
            IUiDispatcher onUiThread;
            IUiDispatcher? onWorker = null;
            IUiDispatcher? givenBack;
            try
            {
                onUiThread = UiDispatcher.Current;
                var worker = new Thread(() => onWorker = UiDispatcher.Current);
                worker.Start();
                worker.Join();
            }
            finally
            {
                givenBack = UiDispatcher.Exchange(returned);
            }

            Assert.Same(before, returned);
            Assert.Same(host, onUiThread);
            Assert.Same(host, onWorker);
            Assert.Same(host, givenBack);
            Assert.Same(before, UiDispatcher.Current);
        });
}
