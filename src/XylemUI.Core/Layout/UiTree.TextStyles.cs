using System.Runtime.InteropServices;
using XylemUI.Text;

namespace XylemUI;

/// <content>
/// The text styles that containers give the text leaves below them (design §3.2, "The text style a container gives"). A leaf's
/// <see cref="TextLeaf.Style"/> is resolved when the leaf is put in the tree and when a container's value changes, and the layout
/// and the draw stage read nothing else: no frame does more work for it.
/// </content>
internal sealed partial class UiTree
{
    // Made when a container first gets a text style: until then an attach, a detach, or a free reads this field alone.
    private TextStyleTables? _textStyles;

    /// <summary>
    /// Gets the number of text leaves whose style was resolved again from their ancestors' (design §3.2, "The text style a container
    /// gives"): tests check that a change walks only the leaves it can change, and that a subtree put back where it was walks none.
    /// </summary>
    internal long TextLeavesResolved => _textStyles?.Resolved ?? 0;

    /// <summary>
    /// Gets the number of entries in each of the tree's text style tables (<see cref="TextStyleTables"/>): tests check that a freed
    /// node takes its entries out with it.
    /// </summary>
    internal (int Held, int Detached, int OwnLineHeights) TextStyleEntries =>
        _textStyles is { } tables ? (tables.Held.Count, tables.Detached.Count, tables.OwnLineHeights.Count) : (0, 0, 0);

    /// <summary>
    /// Sets fields of a text style on a node (design §3.2, "The text style a container gives"): on a text leaf, as its own, which
    /// its ancestors do not change; on another node, as the values the text leaves below it take where neither they nor a nearer
    /// container set the field. Only the leaves whose resolved style changes are marked (<see cref="ChangeKind.Font"/>); values
    /// equal to those there change nothing and walk nothing. It allocates only when a container gets its first text style.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The fields and their values, checked by the caller.</param>
    internal void SetTextStyleField(NodeHandle node, in InheritedTextStyle value)
    {
        if (Nodes.GetRef(node).Text is { } leaf)
        {
            SetOwnTextStyle(node, leaf, in value);
        }
        else
        {
            SetHeldTextStyle(node, in value);
        }
    }

    /// <summary>
    /// Returns the text style a node holds for the text leaves below it (design §3.2, "The text style a container gives").
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The fields it set and their values; none for a node that set none.</returns>
    internal InheritedTextStyle HeldTextStyleOf(NodeHandle node) =>
        _textStyles is { } tables && tables.Held.TryGetValue(node, out InheritedTextStyle held) ? held : default;

    // A leaf's own fields: they keep its values, and the rest are resolved from its ancestors again, since a size of its own changes
    // the multiple a line height in logical pixels it takes makes.
    private void SetOwnTextStyle(NodeHandle node, TextLeaf leaf, in InheritedTextStyle value)
    {
        TextStyleFields fields = value.Fields;
        TextStyle current = leaf.Style;
        FontRequest font = current.Font;
        var own = new InheritedTextStyle
        {
            Fields = fields,
            Family = font.Family,
            Weight = font.Weight,
            Slant = font.Slant,
            Stretch = font.Stretch,
            Size = current.Size,
            LineHeight = OwnLineHeightOf(node),
        };
        ref UiNode owner = ref Nodes.GetRef(node);
        if ((owner.TextOwnFields & fields) == fields && own.Covers(in value))
        {
            return;
        }

        own.Override(in value);
        owner.TextOwnFields |= fields;
        if ((fields & TextStyleFields.LineHeight) != 0)
        {
            (_textStyles ??= new TextStyleTables()).OwnLineHeights[node] = value.LineHeight;
        }

        var written = new TextStyle(new FontRequest(own.Family, own.Weight, own.Slant, own.Stretch), own.Size, current.Language, current.LineHeight);
        if (leaf.SetStyle(OuterTextStyleOf(node).Resolve(in written, owner.TextOwnFields, OwnLineHeightOf(node))))
        {
            Invalidation.Invalidate(this, node, ChangeKind.Font);
        }
    }

    // A container's fields: its subtree is walked down from it, and the walk stops below a container that holds every field that
    // changed, as the nearer value wins there (ResolveTextStyles).
    private void SetHeldTextStyle(NodeHandle node, in InheritedTextStyle value)
    {
        // A stale handle throws here, before the table changes.
        _ = Nodes.GetRef(node);
        TextStyleTables tables = _textStyles ??= new TextStyleTables();
        ref InheritedTextStyle held = ref CollectionsMarshal.GetValueRefOrAddDefault(tables.Held, node, out bool exists);
        if (exists && held.Covers(in value))
        {
            return;
        }

        held.Override(in value);
        Nodes.GetRef(node).TextStyleMarks |= TextStyleMarks.Holds | TextStyleMarks.Inside;
        InheritedTextStyle given = TextStyleGivenBy(node);
        for (NodeHandle child = Nodes.GetRef(node).FirstChild; !child.IsNone; child = Nodes.GetRef(child).NextSibling)
        {
            ResolveTextStyles(child, in given, value.Fields);
        }
    }

