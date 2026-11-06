using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <summary>
/// How a dialog closes, what is behind it, and how it moves (design §13.1, "Menu and Dialog").
/// </summary>
public sealed record DialogOptions
{
    /// <summary>
    /// Gets a value indicating whether a press on the backdrop closes the dialog; it does not by default (design §7.3, "Closing").
    /// </summary>
    public bool DismissOnBackdropPress { get; init; }

    /// <summary>
    /// Gets the paint of the backdrop over the window; <see langword="null"/> paints nothing. The backdrop takes the pointer either
    /// way.
    /// </summary>
    public Paint? Backdrop { get; init; }

    /// <summary>
    /// Gets how the dialog comes in and leaves; <see langword="null"/> for at once.
    /// </summary>
    public ShowMotion? Motion { get; init; }
}

/// <content>
/// The modal dialog (design §13.1, "Menu and Dialog").
/// </content>
public static partial class Primitive
{
    /// <summary>
    /// Makes a modal dialog, open while the signal is true (design §13.1, "Dialog"): over the whole window, a backdrop with the
    /// content at its center. While it is open, the focus stays in it, Tab goes round it, and the nodes behind it take no pointer.
    /// It focuses the first focusable node of the content when it opens, and gives the focus back when it closes; Esc closes it and
    /// writes false to the signal.
    /// </summary>
    /// <param name="isOpen">Whether the dialog is open.</param>
    /// <param name="content">The dialog's content: its look, with what it holds.</param>
    /// <param name="options">How it closes, its backdrop, and how it moves; the defaults when <see langword="null"/>.</param>
    /// <returns>A node of no size, out of its parent's flow, which the dialog's popup belongs to.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Dialog(Signal<bool> isOpen, Func<Element> content, DialogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(isOpen);
        ArgumentNullException.ThrowIfNull(content);
        DialogOptions dialog = options ?? new DialogOptions();

        // The anchor is out of its parent's flow, so that it takes no room, nor a gap of a flex parent.
        Element anchor = Box();
        UiTree tree = anchor.Tree!;
        tree.SetStyle(
            anchor.Node,
            new LayoutStyle
            {
                Position = Position.Absolute,
                Width = Length.Px(0),
                Height = Length.Px(0),
            }
        );
        var popup = new PopupOptions
        {
            IsModal = true,
            Placement = PopupPlacement.Fill,
            DismissOnOutsidePress = dialog.DismissOnBackdropPress,
            Motion = dialog.Motion,
        };
        _ = PopupBlock.Create(tree, anchor.Node, isOpen, () => Backdrop(content(), dialog.Backdrop), popup, _ => FocusContent(tree));
        return anchor;
    }

    // The backdrop fills the popup and centers the content. The content's box takes the pointer, so that a press on it is not a
    // press on the backdrop, which the root's hit region is (design §7.3, "Closing").
    private static Element Backdrop(Element content, Paint? paint)
    {
        Element centered = Box(content).BlocksPointer();
        centered.Tree!.SetPartAccessibility(centered.Node, Roles.Dialog);
        Element backdrop = Box(centered);
        backdrop.Tree!.SetStyle(
            backdrop.Node,
            new LayoutStyle
            {
                Algorithm = new LayoutAlgorithm.Flex(
                    new FlexProps
                    {
                        Direction = FlexDirection.Column,
                        JustifyContent = new AlignContent(AlignContentKeyword.Center),
                        AlignItems = new AlignItems(AlignItemsKeyword.Center),
                    }
                ),
                Width = Length.Percent(1),
                Height = Length.Percent(1),
            }
        );
        return paint is { } fill ? backdrop.Background(fill) : backdrop;
    }

    // The first focusable node of the dialog takes the focus; with none, the focus behind it is emptied, so that no key goes to a
    // node the dialog covers.
    private static void FocusContent(UiTree tree)
    {
        if (tree.Focus.MoveToTabEnd(last: false))
        {
            return;
        }

        NodeHandle focused = tree.Focus.Focused;
        if (tree.Nodes.IsAlive(focused))
        {
            tree.Focus.Lose(focused);
        }
    }
}
