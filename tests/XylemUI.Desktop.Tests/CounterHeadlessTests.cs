using Counter;
using XylemUI.Testing.XUnit;

namespace XylemUI.Desktop.Tests;

// The counter sample in a headless app (design §12, row P02a): the example of §12.2 as a test, with the button found by its text.
// The tests of the real window, its messages, its pixels, and its closing are in CounterTests.
public sealed class CounterHeadlessTests
{
    /// <summary>
    /// The example of design §12.2: a click on the node found by the text "+1", and a pump, make the text "clicks: 1".
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter sample in a headless app, with the test font.
    ///
    /// Steps:
    /// - Click the node found by "+1"; pump until settled.
    ///
    /// Expected:
    /// - One node has the text "clicks: 1", and none "clicks: 0"; the pump presented one frame.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Click_OnPlusOne_CountsOne()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(CounterApp.Counter);
        int presents = app.PresentCount;

        app.Pointer.Click(app.Find("+1"));
        _ = app.PumpAndSettle();

        Assert.Single(app.FindAll("clicks: 1"));
        Assert.Empty(app.FindAll("clicks: 0"));
        Assert.True(app.PresentCount > presents);
    }

    /// <summary>
    /// Closing the counter leaves no scope alive: its signal's effects and the button's belong to the root scope.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter sample in a headless app, clicked three times.
    ///
    /// Steps:
    /// - Check for leaked scopes.
    ///
    /// Expected:
    /// - No scope is left; the text read "clicks: 3" before.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Close_LeavesNoScope()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(CounterApp.Counter);
        for (int i = 0; i < 3; i++)
        {
            app.Pointer.Click(app.Find("+1"));
        }

        _ = app.PumpAndSettle();
        _ = app.Find("clicks: 3");
        app.AssertNoLeakedScopes();
    }
}
