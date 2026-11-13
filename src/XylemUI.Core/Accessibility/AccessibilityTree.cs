using System.Numerics;
using System.Text;

namespace XylemUI;

/// <summary>
/// What the accessibility tree hands the Platform after an update (design §6): the nodes it made, changed, or took away. The
/// Platform reads the nodes, and the node that holds the focus, from the tree and never changes them (Invariant 10). A change of
/// the focus changes the Focused state of the node that had it or of the node that takes it, so it comes with the nodes; when
/// neither has a node, the window's node has the focus before and after.
/// </summary>
internal interface IAccessibilityBridge
{
    /// <summary>
    /// Takes the nodes an update made, changed, or took away, in the order of the update: a parent before the children it made.
    /// </summary>
    /// <param name="changes">The nodes and what became of each.</param>
    void NodesChanged(ReadOnlySpan<AccessibilityChange> changes);
}

/// <summary>
/// The accessibility tree of a UI tree (design §4.4.1, "The shape of P02g"): a node for each node of the UI tree that has a role,
/// under the nearest ancestor that has one, made when assistive technology first asks for it. Until then nothing is made and the
/// marks are not kept, so a window nobody reads pays for one test of <see cref="IsActive"/>. After it, a change of a node of the UI
/// tree marks it, and the stage after the layout pass (<see cref="Update"/>) makes again only the nodes the marks reach: the node
/// a mark is on, the nearest node above it, and their children.
/// </summary>
/// <param name="tree">The UI tree.</param>
internal sealed class AccessibilityTree(UiTree tree)
{
    private readonly Dictionary<NodeHandle, AccessibilityNode> _nodes = [];
    private readonly Dictionary<ulong, AccessibilityNode> _byId = [];
    private readonly HashSet<NodeHandle> _marked = [];
    private readonly HashSet<NodeHandle> _markedSelf = [];
    private readonly List<AccessibilityNode> _work = [];
    private readonly HashSet<AccessibilityNode> _inWork = [];
    private readonly List<AccessibilityChange> _changes = [];
    private readonly List<NodeHandle> _popupRoots = [];
    private readonly List<NodeHandle> _found = [];
    private readonly StringBuilder _name = new();
    private ulong _nextId = 1;
    private int _stamp;

    /// <summary>
    /// Gets a value indicating whether the tree was made: assistive technology asked for it.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the node of the window, the root; <see langword="null"/> before the tree is made.
    /// </summary>
    public AccessibilityNode? Root { get; private set; }

    /// <summary>
    /// Gets or sets the Platform that takes the changes.
    /// </summary>
    public IAccessibilityBridge? Bridge { get; set; }

    /// <summary>
    /// Gets the number of nodes the last update made again, for the tests.
    /// </summary>
    internal int LastRefreshed { get; private set; }

    /// <summary>
    /// Gets the number of nodes of the UI tree marked since the last update, for the tests.
    /// </summary>
    internal int MarkedCount => _marked.Count + _markedSelf.Count;

