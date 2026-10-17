namespace XylemUI.Tests;

// Block and Flex against Chrome's results, through Taffy's XML tests (design §3.2, the reference of the layout tests).
[Collection(UiThreadGroup.Name)]
public sealed class TaffyFixtureTests(UiThreadFixture ui)
{
    public static TheoryData<string> Names => [.. TaffyFixture.Names];

    /// <summary>
    /// A test of Taffy's XML suite lays out to Chrome's rectangles, rounded as Taffy rounds them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test's tree (Fixtures/Taffy, the border-box and left-to-right variant), its styles read with CSS's initial values,
    ///   and Ahem text measured as Taffy's test measure does. A test that uses a feature of a later row is skipped with that
    ///   row (TaffyFixtureScope).
    ///
    /// Steps:
    /// - Lay the root out in the test's viewport.
    ///
    /// Expected:
    /// - Every node's x, y, width, and height, rounded on the cumulative position, equal Chrome's.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Names))]
    public void Layout_MatchesChrome(string name)
    {
        if (TaffyFixture.ExclusionOf(name) is { } reason)
        {
            Assert.Skip(reason);
        }

        IReadOnlyList<string> differences = [];
        ui.Invoke(() => differences = TaffyFixture.Run(name));
        Assert.True(differences.Count == 0, $"{name}\n{string.Join('\n', differences)}");
    }
}
