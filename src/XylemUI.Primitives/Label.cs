using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <content>
/// The label (design §13.1, "The label").
/// </content>
public static partial class Primitive
{
    /// <summary>
    /// Makes a label for a node (design §13.1, "The label", Avalonia's <c>Label</c>): a box that holds the content, takes no focus
    /// itself, and gives the focus to <paramref name="target"/> on a press of the left button and on the access key of an
    /// <see cref="AccessText"/> in the content (Alt and its letter).
    /// </summary>
    /// <param name="content">The content: a node of the current tree with no parent, usually an <see cref="AccessText"/>.</param>
    /// <param name="target">The node the label gives the focus to.</param>
    /// <returns>The label.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Label(Element content, Element target)
    {
        Element label = Box(content);
        UiTree tree = label.Tree!;
        NodeHandle to = target.Node;
        RegisterHost(tree, label.Node, new LabelHost(to));
        return label.On(
            PointerEventKind.Down,
            EventPhase.Bubble,
            (in PointerEvent e) =>
            {
                if (e.Button == PointerButton.Left && tree.Focus.CanFocus(to))
                {
                    _ = tree.Focus.Focus(to);
                }
            }
        );
    }

    // Whether a node shows: neither it nor an ancestor is hidden, nor disabled, and it is in a root.
    private static bool Shows(UiTree tree, NodeHandle node)
    {
        if (!tree.Nodes.IsAlive(node) || !tree.Nodes.GetRef(node).IsAttached)
        {
            return false;
        }

        for (NodeHandle at = node; !at.IsNone; at = tree.Nodes.GetRef(at).Parent)
        {
            ref UiNode n = ref tree.Nodes.GetRef(at);
            if (n.Layout.Style.Hidden || (n.Pseudo & PseudoState.Disabled) != 0)
            {
                return false;
            }
        }

        return true;
    }
}
