namespace XylemUI;

/// <content>
/// What row AX1 adds to the accessibility of a node (design §4.4.1, "The shape AX1 adds"): the handlers of the actions other than a
/// click, the relations to other nodes, and the nodes a draw function adds.
/// </content>
public static partial class ElementExtensions
{
    /// <summary>
    /// Adds the handler of assistive technology's Increment (design §4.4.1, "Actions other than a click"): it is called on the UI
    /// thread, not on a disabled node. On Windows, whose UIA has no Increment, a number set on a node with a range and no
    /// <see cref="OnSetRangeValue"/> that is larger than its value calls it once. It replaces the node's handler of Increment, and is
    /// removed when the current scope is disposed.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element OnIncrement(this Element element, Action handler) =>
        AddAccessibilityHandler(element, handler, static (handlers, h) => handlers.Increment = h, static handlers => handlers.Increment);

    /// <summary>
    /// Adds the handler of assistive technology's Decrement, as <see cref="OnIncrement"/> adds that of Increment: on Windows, a number
    /// set on a node with a range and no <see cref="OnSetRangeValue"/> that is smaller than its value calls it once.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element OnDecrement(this Element element, Action handler) =>
        AddAccessibilityHandler(element, handler, static (handlers, h) => handlers.Decrement = h, static handlers => handlers.Decrement);

    /// <summary>
    /// Adds the handler of a number assistive technology sets on a node with a range (<see cref="AccessibilityInfo.Range"/>; UIA's
    /// RangeValue.SetValue): it takes the number brought into the range, never NaN, on the UI thread, and is not called on a disabled
    /// node. A node with a range and none of this handler, <see cref="OnIncrement"/>, and <see cref="OnDecrement"/> is read-only to
    /// assistive technology. It replaces the node's handler of a number, and is removed when the current scope is disposed.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element OnSetRangeValue(this Element element, Action<double> handler) =>
        AddAccessibilityHandler(element, handler, static (handlers, h) => handlers.SetRangeValue = h, static handlers => handlers.SetRangeValue);

    /// <summary>
    /// Adds the handler of assistive technology's Expand on a node that opens and closes and anchors no popup (a tree's item, a
    /// disclosure; <see cref="SemanticsStates.Expandable"/>): it is called on the UI thread, not on a disabled node. A popup's anchor
    /// opens its popup instead. It replaces the node's handler of Expand, and is removed when the current scope is disposed.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element OnExpand(this Element element, Action handler) =>
        AddAccessibilityHandler(element, handler, static (handlers, h) => handlers.Expand = h, static handlers => handlers.Expand);

    /// <summary>
    /// Adds the handler of assistive technology's Collapse, as <see cref="OnExpand"/> adds that of Expand.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element OnCollapse(this Element element, Action handler) =>
        AddAccessibilityHandler(element, handler, static (handlers, h) => handlers.Collapse = h, static handlers => handlers.Collapse);

    /// <summary>
    /// Adds a node whose text names this one (design §4.4.1, "Relations"; ARIA's <c>aria-labelledby</c>), after those added before:
    /// the name is the app's <see cref="AccessibilityInfo.Name"/>, else the text of these nodes joined by spaces, else the part's
    /// name, else the text of the content. A node that is gone, hidden, or excluded names nothing, and names again when it comes back.
    /// Both nodes are made before the call.
    /// </summary>
    /// <param name="element">The node that is named, such as a field.</param>
    /// <param name="label">The node whose text names it.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentException"><paramref name="label"/> is the node itself, not a node of its tree, or a node removed.</exception>
    public static Element LabelledBy(this Element element, Element label) => Relate(element, AccessibilityRelation.LabelledBy, label, nameof(label));

    /// <summary>
    /// Adds a node whose text describes this one (ARIA's <c>aria-describedby</c>), after those added before. On Windows its text joins
    /// the node's description (design §4.4.1, "Folding on Windows").
    /// </summary>
    /// <param name="element">The node that is described.</param>
    /// <param name="description">The node whose text describes it.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentException"><paramref name="description"/> is the node itself, not a node of its tree, or a node removed.</exception>
    public static Element DescribedBy(this Element element, Element description) =>
        Relate(element, AccessibilityRelation.DescribedBy, description, nameof(description));

