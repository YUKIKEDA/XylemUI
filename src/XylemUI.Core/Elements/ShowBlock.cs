namespace XylemUI;

/// <summary>
/// What a <see cref="Elements.Show"/> keeps (design §2.7, "The shape of V3"): the node it put in its parent, the scope of that
/// node's part, and whether a part was shown yet. A change of the condition makes the other part in a new scope, puts it in the
/// place of the old node, and disposes the old part.
/// </summary>
internal sealed class ShowBlock
{
    private readonly UiTree _tree;
    private readonly Func<Element> _render;
    private readonly Func<Element>? _fallback;
    private readonly ScopeRef _owner;
    private ScopeHandle _part;
    private NodeHandle _placeholder;
    private bool _hasPart;

    private ShowBlock(UiTree tree, Func<Element> render, Func<Element>? fallback, ScopeRef owner)
    {
        _tree = tree;
        _render = render;
        _fallback = fallback;
        _owner = owner;
    }

    /// <summary>
    /// Gets the node the block puts in its parent now: the part's node, or the empty hidden node.
    /// </summary>
    internal NodeHandle Node { get; private set; }

    /// <summary>
    /// Makes a block in a scope of its own under the current one, and its first part.
    /// </summary>
    /// <param name="tree">The tree the parts are made in.</param>
    /// <param name="when">The condition.</param>
    /// <param name="render">What the true state shows.</param>
    /// <param name="fallback">What the false state shows; nothing when <see langword="null"/>.</param>
    /// <returns>The block.</returns>
    internal static ShowBlock Create(UiTree tree, IReadOnlySignal<bool> when, Func<Element> render, Func<Element>? fallback)
    {
        ShowBlock? block = null;
        _ = Scope.Create(
            () =>
            {
                block = new ShowBlock(tree, render, fallback, Scope.CurrentRef);
                ShowBlock made = block;
                Scope.OnCleanup(made.FreePlaceholder);
                _ = Effect.Create(() => made.Show(when.Get()));
            },
            "Show"
        );
        return block!;
    }

    // Shows the part of the condition's truth. The effect runs again only when the condition's value changed, and a bool's change
    // is a change of its truth.
    private void Show(bool shown)
    {
        bool first = !_hasPart;
        NodeHandle old = Node;
        ScopeHandle oldPart = _part;

        // The node the caller holds is the one it puts in a parent; a swap before that would leave the caller a freed node.
        if (!first && _tree.Nodes.GetRef(old).Parent.IsNone)
        {
            throw new InvalidOperationException("The condition of Show changed before its node was put in a parent; put the node in a parent first.");
        }

        _hasPart = true;
        Func<Element>? part = shown ? _render : _fallback;
        (NodeHandle next, ScopeHandle nextPart) = part is null ? (Placeholder(), default) : Build(part);
        if (!first)
        {
            _tree.Focus.Lose(old);
            _tree.ReplaceChild(old, next);
        }

        Node = next;
        _part = nextPart;
        if (!first)
        {
            if (old == _placeholder)
            {
                FreePlaceholder();
            }
            else
            {
                Scope.Dispose(oldPart);
            }
        }
    }

    // Makes a part in a new scope under the block's own, with the tree as the current one, and with no effect reading what the part
    // reads: the part's own effects follow its signals.
    private (NodeHandle Node, ScopeHandle Scope) Build(Func<Element> part)
    {
        Element element = default;
        ScopeHandle made = default;
        UiTree? previousTree = UiTree.Current;
        Computation? running = ReactiveRuntime.Current;
        if (!Scope.TryEnter(_owner, out ScopeRef previousScope))
        {
            throw new InvalidOperationException("The scope of Show was disposed.");
        }

        UiTree.Current = _tree;
        ReactiveRuntime.Current = null;
        try
        {
            made = Scope.Create(() => element = part(), "Show part");
        }
        finally
        {
            ReactiveRuntime.Current = running;
            UiTree.Current = previousTree;
            Scope.Exit(previousScope);
        }

        if (element.IsNone || !ReferenceEquals(element.Tree, _tree) || !_tree.Nodes.GetRef(element.Node).Parent.IsNone)
        {
            Scope.Dispose(made);
            throw new InvalidOperationException("A part of Show returns a node of the same tree with no parent.");
        }

        return (element.Node, made);
    }

    // The empty node that keeps the place of a part that shows nothing: hidden, so the layout does not see it.
    private NodeHandle Placeholder()
    {
        if (!_tree.Nodes.IsAlive(_placeholder))
        {
            _placeholder = _tree.CreateNode(new LayoutStyle { Hidden = true });
        }

        return _placeholder;
    }

    private void FreePlaceholder()
    {
        if (_tree.Nodes.IsAlive(_placeholder))
        {
            _tree.Remove(_placeholder);
        }

        _placeholder = default;
    }
}
