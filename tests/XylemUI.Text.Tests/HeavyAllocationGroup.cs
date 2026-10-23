namespace XylemUI.Text.Tests;

/// <summary>
/// The tests that allocate or inflate tens of megabytes, run apart from every other test of this project (Issue #221). While the
/// PNG decoder's 64 MB inflate test ran beside the Zero-Alloc tests, <c>TextSteadyStateTests</c> counted 1.5 to 6 KB on its own
/// thread inside <c>TextShaper.Shape</c>, with no collection in its window; the cause is not known (row Q1, Issue #92).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyAllocationGroup
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    public const string Name = "Heavy allocation";
}
