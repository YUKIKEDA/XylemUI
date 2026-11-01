using System.Text;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <content>
/// The menu bar (design §13.1, "The window's access keys, the label, and the menu bar").
/// </content>
public static partial class Primitive
{
    /// <summary>
    /// Makes a menu bar (design §13.1, "The menu bar"): a row of <see cref="MenuBarItem"/>s with no look of its own. Alt alone (down
    /// and up with no other key between) or F10 selects it: the first item takes the focus, and no menu opens. While it is selected,
    /// Left and Right move between the items, round at the ends; Down, Enter, and Space open the item's menu with its first item
    /// focused, and Up with its last; a letter is an item's access key without Alt. Alt, F10, and Esc while no menu is open leave it
    /// and give the focus back to where it was; Esc while a menu is open closes the menu only. Choosing a menu's item leaves it too,
    /// and so does the focus moving out of the bar and its menus. While a menu is open, Left and Right that the menu does not take
    /// open the next item's menu. The items take no focus while the bar is not selected, so Tab does not stop at them.
    /// </summary>
    /// <param name="items">The items, each made by <see cref="MenuBarItem"/>.</param>
    /// <returns>The menu bar.</returns>
    /// <exception cref="ArgumentException">An item is not made by <see cref="MenuBarItem"/>.</exception>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element MenuBar(params ReadOnlySpan<Element> items)
    {
        Element bar = Row(items);
        UiTree tree = bar.Tree!;
        RequireScope(nameof(MenuBar));
        var state = new MenuBarState(tree, bar.Node);
        foreach (Element item in items)
        {
            MenuBarItemState barItem =
                BarItemOf(tree, item.Node) ?? throw new ArgumentException("A menu bar takes the items MenuBarItem makes.", nameof(items));
            barItem.Bar = state;
            state.Items.Add(barItem);
        }

        AccessKeyRegistry registry = RegistryOf(tree);
        registry.Bars.Add(state);
        tree.MenuBarKeys ??= MenuBarKeyOf;
        Scope.OnCleanup(() => registry.Bars.Remove(state));
        return bar.On(FocusEventKind.FocusOut, EventPhase.Bubble, state.FocusOut);
    }

