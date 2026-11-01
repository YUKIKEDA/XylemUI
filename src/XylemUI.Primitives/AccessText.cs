using System.Runtime.CompilerServices;
using System.Text;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <content>
/// The access keys of the menus (design §13.1, "Access keys").
/// </content>
public static partial class Primitive
{
    // The access keys of each tree: the key of each AccessText's node, what each menu item and menu bar item does with its key, which
    // a letter typed in an open menu or a selected menu bar finds, and the menu bars.
    private static readonly ConditionalWeakTable<UiTree, AccessKeyRegistry> AccessKeys = [];

    private static readonly SpanStyle Underlined = new(Decoration: new TextDecoration(DecorationLines.Underline));

    /// <summary>
    /// Makes a text with an access key (design §13.1, "Access keys"): the character after the first single <c>_</c> is the key, and the
    /// text is drawn without the <c>_</c>; <c>__</c> draws one <c>_</c>. The key is underlined while the window shows the access keys:
    /// always with Windows' "underline access keys" setting on, else after Alt goes down and in a menu opened from the keys, until a
    /// pointer's press. A <see cref="MenuItem"/> or a <see cref="SubmenuItem"/> takes the key of the first access text in its content:
    /// in an open menu, typing the key, with Alt or without, chooses the item, or moves the focus between the items that share it.
    /// </summary>
    /// <param name="text">The text, with <c>_</c> before its key.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element AccessText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        UiTree tree = UiTree.Current ?? throw new InvalidOperationException("AccessText is made while a window's root function runs.");
        var access = new AccessTextState(tree, text);
        Element element = Elements.Text(access.Write);
        if (access.Key is { } key)
        {
            AccessKeyRegistry registry = RegistryOf(tree);
            NodeHandle node = element.Node;
            registry.Texts[node] = key;
            Scope.OnCleanup(() => registry.Texts.Remove(node));
        }

