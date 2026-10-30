using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The keys of Find (design §12.5, "The keys") on a tree with text: a test ID first, and a text leaf's text only when no node has the
// ID. Here, since the test font is shaped with HarfBuzz (Windows until rows P04 and P06); the test-ID keys alone are tested on every
// OS in XylemUI.Testing.Tests.
public sealed class HeadlessFindTests
{
    /// <summary>
    /// A test ID wins over a text that is the same, and a text is found only when no node has the ID; the text key counts as the
    /// test ID does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of a column: the text "x", a box with the test ID "x", the text "dup" twice, and the text "one".
    ///
    /// Steps:
    /// - Find "x", "one", "dup", and "none"; find all of "x", "dup", and "none".
    ///
    /// Expected:
    /// - "x" is the box, not the text, and all of "x" is the box alone; "one" is its text leaf; "dup" throws for two and all of it
    ///   is the two leaves in order; "none" throws for none and all of it is empty.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Find_TestIdWinsOverText_AndTextCountsTheSame()
    {
        Element box = default;
        Element first = default;
        Element second = default;
        Element one = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                Elements.Text("x"),
                box = Box().TestId("x"),
                first = Elements.Text("dup"),
                second = Elements.Text("dup"),
                one = Elements.Text("one")
            )
        );

        Assert.Equal(box, app.Find("x"));
        Assert.Equal([box], app.FindAll("x"));
        Assert.Equal(one, app.Find("one"));
        Assert.Contains("2 nodes", Assert.Throws<InvalidOperationException>(() => app.Find("dup")).Message, StringComparison.Ordinal);
        Assert.Equal([first, second], app.FindAll("dup"));
        Assert.Contains("No node", Assert.Throws<InvalidOperationException>(() => app.Find("none")).Message, StringComparison.Ordinal);
        Assert.Empty(app.FindAll("none"));
    }
}
