namespace XylemUI;

/// <summary>
/// The input part of a node (design §7, "Pointer input"): its pointer handlers, its cursor, and whether it blocks the pointer
/// without a handler. Only a node with any of them has one (<see cref="UiNode.Input"/>); the tree makes it at the first setter, as
/// it makes <see cref="NodeVisual"/>.
/// </summary>
/// <remarks>
/// The handlers are an array that a change replaces, so that a handler that adds or removes one while the event is delivered does
/// not change the array being walked. Adding or removing allocates; delivering does not.
/// </remarks>
internal sealed class NodeInput
{
    /// <summary>
    /// Gets the pointer handlers, in the order they were added.
    /// </summary>
    public HandlerEntry[] Handlers { get; private set; } = [];

    /// <summary>
    /// Gets or sets the cursor over the node and its descendants that set none; <see langword="null"/> for none.
    /// </summary>
    public CursorKind? Cursor { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether the node writes a hit region with no handler, cursor, or hovered look, so that the
    /// pointer does not reach the nodes behind it.
    /// </summary>
    public bool BlocksPointer { get; internal set; }

    /// <summary>
    /// Gets or sets the index of the node's hit region in the display list (<see cref="DisplayList.HitEntries"/>), valid while
    /// <see cref="HitBuild"/> is the frame's build.
    /// </summary>
    internal int HitEntry { get; set; }

    /// <summary>
    /// Gets or sets the build of the display list <see cref="HitEntry"/> belongs to (<see cref="WindowFrames.Builds"/>).
    /// </summary>
    internal int HitBuild { get; set; }

    /// <summary>
    /// Gets a value indicating whether the node writes a hit region for its input: a handler, a cursor, or the block.
    /// </summary>
    public bool WritesHitRegion => Handlers.Length > 0 || Cursor is not null || BlocksPointer;

    /// <summary>
    /// Adds a handler.
    /// </summary>
    internal void Add(in HandlerEntry entry)
    {
        var handlers = new HandlerEntry[Handlers.Length + 1];
        Handlers.CopyTo(handlers, 0);
        handlers[^1] = entry;
        Handlers = handlers;
    }

    /// <summary>
    /// Removes the first handler equal to <paramref name="entry"/>; returns whether there was one.
    /// </summary>
    internal bool Remove(in HandlerEntry entry)
    {
        int index = Array.IndexOf(Handlers, entry);
        if (index < 0)
        {
            return false;
        }

        var handlers = new HandlerEntry[Handlers.Length - 1];
        Array.Copy(Handlers, handlers, index);
        Array.Copy(Handlers, index + 1, handlers, index, handlers.Length - index);
        Handlers = handlers;
        return true;
    }
}

/// <summary>
/// A pointer handler of a node: the kind of event and the phase it is called for.
/// </summary>
/// <param name="Kind">The kind of event.</param>
/// <param name="Phase">The phase of a routed event; an enter and a leave call the handlers of either phase.</param>
/// <param name="Handler">The handler.</param>
internal readonly record struct HandlerEntry(PointerEventKind Kind, EventPhase Phase, PointerHandler Handler);
