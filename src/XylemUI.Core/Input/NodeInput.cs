namespace XylemUI;

/// <summary>
/// The input part of a node (design §7, "Pointer input" and "Keys and the focus"): its handlers, its cursor, whether it blocks the
/// pointer without a handler, and whether it takes the focus. Only a node with any of them has one (<see cref="UiNode.Input"/>); the
/// tree makes it at the first setter, as it makes <see cref="NodeVisual"/>.
/// </summary>
/// <remarks>
/// The handlers are arrays that a change replaces, so that a handler that adds or removes one while the event is delivered does not
/// change the array being walked. Adding or removing allocates; delivering does not.
/// </remarks>
internal sealed class NodeInput
{
    /// <summary>
    /// Gets the pointer handlers, in the order they were added.
    /// </summary>
    public HandlerEntry[] Handlers { get; private set; } = [];

    /// <summary>
    /// Gets the key handlers, in the order they were added.
    /// </summary>
    public KeyHandlerEntry[] KeyHandlers { get; private set; } = [];

    /// <summary>
    /// Gets the focus handlers, in the order they were added.
    /// </summary>
    public FocusHandlerEntry[] FocusHandlers { get; private set; } = [];

    /// <summary>
    /// Gets the text input handlers, in the order they were added.
    /// </summary>
    public TextHandlerEntry[] TextHandlers { get; private set; } = [];

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
    /// Gets or sets a value indicating whether the node takes the focus: from a press on it or a descendant, from Tab, and from
    /// <see cref="ElementExtensions.Focus"/>, while neither it nor an ancestor is hidden (design §7, "Keys and the focus").
    /// </summary>
    public bool Focusable { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether a press on the node, or on a descendant with no node that takes the focus between them,
    /// leaves the focus where it is, even when the node takes the focus itself (design §4.9, "A press that keeps the focus"; zed's
    /// button).
    /// </summary>
    public bool KeepsFocusOnPress { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether the node's subtree keeps its text out of a selection area's selection
    /// (<see cref="ElementExtensions.Unselectable"/>, design §4.10).
    /// </summary>
    public bool Unselectable { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether the node is a selection area, whose subtree's text leaves are selected across
    /// (design §4.10): a text leaf in it shows the I-beam (§7, "Cursor shape").
    /// </summary>
    internal bool SelectsText { get; set; }

    /// <summary>
    /// Gets or sets the node's place in the order of Tab (design §7.1): a smaller index comes first, and the nodes of the same index
    /// come in the order of the tree. 0 by default.
    /// </summary>
    public int TabIndex { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether the node itself is disabled: the mark <see cref="ElementExtensions.Disabled(Element, bool)"/>
    /// writes. The node and its descendants have <see cref="PseudoState.Disabled"/> while it is set (design §7, "Disabled").
    /// </summary>
    public bool Disabled { get; internal set; }

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
    /// Gets a value indicating whether the node writes a hit region for its input: a pointer handler, a cursor, the block, or the
    /// focus, which a press on the node takes.
    /// </summary>
    public bool WritesHitRegion => Handlers.Length > 0 || Cursor is not null || BlocksPointer || Focusable;

    /// <summary>
    /// Adds a pointer handler.
    /// </summary>
    /// <param name="entry">The handler, with its kind, phase, and scope.</param>
    internal void Add(in HandlerEntry entry) => Handlers = Added(Handlers, entry);

    /// <summary>
    /// Adds a key handler.
    /// </summary>
    /// <param name="entry">The handler, with its kind, phase, and scope.</param>
    internal void Add(in KeyHandlerEntry entry) => KeyHandlers = Added(KeyHandlers, entry);

    /// <summary>
    /// Adds a focus handler.
    /// </summary>
    /// <param name="entry">The handler, with its kind, phase, and scope.</param>
    internal void Add(in FocusHandlerEntry entry) => FocusHandlers = Added(FocusHandlers, entry);

    /// <summary>
    /// Adds a text input handler.
    /// </summary>
    /// <param name="entry">The handler, with its phase and scope.</param>
    internal void Add(in TextHandlerEntry entry) => TextHandlers = Added(TextHandlers, entry);

    /// <summary>
    /// Removes the first pointer handler of the kind and the phase with the same delegate.
    /// </summary>
    /// <param name="kind">The kind of event the handler is for.</param>
    /// <param name="phase">The phase the handler runs in.</param>
    /// <param name="handler">The delegate.</param>
    /// <returns>Whether there was such a handler.</returns>
    internal bool Remove(PointerEventKind kind, EventPhase phase, PointerHandler handler)
    {
        for (int i = 0; i < Handlers.Length; i++)
        {
            if (Handlers[i].Kind == kind && Handlers[i].Phase == phase && Handlers[i].Handler == handler)
            {
                Handlers = Removed(Handlers, i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes the first key handler of the kind and the phase with the same delegate.
    /// </summary>
    /// <param name="kind">The kind of event the handler is for.</param>
    /// <param name="phase">The phase the handler runs in.</param>
    /// <param name="handler">The delegate.</param>
    /// <returns>Whether there was such a handler.</returns>
    internal bool Remove(KeyEventKind kind, EventPhase phase, KeyHandler handler)
    {
        for (int i = 0; i < KeyHandlers.Length; i++)
        {
            if (KeyHandlers[i].Kind == kind && KeyHandlers[i].Phase == phase && KeyHandlers[i].Handler == handler)
            {
                KeyHandlers = Removed(KeyHandlers, i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes the first focus handler of the kind and the phase with the same delegate.
    /// </summary>
    /// <param name="kind">The kind of event the handler is for.</param>
    /// <param name="phase">The phase the handler runs in.</param>
    /// <param name="handler">The delegate.</param>
    /// <returns>Whether there was such a handler.</returns>
    internal bool Remove(FocusEventKind kind, EventPhase phase, FocusHandler handler)
    {
        for (int i = 0; i < FocusHandlers.Length; i++)
        {
            if (FocusHandlers[i].Kind == kind && FocusHandlers[i].Phase == phase && FocusHandlers[i].Handler == handler)
            {
                FocusHandlers = Removed(FocusHandlers, i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes the first text input handler of the phase with the same delegate.
    /// </summary>
    /// <param name="phase">The phase the handler runs in.</param>
    /// <param name="handler">The delegate.</param>
    /// <returns>Whether there was such a handler.</returns>
    internal bool Remove(EventPhase phase, TextInputHandler handler)
    {
        for (int i = 0; i < TextHandlers.Length; i++)
        {
            if (TextHandlers[i].Phase == phase && TextHandlers[i].Handler == handler)
            {
                TextHandlers = Removed(TextHandlers, i);
                return true;
            }
        }

        return false;
    }

    private static T[] Added<T>(T[] entries, in T entry)
    {
        var added = new T[entries.Length + 1];
        entries.CopyTo(added, 0);
        added[^1] = entry;
        return added;
    }

    private static T[] Removed<T>(T[] entries, int index)
    {
        var removed = new T[entries.Length - 1];
        Array.Copy(entries, removed, index);
        Array.Copy(entries, index + 1, removed, index, removed.Length - index);
        return removed;
    }
}

/// <summary>
/// A pointer handler of a node: the kind of event and the phase it is called for.
/// </summary>
/// <param name="Kind">The kind of event.</param>
/// <param name="Phase">The phase of a routed event; an enter and a leave call the handlers of either phase.</param>
/// <param name="Handler">The handler.</param>
/// <param name="Scope">The scope that was current when the handler was added, which the handler runs in (design §2.13); none for a
/// handler added outside every scope, which runs in none.</param>
internal readonly record struct HandlerEntry(PointerEventKind Kind, EventPhase Phase, PointerHandler Handler, ScopeRef Scope);

/// <summary>
/// A key handler of a node: the kind of event and the phase it is called for.
/// </summary>
/// <param name="Kind">The kind of event.</param>
/// <param name="Phase">The phase of the route.</param>
/// <param name="Handler">The handler.</param>
/// <param name="Scope">The scope the handler runs in, as <see cref="HandlerEntry.Scope"/>.</param>
internal readonly record struct KeyHandlerEntry(KeyEventKind Kind, EventPhase Phase, KeyHandler Handler, ScopeRef Scope);

/// <summary>
/// A focus handler of a node: the kind of event and the phase it is called for.
/// </summary>
/// <param name="Kind">The kind of event.</param>
/// <param name="Phase">The phase of the route.</param>
/// <param name="Handler">The handler.</param>
/// <param name="Scope">The scope the handler runs in, as <see cref="HandlerEntry.Scope"/>.</param>
internal readonly record struct FocusHandlerEntry(FocusEventKind Kind, EventPhase Phase, FocusHandler Handler, ScopeRef Scope);

/// <summary>
/// A text input handler of a node: the phase it is called in.
/// </summary>
/// <param name="Phase">The phase of the route.</param>
/// <param name="Handler">The handler.</param>
/// <param name="Scope">The scope the handler runs in, as <see cref="HandlerEntry.Scope"/>.</param>
internal readonly record struct TextHandlerEntry(EventPhase Phase, TextInputHandler Handler, ScopeRef Scope);
