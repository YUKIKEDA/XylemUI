namespace XylemUI.Tests;

[Collection(UiThreadGroup.Name)]
public sealed class LayoutCacheKeyTests(UiThreadFixture ui)
{
    /// <summary>
    /// <see cref="LayoutCacheKey.MatchesInput"/> answers as <see cref="LayoutCacheKey.Matches"/> with the key of the input does
    /// (row L4, PR #103's review): the comparison without the key keeps the meaning of the key.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two nodes: one whose style uses no percentage, one whose width and height are percentages. Inputs from every
    ///   combination of a known width (none, 10), a known height (none, 20), the two definite flags, a parent size (none,
    ///   100 x 200), the available space of each axis (definite 50, definite 60, min-content, max-content), and the sizing
    ///   mode (1,024 inputs).
    ///
    /// Steps:
    /// - For each node and each pair of inputs (a, b): compare the key of a with the key of b, and the key of a with b directly.
    ///
    /// Expected:
    /// - The two comparisons agree for every pair.
    /// </remarks>
    [Fact]
    public void MatchesInput_AgreesWithTheKeyOfTheInput() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle plain = tree.CreateNode(new LayoutStyle());
            NodeHandle percent = tree.CreateNode(new LayoutStyle { Width = Length.Percent(0.5f), Height = Length.Percent(0.5f) });
            List<LayoutInput> inputs = Inputs();
            foreach (NodeHandle handle in new[] { plain, percent })
            {
                ref UiNode node = ref tree.Nodes.GetRef(handle);
                foreach (LayoutInput a in inputs)
                {
                    LayoutCacheKey stored = LayoutCacheKey.Of(in node, in a);
                    foreach (LayoutInput b in inputs)
                    {
                        Assert.Equal(stored.Matches(LayoutCacheKey.Of(in node, in b)), stored.MatchesInput(in node, in b));
                    }
                }
            }
        });

    private static List<LayoutInput> Inputs()
    {
        float[] widths = [float.NaN, 10];
        float[] heights = [float.NaN, 20];
        OptionalSize[] parents = [OptionalSize.None, new OptionalSize(100, 200)];
        AvailableSpace[] spaces = [AvailableSpace.Definite(50), AvailableSpace.Definite(60), AvailableSpace.MinContent, AvailableSpace.MaxContent];
        var inputs = new List<LayoutInput>();
        foreach (float width in widths)
        {
            foreach (float height in heights)
            {
                foreach (bool widthIsDefinite in new[] { false, true })
                {
                    foreach (bool heightIsDefinite in new[] { false, true })
                    {
                        foreach (OptionalSize parent in parents)
                        {
                            foreach (AvailableSpace availableWidth in spaces)
                            {
                                foreach (AvailableSpace availableHeight in spaces)
                                {
                                    foreach (SizingMode mode in new[] { SizingMode.InherentSize, SizingMode.ContentSize })
                                    {
                                        inputs.Add(
                                            new LayoutInput(
                                                new OptionalSize(width, height),
                                                widthIsDefinite,
                                                heightIsDefinite,
                                                parent,
                                                new AvailableSize(availableWidth, availableHeight),
                                                mode,
                                                RequestedAxis.Both
                                            )
                                        );
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return inputs;
    }
}
