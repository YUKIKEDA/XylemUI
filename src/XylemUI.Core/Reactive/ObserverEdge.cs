namespace XylemUI;

/// <summary>
/// An edge held by a node: a computation that reads it (design §2.2.2).
/// </summary>
/// <param name="Observer">The computation that reads the node.</param>
/// <param name="SlotInObserver">The position of the matching <see cref="SourceEdge"/> in the observer's sources.</param>
internal readonly record struct ObserverEdge(Computation Observer, int SlotInObserver);
