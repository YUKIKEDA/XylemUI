namespace XylemUI;

/// <content>
/// The visibility (design §3.2, "Nodes not shown").
/// </content>
public static partial class ElementExtensions
{
    /// <summary>
    /// Sets whether the node and its subtree are shown while the node keeps its place (design §3.2, "Nodes not shown"; CSS's
    /// <c>visibility: hidden</c>, GPUI's <c>Visibility::Hidden</c>, Flutter's <c>Visibility</c> with <c>maintainSize</c>). A hidden
    /// node keeps its size and the places of its siblings, and it and its whole subtree are neither drawn nor hit by the pointer,
    /// which reaches the nodes behind them; they take no focus, Tab passes over them, and they are left out of the accessibility
    /// tree. A focused node in the subtree loses the focus. A descendant set to <see cref="XylemUI.Visibility.Visible"/> stays hidden. A
    /// change runs no layout.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="visibility">The visibility.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="visibility"/> is not a value of <see cref="XylemUI.Visibility"/>.</exception>
    [UiThreadOnly]
    public static Element Visibility(this Element element, Visibility visibility)
    {
        element.TreeOrThrow().SetVisibility(element.Node, visibility);
        return element;
    }

    /// <summary>
    /// Sets whether the node and its subtree are shown from a signal, written again whenever it changes.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="visibility">The signal.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope to own the effect.</exception>
    public static Element Visibility(this Element element, IReadOnlySignal<Visibility> visibility) =>
        Bind(element, NodeProperties.Visibility, visibility);
}