        return element;
    }

    // Registers a menu item or a menu bar item, so that a letter typed in its menu or its selected bar finds it.
    private static void RegisterItem(UiTree tree, NodeHandle node, Action activate)
    {
        AccessKeyRegistry registry = RegistryOf(tree);
        registry.Items[node] = activate;
        Scope.OnCleanup(() => registry.Items.Remove(node));
    }

    // The access keys of a tree, made the first time: the tree's window then sends it the window's access keys.
    private static AccessKeyRegistry RegistryOf(UiTree tree) =>
        AccessKeys.GetValue(
            tree,
            static tree =>
            {
                tree.AccessKeys = WindowKey;
                return new AccessKeyRegistry();
            }
        );

    // Registers what a node does with the access key of an access text under it: a button clicks, a label focuses its target.
    private static void RegisterHost(UiTree tree, NodeHandle node, AccessKeyHost host)
    {
        AccessKeyRegistry registry = RegistryOf(tree);
        registry.Hosts[node] = host;
        Scope.OnCleanup(() => registry.Hosts.Remove(node));
    }

    // The window's access key (design §13.1, "The window's access keys"): the access texts of the key, in the window's tree and its
    // open popups in the order of the tree, each with the target its nearest host or focusable ancestor gives. Each target needs a
    // node that can take the focus, which a node outside the top modal scope cannot (FocusManager.CanFocus), so a modal dialog keeps
    // the keys inside it. One target is invoked; several take the focus in turn, round the end, and none is invoked.
    private static bool WindowKey(WindowInput input, char letter, TimeSpan timestamp)
    {
        UiTree tree = input.Tree;
        if (!AccessKeys.TryGetValue(tree, out AccessKeyRegistry? registry) || registry.Texts.Count == 0 || InMenu(tree, registry))
        {
            return false;
        }

        char key = char.ToUpperInvariant(letter);
        var targets = new List<AccessKeyTarget>();
        Collect(tree, registry, tree.Root, key, targets);
        IReadOnlyList<PopupHandle> open = tree.Popups.Opened;
        for (int i = 0; i < open.Count; i++)
        {
            if (!open[i].Closing)
            {
                Collect(tree, registry, open[i].Root, key, targets);
            }
        }

        if (targets.Count == 0)
        {
            return false;
        }

        if (targets.Count == 1)
        {
            targets[0].Invoke(input, timestamp);
            return true;
        }

        // The focus goes round the targets that can take it: a menu bar's item takes none while the bar is not selected.
        _ = targets.RemoveAll(t => !tree.Focus.CanFocus(t.FocusNode));
        if (targets.Count == 0)
        {
            return true;
        }

        int focused = targets.FindIndex(t => t.FocusNode == tree.Focus.Focused);
        _ = tree.Focus.Focus(targets[(focused + 1) % targets.Count].FocusNode);
        return true;
    }

    // Whether the focus is in an open menu or a menu bar is selected: a letter their items did not take is not the window's access
    // key (design §13.1, "The window's access keys"; Win32's WM_MENUCHAR, whose default discards the character).
    private static bool InMenu(UiTree tree, AccessKeyRegistry registry) =>
        (tree.Popups.PopupContaining(tree.Focus.Focused) is { } popup && IsMenu(popup)) || registry.Bars.Exists(static bar => bar.Selected);

    // The targets of the access texts of a key under a root, once each, in the order of the tree.
    private static void Collect(UiTree tree, AccessKeyRegistry registry, NodeHandle root, char key, List<AccessKeyTarget> targets)
    {
        for (NodeHandle node = root; !node.IsNone; node = NextInSubtree(tree, node, root))
        {
            if (
                registry.Texts.TryGetValue(node, out char text)
                && char.ToUpperInvariant(text) == key
                && TargetOf(tree, registry, node) is { } target
                && !targets.Exists(t => t.Node == target.Node)
            )
            {
                targets.Add(target);
            }
        }
    }

    // The target of an access text: its nearest ancestor that is a host (a button, a label) or takes the focus; none when that one
    // cannot take the key now (disabled, hidden), or there is none.
    private static AccessKeyTarget? TargetOf(UiTree tree, AccessKeyRegistry registry, NodeHandle text)
    {
        for (NodeHandle at = text; !at.IsNone; at = tree.Nodes.GetRef(at).Parent)
        {
            if (registry.Hosts.TryGetValue(at, out AccessKeyHost? host))
            {
                return host.TargetOf(tree, at);
            }

            if (tree.Focus.CanFocus(at))
            {
                return new AccessKeyTarget(at, at, null);
            }
        }

        return null;
    }

    // A letter typed in an open menu (Avalonia's MenuItemAccessKeyHandler.cs lines 22 to 34, DefaultMenuInteractionHandler.cs lines 86
    // to 90): the enabled items of the panel whose key it is; one is chosen, or opens its submenu; several pass the focus round them.
    private static bool ChooseByKey(UiTree tree, NodeHandle panel, char letter)
    {
        if (!AccessKeys.TryGetValue(tree, out AccessKeyRegistry? registry) || registry.Items.Count == 0)
        {
            return false;
        }

        char key = char.ToUpperInvariant(letter);
        NodeHandle focused = tree.Focus.Focused;
        int count = 0;
        NodeHandle first = default;
        NodeHandle afterFocused = default;
        bool passed = false;
        Action? only = null;
        for (NodeHandle node = panel; !node.IsNone; node = NextInSubtree(tree, node, panel))
        {
            if (node == focused)
            {
                passed = true;
            }

            if (!registry.Items.TryGetValue(node, out Action? item) || !tree.Focus.CanFocus(node) || KeyOf(tree, registry, node) != key)
            {
                continue;
            }

            count++;
            only = item;
            if (first.IsNone)
            {
                first = node;
            }

            if (passed && node != focused && afterFocused.IsNone)
            {
                afterFocused = node;
            }
        }

        if (count == 0)
        {
            return false;
        }

        if (count == 1)
        {
            only!();
        }
        else
        {
            _ = tree.Focus.Focus(afterFocused.IsNone ? first : afterFocused);
        }

        return true;
    }

    // The key of the first access text in an item's content, upper case; '\0' with none.
    private static char KeyOf(UiTree tree, AccessKeyRegistry registry, NodeHandle item)
    {
        for (NodeHandle node = item; !node.IsNone; node = NextInSubtree(tree, node, item))
        {
            if (registry.Texts.TryGetValue(node, out char key))
            {
                return char.ToUpperInvariant(key);
            }
        }

        return '\0';
    }

    // The node after a node in the order of the tree, inside a subtree; none past its end.
    private static NodeHandle NextInSubtree(UiTree tree, NodeHandle node, NodeHandle top)
    {
        ref UiNode n = ref tree.Nodes.GetRef(node);
        if (!n.FirstChild.IsNone)
        {
            return n.FirstChild;
        }

        while (node != top)
        {
            ref UiNode at = ref tree.Nodes.GetRef(node);
            if (!at.NextSibling.IsNone)
            {
                return at.NextSibling;
            }

            node = at.Parent;
        }

        return default;
    }

    private sealed class AccessKeyRegistry
    {
        public Dictionary<NodeHandle, char> Texts { get; } = [];

        public Dictionary<NodeHandle, Action> Items { get; } = [];

        public Dictionary<NodeHandle, AccessKeyHost> Hosts { get; } = [];

        public List<MenuBarState> Bars { get; } = [];
    }

    // What a host does with an access key: its target, if it takes the key now.
    private abstract class AccessKeyHost
    {
        public abstract AccessKeyTarget? TargetOf(UiTree tree, NodeHandle node);
    }

    // A button clicks, as its Enter does (design §7, "The button's keys").
    private sealed class ButtonHost : AccessKeyHost
    {
        public static ButtonHost Instance { get; } = new();

        public override AccessKeyTarget? TargetOf(UiTree tree, NodeHandle node) =>
            tree.Focus.CanFocus(node) ? new AccessKeyTarget(node, node, static (input, n, t) => input.ClickFromKey(n, t)) : null;
    }

    // A label focuses its target (Avalonia's Label.cs lines 44 to 47 and 74 to 80), while the label shows and the target can take the
    // focus.
    private sealed class LabelHost(NodeHandle target) : AccessKeyHost
    {
        public override AccessKeyTarget? TargetOf(UiTree tree, NodeHandle node) =>
            Shows(tree, node) && tree.Focus.CanFocus(target) ? new AccessKeyTarget(node, target, null) : null;
    }

    // A target of an access key: the node it belongs to, the node it focuses, and what invoking it does besides; a target with no
    // action takes the focus.
    private sealed record AccessKeyTarget(NodeHandle Node, NodeHandle FocusNode, Action<WindowInput, NodeHandle, TimeSpan>? Action)
    {
        public void Invoke(WindowInput input, TimeSpan timestamp)
        {
            if (Action is { } action)
            {
                action(input, Node, timestamp);
                return;
            }

            _ = input.Tree.Focus.Focus(FocusNode);
        }
    }

    // One access text: the text it draws, and where its key is in it.
    private sealed class AccessTextState
    {
        private readonly UiTree _tree;
        private readonly string _shown;
        private readonly int _at = -1;

        public AccessTextState(UiTree tree, string text)
        {
            _tree = tree;
            var shown = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '_' && i + 1 < text.Length)
                {
                    i++;
                    if (text[i] != '_' && _at < 0)
                    {
                        _at = shown.Length;
                    }
                }

                _ = shown.Append(text[i]);
            }

            _shown = shown.ToString();
        }

        public char? Key => _at >= 0 ? _shown[_at] : null;

        // The text, the key underlined while the window shows the access keys; the cues' signal makes it written again.
        public void Write(ref NodeTextWriter text)
        {
            if (_at < 0 || !_tree.AccessKeyCues.Get())
            {
                text.Write(_shown);
                return;
            }

            text.Write(_shown.AsSpan(0, _at));
            using (text.Style(Underlined))
            {
                text.Write(_shown.AsSpan(_at, 1));
            }

            text.Write(_shown.AsSpan(_at + 1));
        }
    }
}