    /// <summary>
    /// Makes the whole tree, when assistive technology first asks for it, and hands the Platform every node.
    /// </summary>
    public void Start()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        _stamp++;
        SnapshotPopupRoots();
        Root = Make(tree.Root, parent: null);
        Send();
    }

    /// <summary>
    /// Marks a node of the UI tree whose change may change the accessibility tree. Its callers mark only once the tree is made
    /// (<see cref="UiTree.AccessibilityActive"/>), so that a window nobody reads keeps no marks.
    /// </summary>
    /// <param name="node">The node.</param>
    public void Mark(NodeHandle node)
    {
        if (_marked.Add(node))
        {
            tree.NoteWork();
        }
    }

    /// <summary>
    /// Marks a node of the UI tree whose change may change its own node of the accessibility tree alone: a state, a value, or a scroll
    /// position, which neither makes nor takes away a node. The update makes that node again, not its parent's, which looks over all
    /// its children; a node of the UI tree with no node of its own changes nothing. Its callers mark only once the tree is made.
    /// </summary>
    /// <param name="node">The node.</param>
    public void MarkSelf(NodeHandle node)
    {
        if (_markedSelf.Add(node))
        {
            tree.NoteWork();
        }
    }

    /// <summary>
    /// Returns the node of the accessibility tree that stands for a node of the UI tree, or <see langword="null"/>.
    /// </summary>
    /// <param name="node">The node of the UI tree.</param>
    /// <returns>The node.</returns>
    public AccessibilityNode? Find(NodeHandle node) => _nodes.GetValueOrDefault(node);

    /// <summary>
    /// Returns the node that holds the focus for assistive technology: the focused node's, else its nearest ancestor's that has one,
    /// so that the focus is never on no node; <see langword="null"/> when nothing above has one (design §4.4.1, "Excluded nodes"). A
    /// focus inside an excluded node is a fault of the tree, which a Debug build reports.
    /// </summary>
    /// <returns>The node, or <see langword="null"/>.</returns>
    public AccessibilityNode? FocusNode()
    {
        NodeHandle focused = tree.Focus.Focused;
        if (!tree.Nodes.IsAlive(focused))
        {
            return null;
        }

#if DEBUG
        for (NodeHandle at = focused; !at.IsNone; at = tree.Nodes.GetRef(at).Parent)
        {
            if (IsExcluded(at))
            {
                Invariant.Fail(
                    "The focus is inside a node left out of the accessibility tree (AccessibilityInfo.Excluded); a decorative node holds nothing that takes the focus."
                );
            }
        }
#endif

        for (NodeHandle at = focused; !at.IsNone; at = tree.Nodes.GetRef(at).Parent)
        {
            if (Find(at) is { } node)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the node of an id, or <see langword="null"/> when it went away.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>The node.</returns>
    public AccessibilityNode? Find(AccessibilityNodeId id) => _byId.GetValueOrDefault(id.Value);

    /// <summary>
    /// The stage after the layout pass (design §2.10): makes again the nodes the marks reach, from the top down, and hands the
    /// Platform what changed. A frame with no mark and no popup opened or closed does nothing.
    /// </summary>
    public void Update()
    {
        if (!IsActive || Root is null)
        {
            return;
        }

        if (PopupRootsChanged())
        {
            SnapshotPopupRoots();
            _ = _marked.Add(tree.Root);
        }

        if (_marked.Count == 0 && _markedSelf.Count == 0)
        {
            return;
        }

        _stamp++;
        foreach (NodeHandle node in _marked)
        {
            if (!tree.Nodes.IsAlive(node))
            {
                continue;
            }

            AddWork(Owner(node));
            NodeHandle parent = tree.Nodes.GetRef(node).Parent;
            if (!parent.IsNone)
            {
                AddWork(Owner(parent));
            }
        }

        foreach (NodeHandle node in _markedSelf)
        {
            if (tree.Nodes.IsAlive(node) && _nodes.TryGetValue(node, out AccessibilityNode? own))
            {
                AddWork(own);
            }
        }

        _marked.Clear();
        _markedSelf.Clear();
        _work.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));
        LastRefreshed = 0;
        foreach (AccessibilityNode node in _work)
        {
            if (_byId.ContainsKey(node.Id.Value))
            {
                Refresh(node);
                LastRefreshed++;
            }
        }

        _work.Clear();
        _inWork.Clear();
        Send();
    }

    // The nearest node of the tree at or above a node of the UI tree: the window's at the latest.
    private AccessibilityNode Owner(NodeHandle node)
    {
        for (NodeHandle current = node; !current.IsNone; current = tree.Nodes.GetRef(current).Parent)
        {
            if (_nodes.TryGetValue(current, out AccessibilityNode? found))
            {
                return found;
            }
        }

        return Root!;
    }

    private void AddWork(AccessibilityNode node)
    {
        if (_inWork.Add(node))
        {
            node.Depth = DepthOf(node);
            _work.Add(node);
        }
    }

    private static int DepthOf(AccessibilityNode node)
    {
        int depth = 0;
        for (AccessibilityNode? current = node.Parent; current is not null; current = current.Parent)
        {
            depth++;
        }

        return depth;
    }

    // Makes a node and its subtree, records each as added.
    private AccessibilityNode Make(NodeHandle handle, AccessibilityNode? parent)
    {
        var node = new AccessibilityNode(new AccessibilityNodeId(_nextId++), handle) { Parent = parent };
        _nodes[handle] = node;
        _byId[node.Id.Value] = node;
        _ = Write(node);
        node.Stamp = _stamp;
        _changes.Add(new AccessibilityChange(node.Id, AccessibilityChangeKind.Added));
        _found.Clear();
        Collect(node);
        foreach (NodeHandle child in _found.ToArray())
        {
            node.Children.Add(Make(child, node));
        }

        _ = SyncLinks(node);
        return node;
    }

    // Writes a node again, its children's list, and its children's boxes; records each node that changed.
    private void Refresh(AccessibilityNode node)
    {
        bool changed = Write(node);
        _found.Clear();
        Collect(node);
        if (!SameChildren(node))
        {
            NodeHandle[] found = _found.ToArray();
            var keep = new HashSet<NodeHandle>(found);
            foreach (AccessibilityNode old in node.Children.ToArray())
            {
                if (!keep.Contains(old.Node))
                {
                    _ = node.Children.Remove(old);
                    RemoveTree(old);
                }
            }

            node.Children.Clear();
            foreach (NodeHandle child in found)
            {
                if (_nodes.TryGetValue(child, out AccessibilityNode? existing))
                {
                    if (existing.Parent != node)
                    {
                        _ = existing.Parent?.Children.Remove(existing);
                        if (existing.Parent is { } previous)
                        {
                            Changed(previous);
                        }

                        existing.Parent = node;
                        Changed(existing);
                    }

                    node.Children.Add(existing);
                }
                else
                {
                    node.Children.Add(Make(child, node));
                }
            }

            changed = true;
        }

        foreach (AccessibilityNode child in node.Children)
        {
            Rect bounds = BoundsOf(child);
            if (child.Bounds != bounds)
            {
                child.Bounds = bounds;
                Changed(child);
            }
        }

        if (SyncLinks(node))
        {
            changed = true;
        }

        if (changed)
        {
            Changed(node);
        }
    }

    // Records a node as updated once in an update; a node made in this update is recorded as added already.
    private void Changed(AccessibilityNode node)
    {
        if (node.Stamp != _stamp)
        {
            node.Stamp = _stamp;
            _changes.Add(new AccessibilityChange(node.Id, AccessibilityChangeKind.Updated));
        }
    }

    private void RemoveTree(AccessibilityNode node)
    {
        foreach (AccessibilityNode child in node.Children)
        {
            RemoveTree(child);
        }

        foreach (AccessibilityNode link in node.Links)
        {
            _ = _byId.Remove(link.Id.Value);
            _changes.Add(new AccessibilityChange(link.Id, AccessibilityChangeKind.Removed));
        }

        if (_nodes.TryGetValue(node.Node, out AccessibilityNode? same) && same == node)
        {
            _ = _nodes.Remove(node.Node);
        }

        _ = _byId.Remove(node.Id.Value);
        _changes.Add(new AccessibilityChange(node.Id, AccessibilityChangeKind.Removed));
    }

    private bool SameChildren(AccessibilityNode node)
    {
        if (node.Children.Count != _found.Count)
        {
            return false;
        }

        for (int i = 0; i < _found.Count; i++)
        {
            if (node.Children[i].Node != _found[i])
            {
                return false;
            }
        }

        return true;
    }

    // Writes the values of a node from its node of the UI tree; returns whether one changed.
    private bool Write(AccessibilityNode node)
    {
        NodeHandle handle = node.Node;
        bool folding = node.Parent is not null && TakesNameFromContent(node.Parent.Role);
        SemanticsRole role = RoleOf(handle, folding);
        AccessibilityInfo? info = tree.AccessibilityInfoOf(handle);
        string? name = info?.Name ?? (TakesNameFromContent(role) ? FoldName(handle, node.Name) : null);
        if (handle == tree.Root)
        {
            name ??= tree.Title;
        }

        string? value = info?.Value ?? tree.AccessibilityValueOf(handle);
        AccessibilityStates states = StatesOf(handle, info);
        ref UiNode n = ref tree.Nodes.GetRef(handle);
        Vector2 childOffset = n.Layout.Style.IsScrollContainer ? -tree.ScrollOffsetOf(handle) : Vector2.Zero;
        Size scrollRange = n.Layout.Style.IsScrollContainer ? tree.MaxScrollOffsetOf(handle) : default;
        Rect bounds = node.Parent is null ? n.Layout.Rect : BoundsOf(node);
        string? automationId = tree.TestIdOf(handle);
        int position = !n.Parent.IsNone && tree.VirtualHostOf(n.Parent) is { } rows && rows.IndexOf(handle, out int index) ? index : -1;
        int size = tree.VirtualHostOf(handle)?.Count ?? -1;
        bool changed =
            node.Role != role
            || node.Name != name
            || node.Description != info?.Description
            || node.Value != value
            || node.States != states
            || node.Range != info?.Range
            || node.ChildOffset != childOffset
            || node.ScrollRange != scrollRange
            || node.Bounds != bounds
            || node.AutomationId != automationId
            || node.PositionInSet != position
            || node.SizeOfSet != size;
        node.Role = role;
        node.Name = name;
        node.Description = info?.Description;
        node.Value = value;

        // Not compared: a field's placeholder comes from its options when the field is made, and the app cannot set it.
        node.Placeholder = info?.Placeholder;
        node.States = states;
        node.Range = info?.Range;
        node.ChildOffset = childOffset;
        node.ScrollRange = scrollRange;
        node.Bounds = bounds;
        node.AutomationId = automationId;
        node.PositionInSet = position;
        node.SizeOfSet = size;
        return changed;
    }

    // The node's border box from its parent's: the boxes of the nodes of the UI tree between them, which have no role, add up. A
    // popup's root has no parent, and its box is in the window's coordinates.
    private Rect BoundsOf(AccessibilityNode node)
    {
        ref UiNode n = ref tree.Nodes.GetRef(node.Node);
        Rect rect = n.Layout.Rect;
        float x = rect.X;
        float y = rect.Y;
        NodeHandle stop = node.Parent?.Node ?? default;
        for (NodeHandle current = n.Parent; !current.IsNone && current != stop; current = tree.Nodes.GetRef(current).Parent)
        {
            Rect r = tree.Nodes.GetRef(current).Layout.Rect;
            x += r.X;
            y += r.Y;
        }

        return new Rect(x, y, rect.Width, rect.Height);
    }

    // The children of a node of the tree: the nodes with a role below its node of the UI tree, down to the first with a role on
    // each path, past the hidden subtrees. The window's children are its tree's, then each open popup's, from the back. A role whose
    // content is its value (a field, an image) has none.
    private void Collect(AccessibilityNode node)
    {
        if (HasNoChildren(node.Role))
        {
            return;
        }

        // A node below a hidden one has no node of its own (CollectBelow); the window's root has one, so its own visibility is read
        // here: hidden, the window holds its popups alone.
        bool folding = TakesNameFromContent(node.Role);
        if (!tree.Nodes.GetRef(node.Node).HidesSubtree)
        {
            CollectBelow(node.Node, folding);
        }

        if (node.Node == tree.Root)
        {
            foreach (NodeHandle root in _popupRoots)
            {
                if (tree.Nodes.IsAlive(root) && !tree.Nodes.GetRef(root).HidesSubtree && !IsExcluded(root))
                {
                    if (RoleOf(root, folding: false) != SemanticsRole.None)
                    {
                        _found.Add(root);
                    }
                    else
                    {
                        CollectBelow(root, folding: false);
                    }
                }
            }
        }
    }

    private void CollectBelow(NodeHandle parent, bool folding)
    {
        // A virtualized list's rows in the tree are those it lays out, and a row it keeps outside its range while the row holds
        // the focus; a row kept for the pointer's capture, a drag, or KeepAlive is not (design §4.4.1, "Virtualized lists").
        IVirtualHost? rows = tree.VirtualHostOf(parent);
        for (NodeHandle child = tree.Nodes.GetRef(parent).FirstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            if (tree.Nodes.GetRef(child).HidesSubtree || IsExcluded(child))
            {
                continue;
            }

            if (rows is not null && !rows.LaysOut(child, out _) && !HoldsFocus(child))
            {
                continue;
            }

            if (RoleOf(child, folding) != SemanticsRole.None)
            {
                _found.Add(child);
            }
            else
            {
                CollectBelow(child, folding);
            }
        }
    }

    // Whether a node is left out with its subtree (design §4.4.1, "Excluded nodes").
    private bool IsExcluded(NodeHandle node) => tree.IsAccessibilityExcluded(node);

    private bool HoldsFocus(NodeHandle node)
    {
        NodeHandle focused = tree.Focus.Focused;
        return tree.Nodes.IsAlive(focused) && tree.IsInSubtree(focused, node);
    }

    // Whether a node has a click handler of its own in the bubble phase, which a click routed from the node reaches first.
    private static bool TakesClick(NodeInput input)
    {
        foreach (HandlerEntry entry in input.Handlers)
        {
            if (entry.Kind == PointerEventKind.Click && entry.Phase == EventPhase.Bubble)
            {
                return true;
            }
        }

        return false;
    }

    // The nodes of a text leaf's link ranges (design §4.8): one Hyperlink node for each link, after the node's children, named by
    // the link's text and boxed by its lines. A text leaf whose text a role above folds into its name has no node of its own, and so
    // none of its links. Returns whether a link node was made or taken away.
    private bool SyncLinks(AccessibilityNode node)
    {
        int count =
            node.Role == SemanticsRole.Text && tree.Nodes.GetRef(node.Node).Text is { Ranges: { HasLinks: true } ranges } ? ranges.Links.Length : 0;
        bool changed = false;
        while (node.Links.Count > count)
        {
            AccessibilityNode last = node.Links[^1];
            node.Links.RemoveAt(node.Links.Count - 1);
            _ = _byId.Remove(last.Id.Value);
            _changes.Add(new AccessibilityChange(last.Id, AccessibilityChangeKind.Removed));
            changed = true;
        }

        for (int i = 0; i < count; i++)
        {
            if (i == node.Links.Count)
            {
                var link = new AccessibilityNode(new AccessibilityNodeId(_nextId++), node.Node)
                {
                    Parent = node,
                    LinkIndex = i,
                    Role = SemanticsRole.Hyperlink,
                    States = AccessibilityStates.Clickable,
                };
                _ = WriteLink(link);
                link.Stamp = _stamp;
                _byId[link.Id.Value] = link;
                node.Links.Add(link);
                _changes.Add(new AccessibilityChange(link.Id, AccessibilityChangeKind.Added));
                changed = true;
            }
            else if (WriteLink(node.Links[i]))
            {
                Changed(node.Links[i]);
            }
        }

        return changed;
    }

    // A link node's name, the text of the link's ranges joined, and its box in its text leaf's border box: from the caret at the
    // link's start to the caret at its end on one line, and the lines it spans at the content's width on more.
    private bool WriteLink(AccessibilityNode link)
    {
        TextLeaf leaf = tree.Nodes.GetRef(link.Node).Text!;
        TextRanges ranges = leaf.Ranges!;
        _name.Clear();
        int start = -1;
        int end = -1;
        foreach (PaintRun run in ranges.Paint)
        {
            if (run.Link == link.LinkIndex)
            {
                start = start < 0 ? run.Start : start;
                end = run.Start + run.Length;
                _ = _name.Append(leaf.Text.Slice(run.Start, run.Length));
            }
        }

        string? name =
            _name.Length == 0 ? null
            : link.Name is { } before && _name.Equals(before.AsSpan()) ? before
            : _name.ToString();
        Rect bounds = default;
        if (start >= 0 && TextCaret.HasLines(leaf))
        {
            Rect first = TextCaret.CaretGeometry(leaf, start, Affinity.Downstream).Rect;
            Rect last = TextCaret.CaretGeometry(leaf, end, Affinity.Upstream).Rect;
            Edges edges = leaf.PaddingBorder;
            if (first.Y == last.Y)
            {
                bounds = new Rect(edges.Left + first.X, edges.Top + first.Y, last.X - first.X, first.Height);
            }
            else
            {
                Rect box = tree.Nodes.GetRef(link.Node).Layout.Rect;
                float width = box.Width - edges.Left - edges.Right;
                bounds = new Rect(edges.Left, edges.Top + first.Y, width, last.Y + last.Height - first.Y);
            }
        }

        bool changed = link.Name != name || link.Bounds != bounds;
        link.Name = name;
        link.Bounds = bounds;
        return changed;
    }

    // A node's role: the window's; the one its information gives (None leaves it out); a virtualized list's box and its rows; a box
    // that scrolls; a text leaf that no node above takes as its name; a Group for a node that takes the focus, so that the focus is
    // never on a node outside the tree.
    private SemanticsRole RoleOf(NodeHandle handle, bool folding)
    {
        if (handle == tree.Root)
        {
            return SemanticsRole.Window;
        }

        if (tree.AccessibilityInfoOf(handle)?.Role is { } role)
        {
            return role;
        }

        ref UiNode n = ref tree.Nodes.GetRef(handle);
        if (tree.VirtualHostOf(handle) is not null)
        {
            return SemanticsRole.List;
        }

        if (!n.Parent.IsNone && tree.VirtualHostOf(n.Parent) is not null)
        {
            return SemanticsRole.ListItem;
        }

        if (n.Layout.Style.IsScrollContainer)
        {
            return SemanticsRole.ScrollViewer;
        }

        if (n.Text is not null && !folding)
        {
            return SemanticsRole.Text;
        }

        return n.Input is { Focusable: true } ? SemanticsRole.Group : SemanticsRole.None;
    }

    // The text of the leaves of a node's subtree that have no role, in preorder, joined by one space: the name the node had when it
    // is the same, so that a frame that changes no name makes no string.
    private string? FoldName(NodeHandle handle, string? before)
    {
        _name.Clear();
        AppendText(handle, top: true);
        if (_name.Length == 0)
        {
            return null;
        }

        return before is not null && _name.Equals(before.AsSpan()) ? before : _name.ToString();
    }

    private void AppendText(NodeHandle handle, bool top)
    {
        ref UiNode n = ref tree.Nodes.GetRef(handle);

        // A node that is a node of the tree under the folding one (CollectBelow) gives its text to its own node, not to the name.
        if (n.HidesSubtree || IsExcluded(handle) || (!top && RoleOf(handle, folding: true) != SemanticsRole.None))
        {
            return;
        }

        if (n.Text is { } leaf && leaf.Text.Length > 0)
        {
            if (_name.Length > 0)
            {
                _ = _name.Append(' ');
            }

            _ = _name.Append(leaf.Text);
        }

        for (NodeHandle child = tree.Nodes.GetRef(handle).FirstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            AppendText(child, top: false);
        }
    }

    private AccessibilityStates StatesOf(NodeHandle handle, AccessibilityInfo? info)
    {
        ref UiNode n = ref tree.Nodes.GetRef(handle);
        var states = AccessibilityStates.None;
        if ((n.Pseudo & PseudoState.Disabled) != 0)
        {
            states |= AccessibilityStates.Disabled;
        }

        if (tree.Focus.Focused == handle)
        {
            states |= AccessibilityStates.Focused;
        }

        if (n.Input is { Focusable: true })
        {
            states |= AccessibilityStates.Focusable;
        }

        if (n.Input is { } input && TakesClick(input))
        {
            states |= AccessibilityStates.Clickable;
        }

        if (n.Layout.Style.IsScrollContainer)
        {
            states |= AccessibilityStates.ClipsChildren;
        }

        if (tree.IsPopupAnchor(handle))
        {
            states |= AccessibilityStates.Expandable;
            if ((n.Pseudo & PseudoState.Open) != 0)
            {
                states |= AccessibilityStates.Expanded;
            }
        }

        SemanticsStates own = info?.States ?? SemanticsStates.None;
        if ((own & SemanticsStates.Checked) != 0)
        {
            states |= AccessibilityStates.Checked;
        }

        if ((own & SemanticsStates.Mixed) != 0)
        {
            states |= AccessibilityStates.Mixed;
        }

        if ((own & SemanticsStates.Selected) != 0)
        {
            states |= AccessibilityStates.Selected;
        }

        if ((own & SemanticsStates.ReadOnly) != 0)
        {
            states |= AccessibilityStates.ReadOnly;
        }

        if ((own & SemanticsStates.Required) != 0)
        {
            states |= AccessibilityStates.Required;
        }

        return states | (info?.PartStates ?? AccessibilityStates.None);
    }

    // The roles that take their name from the text of their content, whose text leaves are not nodes of their own (Flutter merges a
    // button's children, Avalonia names a button by its content).
    private static bool TakesNameFromContent(SemanticsRole role) =>
        role
            is SemanticsRole.Button
                or SemanticsRole.CheckBox
                or SemanticsRole.RadioButton
                or SemanticsRole.ComboBox
                or SemanticsRole.Hyperlink
                or SemanticsRole.ListItem
                or SemanticsRole.MenuItem
                or SemanticsRole.TabItem
                or SemanticsRole.ToolTip
                or SemanticsRole.Text;

    // The roles whose content is their value, with no children in the tree.
    private static bool HasNoChildren(SemanticsRole role) =>
        role is SemanticsRole.Edit or SemanticsRole.Image or SemanticsRole.Slider or SemanticsRole.ProgressBar or SemanticsRole.Separator;

    private bool PopupRootsChanged()
    {
        IReadOnlyList<PopupHandle> open = tree.Popups.Opened;
        if (open.Count != _popupRoots.Count)
        {
            return true;
        }

        for (int i = 0; i < open.Count; i++)
        {
            if (open[i].Root != _popupRoots[i])
            {
                return true;
            }
        }

        return false;
    }

    private void SnapshotPopupRoots()
    {
        _popupRoots.Clear();
        IReadOnlyList<PopupHandle> open = tree.Popups.Opened;
        for (int i = 0; i < open.Count; i++)
        {
            _popupRoots.Add(open[i].Root);
        }
    }

    private void Send()
    {
        if (_changes.Count > 0)
        {
            Bridge?.NodesChanged(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_changes));
            _changes.Clear();
        }
    }
}
