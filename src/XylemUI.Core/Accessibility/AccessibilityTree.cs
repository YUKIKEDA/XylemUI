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
    private readonly StringBuilder _description = new();
    private readonly List<AccessibilityNodeId> _ids = [];

    // The nodes that hold a relation, by the node it names (design §4.4.1, "Relations"): kept while the tree is made, so that a change
    // of the node named makes the holder's node again.
    private readonly Dictionary<NodeHandle, List<NodeHandle>> _holders = [];
    private readonly List<NodeHandle> _relationWork = [];
    private readonly HashSet<NodeHandle> _relationQueued = [];
    private readonly HashSet<NodeHandle> _relationDone = [];

    // What reads the signals of the active descendants and of the draw functions of nodes, for the nodes in the tree.
    private readonly Dictionary<NodeHandle, AccessibilityWatch> _descendantWatches = [];
    private readonly Dictionary<NodeHandle, AccessibilityWatch> _childWatches = [];

    // What a draw function of nodes is handed, and the nodes made again from it.
    private readonly AccessibleChildren _drawn = new();
    private readonly Dictionary<int, AccessibilityNode> _keyed = [];
    private readonly List<AccessibilityNode> _made = [];
    private readonly HashSet<int> _keys = [];
    private Action? _runBuild;
    private AccessibleChildrenFunc? _building;
    private Rect _buildBounds;
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
        foreach (KeyValuePair<NodeHandle, AccessibilityRelations> held in tree.AccessibilityRelationHolders)
        {
            IndexRelations(held.Key, held.Value);
        }

        SnapshotPopupRoots();
        Root = Make(tree.Root, parent: null);
        RefreshHolders();
        Send();
    }

    /// <summary>
    /// Notes that a node holds a relation to another, once the tree is made: a change of the other makes the holder's node again.
    /// </summary>
    /// <param name="holder">The node that holds the relation.</param>
    /// <param name="target">The node it names.</param>
    public void NoteRelation(NodeHandle holder, NodeHandle target)
    {
        if (!_holders.TryGetValue(target, out List<NodeHandle>? holders))
        {
            holders = [];
            _holders[target] = holders;
        }

        if (!holders.Contains(holder))
        {
            holders.Add(holder);
        }
    }

    /// <summary>
    /// Forgets the relations of a node that is removed, once the tree is made.
    /// </summary>
    /// <param name="holder">The node.</param>
    /// <param name="relations">Its relations.</param>
    public void ForgetRelations(NodeHandle holder, AccessibilityRelations relations)
    {
        Forget(holder, relations.LabelledBy);
        Forget(holder, relations.DescribedBy);
        Forget(holder, relations.Controls);
        Forget(holder, relations.ErrorMessage);
        StopWatch(_descendantWatches, holder);
    }

    /// <summary>
    /// Carries out assistive technology's Increment on a node (design §4.4.1, "Actions other than a click"): its handler, unless it is
    /// disabled.
    /// </summary>
    /// <param name="node">The node.</param>
    public void Increment(AccessibilityNode node) => HandlersOf(node)?.Increment?.Invoke();

    /// <summary>
    /// Carries out assistive technology's Decrement on a node: its handler, unless it is disabled.
    /// </summary>
    /// <param name="node">The node.</param>
    public void Decrement(AccessibilityNode node) => HandlersOf(node)?.Decrement?.Invoke();

    /// <summary>
    /// Carries out a number assistive technology sets on a node that has a range (design §4.4.1, "Setting a number"): NaN is dropped,
    /// and another is brought into the range. The node's handler of a number takes it; with none, a larger number than the value
    /// calls the handler of Increment once, a smaller one that of Decrement once, and the same calls neither. A disabled node takes
    /// nothing.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The number.</param>
    public void SetRangeValue(AccessibilityNode node, double value)
    {
        if (double.IsNaN(value) || node.Range is not { } range || HandlersOf(node) is not { } handlers)
        {
            return;
        }

        double clamped = Math.Max(range.Minimum, Math.Min(range.Maximum, value));
        if (handlers.SetRangeValue is { } set)
        {
            set(clamped);
        }
        else if (clamped > range.Value)
        {
            handlers.Increment?.Invoke();
        }
        else if (clamped < range.Value)
        {
            handlers.Decrement?.Invoke();
        }
    }

    /// <summary>
    /// Carries out assistive technology's Expand or Collapse on a node (design §4.4.1, "Opening and closing"): a popup's anchor opens or
    /// closes its popup; another node's handler is called, unless it is disabled. A node with neither drops it.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="open">Whether to open it.</param>
    public void SetExpanded(AccessibilityNode node, bool open)
    {
        if (node.ChildKey is not null || !tree.Nodes.IsAlive(node.Node) || tree.SetPopupOpen(node.Node, open))
        {
            return;
        }

        AccessibilityHandlers? handlers = HandlersOf(node);
        (open ? handlers?.Expand : handlers?.Collapse)?.Invoke();
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

            // A node a relation names may have no node of its own, and give its leaves' text: a change below it renames the holder.
            if (_holders.Count > 0)
            {
                for (NodeHandle at = node; !at.IsNone; at = tree.Nodes.GetRef(at).Parent)
                {
                    QueueHolders(at);
                }
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
        RefreshHolders();
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
        _ = SyncSynthetic(node);
        _ = SyncActive(node);
        QueueHolders(handle);
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

        if (SyncSynthetic(node))
        {
            changed = true;
        }

        if (SyncActive(node))
        {
            changed = true;
        }

        if (changed)
        {
            Changed(node);
        }
    }

    // Makes again the nodes whose relations name a node that was made, went away, or was renamed in this update, once each; a holder
    // renamed again after its turn is marked for the next update, so that a cycle of names ends.
    private void RefreshHolders()
    {
        for (int i = 0; i < _relationWork.Count; i++)
        {
            NodeHandle holder = _relationWork[i];
            _ = _relationQueued.Remove(holder);
            if (!tree.Nodes.IsAlive(holder))
            {
                continue;
            }

            if (!_relationDone.Add(holder))
            {
                MarkSelf(holder);
                continue;
            }

            if (_nodes.TryGetValue(holder, out AccessibilityNode? node) && (Write(node) | SyncActive(node)))
            {
                Changed(node);
            }
        }

        _relationWork.Clear();
        _relationQueued.Clear();
        _relationDone.Clear();
    }

    // Queues the nodes whose relations name a node, once each, to be made again after the nodes the marks reach.
    private void QueueHolders(NodeHandle target)
    {
        if (_holders.Count > 0 && _holders.TryGetValue(target, out List<NodeHandle>? holders))
        {
            foreach (NodeHandle holder in holders)
            {
                if (_relationQueued.Add(holder))
                {
                    _relationWork.Add(holder);
                }
            }
        }
    }

    private void IndexRelations(NodeHandle holder, AccessibilityRelations relations)
    {
        Index(holder, relations.LabelledBy);
        Index(holder, relations.DescribedBy);
        Index(holder, relations.Controls);
        if (!relations.ErrorMessage.IsNone)
        {
            NoteRelation(holder, relations.ErrorMessage);
        }
    }

    private void Index(NodeHandle holder, List<NodeHandle>? targets)
    {
        if (targets is not null)
        {
            foreach (NodeHandle target in targets)
            {
                NoteRelation(holder, target);
            }
        }
    }

    private void Forget(NodeHandle holder, List<NodeHandle>? targets)
    {
        if (targets is not null)
        {
            foreach (NodeHandle target in targets)
            {
                Forget(holder, target);
            }
        }
    }

    private void Forget(NodeHandle holder, NodeHandle target)
    {
        if (_holders.TryGetValue(target, out List<NodeHandle>? holders) && holders.Remove(holder) && holders.Count == 0)
        {
            _ = _holders.Remove(target);
        }
    }

    private static void StopWatch(Dictionary<NodeHandle, AccessibilityWatch> watches, NodeHandle node)
    {
        if (watches.Count > 0 && watches.Remove(node, out AccessibilityWatch? watch))
        {
            watch.Stop();
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

        foreach (AccessibilityNode drawn in node.Synthetic)
        {
            RemoveSynthetic(drawn);
        }

        if (_nodes.TryGetValue(node.Node, out AccessibilityNode? same) && same == node)
        {
            _ = _nodes.Remove(node.Node);
            StopWatch(_childWatches, node.Node);
            StopWatch(_descendantWatches, node.Node);
            QueueHolders(node.Node);
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
        AccessibilityRelations? relations = tree.AccessibilityRelationsOf(handle);
        string? name = NameOf(handle, role, relations, node.Name);
        if (handle == tree.Root)
        {
            name ??= tree.Title;
        }

        string? value = info?.Value ?? tree.AccessibilityValueOf(handle);
        AccessibilityStates states = StatesOf(handle, info);
        string? description = relations is null ? info?.Description : DescriptionOf(info, relations, states, node.Description);
        ref UiNode n = ref tree.Nodes.GetRef(handle);
        Vector2 childOffset = n.Layout.Style.IsScrollContainer ? -tree.ScrollOffsetOf(handle) : Vector2.Zero;
        Size scrollRange = n.Layout.Style.IsScrollContainer ? tree.MaxScrollOffsetOf(handle) : default;
        Rect bounds = node.Parent is null ? n.Layout.Rect : BoundsOf(node);
        string? automationId = tree.TestIdOf(handle);

        // The app's place and count win over a virtualized list's (design §4.4.1, "Level, place, and count").
        int position =
            info is { PositionInSet: > 0 } ? info.PositionInSet - 1
            : !n.Parent.IsNone && tree.VirtualHostOf(n.Parent) is { } rows && rows.IndexOf(handle, out int index) ? index
            : -1;
        int size = info is { SizeOfSet: > 0 } ? info.SizeOfSet : tree.VirtualHostOf(handle)?.Count ?? -1;
        int level = info is { Level: > 0 } ? info.Level - 1 : -1;
        SemanticsLive live = info is { Live: not SemanticsLive.Inherit } ? info.Live : LiveOf(role);
        SemanticsOrientation orientation = info?.Orientation ?? SemanticsOrientation.None;
        bool renamed = node.Name != name;
        bool changed =
            node.Role != role
            || renamed
            || node.Description != description
            || node.Value != value
            || node.States != states
            || node.Range != info?.Range
            || node.ChildOffset != childOffset
            || node.ScrollRange != scrollRange
            || node.Bounds != bounds
            || node.AutomationId != automationId
            || node.PositionInSet != position
            || node.SizeOfSet != size
            || node.Level != level
            || node.Live != live
            || node.Orientation != orientation;
        if (renamed)
        {
            QueueHolders(handle);
        }

        node.Role = role;
        node.Name = name;
        node.Description = description;
        node.Value = value;
        node.Level = level;
        node.Live = live;
        node.Orientation = orientation;
        changed |= WriteRelations(node, relations);

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

    // A node's name (design §4.4.1, "Folding on Windows"): the app's name, the text of the nodes that name it, its part's name, and
    // the text of its content for a role that takes its name from it, the first there is. The name the node had is kept when it is
    // the same, so that a frame that changes no name makes no string.
    private string? NameOf(NodeHandle handle, SemanticsRole role, AccessibilityRelations? relations, string? before)
    {
        if (tree.AppAccessibilityOf(handle)?.Name is { } own)
        {
            return own;
        }

        if (relations?.LabelledBy is { Count: > 0 } labels)
        {
            _name.Clear();
            foreach (NodeHandle label in labels)
            {
                AppendTargetText(_name, label);
            }

            if (_name.Length > 0)
            {
                return Kept(_name, before);
            }
        }

        if (tree.PartAccessibilityOf(handle)?.Name is { } part)
        {
            return part;
        }

        return TakesNameFromContent(role) ? FoldName(handle, before) : null;
    }

    // A node's description with its relations: while the node is invalid, the text of its error message first, then the app's
    // description and the text of the nodes that describe it, joined by one space (design §4.4.1, "Folding on Windows"). A node invalid
    // with no error message gets no word of it: Windows says nothing of it until row AX2.
    private string? DescriptionOf(AccessibilityInfo? info, AccessibilityRelations relations, AccessibilityStates states, string? before)
    {
        _description.Clear();
        if ((states & AccessibilityStates.Invalid) != 0 && !relations.ErrorMessage.IsNone)
        {
            AppendTargetText(_description, relations.ErrorMessage);
        }

        if (info?.Description is { Length: > 0 } own)
        {
            Append(_description, own);
        }

        if (relations.DescribedBy is { } described)
        {
            foreach (NodeHandle target in described)
            {
                AppendTargetText(_description, target);
            }
        }

        return _description.Length == 0 ? null : Kept(_description, before);
    }

    // The text a node a relation names gives: its node's name, or, for a node with none, the text of its leaves in preorder. A node
    // that is gone, or in a hidden or excluded subtree, gives none.
    private void AppendTargetText(StringBuilder text, NodeHandle target)
    {
        if (target.IsNone || !tree.Nodes.IsAlive(target))
        {
            return;
        }

        for (NodeHandle at = target; !at.IsNone; at = tree.Nodes.GetRef(at).Parent)
        {
            if (tree.Nodes.GetRef(at).HidesSubtree || IsExcluded(at))
            {
                return;
            }
        }

        if (_nodes.TryGetValue(target, out AccessibilityNode? node) && node.Name is { Length: > 0 } name)
        {
            Append(text, name);
        }
        else
        {
            AppendText(text, target, top: true);
        }
    }

    private static void Append(StringBuilder text, string part)
    {
        if (text.Length > 0)
        {
            _ = text.Append(' ');
        }

        _ = text.Append(part);
    }

    // The text built, as the string there was when it is the same.
    private static string Kept(StringBuilder text, string? before) => before is not null && text.Equals(before.AsSpan()) ? before : text.ToString();

    // The ids of the nodes a node's relations name, those in the tree (design §4.4.1, "Relations"): a node that is gone, hidden,
    // excluded, out of a virtualized list's range, or has no node is not handed to AccessKit. Returns whether one changed.
    private bool WriteRelations(AccessibilityNode node, AccessibilityRelations? relations)
    {
        bool changed = node.SetRelation(AccessibilityRelation.LabelledBy, IdsOf(relations?.LabelledBy));
        changed |= node.SetRelation(AccessibilityRelation.DescribedBy, IdsOf(relations?.DescribedBy));
        changed |= node.SetRelation(AccessibilityRelation.Controls, IdsOf(relations?.Controls));
        AccessibilityNodeId error = relations is null ? default : IdOf(relations.ErrorMessage);
        if (node.ErrorMessage != error)
        {
            node.ErrorMessage = error;
            changed = true;
        }

        return changed;
    }

    private ReadOnlySpan<AccessibilityNodeId> IdsOf(List<NodeHandle>? targets)
    {
        _ids.Clear();
        if (targets is not null)
        {
            foreach (NodeHandle target in targets)
            {
                if (IdOf(target) is { Value: not 0 } id)
                {
                    _ids.Add(id);
                }
            }
        }

        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_ids);
    }

    private AccessibilityNodeId IdOf(NodeHandle target) =>
        !target.IsNone && _nodes.TryGetValue(target, out AccessibilityNode? node) ? node.Id : default;

    // The node's active descendant (design §4.4.1, "Relations"): read from its signal, which a watch reads only while the tree is
    // made; an element's node, or the node of a key among the nodes the node's draw function adds. Returns whether it changed.
    private bool SyncActive(AccessibilityNode node)
    {
        AccessibilityNodeId active = default;
        if (tree.AccessibilityRelationsOf(node.Node) is { } relations && (relations.ActiveElement is not null || relations.ActiveKey is not null))
        {
            if (!_descendantWatches.TryGetValue(node.Node, out AccessibilityWatch? watch))
            {
                watch = MakeActiveWatch(node.Node, relations);
                _descendantWatches[node.Node] = watch;
            }

            watch.Track();
            active = relations.ActiveKey is not null
                ? (relations.ReadKey is int key ? DrawnIdOf(node.Synthetic, key) : default)
                : IdOf(relations.ReadElement);
        }
        else
        {
            StopWatch(_descendantWatches, node.Node);
        }

        if (node.ActiveDescendant == active)
        {
            return false;
        }

        node.ActiveDescendant = active;
        return true;
    }

    // A watch of a node's active descendant, made once. Its lambda is in a method of its own: in SyncActive, the closure of the
    // relations would be made at each call, for each node made again, also one with no active descendant.
    private AccessibilityWatch MakeActiveWatch(NodeHandle holder, AccessibilityRelations relations) => new(tree, holder, () => ReadActive(relations));

    // What a watch of an active descendant runs: an element of another tree, and default, are none.
    private void ReadActive(AccessibilityRelations relations)
    {
        relations.ReadElement = relations.ActiveElement?.Get() is { } element && element.Tree == tree ? element.Node : default;
        relations.ReadKey = relations.ActiveKey?.Get();
    }

    private static AccessibilityNodeId DrawnIdOf(List<AccessibilityNode> drawn, int key)
    {
        foreach (AccessibilityNode node in drawn)
        {
            if (node.ChildKey == key)
            {
                return node.Id;
            }

            if (DrawnIdOf(node.Children, key) is { Value: not 0 } id)
            {
                return id;
            }
        }

        return default;
    }

    // The nodes a node's draw function adds (design §4.4.1, "The nodes of a draw function"), after its children and its link nodes:
    // made again from what the function adds now, a node of a key there before keeping its id. The function runs in a watch, whose
    // signals mark the node. Returns whether the node's own list of them changed.
    private bool SyncSynthetic(AccessibilityNode owner)
    {
        AccessibleChildrenFunc? build = HasNoChildren(owner.Role) ? null : tree.AccessibleChildrenOf(owner.Node);
        if (build is null)
        {
            StopWatch(_childWatches, owner.Node);
            if (owner.Synthetic.Count == 0)
            {
                return false;
            }

            foreach (AccessibilityNode drawn in owner.Synthetic)
            {
                RemoveSynthetic(drawn);
            }

            owner.Synthetic.Clear();
            return true;
        }

        if (!_childWatches.TryGetValue(owner.Node, out AccessibilityWatch? watch))
        {
            watch = new AccessibilityWatch(tree, owner.Node, _runBuild ??= RunBuild);
            _childWatches[owner.Node] = watch;
        }

        Rect box = tree.Nodes.GetRef(owner.Node).Layout.Rect;
        _drawn.Clear();
        _building = build;
        _buildBounds = new Rect(0, 0, box.Width, box.Height);
        try
        {
            watch.Track();
        }
        finally
        {
            _building = null;
        }

        _keyed.Clear();
        IndexSynthetic(owner.Synthetic);
        _made.Clear();
        _keys.Clear();
        for (int i = 0; i < _drawn.Count; i++)
        {
            AccessibleChildren.Entry entry = _drawn[i];
            bool repeated = !_keys.Add(entry.Key);
#if DEBUG
            if (repeated)
            {
                Invariant.Fail("A draw function added two nodes of one key (AccessibleChildren); a key is unique in the node that draws.");
            }
#endif
            AccessibilityNode drawn = !repeated && _keyed.Remove(entry.Key, out AccessibilityNode? before) ? before : MakeSynthetic(owner, entry.Key);
            Rect bounds = entry.Parent < 0 ? entry.Bounds : Offset(entry.Bounds, _drawn[entry.Parent].Bounds);
            if (WriteSynthetic(drawn, entry.Info, bounds))
            {
                Changed(drawn);
            }

            _made.Add(drawn);
        }

        // The lists are made again; a node that moved, came, or went changes the list it left and the list it joined.
        bool changed = false;
        owner.Synthetic.Clear();
        foreach (AccessibilityNode drawn in _made)
        {
            drawn.Children.Clear();
        }

        for (int i = 0; i < _made.Count; i++)
        {
            AccessibilityNode drawn = _made[i];
            int at = _drawn[i].Parent;
            AccessibilityNode parent = at < 0 ? owner : _made[at];
            List<AccessibilityNode> list = at < 0 ? owner.Synthetic : parent.Children;
            if (drawn.Parent != parent || drawn.SyntheticIndex != list.Count)
            {
                changed |= ListChanged(drawn.Parent, owner) | ListChanged(parent, owner);
            }

            drawn.Parent = parent;
            drawn.SyntheticIndex = list.Count;
            list.Add(drawn);
        }

        foreach (AccessibilityNode gone in _keyed.Values)
        {
            changed |= ListChanged(gone.Parent, owner);
            gone.Children.Clear();
            _ = _byId.Remove(gone.Id.Value);
            _changes.Add(new AccessibilityChange(gone.Id, AccessibilityChangeKind.Removed));
        }

        _keyed.Clear();
        _made.Clear();
        return changed;
    }

    // Records the change of a list of drawn nodes: the owner's own list is the owner's change, which its caller records.
    private bool ListChanged(AccessibilityNode? parent, AccessibilityNode owner)
    {
        if (parent == owner)
        {
            return true;
        }

        if (parent is not null)
        {
            Changed(parent);
        }

        return false;
    }

    private void RunBuild() => _building!(_drawn, _buildBounds);

    private void IndexSynthetic(List<AccessibilityNode> drawn)
    {
        foreach (AccessibilityNode node in drawn)
        {
            if (node.ChildKey is int key)
            {
                _keyed[key] = node;
            }

            IndexSynthetic(node.Children);
        }
    }

    private AccessibilityNode MakeSynthetic(AccessibilityNode owner, int key)
    {
        var drawn = new AccessibilityNode(new AccessibilityNodeId(_nextId++), owner.Node) { ChildKey = key, Stamp = _stamp };
        _byId[drawn.Id.Value] = drawn;
        _changes.Add(new AccessibilityChange(drawn.Id, AccessibilityChangeKind.Added));
        return drawn;
    }

    // The values of a drawn node from the information its function gave: no action, so a range is read-only.
    private static bool WriteSynthetic(AccessibilityNode node, AccessibilityInfo info, Rect bounds)
    {
        SemanticsRole role = info.Role ?? SemanticsRole.Group;
        AccessibilityStates states = AppStates(info.States) | (info.Range is null ? AccessibilityStates.None : AccessibilityStates.ReadOnly);
        if ((info.States & SemanticsStates.Expandable) != 0)
        {
            states |= AccessibilityStates.Expandable;
            if ((info.States & SemanticsStates.Expanded) != 0)
            {
                states |= AccessibilityStates.Expanded;
            }
        }

        SemanticsLive live = info.Live != SemanticsLive.Inherit ? info.Live : LiveOf(role);
        int level = info.Level > 0 ? info.Level - 1 : -1;
        int position = info.PositionInSet > 0 ? info.PositionInSet - 1 : -1;
        int size = info.SizeOfSet > 0 ? info.SizeOfSet : -1;
        bool changed =
            node.Role != role
            || node.Name != info.Name
            || node.Description != info.Description
            || node.Value != info.Value
            || node.States != states
            || node.Range != info.Range
            || node.Bounds != bounds
            || node.Live != live
            || node.Orientation != info.Orientation
            || node.Level != level
            || node.PositionInSet != position
            || node.SizeOfSet != size;
        node.Role = role;
        node.Name = info.Name;
        node.Description = info.Description;
        node.Value = info.Value;
        node.States = states;
        node.Range = info.Range;
        node.Bounds = bounds;
        node.Live = live;
        node.Orientation = info.Orientation;
        node.Level = level;
        node.PositionInSet = position;
        node.SizeOfSet = size;
        return changed;
    }

    private static Rect Offset(Rect rect, Rect parent) => new(rect.X - parent.X, rect.Y - parent.Y, rect.Width, rect.Height);

    private void RemoveSynthetic(AccessibilityNode drawn)
    {
        foreach (AccessibilityNode child in drawn.Children)
        {
            RemoveSynthetic(child);
        }

        _ = _byId.Remove(drawn.Id.Value);
        _changes.Add(new AccessibilityChange(drawn.Id, AccessibilityChangeKind.Removed));
    }

    // The handlers of the actions other than a click a node has, which a disabled node and a drawn node do not take.
    private AccessibilityHandlers? HandlersOf(AccessibilityNode node) =>
        node.ChildKey is null && tree.Nodes.IsAlive(node.Node) && (tree.Nodes.GetRef(node.Node).Pseudo & PseudoState.Disabled) == 0
            ? tree.AccessibilityHandlersOf(node.Node)
            : null;

    // The setting a role's node gives a change of its name when the app gives none (WAI-ARIA 1.2's implicit aria-live of alert, status,
    // and log): AccessKit makes none from the role, and a node with none takes its parent's.
    private static SemanticsLive LiveOf(SemanticsRole role) =>
        role switch
        {
            SemanticsRole.Alert => SemanticsLive.Assertive,
            SemanticsRole.Status or SemanticsRole.Log => SemanticsLive.Polite,
            _ => SemanticsLive.Inherit,
        };

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

        bool folding = TakesNameFromContent(node.Role);
        CollectBelow(node.Node, folding);
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
        AppendText(_name, handle, top: true);
        return _name.Length == 0 ? null : Kept(_name, before);
    }

    private void AppendText(StringBuilder text, NodeHandle handle, bool top)
    {
        ref UiNode n = ref tree.Nodes.GetRef(handle);

        // A node that is a node of the tree under the folding one (CollectBelow) gives its text to its own node, not to the name.
        if (n.HidesSubtree || IsExcluded(handle) || (!top && RoleOf(handle, folding: true) != SemanticsRole.None))
        {
            return;
        }

        if (n.Text is { } leaf && leaf.Text.Length > 0)
        {
            if (text.Length > 0)
            {
                _ = text.Append(' ');
            }

            _ = text.Append(leaf.Text);
        }

        for (NodeHandle child = tree.Nodes.GetRef(handle).FirstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            AppendText(text, child, top: false);
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

        SemanticsStates own = info?.States ?? SemanticsStates.None;
        AccessibilityStates handled = tree.AccessibilityHandlersOf(handle)?.States() ?? AccessibilityStates.None;

        // One true value of the open state: a popup's anchor's comes from its popup, another node's from the app's two bits, of which
        // Expanded alone is not read (design §4.4.1, "States").
        if (tree.IsPopupAnchor(handle))
        {
            states |= AccessibilityStates.Expandable;
            if ((n.Pseudo & PseudoState.Open) != 0)
            {
                states |= AccessibilityStates.Expanded;
            }
        }
        else if ((own & SemanticsStates.Expandable) != 0)
        {
#if DEBUG
            if ((handled & AccessibilityStates.ExpandsByHandler) == 0)
            {
                Invariant.Fail(
                    "A node is expandable (SemanticsStates.Expandable) with no popup and no handler of Expand or Collapse (OnExpand, OnCollapse)."
                );
            }
#endif
            states |= AccessibilityStates.Expandable;
            if ((own & SemanticsStates.Expanded) != 0)
            {
                states |= AccessibilityStates.Expanded;
            }
        }

        // A range no handler can change is read-only, so that UIA does not show a value it can write (design §4.4.1, "Read-only").
        if (
            info?.Range is not null
            && (handled & (AccessibilityStates.Increments | AccessibilityStates.Decrements | AccessibilityStates.SetsRangeValue)) == 0
        )
        {
            states |= AccessibilityStates.ReadOnly;
        }

        return states | AppStates(own) | handled | (info?.PartStates ?? AccessibilityStates.None);
    }

    // The states only the app knows, but the open state, as the tree's.
    private static AccessibilityStates AppStates(SemanticsStates own)
    {
        var states = AccessibilityStates.None;
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

        if ((own & SemanticsStates.Invalid) != 0)
        {
            states |= AccessibilityStates.Invalid;
        }

        if ((own & SemanticsStates.Multiselectable) != 0)
        {
            states |= AccessibilityStates.Multiselectable;
        }

        if ((own & SemanticsStates.Busy) != 0)
        {
            states |= AccessibilityStates.Busy;
        }

        if ((own & SemanticsStates.Modal) != 0)
        {
            states |= AccessibilityStates.Modal;
        }

        return states;
    }

    // The roles that take their name from the text of their content, whose text leaves are not nodes of their own (Flutter merges a
    // button's children, Avalonia names a button by its content).
    // The roles row AX1 added take their name from their content as the table of roles says (design §4.4.1, "The shape AX1 adds").
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
                or SemanticsRole.Text
                or SemanticsRole.Status
                or SemanticsRole.Alert
                or SemanticsRole.Switch
                or SemanticsRole.MenuItemCheckBox
                or SemanticsRole.MenuItemRadio
                or SemanticsRole.ListBoxItem
                or SemanticsRole.TreeItem
                or SemanticsRole.Cell
                or SemanticsRole.GridCell
                or SemanticsRole.ColumnHeader
                or SemanticsRole.RowHeader
                or SemanticsRole.Heading
                or SemanticsRole.Disclosure;

    // The roles whose content is their value, with no children in the tree.
    private static bool HasNoChildren(SemanticsRole role) =>
        role
            is SemanticsRole.Edit
                or SemanticsRole.Image
                or SemanticsRole.Slider
                or SemanticsRole.ProgressBar
                or SemanticsRole.Separator
                or SemanticsRole.Meter
                or SemanticsRole.SpinButton
                or SemanticsRole.SearchBox
                or SemanticsRole.ScrollBar
                or SemanticsRole.DateInput
                or SemanticsRole.TimeInput
                or SemanticsRole.DateTimeInput;

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
