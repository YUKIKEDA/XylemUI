using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// Makes the nodes of a window's tree (design §2.13, "The component API"): boxes, the containers that lay their children out in a
/// column or a row, and text. Write <c>using static XylemUI.Elements;</c> to call them by name.
/// </summary>
/// <remarks>
/// A node is made in the tree whose root function the window's host runs now (<c>XylemApp.Run</c>), and belongs to the current scope
/// (design §2.3): disposing the scope takes the node out of the tree and frees it.
/// </remarks>
public static class Elements
{
    /// <summary>
    /// Gets the size of text that sets none, in logical pixels: Flutter's <c>kDefaultFontSize</c> (design §2.13).
    /// </summary>
    public const float DefaultFontSize = 14;

    /// <summary>
    /// Makes a box with no child.
    /// </summary>
    /// <returns>The box.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Box() => Own(new LayoutStyle());

    /// <summary>
    /// Makes a box that holds one child.
    /// </summary>
    /// <param name="child">The child: a node of the same tree with no parent.</param>
    /// <returns>The box.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    /// <exception cref="ArgumentException">The child is no node, is of another tree, or has a parent.</exception>
    public static Element Box(Element child)
    {
        Element box = Box();
        Append(box, child, nameof(child));
        return box;
    }

    /// <summary>
    /// Makes a box that scrolls its one child vertically (design §3.4.1, "The scroll container"): its height overflows on
    /// <see cref="Overflow.Scroll"/>, its width stays <see cref="Overflow.Visible"/>. Give it a height, or a parent that bounds
    /// it, for the content to have room to scroll in.
    /// </summary>
    /// <param name="child">The child: a node of the same tree with no parent.</param>
    /// <param name="state">The scroll state the app reads and writes; <see langword="null"/> for one of the box's own.</param>
    /// <returns>The box.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    /// <exception cref="ArgumentException">The child is no node, is of another tree, or has a parent.</exception>
    public static Element ScrollView(Element child, ScrollState? state = null) =>
        Box(child).Overflow(Overflow.Visible, Overflow.Scroll).ScrollState(state ?? new ScrollState());

    /// <summary>
    /// Makes a container that lays its children out in a column, in the order given (a flex column).
    /// </summary>
    /// <param name="children">The children: nodes of the same tree with no parent.</param>
    /// <returns>The column.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    /// <exception cref="ArgumentException">A child is no node, is of another tree, or has a parent.</exception>
    public static Element Column(params ReadOnlySpan<Element> children) => Container(LayoutAlgorithm.Flex.Column, children);

    /// <summary>
    /// Makes a container that lays its children out in a row, in the order given (a flex row).
    /// </summary>
    /// <param name="children">The children: nodes of the same tree with no parent.</param>
    /// <returns>The row.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    /// <exception cref="ArgumentException">A child is no node, is of another tree, or has a parent.</exception>
    public static Element Row(params ReadOnlySpan<Element> children) => Container(LayoutAlgorithm.Flex.Row, children);

