using System.Numerics;
using System.Text;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <summary>
/// How a menu is placed and moves (design §13.1, "Menu and Dialog").
/// </summary>
public sealed record MenuOptions
{
    /// <summary>
    /// Gets where the menu goes against its anchor; below it from the left edge by default.
    /// </summary>
    public PopupPlacement Placement { get; init; } = PopupPlacement.Below;

    /// <summary>
    /// Gets how the menu comes in and leaves; <see langword="null"/> for at once.
    /// </summary>
    public ShowMotion? Motion { get; init; }
}

/// <content>
/// The menus (design §13.1, "Menu and Dialog").
/// </content>
public static partial class Primitive
{
    // The owner of a menu's popup: a chosen item closes its popup and the menus it was opened from with it, up to the first popup
    // that is not a menu.
    private static readonly object MenuMark = new();

    // The context of the menu whose panel is being made on this thread, for the items made in it; none outside a panel.
    [ThreadStatic]
    private static MenuContext? _building;

    /// <summary>
    /// Opens a menu against the node while the signal is true (design §13.1, "Menu and Dialog"): a popup that holds the panel and
    /// focuses its first enabled item when it opens. Up and Down move the focus between the panel's items, round at the ends; Home
    /// and End move it to the first and the last; Esc closes the menu, and a press outside it, Esc, or the window's deactivation
    /// write false to the signal. The menu has no look of its own: the panel's node is its look.
    /// </summary>
    /// <param name="anchor">The node the menu is placed against.</param>
    /// <param name="isOpen">Whether the menu is open.</param>
    /// <param name="panel">The menu's panel: its look, with its items (<see cref="MenuItem(Element, Action, bool)"/>, <see cref="SubmenuItem"/>).</param>
    /// <param name="options">Where the menu goes and how it moves; below the node, at once, when <see langword="null"/>.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope, or <paramref name="panel"/> returns no node of the tree
    /// with no parent.</exception>
    public static Element Menu(this Element anchor, Signal<bool> isOpen, Func<Element> panel, MenuOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(isOpen);
        ArgumentNullException.ThrowIfNull(panel);
        UiTree tree = anchor.TreeOrThrow();
        RequireScope(nameof(Menu));
        MenuOptions menu = options ?? new MenuOptions();
        _ = OpenMenu(tree, anchor.Node, isOpen, panel, new PopupOptions { Placement = menu.Placement, Motion = menu.Motion }, FocusFirst(tree));
        return anchor;
    }

