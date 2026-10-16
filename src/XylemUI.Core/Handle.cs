namespace XylemUI;

/// <summary>
/// Identifies one slot of an arena together with the generation it was allocated in (design §2.5, Invariant 2).
/// </summary>
/// <typeparam name="T">The kind of value the handle points to, so that handles of different arenas cannot be mixed up.</typeparam>
/// <param name="Index">The slot index in the arena.</param>
/// <param name="Generation">The generation of the slot when the value was allocated. A freed slot moves to the next generation.</param>
public readonly record struct Handle<T>(int Index, ulong Generation);
