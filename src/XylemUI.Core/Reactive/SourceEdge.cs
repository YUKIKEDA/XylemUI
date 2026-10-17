namespace XylemUI;

/// <summary>
/// An edge held by a computation: a node it read in its last run, in the order it read them (design §2.2.2).
/// </summary>
/// <param name="Source">The node that was read.</param>
/// <param name="SlotInSource">
/// The position of the matching <see cref="ObserverEdge"/> in the source's observers; -1 while the computation is not live and
/// is not registered with the source.
/// </param>
/// <param name="SeenVersion">The source's version when it was read.</param>
internal record struct SourceEdge(ReactiveNode Source, int SlotInSource, ulong SeenVersion);