    /// <summary>
    /// Makes a menu's item (design §13.1, "Menu and Dialog"): a box that holds the content and takes the focus. A press on it, Enter,
    /// or Space chooses it: the menus it is in close and give the focus back to where they were opened from, and then the action
    /// runs. The pointer coming over it gives it the focus and closes a submenu open beside it. A disabled item is not chosen and is
    /// passed over by the keys.
    /// </summary>
    /// <param name="content">The content: a node of the current tree with no parent.</param>
    /// <param name="onSelect">What choosing the item runs, after the menus closed.</param>
    /// <param name="enabled">Whether the item can be chosen.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element MenuItem(Element content, Action onSelect, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        Element item = Box(content).Focusable().Disabled(!enabled);
        var state = new MenuItemState(item.Tree!, item.Node, onSelect, submenu: null);
        return state.Attach(item);
    }

    /// <summary>
    /// Makes a menu's item that names an action (design §7, "Menu items that name actions"): choosing it closes the menus it is in
    /// and dispatches the action from the node the focus was on before the outermost of them opened, its context node; for a
    /// <see cref="ContextMenu"/> whose node the focus was not in, that is the menu's node. The item is disabled while the action
    /// would not be handled from there (<see cref="ElementExtensions.CanDispatch(Element, IAction, Element?)"/>), and its content
    /// is given the keys that ask for the action there (<see cref="ElementExtensions.KeyBindingFor(Element, IAction, Element?)"/>),
    /// to show: the app draws them, since the item has no look of its own. An item made outside a menu's panel reads and
    /// dispatches from the focused node.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="content">
    /// Makes the content, a node of the current tree with no parent, from the row of the keymap whose keys ask for the action; the
    /// row is <see langword="null"/> when no keys do, and changes when the keymap's rows do.
    /// </param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element MenuItem(IAction action, Func<Computed<KeyBinding?>, Element> content)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(content);
        UiTree tree =
            UiTree.Current ?? throw new InvalidOperationException("MenuItem is made while a window's root function runs; no tree is current.");
        RequireScope(nameof(MenuItem));
        var named = new NamedAction(tree, action, _building);
        Element item = Box(content(Computed.Create(named.Keys))).Focusable().Disabled(Computed.Create(named.Unavailable));
        var state = new MenuItemState(tree, item.Node, named.Dispatch, submenu: null);
        return state.Attach(item);
    }

    /// <summary>
    /// Makes an item that opens a submenu to its right (design §13.1, "SubmenuItem"): the pointer coming over it, a press on it, the
    /// Right key, Enter, or Space opens the submenu, and the keys also focus the submenu's first item. In the submenu, Left and Esc
    /// close it and give the focus back to this item. The pointer coming over another item of this item's menu closes it.
    /// </summary>
    /// <param name="content">The content: a node of the current tree with no parent.</param>
    /// <param name="panel">The submenu's panel, as for <see cref="Menu"/>.</param>
    /// <param name="enabled">Whether the item can open its submenu.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element SubmenuItem(Element content, Func<Element> panel, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(panel);
        Element item = Box(content).Focusable().Disabled(!enabled);
        UiTree tree = item.Tree!;
        var state = new MenuItemState(tree, item.Node, onSelect: null, new Submenu(tree, item.Node, panel));
        return state.Attach(item);
    }

    /// <summary>
    /// Opens a menu from the node (design §13.1, "ContextMenu"): at the pointer on the up of the right button over the node, and below
    /// the node on the down of Shift+F10 or the up of the application key while the focus is in it. The menu is a <see cref="Menu"/>
    /// in all else.
    /// </summary>
    /// <param name="target">The node the menu opens from.</param>
    /// <param name="panel">The menu's panel, as for <see cref="Menu"/>.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element ContextMenu(this Element target, Func<Element> panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        UiTree tree = target.TreeOrThrow();
        RequireScope(nameof(ContextMenu));
        var trigger = new ContextTrigger(new ContextMenuHost(tree, target.Node, panel));
        return target
            .On(PointerEventKind.Up, EventPhase.Bubble, trigger.Up)
            .OnAction<MenuActions.OpenContextMenu>(trigger.Ask)
            .On(KeyEventKind.Up, EventPhase.Bubble, trigger.KeyUp);
    }

    // Whether the up of a key asks for the context menu: the application key's (WM_CONTEXTMENU's keys). The other key, the down of
    // Shift+F10, is a row of the keymap that asks for MenuActions.OpenContextMenu.
    private static bool IsMenuKeyUp(in KeyEvent e) => e.Key == Key.Apps && e.Modifiers == KeyModifiers.None;

    private static void RequireScope(string name)
    {
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException($"{name} is made inside a scope, which owns its popup; there is no current scope.");
        }
    }

    // A menu's popup: its panel, which takes the keys that move between its items, in a popup marked as a menu's.
    private static PopupBlock OpenMenu(
        UiTree tree,
        NodeHandle anchor,
        Signal<bool> isOpen,
        Func<Element> panel,
        PopupOptions options,
        Action<NodeHandle>? opened,
        bool contextMenu = false
    )
    {
        // A submenu is made in its menu's panel and has that menu's context; a menu made elsewhere finds its own each time it opens.
        MenuContext? outer = _building;

        // A menu opened from the keys shows the access keys while it is open (design §13.1, "Access keys"): its part's scope, which
        // goes when the menu closes, ends what it began.
        MenuCues? cues = null;
        return PopupBlock.Create(
            tree,
            anchor,
            isOpen,
            () =>
            {
                cues = new MenuCues(tree);
                Scope.OnCleanup(cues.Release);
                MenuContext? previous = _building;
                _building = outer ?? new MenuContext(ContextNode(tree, anchor, contextMenu));
                Element menuPanel;
                try
                {
                    menuPanel = panel();
                }
                finally
                {
                    _building = previous;
                }

                tree.SetPartAccessibility(menuPanel.Node, Roles.Menu);
                return menuPanel
                    .PartKeyContext(PrimitiveKeyBindings.MenuContext)
                    .On(KeyEventKind.Down, EventPhase.Bubble, PanelKeys)
                    .OnActionsOf<MenuAction>(PanelAction);
            },
            options,
            root =>
            {
                cues?.Hold();
                opened?.Invoke(root);
            },
            MenuMark
        );
    }

    // The context node of a menu that opens (design §7, "Menu items that name actions"): the node the focus is on, which the menu
    // has not taken yet; for a menu of a selected menu bar, whose item has the focus, the node the bar gives the focus back to.
    // A context menu whose node the focus is not in has that node.
    private static NodeHandle ContextNode(UiTree tree, NodeHandle anchor, bool contextMenu)
    {
        NodeHandle before = BarItemOf(tree, anchor) is { Bar: { Selected: true } bar } ? bar.Restore : tree.Focus.Focused;
        if (!contextMenu)
        {
            return before;
        }

        for (NodeHandle at = before; !at.IsNone && tree.Nodes.IsAlive(at); at = tree.KeyParent(at))
        {
            if (at == anchor)
            {
                return before;
            }
        }

        return anchor;
    }

    // A menu that opens focuses its first enabled item (Avalonia's flyout focuses its first tab stop, PopupFlyoutBase.cs lines 315
    // to 326). The popup's scope is the top one there, so the order of Tab is the panel's.
    private static Action<NodeHandle> FocusFirst(UiTree tree) => _ => tree.Focus.MoveToTabEnd(last: false);

    // The access keys of a panel's items (design §13.1, "Access keys"); the other keys of a panel are rows of the keymap.
    private static void PanelKeys(in KeyEvent e)
    {
        UiTree tree = e.Input.Tree;

        // A letter or a digit, with Alt or Shift or neither (either case), is an access key of the panel's items.
        if (
            (e.Modifiers & ~(KeyModifiers.Alt | KeyModifiers.Shift)) == 0
            && Rune.IsLetterOrDigit(e.KeyChar)
            && e.KeyChar.IsBmp
            && ChooseByKey(tree, e.CurrentNode, (char)e.KeyChar.Value)
        )
        {
            e.StopPropagation();
        }
    }

    // The actions of a panel, from its focused item (design §13.1, "Keys"; §7, "The default keys and actions"). The items are the
    // focusable nodes of the panel's popup, in the order of Tab, which passes over the disabled ones; a submenu open beside them
    // closes first, so that its scope is not the one moved in. A panel handles a move also when the focus does not change, and
    // an arrow it has no use for: the keys of a menu do not go on to the page under it. A menu open from a menu bar leaves such an
    // arrow to the bar, which opens the menu before or after.
    private static void PanelAction(in ActionEvent<MenuAction> e)
    {
        UiTree tree = e.CurrentTarget.Tree!;
        PopupHandle? popup = tree.Popups.PopupContaining(e.CurrentNode);
        switch (e.Action.Step)
        {
            case MenuStep.SelectPrevious or MenuStep.SelectNext:
                CloseSubmenus(tree, popup, except: default);
                _ = tree.Focus.MoveInTabOrder(backward: e.Action.Step == MenuStep.SelectPrevious);
                break;
            case MenuStep.SelectFirst or MenuStep.SelectLast:
                CloseSubmenus(tree, popup, except: default);
                _ = tree.Focus.MoveToTabEnd(last: e.Action.Step == MenuStep.SelectLast);
                break;
            case MenuStep.Confirm:
                // Enter and Space on an item do what its access key does: a submenu opens with its first item focused; an item
                // is chosen.
                if (!ActivateItem(tree, e.TargetNode))
                {
                    e.Propagate();
                }

                break;
            case MenuStep.CloseSubmenu when popup is { Parent: { } parent } && IsMenu(parent):
                // A submenu closes, and its scope gives the focus back to the item that opened it.
                popup.Block.Dismiss();
                break;
            default:
                // The left arrow in a menu that is no submenu, and the right arrow on an item with no submenu (an item with one
                // took the action before the panel).
                if (IsOpenFromBar(tree, popup))
                {
                    e.Propagate();
                }

                break;
        }
    }

    // Whether the outermost menu of a menu's popup was opened from an item of a menu bar.
    private static bool IsOpenFromBar(UiTree tree, PopupHandle? popup)
    {
        PopupHandle? outer = null;
        for (; popup is not null && IsMenu(popup); popup = popup.Parent)
        {
            outer = popup;
        }

        return outer is not null && BarItemOf(tree, outer.Anchor) is not null;
    }

    private static bool IsMenu(PopupHandle popup) => ReferenceEquals(popup.Block.Owner, MenuMark);

    // Closes the submenus open from a popup, but the one of the item given.
    private static void CloseSubmenus(UiTree tree, PopupHandle? popup, NodeHandle except)
    {
        if (popup is null)
        {
            return;
        }

        IReadOnlyList<PopupHandle> open = tree.Popups.Opened;
        for (int i = open.Count - 1; i >= 0; i--)
        {
            if (i < open.Count && open[i] is { Closing: false } child && child.Parent == popup && child.Anchor != except)
            {
                child.Block.Dismiss();
            }
        }
    }

    // Closes the menus a chosen item is in: its popup and the menus it was opened from, as one (GPUI's context menu dismisses
    // before it dispatches, ui/src/components/context_menu.rs lines 717 to 720). The outer one gives the focus back; a menu bar's
    // menu also leaves the bar, which gives the focus back to where it was before the bar was selected.
    private static void CloseMenus(UiTree tree, NodeHandle item)
    {
        PopupHandle? outer = null;
        for (PopupHandle? popup = tree.Popups.PopupContaining(item); popup is not null && IsMenu(popup); popup = popup.Parent)
        {
            outer = popup;
        }

        if (outer is null)
        {
            return;
        }

        outer.Block.Dismiss();
        if (BarItemOf(tree, outer.Anchor) is { } barItem)
        {
            barItem.Bar?.Leave(restore: true);
        }
    }

    // Where the items of a menu read and dispatch their actions: the node, or none for the root.
    private sealed class MenuContext(NodeHandle node)
    {
        public NodeHandle Node { get; } = node;
    }

    // An item's action, read and dispatched at its menu's context node; with no menu, at the focused node. With a context node that
    // has gone, or none, the route starts where it does with no focus (FocusManager.KeyTarget).
    private sealed class NamedAction(UiTree tree, IAction action, MenuContext? context)
    {
        private NodeHandle Start =>
            context is null ? tree.Focus.KeyTarget
            : tree.Nodes.IsAlive(context.Node) ? context.Node
            : tree.Nodes.IsAlive(tree.KeyRoot) ? tree.KeyRoot
            : tree.Root;

        public KeyBinding? Keys()
        {
            tree.Keymap.TrackChanges();
            tree.Actions.TrackAvailability(atFocus: context is null);
            return KeymapLookup.BindingFor(tree, Start, action.GetType(), action);
        }

        public bool Unavailable()
        {
            tree.Actions.TrackAvailability(atFocus: context is null);
            return !tree.Actions.IsAvailable(Start, action);
        }

        public void Dispatch() => _ = tree.Actions.Dispatch(Start, action);
    }

    // One item of a menu: what choosing it does, and the submenu it opens, if any.
    private sealed class MenuItemState(UiTree tree, NodeHandle node, Action? onSelect, Submenu? submenu)
    {
        public Element Attach(Element item)
        {
            RegisterItem(tree, node, Activate);
            tree.SetPartAccessibility(item.Node, Roles.MenuItem);
            item = item.OnClick(Click).On(PointerEventKind.Enter, EventPhase.Bubble, Enter);

            // The panel takes the item's other actions; only an item with a submenu has a handler of its own.
            return submenu is null ? item : item.OnAction<MenuActions.OpenSubmenu>(OpenSubmenu);
        }

        // Its access key typed in its menu: a submenu opens with its first item focused; an item is chosen.
        public void Activate()
        {
            if (submenu is not null)
            {
                _ = tree.Focus.Focus(node);
                submenu.Open(focusFirst: true);
                return;
            }

            CloseMenus(tree, node);
            onSelect!();
        }

        // A press: a submenu opens and keeps the focus where it is; an item is chosen.
        private void Click()
        {
            if (submenu is not null)
            {
                submenu.Open(focusFirst: false);
                return;
            }

            CloseMenus(tree, node);
            onSelect!();
        }

        // The right arrow on the item opens its submenu.
        private void OpenSubmenu(in ActionEvent<MenuActions.OpenSubmenu> e)
        {
            if (e.TargetNode != node)
            {
                e.Propagate();
                return;
            }

            submenu!.Open(focusFirst: true);
        }

        // The pointer over the item gives it the focus, closes the submenu of another item beside it, and opens its own.
        private void Enter(in PointerEvent e)
        {
            if (!tree.Focus.CanFocus(node))
            {
                return;
            }

            CloseSubmenus(tree, tree.Popups.PopupContaining(node), except: node);
            _ = tree.Focus.Focus(node);
            submenu?.Open(focusFirst: false);
        }
    }

    // The access keys a menu opened from the keys shows while it is open.
    private sealed class MenuCues(UiTree tree)
    {
        private bool _held;

        public void Hold()
        {
            if (!_held && tree.Focus.LastInputWasKeyboard)
            {
                _held = true;
                tree.HoldMenuCues();
            }
        }

        public void Release()
        {
            if (_held)
            {
                _held = false;
                tree.ReleaseMenuCues();
            }
        }
    }

    // The submenu of an item: a menu to its right, open while its signal is true.
    private sealed class Submenu
    {
        private readonly UiTree _tree;
        private readonly Signal<bool> _open = Signal.Create(false);
        private readonly PopupBlock _block;
        private bool _focusFirst;

        public Submenu(UiTree tree, NodeHandle item, Func<Element> panel)
        {
            _tree = tree;
            _block = OpenMenu(tree, item, _open, panel, new PopupOptions { Placement = PopupPlacement.Right }, Opened);
        }

        // Opens the submenu; the keys also focus its first item, there or in a submenu open already.
        public void Open(bool focusFirst)
        {
            if (_block.Popup is { Closing: false } popup)
            {
                // A submenu open from this one closes first, so that its scope is not the top one the focus moves in.
                if (focusFirst)
                {
                    CloseSubmenus(_tree, popup, except: default);
                    _ = _tree.Focus.MoveToTabEnd(last: false);
                }

                return;
            }

            _focusFirst = focusFirst;
            _open.Set(true);
        }

        private void Opened(NodeHandle root)
        {
            if (_focusFirst)
            {
                _ = _tree.Focus.MoveToTabEnd(last: false);
            }
        }
    }

    // What ContextMenu takes: the right button's up over the node, and the keys that ask for the menu while the focus is in it.
    private sealed class ContextTrigger(ContextMenuHost host)
    {
        public void Up(in PointerEvent e)
        {
            if (e.Button == PointerButton.Right)
            {
                e.StopPropagation();
                host.OpenAt(e.Position);
            }
        }

        public void Ask(in ActionEvent<MenuActions.OpenContextMenu> e) => host.OpenBelow();

        public void KeyUp(in KeyEvent e)
        {
            if (IsMenuKeyUp(e))
            {
                e.StopPropagation();
                host.OpenBelow();
            }
        }
    }

    // A context menu's popup: a menu opened at a point, or below its node, each time it is asked for (design §13.1, "ContextMenu").
    // The text field and the selection area open theirs with it.
    private sealed class ContextMenuHost
    {
        private readonly Signal<bool> _open = Signal.Create(false);
        private readonly PopupBlock _block;

        public ContextMenuHost(UiTree tree, NodeHandle target, Func<Element> panel) =>
            _block = OpenMenu(tree, target, _open, panel, new PopupOptions(), FocusFirst(tree), contextMenu: true);

        // Whether the menu is open or leaving: while it closes, it gives the focus back to its node.
        public bool IsOpen => _block.Popup is not null;

        // At a point of the window, in logical pixels: the pointer's, or the caret's.
        public void OpenAt(Vector2 point) => Open(PopupPlacement.AtPoint(point));

        // Below the node, from its left edge.
        public void OpenBelow() => Open(PopupPlacement.Below);

        // A menu open already is closed first, so that it opens again at the new place.
        private void Open(PopupPlacement placement)
        {
            if (_block.Popup is { Closing: false })
            {
                _block.Dismiss();
            }

            _block.Options = _block.Options with { Placement = placement };
            _open.Set(true);
        }
    }
}
