namespace XylemUI;

/// <summary>
/// What a cached layout of a node was computed from (design §2.4). Equal keys mean the cached size is still right.
/// </summary>
/// <param name="AvailableSpace">The constraint the parent gave.</param>
/// <param name="LayoutStyleVersion">The node's <see cref="NodeVersions.LayoutStyle"/>.</param>
/// <param name="ChildStructureVersion">The node's <see cref="NodeVersions.Children"/>.</param>
/// <param name="DescendantLayoutVersion">The node's <see cref="NodeVersions.DescendantLayout"/>.</param>
internal readonly record struct LayoutCacheKey(
    AvailableSpace AvailableSpace,
    ulong LayoutStyleVersion,
    ulong ChildStructureVersion,
    ulong DescendantLayoutVersion
);