    /// <summary>
    /// Sets the node whose text says why this one is not valid (ARIA's <c>aria-errormessage</c>), in place of the one set before. On
    /// Windows its text starts the node's description while the node is <see cref="SemanticsStates.Invalid"/>.
    /// </summary>
    /// <param name="element">The node that is not valid, such as a field.</param>
    /// <param name="message">The node of the message.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentException"><paramref name="message"/> is the node itself, not a node of its tree, or a node removed.</exception>
    public static Element ErrorMessage(this Element element, Element message) => Relate(element, null, message, nameof(message));

    /// <summary>
    /// Adds a node this one controls (ARIA's <c>aria-controls</c>; UIA's ControllerFor), after those added before: the panel of a tab.
    /// </summary>
    /// <param name="element">The node that controls.</param>
    /// <param name="controlled">The node it controls.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentException"><paramref name="controlled"/> is the node itself, not a node of its tree, or a node removed.</exception>
    public static Element Controls(this Element element, Element controlled) =>
        Relate(element, AccessibilityRelation.Controls, controlled, nameof(controlled));

    /// <summary>
    /// Sets the signal of the node that is active inside this one while this one holds the focus (ARIA's
    /// <c>aria-activedescendant</c>): the option a field's list has chosen, also in a popup. Moving to another option writes the
    /// signal; <c>default</c>, and an element of another tree, is none. It replaces what was set before.
    /// </summary>
    /// <param name="element">The node that holds the focus.</param>
    /// <param name="descendant">The signal of the active node.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element ActiveDescendant(this Element element, IReadOnlySignal<Element> descendant)
    {
        ArgumentNullException.ThrowIfNull(descendant);
        element.TreeOrThrow().SetActiveDescendant(element.Node, descendant, null);
        return element;
    }

    /// <summary>
    /// Sets the signal of the key of the node that is active among the nodes this one's draw function adds
    /// (<see cref="AccessibleChildren(Element, AccessibleChildrenFunc)"/>); <see langword="null"/>, and a key no node has now, is none.
    /// It replaces what was set before.
    /// </summary>
    /// <param name="element">The node that holds the focus and draws the nodes.</param>
    /// <param name="childKey">The signal of the key.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element ActiveDescendant(this Element element, IReadOnlySignal<int?> childKey)
    {
        ArgumentNullException.ThrowIfNull(childKey);
        element.TreeOrThrow().SetActiveDescendant(element.Node, null, childKey);
        return element;
    }

    /// <summary>
    /// Sets the function that adds the nodes of the accessibility tree a node drawn by a draw function holds (design §4.4.1, "The
    /// nodes of a draw function"), after its children. It is called only while assistive technology reads the window. The node needs a
    /// role whose node holds children (not one whose content is its value, such as <see cref="SemanticsRole.Image"/>).
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="build">The function.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element AccessibleChildren(this Element element, AccessibleChildrenFunc build)
    {
        ArgumentNullException.ThrowIfNull(build);
        element.TreeOrThrow().SetAccessibleChildren(element.Node, build);
        return element;
    }

    // Puts a handler in its place in the node's handlers, and takes it out with the scope, when it is still the one there.
    private static Element AddAccessibilityHandler<T>(
        Element element,
        T handler,
        Action<AccessibilityHandlers, T?> set,
        Func<AccessibilityHandlers, T?> get
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        UiTree tree = element.TreeOrThrow();
        NodeHandle node = element.Node;
        RequireScope();
        set(tree.AccessibilityHandlersFor(node), handler);
        tree.AccessibilityHandlersChanged(node);
        Scope.OnCleanup(() =>
        {
            if (tree.Nodes.IsAlive(node) && tree.AccessibilityHandlersOf(node) is { } handlers && ReferenceEquals(get(handlers), handler))
            {
                set(handlers, null);
                tree.AccessibilityHandlersChanged(node);
            }
        });
        return element;
    }

    // Adds a relation to a node of the same tree that is not the node itself; a null kind is the error message.
    private static Element Relate(Element element, AccessibilityRelation? kind, Element target, string name)
    {
        UiTree tree = element.TreeOrThrow();
        if (target.Tree != tree || target.Node == element.Node || !tree.Nodes.IsAlive(target.Node))
        {
            throw new ArgumentException("A relation names another node of the same tree.", name);
        }

        tree.AddAccessibilityRelation(element.Node, kind, target.Node);
        return element;
    }
}
