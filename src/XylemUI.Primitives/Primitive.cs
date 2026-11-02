using System.Numerics;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <summary>
/// Makes the headless primitives (design §13.1): parts with behavior and state but no look, which the app or a theme styles with
/// the method chain. Write <c>using static XylemUI.Primitives.Primitive;</c> to call them by name.
/// </summary>
public static partial class Primitive
{
    /// <summary>
    /// Makes a button: a box that holds the content, takes clicks, has the pressed pseudo-state while the left button is down on it,
    /// and takes the focus, from a press and from Tab (design §2.13). While it has the focus, Enter clicks it on each down, repeats
    /// included, and Space presses it on the down and clicks it on the up; the focus lost while Space is down releases it without a
    /// click (design §7, "The button's keys"). A key's click has a <see cref="PointerEvent.ClickCount"/> of 0. It sets no background
    /// and no color of text.
    /// </summary>
    /// <param name="content">The content: a node of the current tree with no parent.</param>
    /// <param name="onClick">What a click runs, in the scope current now (<see cref="ElementExtensions.OnClick"/>).</param>
    /// <returns>The button.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    /// <exception cref="ArgumentException">The content is no node, is of another tree, or has a parent.</exception>
    public static Element Button(Element content, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        Element button = Box(content).Focusable().OnClick(onClick);
        button.Tree!.SetPartAccessibility(button.Node, Roles.Button);
        var keys = new ButtonKeys(button.Tree!, button.Node);

        // An access text in the content clicks the button (design §13.1, "The window's access keys").
        RegisterHost(button.Tree!, button.Node, ButtonHost.Instance);
        return button
            .On(KeyEventKind.Down, EventPhase.Bubble, keys.Down)
            .On(KeyEventKind.Up, EventPhase.Bubble, keys.Up)
            .On(FocusEventKind.FocusOut, EventPhase.Bubble, keys.FocusOut);
    }

    /// <summary>
    /// Makes a button whose content is a text (<see cref="Elements.Text(string)"/>).
    /// </summary>
    /// <param name="label">The text.</param>
    /// <param name="onClick">What a click runs, in the scope current now.</param>
    /// <returns>The button.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Button(string label, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(onClick);
        return Button(Elements.Text(label), onClick);
    }

    // A node's origin in the window's logical pixels: its layout box, moved by the scroll of the boxes it is in.
    private static Vector2 WindowOrigin(UiTree tree, NodeHandle node)
    {
        Rect rect = tree.Nodes.GetRef(node).Layout.Rect;
        var point = new Vector2(rect.X, rect.Y);
        for (NodeHandle parent = tree.Nodes.GetRef(node).Parent; !parent.IsNone; parent = tree.Nodes.GetRef(parent).Parent)
        {
            Rect box = tree.Nodes.GetRef(parent).Layout.Rect;
            point += new Vector2(box.X, box.Y) - tree.ScrollOffsetOf(parent);
        }

        return point;
    }

    // The keys of one button, as Avalonia's Button takes them (Button.cs lines 307 to 344 and 465 to 469): its own bubble handlers,
    // for the keys with no modifier that the button itself is the target of (a focused descendant keeps its own keys).
    private sealed class ButtonKeys(UiTree tree, NodeHandle node)
    {
        private bool _spaceDown;

        public void Down(in KeyEvent e)
        {
            if (e.TargetNode != node || e.Modifiers != KeyModifiers.None)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.StopPropagation();
                e.Input.ClickFromKey(node, e.Timestamp);
            }
            else if (e.Key == Key.Space)
            {
                e.StopPropagation();
                if (!e.IsRepeat)
                {
                    _spaceDown = true;
                    tree.SetPseudoState(node, PseudoState.Pressed, on: true);
                }
            }
        }

        public void Up(in KeyEvent e)
        {
            // Pressed is gone when the button was disabled meanwhile, whose handlers missed the focus-out (PR #360's review).
            if (e.TargetNode != node || e.Key != Key.Space || !_spaceDown || (tree.Nodes.GetRef(node).Pseudo & PseudoState.Pressed) == 0)
            {
                _spaceDown &= e.Key != Key.Space;
                return;
            }

            e.StopPropagation();
            Release();
            e.Input.ClickFromKey(node, e.Timestamp);
        }

        // The focus lost while Space is down, the window's deactivation included, releases the button without a click.
        public void FocusOut(in FocusEvent e)
        {
            if (e.TargetNode == node && _spaceDown)
            {
                Release();
            }
        }

        private void Release()
        {
            _spaceDown = false;
            if (tree.Nodes.IsAlive(node))
            {
                tree.SetPseudoState(node, PseudoState.Pressed, on: false);
            }
        }
    }
}

/// <summary>
/// The accessibility information the parts give their nodes by default (design §13.1, §4.4.1): one object for each role, made
/// once.
/// </summary>
internal static class Roles
{
    /// <summary>
    /// Gets the information of a text field; its value comes from its text's signal.
    /// </summary>
    public static AccessibilityInfo Edit { get; } = new() { Role = SemanticsRole.Edit };

    /// <summary>
    /// Gets the information of a button.
    /// </summary>
    public static AccessibilityInfo Button { get; } = new() { Role = SemanticsRole.Button };

    /// <summary>
    /// Gets the information of a menu's panel.
    /// </summary>
    public static AccessibilityInfo Menu { get; } = new() { Role = SemanticsRole.Menu };

    /// <summary>
    /// Gets the information of a menu bar.
    /// </summary>
    public static AccessibilityInfo MenuBar { get; } = new() { Role = SemanticsRole.MenuBar };

    /// <summary>
    /// Gets the information of an item of a menu or a menu bar.
    /// </summary>
    public static AccessibilityInfo MenuItem { get; } = new() { Role = SemanticsRole.MenuItem };

    /// <summary>
    /// Gets the information of a select's box.
    /// </summary>
    public static AccessibilityInfo ComboBox { get; } = new() { Role = SemanticsRole.ComboBox };

    /// <summary>
    /// Gets the information of a select's list.
    /// </summary>
    public static AccessibilityInfo List { get; } = new() { Role = SemanticsRole.List };

    /// <summary>
    /// Gets the information of a choice of a select.
    /// </summary>
    public static AccessibilityInfo ListItem { get; } = new() { Role = SemanticsRole.ListItem };

    /// <summary>
    /// Gets the information of the choice a select shows.
    /// </summary>
    public static AccessibilityInfo SelectedListItem { get; } = new() { Role = SemanticsRole.ListItem, States = SemanticsStates.Selected };

    /// <summary>
    /// Gets the information of a tooltip.
    /// </summary>
    public static AccessibilityInfo ToolTip { get; } = new() { Role = SemanticsRole.ToolTip };

    /// <summary>
    /// Gets the information of a dialog's content.
    /// </summary>
    public static AccessibilityInfo Dialog { get; } = new() { Role = SemanticsRole.Dialog };
}
