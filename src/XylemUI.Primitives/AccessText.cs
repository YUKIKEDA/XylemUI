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
    // The access keys of each tree: the key of each AccessText's node, and the state of each menu item, which a letter typed in an
    // open menu finds.
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
            AccessKeyRegistry registry = AccessKeys.GetValue(tree, static _ => new AccessKeyRegistry());
            NodeHandle node = element.Node;
            registry.Texts[node] = key;
            Scope.OnCleanup(() => registry.Texts.Remove(node));
        }

        return element;
    }

    // Registers a menu item, so that a letter typed in its menu finds it.
    private static void RegisterItem(UiTree tree, NodeHandle node, MenuItemState state)
    {
        AccessKeyRegistry registry = AccessKeys.GetValue(tree, static _ => new AccessKeyRegistry());
        registry.Items[node] = state;
        Scope.OnCleanup(() => registry.Items.Remove(node));
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
        MenuItemState? only = null;
        for (NodeHandle node = panel; !node.IsNone; node = NextInSubtree(tree, node, panel))
        {
            if (node == focused)
            {
                passed = true;
            }

            if (!registry.Items.TryGetValue(node, out MenuItemState? item) || !tree.Focus.CanFocus(node) || KeyOf(tree, registry, node) != key)
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
            only!.Activate();
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

        public Dictionary<NodeHandle, MenuItemState> Items { get; } = [];
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