    // A subtree put under a parent: its leaves are resolved from the new ancestors, unless they give the values the subtree took
    // when it was taken out (a list's row put back in its place), or the parent gives none and the subtree took none.
    private void TextStyleAttached(NodeHandle parent, NodeHandle child)
    {
        if (_textStyles is not { } tables)
        {
            return;
        }

        InheritedTextStyle before = default;
        bool kept = tables.Detached.Count > 0 && tables.Detached.Remove(child, out before);
        if (!kept && (Nodes.GetRef(parent).TextStyleMarks & TextStyleMarks.Inside) == 0)
        {
            return;
        }

        InheritedTextStyle given = TextStyleGivenBy(parent);
        if (kept && given.SameAs(in before))
        {
            return;
        }

        ResolveTextStyles(child, in given, TextStyleFields.All);
    }

    // A subtree taken out from under a parent that gives a text style keeps what it took, until it is put under a parent again.
    private void TextStyleDetached(NodeHandle parent, NodeHandle child)
    {
        if (TakesTextStyle(parent, out InheritedTextStyle given))
        {
            _textStyles!.Detached[child] = given;
        }
    }

    // Whether the children of a node take a text style from it or its ancestors, and the values they take.
    private bool TakesTextStyle(NodeHandle parent, out InheritedTextStyle given)
    {
        if (_textStyles is null || (Nodes.GetRef(parent).TextStyleMarks & TextStyleMarks.Inside) == 0)
        {
            given = default;
            return false;
        }

        given = TextStyleGivenBy(parent);
        return given.Fields != TextStyleFields.None;
    }

    // The line height a text leaf set itself, in logical pixels, from the tables (TextStyleTables.OwnLineHeights); NaN when it set
    // none, or when its own line height is the multiple in its style.
    private float OwnLineHeightOf(NodeHandle node) =>
        (Nodes.GetRef(node).TextOwnFields & TextStyleFields.LineHeight) != 0
        && _textStyles is { OwnLineHeights.Count: > 0 } tables
        && tables.OwnLineHeights.TryGetValue(node, out float height)
            ? height
            : float.NaN;

    // The values a node's ancestors give it: its parent's, or what a subtree taken out took when the node is its root.
    private InheritedTextStyle OuterTextStyleOf(NodeHandle node)
    {
        NodeHandle parent = Nodes.GetRef(node).Parent;
        if (!parent.IsNone)
        {
            return TextStyleGivenBy(parent);
        }

        return _textStyles is { Detached.Count: > 0 } tables && tables.Detached.TryGetValue(node, out InheritedTextStyle taken) ? taken : default;
    }

    // The values a node gives its children: its own and its ancestors', the nearest first. The walk up stops at the first node
    // that neither holds a style nor is below one, and once every field is set.
    private InheritedTextStyle TextStyleGivenBy(NodeHandle node)
    {
        InheritedTextStyle values = default;
        if (_textStyles is not { } tables)
        {
            return values;
        }

        while (true)
        {
            ref readonly UiNode n = ref Nodes.GetRef(node);
            if ((n.TextStyleMarks & TextStyleMarks.Inside) == 0)
            {
                return values;
            }

            if ((n.TextStyleMarks & TextStyleMarks.Holds) != 0 && tables.Held.TryGetValue(node, out InheritedTextStyle held))
            {
                values.FillFrom(in held);
            }

            if (values.Fields == TextStyleFields.All)
            {
                return values;
            }

            if (n.Parent.IsNone)
            {
                if (tables.Detached.Count > 0 && tables.Detached.TryGetValue(node, out InheritedTextStyle taken))
                {
                    values.FillFrom(in taken);
                }

                return values;
            }

            node = n.Parent;
        }
    }

    // Resolves the text leaves of a subtree from the values its ancestors give, for the fields that changed, and marks the leaves
    // whose style changed. Below a container that holds every field that changed, nothing changes, and the walk stops; a leaf's own
    // fields keep its values (InheritedTextStyle.Resolve). A text leaf's inline objects take what the leaf's ancestors give, not the
    // leaf's own values.
    private void ResolveTextStyles(NodeHandle node, in InheritedTextStyle inherited, TextStyleFields changed)
    {
        ref UiNode n = ref Nodes.GetRef(node);
        InheritedTextStyle here = inherited;
        if ((n.TextStyleMarks & TextStyleMarks.Holds) != 0 && _textStyles!.Held.TryGetValue(node, out InheritedTextStyle held))
        {
            here.Override(in held);
            changed &= ~held.Fields;
        }

        n.TextStyleMarks = here.Fields != TextStyleFields.None ? n.TextStyleMarks | TextStyleMarks.Inside : n.TextStyleMarks & ~TextStyleMarks.Inside;
        if (n.Text is { } leaf)
        {
            _textStyles!.Resolved++;
            if (leaf.SetStyle(here.Resolve(leaf.Style, n.TextOwnFields, OwnLineHeightOf(node))))
            {
                Invalidation.Invalidate(this, node, ChangeKind.Font);
            }
        }

        if (changed == TextStyleFields.None)
        {
            return;
        }

        for (NodeHandle child = Nodes.GetRef(node).FirstChild; !child.IsNone; child = Nodes.GetRef(child).NextSibling)
        {
            ResolveTextStyles(child, in here, changed);
        }
    }
}