    /// <summary>
    /// Makes an item of a <see cref="MenuBar"/> (design §13.1, "The menu bar"): a box that holds the content and opens the menu of
    /// the panel below it. A press on it opens the menu, and a press while it is open closes it; the pointer coming over it while
    /// another item's menu is open opens its menu in place of that one. The access key of an <see cref="AccessText"/> in the content
    /// (Alt and its letter, from anywhere in the window) selects the bar and opens the menu with its first item focused.
    /// </summary>
    /// <param name="content">The content: a node of the current tree with no parent, usually an <see cref="AccessText"/>.</param>
    /// <param name="panel">The menu's panel, as for <see cref="Menu"/>.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element MenuBarItem(Element content, Func<Element> panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        Element item = Box(content);
        UiTree tree = item.Tree!;
        RequireScope(nameof(MenuBarItem));
        var state = new MenuBarItemState(tree, item.Node, panel);
        RegisterHost(tree, item.Node, new MenuBarItemHost(state));
        RegisterItem(tree, item.Node, state.OpenFromKey);
        return item.OnClick(state.Click)
            .On(KeyEventKind.Down, EventPhase.Bubble, state.KeyDown)
            .On(PointerEventKind.Enter, EventPhase.Bubble, state.Enter);
    }

    // The menu bar item a node is, if it is one.
    private static MenuBarItemState? BarItemOf(UiTree tree, NodeHandle node) =>
        AccessKeys.TryGetValue(tree, out AccessKeyRegistry? registry)
        && registry.Hosts.TryGetValue(node, out AccessKeyHost? host)
        && host is MenuBarItemHost barItem
            ? barItem.Item
            : null;

    // Alt alone and F10 (design §13.1, "The menu bar"), for the first menu bar of the window that shows. Alt's down and F10 leave a
    // bar that is selected or has a menu open; Alt's up and F10 select one, while no popup but a tooltip is open.
    private static bool MenuBarKeyOf(WindowInput input, MenuBarKey key)
    {
        UiTree tree = input.Tree;
        if (!AccessKeys.TryGetValue(tree, out AccessKeyRegistry? registry))
        {
            return false;
        }

        MenuBarState? bar = registry.Bars.Find(b => Shows(tree, b.Node));
        if (bar is null)
        {
            return false;
        }

        if (key != MenuBarKey.AltUp && (bar.Selected || bar.OpenItem is not null))
        {
            bar.Leave(restore: true);
            return true;
        }

        return key != MenuBarKey.AltDown && !PopupOpen(tree) && bar.Select();
    }

    // Whether a popup is open that is not leaving, a tooltip apart.
    private static bool PopupOpen(UiTree tree)
    {
        IReadOnlyList<PopupHandle> open = tree.Popups.Opened;
        for (int i = 0; i < open.Count; i++)
        {
            if (!open[i].Closing && !open[i].Options.IsTransient)
            {
                return true;
            }
        }

        return false;
    }

    // A menu bar: its items, whether it is selected, and where the focus was before.
    private sealed class MenuBarState(UiTree tree, NodeHandle node)
    {
        private NodeHandle _restore;

        public NodeHandle Node => node;

        public List<MenuBarItemState> Items { get; } = [];

        public bool Selected { get; private set; }

        // The item whose menu is open, if any.
        public MenuBarItemState? OpenItem
        {
            get
            {
                foreach (MenuBarItemState item in Items)
                {
                    if (item.IsOpen)
                    {
                        return item;
                    }
                }

                return null;
            }
        }

        // Selects the bar: its items take the focus, and the first that shows, or the one given, takes it. The bar shows the access
        // keys while it is selected (design §13.1, "Access keys").
        public bool Select(MenuBarItemState? item = null)
        {
            if (Selected)
            {
                return item is null || tree.Focus.Focus(item.Node);
            }

            NodeHandle before = tree.Focus.Focused;
            SetFocusable(true);
            MenuBarItemState? target = item ?? Items.Find(i => tree.Focus.CanFocus(i.Node));
            if (target is null || !tree.Focus.Focus(target.Node))
            {
                SetFocusable(false);
                return false;
            }

            Selected = true;
            _restore = before;
            tree.HoldMenuCues();
            return true;
        }

        // Leaves the bar: its menu closes, and the focus goes back to where it was before the bar was selected, when it is asked and
        // that node can take it; the items take no focus again.
        public void Leave(bool restore)
        {
            OpenItem?.Close();
            if (!Selected)
            {
                return;
            }

            Selected = false;
            NodeHandle back = _restore;
            _restore = default;
            if (restore && tree.Focus.CanFocus(back))
            {
                _ = tree.Focus.Focus(back);
            }

            SetFocusable(false);
            tree.ReleaseMenuCues();
        }

        // Opens an item's menu, closing another one open; a selected bar gives the item the focus first, so that the menu gives it
        // back when it closes.
        public void Open(MenuBarItemState item, bool last)
        {
            MenuBarItemState? open = OpenItem;
            if (open is not null && open != item)
            {
                open.Close();
            }

            if (Selected)
            {
                _ = tree.Focus.Focus(item.Node);
            }

            item.Open(last);
        }

        // The next item that shows, round the ends: its menu opens in place of the one open, or it takes the focus.
        public void Move(MenuBarItemState from, int step, bool openMenu)
        {
            int at = Items.IndexOf(from);
            for (int i = 1; i < Items.Count; i++)
            {
                MenuBarItemState next = Items[(((at + (step * i)) % Items.Count) + Items.Count) % Items.Count];
                if (!Shows(tree, next.Node))
                {
                    continue;
                }

                if (openMenu)
                {
                    Open(next, last: false);
                }
                else
                {
                    _ = tree.Focus.Focus(next.Node);
                }

                return;
            }
        }

        // The focus moving out of the bar and its menus leaves it, with no focus given back.
        public void FocusOut(in FocusEvent e)
        {
            if (Selected && !Inside(tree.Focus.Focused))
            {
                Leave(restore: false);
            }
        }

        // Whether a node is in the bar or in a popup opened from it.
        private bool Inside(NodeHandle at)
        {
            for (; !at.IsNone && tree.Nodes.IsAlive(at); at = tree.KeyParent(at))
            {
                if (at == node)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetFocusable(bool focusable)
        {
            foreach (MenuBarItemState item in Items)
            {
                if (tree.Nodes.IsAlive(item.Node))
                {
                    tree.SetFocusable(item.Node, focusable);
                }
            }
        }
    }

    // One item of a menu bar: its menu, below it.
    private sealed class MenuBarItemState
    {
        private readonly UiTree _tree;
        private readonly Signal<bool> _open = Signal.Create(false);
        private readonly PopupBlock _block;
        private bool _last;

        public MenuBarItemState(UiTree tree, NodeHandle node, Func<Element> panel)
        {
            _tree = tree;
            Node = node;

            // A press outside the menu, on this item included, closes it and goes no further, as a Win32 menu does: a press on the
            // item while its menu is open closes the menu, and does not open it again.
            _block = OpenMenu(tree, node, _open, panel, new PopupOptions { ConsumeOutsidePress = true }, Opened);
        }

        public NodeHandle Node { get; }

        public MenuBarState? Bar { get; set; }

        public bool IsOpen => _block.Popup is { Closing: false };

        // Opens the menu with its first or its last item focused, there or in the menu open already.
        public void Open(bool last)
        {
            if (IsOpen)
            {
                _ = _tree.Focus.MoveToTabEnd(last);
                return;
            }

            _last = last;
            _open.Set(true);
        }

        public void Close()
        {
            if (IsOpen)
            {
                _block.Dismiss();
            }
        }

        // Its access key: the bar is selected with this item, and its menu opens with its first item focused.
        public void OpenFromKey()
        {
            if (Bar is not null && Bar.Select(this))
            {
                Bar.Open(this, last: false);
            }
        }

        public void Click()
        {
            if (Bar is not null)
            {
                Bar.Open(this, last: false);
            }
        }

        // The keys of the item while the bar is selected, and Left and Right that its menu did not take.
        public void KeyDown(in KeyEvent e)
        {
            if (Bar is not { } bar)
            {
                return;
            }

            if (e.TargetNode != Node)
            {
                if (e.Modifiers == KeyModifiers.None && e.Key is Key.Left or Key.Right && IsOpen)
                {
                    e.StopPropagation();
                    bar.Move(this, e.Key == Key.Right ? 1 : -1, openMenu: true);
                }

                return;
            }

            // A letter or a digit, with Alt or Shift or neither, is an item's access key in the selected bar.
            if (
                (e.Modifiers & ~(KeyModifiers.Alt | KeyModifiers.Shift)) == 0
                && Rune.IsLetterOrDigit(e.KeyChar)
                && e.KeyChar.IsBmp
                && ChooseByKey(_tree, bar.Node, (char)e.KeyChar.Value)
            )
            {
                e.StopPropagation();
                return;
            }

            if (e.Modifiers != KeyModifiers.None)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Left or Key.Right:
                    bar.Move(this, e.Key == Key.Right ? 1 : -1, openMenu: false);
                    break;
                case Key.Down or Key.Enter or Key.Space or Key.Up:
                    bar.Open(this, last: e.Key == Key.Up);
                    break;
                case Key.Escape:
                    bar.Leave(restore: true);
                    break;
                default:
                    return;
            }

            e.StopPropagation();
        }

        // The pointer over the item while another item's menu is open opens this one's in its place.
        public void Enter(in PointerEvent e)
        {
            if (Bar is { } bar && bar.OpenItem is { } open && open != this && Shows(_tree, Node))
            {
                bar.Open(this, last: false);
            }
        }

        private void Opened(NodeHandle root) => _ = _tree.Focus.MoveToTabEnd(_last);
    }

    // A menu bar item's access key, from anywhere in the window: it selects the bar and opens the menu, while the item shows and no
    // modal popup is open.
    private sealed class MenuBarItemHost(MenuBarItemState item) : AccessKeyHost
    {
        public MenuBarItemState Item => item;

        public override AccessKeyTarget? TargetOf(UiTree tree, NodeHandle node) =>
            Shows(tree, node) && !ModalOpen(tree) ? new AccessKeyTarget(node, node, (_, _, _) => item.OpenFromKey()) : null;

        private static bool ModalOpen(UiTree tree)
        {
            IReadOnlyList<PopupHandle> open = tree.Popups.Opened;
            for (int i = 0; i < open.Count; i++)
            {
                if (!open[i].Closing && open[i].Options.IsModal)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
