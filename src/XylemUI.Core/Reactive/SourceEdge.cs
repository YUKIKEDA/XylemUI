namespace XylemUI;

/// <summary>
/// An edge held by a computation: a node it read in its last run, in the order it read them (design §2.2.2).
/// </summary>
/// <param name="Source">The node that was read.</param>
/// <param name="SlotInSource">The position of the matching <see cref="ObserverEdge"/> in the source's observers.</param>
/// <param name="SeenVersion">The source's version when it was read.</param>
internal record struct SourceEdge(ReactiveNode Source, int SlotInSource, ulong SeenVersion);
