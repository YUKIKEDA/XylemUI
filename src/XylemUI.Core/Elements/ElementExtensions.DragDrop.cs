namespace XylemUI;

/// <content>
/// Drag and drop (design §7, "Drag and drop").
/// </content>
public static partial class ElementExtensions
{
    /// <summary>
    /// Makes the node a drag source of values of type <typeparamref name="T"/> (design §7, "Drag and drop"). A press of the left
    /// button that reaches the node in the bubble phase arms a drag: a node inside that stops the down, as a text field does, keeps
    /// the press its own. A mouse that then moves past the OS's drag size, or a touch or a pen held still for 500 ms within 18 logical
    /// pixels, starts the drag: the value is read, the node takes the pointer's capture, the press gives no click, and the preview, if
    /// any, follows the pointer. The drag ends when the button is released over a drop target that takes the value, which gets it,
    /// or elsewhere; Esc, the window's deactivation, and a lost capture cancel it. The end runs once in every case.
    /// </summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="source">The node.</param>
    /// <param name="value">What reads the value when a drag starts.</param>
    /// <param name="options">The preview, the allowed effects, and what runs at the end; the defaults when <see langword="null"/>.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element DragSource<T>(this Element source, Func<T> value, DragSourceOptions? options = null)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        UiTree tree = source.TreeOrThrow();
        RequireScope();
        NodeHandle node = source.Node;
        DragSourceOptions chosen = options ?? new DragSourceOptions();
        var entry = new DragSourceEntry<T>(value, chosen, Scope.CurrentRef);
        if (chosen.Preview is { } preview)
        {
            // The preview is hit by nothing, so that the drag's hit tests find the targets under it (design §7.3).
            entry.PreviewOpen = Signal.Create(false);
            entry.Preview = PopupBlock.Create(
                tree,
                node,
                entry.PreviewOpen,
                () => preview().HitTestVisible(false),
                new PopupOptions { IsDragImage = true }
            );
        }

        return source.On(PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent e) => e.Input.ArmDrag(e, node, entry));
    }

    /// <summary>
    /// Makes the node a drop target of values of type <typeparamref name="T"/> (design §7, "Drag and drop"): while a drag of a value
    /// it takes is over it, and over no drop target inside it that takes the value, it gets the enter, the moves, and the leave, and
    /// the drop when the value is released over it; it has the drag-over state meanwhile (<see cref="DragOver"/>). A target that is
    /// disabled, whose <see cref="DropTargetOptions{T}.CanDrop"/> is false, or that accepts none of the effects the source allows,
    /// takes nothing, and the drag goes on to the targets around it.
    /// </summary>
    /// <typeparam name="T">The type of the values it takes.</typeparam>
    /// <param name="target">The node.</param>
    /// <param name="options">What it does with the drags.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element DropTarget<T>(this Element target, DropTargetOptions<T> options)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(options);
        UiTree tree = target.TreeOrThrow();
        RequireScope();
        NodeHandle node = target.Node;
        var entry = new DropTargetEntry<T>(options, Scope.CurrentRef);
        Dictionary<NodeHandle, DropTargetEntry> targets = tree.DropTargets ??= [];
        targets[node] = entry;
        Scope.OnCleanup(() =>
        {
            if (tree.DropTargets is { } all && all.TryGetValue(node, out DropTargetEntry? there) && ReferenceEquals(there, entry))
            {
                _ = all.Remove(node);
            }
        });

        // A handler makes the node write a hit region, so that a drag over its empty parts finds it.
        return target.On(PointerEventKind.Move, EventPhase.Bubble, static (in PointerEvent _) => { });
    }

    /// <summary>
    /// Sets what the drag-over state lays over the node's open look, under the focus looks (design §2.13): while a drag of a value the
    /// node takes as a drop target is over it (<see cref="DropTarget{T}"/>).
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="style">The values that replace the node's while a drag it takes is over it.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public static Element DragOver(this Element element, PaintStyle style)
    {
        element.TreeOrThrow().SetDragOverStyle(element.Node, style);
        return element;
    }
}