    /// <summary>
    /// Makes text that does not change, at <see cref="DefaultFontSize"/>, in the OS's UI language (the one the window's host reads), with the color its ancestors give
    /// (design §5.0, "The color of text").
    /// </summary>
    /// <param name="content">The text.</param>
    /// <returns>The text node.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Text(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Element text = TextLeaf();
        text.Tree!.SetText(text.Node, content);
        return text;
    }

    /// <summary>
    /// Makes text that <paramref name="content"/> writes again whenever a signal it read changes. Each change makes a string; use
    /// <see cref="Text(TextBuilder)"/> for text that changes often.
    /// </summary>
    /// <param name="content">The text, read in an effect of the current scope.</param>
    /// <returns>The text node.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Text(Func<string> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Element text = TextLeaf();
        UiTree tree = text.Tree!;
        NodeHandle node = text.Node;
        _ = Effect.Create(() =>
        {
            string written = content();
            if (tree.Nodes.IsAlive(node))
            {
                tree.SetText(node, written);
            }
        });
        return text;
    }

    /// <summary>
    /// Makes text that <paramref name="content"/> writes again whenever a signal it read changes, into a buffer that the node takes
    /// in place of its own when the text differs. A text equal to the one there changes nothing, and writing again allocates nothing
    /// once the buffers have grown (design §2.13).
    /// </summary>
    /// <param name="content">The text's writer, run in an effect of the current scope.</param>
    /// <returns>The text node.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Text(TextBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Element text = TextLeaf();
        UiTree tree = text.Tree!;
        NodeHandle node = text.Node;

        // The inline objects belong to the scopes that made them: the node lets go of them before it goes away, which runs before
        // its removal (a scope's cleanups run newest first), so that the removal of its subtree does not free them (design §4.8).
        Scope.OnCleanup(() =>
        {
            if (tree.Nodes.IsAlive(node) && !tree.Nodes.GetRef(node).FirstChild.IsNone)
            {
                DetachInlines(tree, node);
            }
        });
        _ = Effect.Create(() =>
        {
            if (!tree.Nodes.IsAlive(node) || tree.Nodes.GetRef(node).Text is not { } leaf)
            {
                return;
            }

            // The tree of a text leaf has text tools (CreateTextNode).
            TextContext context = tree.Text!;
            char[] buffer = context.RentSpare(out bool shared, out TextRangeWriter ranges);
            ranges.Tree = tree;
            ranges.Owner = node;
            char[]? released = buffer;
            try
            {
                var writer = new NodeTextWriter(buffer, ranges);
                content(ref writer);
                released = writer.Buffer;
                writer.Finish();
                bool text = leaf.TakeWritten(writer.Buffer, writer.Length, out released);
                bool hadLinks = leaf.Ranges?.HasLinks == true;
                bool look = leaf.TakeRanges(ranges, out bool layout);
                if (look)
                {
                    tree.CountLinkLeaf(hadLinks, leaf.Ranges?.HasLinks == true);
                }

                if (layout)
                {
                    TakeInlines(tree, node, leaf.Ranges);
                }

                // A change of the text or of the layout's ranges lays the leaf out again; a change of the look's only draws it again.
                if (text || layout)
                {
                    Invalidation.Invalidate(tree, node, ChangeKind.TextContent);
                }
                else if (look)
                {
                    Invalidation.Invalidate(tree, node, ChangeKind.Color);
                }
            }
            finally
            {
                if (shared)
                {
                    context.ReturnSpare(released);
                }
            }
        });
        return text;
    }

    /// <summary>
    /// Shows one part or another by a condition (design §2.7): <paramref name="render"/>'s node while the condition is true, and
    /// <paramref name="fallback"/>'s, or nothing, while it is false. No box is added around the part: its node takes the place in
    /// the parent, as a child of it, so the parent lays it out as it would lay out the part itself. Each change of the condition's
    /// truth makes the other part again, in a new scope, and disposes the part shown before, with what it made; a value of the same
    /// truth changes nothing. Nothing in place shows as an empty hidden node, which the layout does not see.
    /// </summary>
    /// <param name="when">The condition.</param>
    /// <param name="render">What the true state shows: a node of the current tree with no parent, made in the part's scope.</param>
    /// <param name="fallback">What the false state shows; nothing when <see langword="null"/>.</param>
    /// <returns>The node of the part shown first, to put in a parent. Put it in a parent before the condition changes: a change
    /// before that is reported to <see cref="ReactiveRuntime.UnhandledExceptionHandler"/> and shows nothing new.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, there is no current scope, or a part returns
    /// no node of the current tree with no parent.</exception>
    public static Element Show(IReadOnlySignal<bool> when, Func<Element> render, Func<Element>? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(render);
        UiTree tree = CurrentTree();
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException("Show is made inside a scope, which owns its parts; there is no current scope.");
        }

        return new Element(tree, ShowBlock.Create(tree, when, render, fallback).Node);
    }

    /// <summary>
    /// Returns the tree whose root function runs now.
    /// </summary>
    /// <returns>The current tree.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running.</exception>
    internal static UiTree CurrentTree() =>
        UiTree.Current
        ?? throw new InvalidOperationException("Elements are made while a window's root function runs (XylemApp.Run); no tree is current.");

    /// <summary>
    /// Returns the text style of text that sets none, in the tree's UI language, or undetermined when the host gave none.
    /// </summary>
    /// <param name="tree">The tree whose UI language the style takes.</param>
    /// <returns>The default text style.</returns>
    internal static TextStyle DefaultTextStyle(UiTree tree) =>
        new(FontRequest.Default, DefaultFontSize, tree.UiLanguage ?? TextLanguage.FromTag("und"), float.NaN);

    // A node of the current tree, owned by the current scope: disposing the scope takes it out and frees it.
    private static Element Own(LayoutStyle style)
    {
        UiTree tree = CurrentTree();
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException("Elements are made inside a scope, which owns them; there is no current scope.");
        }

        NodeHandle node = tree.CreateNode(style);
        Own(tree, node);
        return new Element(tree, node);
    }

    private static void Own(UiTree tree, NodeHandle node) =>
        Scope.OnCleanup(() =>
        {
            if (tree.Nodes.IsAlive(node))
            {
                tree.Remove(node);
            }
        });

    // Makes the inline objects of a text's ranges the children of its node, in the order of the text (design §4.8). The children
    // the write left out are let go of, alive: their scopes remove them. A write with the same children changes nothing.
    private static void TakeInlines(UiTree tree, NodeHandle node, TextRanges? ranges)
    {
        ReadOnlySpan<InlineObject> inlines = ranges is { } r ? r.Inlines : default;
        NodeHandle child = tree.Nodes.GetRef(node).FirstChild;
        int same = 0;
        while (same < inlines.Length && !child.IsNone && inlines[same].Child == child)
        {
            child = tree.Nodes.GetRef(child).NextSibling;
            same++;
        }

        if (same == inlines.Length && child.IsNone)
        {
            return;
        }

        DetachInlines(tree, node, ranges);
        NodeHandle previous = default;
        foreach (InlineObject inline in inlines)
        {
            tree.InsertAfter(node, previous, inline.Child);
            previous = inline.Child;
        }
    }

    // Lets go of a text node's children, alive; one that the ranges do not keep loses the focus first, as a node taken out of the
    // tree does (design §7, "Removed, hidden, or disabled").
    private static void DetachInlines(UiTree tree, NodeHandle node, TextRanges? keep = null)
    {
        for (NodeHandle child = tree.Nodes.GetRef(node).FirstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            if (keep?.HasInline(child) != true)
            {
                tree.Focus.Lose(child);
            }
        }

        tree.DetachChildren(node);
    }

    private static Element TextLeaf()
    {
        UiTree tree = CurrentTree();
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException("Elements are made inside a scope, which owns them; there is no current scope.");
        }

        NodeHandle node = tree.CreateTextNode(new LayoutStyle(), DefaultTextStyle(tree));
        Own(tree, node);
        return new Element(tree, node);
    }

    private static Element Container(LayoutAlgorithm algorithm, ReadOnlySpan<Element> children)
    {
        Element container = Own(new LayoutStyle { Algorithm = algorithm });
        foreach (Element child in children)
        {
            Append(container, child, nameof(children));
        }

        return container;
    }

    private static void Append(Element parent, Element child, string name)
    {
        if (child.IsNone || !ReferenceEquals(child.Tree, parent.Tree))
        {
            throw new ArgumentException("A child is a node of the same tree as its parent.", name);
        }

        if (!child.Tree!.Nodes.GetRef(child.Node).Parent.IsNone)
        {
            throw new ArgumentException("A child has no parent yet.", name);
        }

        parent.Tree!.AppendChild(parent.Node, child.Node);
    }
}
