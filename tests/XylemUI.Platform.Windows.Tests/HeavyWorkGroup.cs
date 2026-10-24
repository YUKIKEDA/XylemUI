namespace XylemUI.Platform.Windows.Tests;

/// <summary>
/// The tests that keep several threads busy for seconds, run apart from every other test of this project: the Zero-Alloc tests
/// here count the bytes of their own thread, and heavy work beside a Zero-Alloc test made it count bytes it did not allocate
/// (Issue #221; why is row Q1's question).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyWorkGroup
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    public const string Name = "Heavy work";
}
