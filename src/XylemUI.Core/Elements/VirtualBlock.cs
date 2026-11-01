using System.Numerics;

namespace XylemUI;

/// <summary>
/// What a <c>VirtualFor</c> keeps (design §3.4, "The shape of P02e"): its box, its items and their keys, the lengths of the rows
/// measured, the rows it made, and the anchor of the scroll. Each row is a scope of its own under the block's rows' owner, as a row
/// of <c>For</c> is. The rows are the box's children, in the order of their indices.
/// </summary>
/// <typeparam name="T">The type of an item.</typeparam>
/// <typeparam name="TKey">The type of an item's key.</typeparam>
internal sealed class VirtualBlock<T, TKey> : IVirtualHost
    where TKey : notnull
{
    private readonly UiTree _tree;
    private readonly NodeHandle _box;
    private readonly Func<T, TKey> _key;
    private readonly Func<IReadOnlySignal<T>, Element> _render;
    private readonly VirtualOptions _options;
    private readonly VirtualListState _state;
    private readonly bool _horizontal;

    // The length each key's row took when it was laid out; the keys gone, a list kept to remove them.
    private readonly Dictionary<TKey, float> _lengths = [];
    private readonly List<TKey> _gone = [];

    // The rows made: by key, by node, and in the order of their indices; the rows dropped, disposed when the box is without them.
    private readonly Dictionary<TKey, Row> _rows = [];
    private readonly Dictionary<NodeHandle, Row> _rowOf = [];
    private readonly List<Row> _order = [];
    private readonly List<Row> _dropped = [];
    private ScopeRef _rowsOwner;

    // The items, their keys by index, and the index of each key.
    private IReadOnlyList<T> _items = [];
    private TKey[] _keys = [];
    private TKey[] _nextKeys = [];
    private int _count;
    private Dictionary<TKey, int> _indexOf = [];
    private Dictionary<TKey, int> _nextIndexOf = [];

    // The lengths by index (NaN for none), and two Fenwick trees of the measured lengths and of the rows measured, which give the
    // position of an index with the estimate for the rest.
    private float[] _extents = [];
    private double[] _lengthTree = [];
    private int[] _countTree = [];
    private double _measuredTotal;
    private int _measuredRows;
    private float _gap;
    private float _paddingStart;
    private float _paddingEnd;

    // The range laid out, and whether the items changed since the last materialization.
    private int _first = -1;
    private int _last = -1;
    private bool _changed;

    // The anchor (design §3.4, "The anchor of the scroll"): the row at the viewport's start, its key, and how far into it the
    // viewport starts. The position the scroll state held when the anchor was last found or written.
    private int _anchor;
    private TKey? _anchorKey;
    private bool _hasAnchorKey;
    private float _shift;
    private float _lastOffset;

    // A move by index waiting for its row to be measured: the index, or -1, and the alignment.
    private int _pendingIndex = -1;
    private ScrollAlign _pendingAlign;

    private VirtualBlock(
        UiTree tree,
        NodeHandle box,
        Func<T, TKey> key,
        Func<IReadOnlySignal<T>, Element> render,
        VirtualListState state,
        VirtualOptions options,
        bool horizontal
    )
    {
        _tree = tree;
        _box = box;
        _key = key;
        _render = render;
        _state = state;
        _options = options;
        _horizontal = horizontal;
    }

    /// <inheritdoc/>
    public float? FixedExtent => _options.FixedExtent;

    /// <inheritdoc/>
    public bool Horizontal => _horizontal;

    // The length a row not measured yet takes: the option's, the mean of the rows measured, or 0 when neither is known yet.
    private float Estimate => _options.EstimatedExtent ?? (_measuredRows > 0 ? (float)(_measuredTotal / _measuredRows) : 0);

    // Whether a position can be found for an index other than the anchor's: a length is known for the rows not measured.
    private bool EstimateKnown => _options.FixedExtent is not null || _options.EstimatedExtent is not null || _measuredRows > 0;

    /// <summary>
    /// Makes a block in a scope of its own under the current one, as <c>For</c> does: the owner of the rows, a scope whose cleanup
    /// takes every row out of the box, and the effect that reads the items. The block is the box's virtual host until its scope goes.
    /// </summary>
    /// <param name="tree">The tree the box is in.</param>
    /// <param name="box">The box, with no child.</param>
    /// <param name="items">The items.</param>
    /// <param name="key">The key of an item.</param>
    /// <param name="render">Makes a row from its item's signal.</param>
    /// <param name="state">The list's state.</param>
    /// <param name="options">How the rows are made.</param>
    /// <param name="horizontal">Whether the box scrolls along the horizontal axis.</param>
    internal static void Create(
        UiTree tree,
        NodeHandle box,
        IReadOnlySignal<IReadOnlyList<T>> items,
        Func<T, TKey> key,
        Func<IReadOnlySignal<T>, Element> render,
        VirtualListState state,
        VirtualOptions options,
        bool horizontal
    )
    {
        var block = new VirtualBlock<T, TKey>(tree, box, key, render, state, options, horizontal);
        tree.SetVirtualHost(box, block);
        state.Host = block;
        _ = Scope.Create(
            () =>
            {
                _ = Scope.Create(() => block._rowsOwner = Scope.CurrentRef, "VirtualFor rows");
                _ = Scope.Create(() => Scope.OnCleanup(block.Teardown), "VirtualFor teardown");
                _ = Effect.Create(() => block.Update(items.Get()));
            },
            "VirtualFor"
        );
    }

    /// <inheritdoc/>
    public bool LaysOut(NodeHandle row, out int index)
    {
        if (_rowOf.TryGetValue(row, out Row? r) && r.Live)
        {
            index = r.Index;
            return true;
        }

        index = -1;
        return false;
    }

    /// <inheritdoc/>
    public void Measured(int index, float extent)
    {
        if (_options.FixedExtent is not null || (uint)index >= (uint)_count)
        {
            return;
        }

        float old = _extents[index];
        if (old == extent)
        {
            return;
        }

        _lengths[_keys[index]] = extent;
        _extents[index] = extent;
        if (float.IsNaN(old))
        {
            AddToTrees(index, extent, 1);
            _measuredTotal += extent;
            _measuredRows++;
        }
        else
        {
            AddToTrees(index, extent - old, 0);
            _measuredTotal += extent - old;
        }
    }

    /// <inheritdoc/>
    public void BeginLayout(float gap, float start, float end)
    {
        _gap = gap;
        _paddingStart = start;
        _paddingEnd = end;
    }

    /// <inheritdoc/>
    public float PositionOf(int index)
    {
        if (_options.FixedExtent is { } fixedExtent)
        {
            return index * (fixedExtent + _gap);
        }

        (double measured, int rows) = SumBefore(index);
        return (float)(measured + ((index - rows) * (double)Estimate) + (index * (double)_gap));
    }

    /// <inheritdoc/>
    public float ContentLength() => _count == 0 ? 0 : PositionOf(_count) - _gap;

    /// <inheritdoc/>
    public float ScrollableEnd() => _paddingStart + ContentLength() + _paddingEnd;

    /// <inheritdoc/>
    public bool Materialize(int round)
    {
        if (!_tree.Nodes.IsAlive(_box) || !Scope.IsAlive(_rowsOwner))
        {
            return false;
        }

        SyncAnchor();
        bool changed = _changed;
        _changed = false;
        int first = -1;
        int last = -1;
        if (_count > 0)
        {
            (first, last) = Wanted(round);
        }

        // The rows outside the range go, unless something holds them (design §3.4, "The rows that leave the range"): they are taken
        // out of the box first, and their scopes disposed after, so that a cleanup sees the box without them.
        for (int i = _order.Count - 1; i >= 0; i--)
        {
            Row row = _order[i];
            bool wanted = row.Index >= first && row.Index <= last;
            if (wanted || Held(row))
            {
                changed |= row.Live != wanted;
                row.Live = wanted;
                continue;
            }

            Drop(i);
            changed = true;
        }

        for (int index = first; index >= 0 && index <= last; index++)
        {
            if (!_rows.ContainsKey(_keys[index]))
            {
                Make(index);
                changed = true;
            }
        }

        _first = first;
        _last = last;
        if (changed)
        {
            Relink();
            Invalidation.MarkMaterialized(_tree, _box);
        }

        return changed;
    }

    /// <inheritdoc/>
    public void AfterLayout()
    {
        if (!_tree.Nodes.IsAlive(_box) || !_tree.Nodes.GetRef(_box).Layout.WasLaidOut)
        {
            return;
        }

        // A move by index whose row is laid out now: the position again, with the row's measured length.
        if (_pendingIndex >= 0 && _pendingIndex < _count && _rows.TryGetValue(_keys[_pendingIndex], out Row? target) && target.Live)
        {
            PlaceIndex(_pendingIndex, _pendingAlign);
            _pendingIndex = -1;
        }

        float offset = Math.Max(0, AnchorOffset());
        Vector2 now = _state.Scroll.OffsetValue;
        Vector2 next = _horizontal ? now with { X = offset } : now with { Y = offset };
        if (next != now)
        {
            _ = _state.Scroll.SetOffset(next);
        }

        _lastOffset = offset;
    }

    /// <inheritdoc/>
    public void NotePosition()
    {
        SyncAnchor();
        WriteVisibleRange(_lastOffset);
    }

    /// <inheritdoc/>
    public bool Draws(NodeHandle row)
    {
        if (!_rowOf.TryGetValue(row, out Row? r) || !r.Live)
        {
            return false;
        }

        float offset = OffsetOnAxis(_state.Scroll.OffsetValue);
        float start = PositionOf(r.Index);
        return start < offset + ViewportLength() && start + LengthOf(r.Index) > offset;
    }

    /// <inheritdoc/>
    public void ScrollToIndex(int index, ScrollAlign align)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index is an index of the list's items.");
        }

        SyncAnchor();
        bool measured = _rows.TryGetValue(_keys[index], out Row? row) && row.Live;
        PlaceIndex(index, align);
        _pendingIndex = measured ? -1 : index;
        _pendingAlign = align;
        float offset = AnchorOffset();
        Vector2 now = _state.Scroll.OffsetValue;
        _ = _state.Scroll.SetOffset(_horizontal ? now with { X = offset } : now with { Y = offset });
        _lastOffset = offset;
        _tree.NoteWork();
    }

    /// <inheritdoc/>
    public bool RevealKeptRow(NodeHandle row)
    {
        if (!_rowOf.TryGetValue(row, out Row? r) || r.Live)
        {
            return false;
        }

        ScrollToIndex(r.Index, ScrollAlign.Nearest);
        return true;
    }

    /// <inheritdoc/>
    public bool MaterializeBeyond(NodeHandle row, NodeHandle next, bool backward)
    {
        if (!_rowOf.TryGetValue(row, out Row? from) || !Scope.IsAlive(_rowsOwner))
        {
            return false;
        }

        int step = backward ? -1 : 1;
        int stop = next.IsNone || !_rowOf.TryGetValue(next, out Row? to) ? (backward ? -1 : _count) : to.Index;
        float length = 0;
        bool made = false;
        for (int index = from.Index + step; index != stop && index >= 0 && index < _count; index += step)
        {
            if (_rows.ContainsKey(_keys[index]))
            {
                if (made)
                {
                    break;
                }

                continue;
            }

            Make(index);
            made = true;
            length += LengthOf(index) + _gap;
            if (length >= _options.CacheExtent)
            {
                break;
            }
        }

        if (made)
        {
            Relink();
            Invalidation.MarkMaterialized(_tree, _box);
        }

        return made;
    }

    // Puts the anchor where an alignment puts a row: the row at the position the alignment gives, and how far into it.
    private void PlaceIndex(int index, ScrollAlign align)
    {
        float start = PositionOf(index);
        float length = LengthOf(index);
        float view = ViewportLength();
        float offset = AnchorOffset();
        if (align == ScrollAlign.Nearest)
        {
            if (start >= offset && start + length <= offset + view)
            {
                return;
            }

            align = start < offset || length > view ? ScrollAlign.Top : ScrollAlign.Bottom;
        }

        float target = align switch
        {
            ScrollAlign.Top => start,
            ScrollAlign.Center => start - ((view - length) / 2),
            _ => start + length - view,
        };
        target = Math.Max(0, target);
        _anchor = IndexAt(target);
        _shift = target - PositionOf(_anchor);
        SetAnchorKey();
    }

    // One update of the items, with no computation reading what the rows read: their own effects follow their signals.
    private void Update(IReadOnlyList<T> items)
    {
        Computation? running = ReactiveRuntime.Current;
        ReactiveRuntime.Current = null;
        try
        {
            Reconcile(items);
        }
        finally
        {
            ReactiveRuntime.Current = running;
        }
    }

    // The new items: their keys and indices first, which throws before anything changes when a key comes twice; then the rows whose
    // keys stay take their items and indices, the others go, the lengths of the keys gone are dropped, and the anchor follows its key
    // or takes the row at its index (design §3.4, "When the anchor row goes").
    private void Reconcile(IReadOnlyList<T> items)
    {
        // The keys go to the array of the next order, which changes places with this one, as For's do: an update allocates nothing.
        int count = items.Count;
        if (_nextKeys.Length < count)
        {
            _nextKeys = new TKey[Math.Max(count, _nextKeys.Length * 2)];
        }

        TKey[] keys = _nextKeys;
        _nextIndexOf.Clear();
        for (int j = 0; j < count; j++)
        {
            TKey key = _key(items[j]);
            if (!_nextIndexOf.TryAdd(key, j))
            {
                _nextIndexOf.Clear();
                throw new InvalidOperationException($"The items of a virtualized list have a key each; the key '{key}' comes twice.");
            }

            keys[j] = key;
        }

        (_indexOf, _nextIndexOf) = (_nextIndexOf, _indexOf);
        int oldCount = _count;
        (_keys, _nextKeys) = (keys, _keys);
        Array.Clear(_nextKeys, 0, Math.Min(oldCount, _nextKeys.Length));
        _items = items;
        _count = count;

        for (int i = _order.Count - 1; i >= 0; i--)
        {
            Row row = _order[i];
            if (_indexOf.TryGetValue(row.Key, out int index))
            {
                row.Index = index;
                T item = items[index];
                if (default(T) is not null || !ReferenceEquals(row.Item, item))
                {
                    row.Item = item;
                    row.Value.Set(item);
                }
            }
            else
            {
                _tree.Focus.Lose(row.Node);
                Drop(i);
            }
        }

        _gone.Clear();
        foreach (TKey key in _lengths.Keys)
        {
            if (!_indexOf.ContainsKey(key))
            {
                _gone.Add(key);
            }
        }

        foreach (TKey key in _gone)
        {
            _ = _lengths.Remove(key);
        }

        _gone.Clear();
        RebuildLengths();

        if (_hasAnchorKey && _indexOf.TryGetValue(_anchorKey!, out int anchor))
        {
            _anchor = anchor;
        }
        else
        {
            _anchor = Math.Clamp(_anchor, 0, Math.Max(0, count - 1));
            _shift = 0;
            SetAnchorKey();
        }

        Relink();
        _changed = true;
        if (_tree.Nodes.IsAlive(_box))
        {
            Invalidation.MarkMaterialized(_tree, _box);
        }
    }

    // The range of indices to lay out: those whose rows meet the viewport with the cache extent before and after it, at the anchor's
    // position. With no length known for a row not measured, the anchor's row alone, which the next round measures. A later round
    // makes at least twice as many rows more than the one before on a side that grows, so that rows much shorter than their estimate
    // fill the range in a number of rounds that grows with the logarithm of the shortfall.
    private (int First, int Last) Wanted(int round)
    {
        if (!EstimateKnown)
        {
            return (_anchor, _anchor);
        }

        float offset = AnchorOffset();
        float cache = _options.CacheExtent;
        int first = IndexAt(Math.Max(0, offset - cache));
        int last = IndexAt(Math.Max(0, offset + ViewportLength() + cache));
        if (round > 0 && _last >= 0)
        {
            int more = 1 << Math.Min(round - 1, 20);
            if (last > _last)
            {
                last = Math.Min(_count - 1, Math.Max(last, _last + more));
            }

            if (first < _first)
            {
                first = Math.Max(0, Math.Min(first, _first - more));
            }
        }

        return (first, last);
    }

    // The index of the row at a position: the last whose start is at or before it.
    private int IndexAt(float position)
    {
        if (_count == 0)
        {
            return 0;
        }

        if (_options.FixedExtent is { } fixedExtent)
        {
            return Math.Clamp((int)(position / (fixedExtent + _gap)), 0, _count - 1);
        }

        int low = 0;
        int high = _count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) >> 1;
            if (PositionOf(middle) <= position)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    // The length of a row: its own when it was measured, the fixed or estimated length otherwise.
    private float LengthOf(int index)
    {
        if (_options.FixedExtent is { } fixedExtent)
        {
            return fixedExtent;
        }

        float measured = _extents[index];
        return float.IsNaN(measured) ? Estimate : measured;
    }

    // The scroll position the anchor gives.
    private float AnchorOffset() => _count == 0 ? 0 : PositionOf(_anchor) + _shift;

    // A position the scroll state holds that the block did not write (the wheel, ScrollTo) gives a new anchor: the row at it, and how
    // far into that row it is.
    private void SyncAnchor()
    {
        float offset = OffsetOnAxis(_state.Scroll.OffsetValue);
        if (offset == _lastOffset)
        {
            return;
        }

        _lastOffset = offset;
        if (_count == 0)
        {
            _anchor = 0;
            _shift = 0;
            _hasAnchorKey = false;
            return;
        }

        _anchor = IndexAt(offset);
        _shift = offset - PositionOf(_anchor);
        SetAnchorKey();
    }

    private void SetAnchorKey()
    {
        _hasAnchorKey = _count > 0;
        _anchorKey = _hasAnchorKey ? _keys[_anchor] : default;
    }

    private float OffsetOnAxis(Vector2 offset) => _horizontal ? offset.X : offset.Y;

    private float ViewportLength()
    {
        if (!_tree.Nodes.IsAlive(_box) || !_tree.Nodes.GetRef(_box).Layout.WasLaidOut)
        {
            return 0;
        }

        Size viewport = _tree.ViewportOf(_box);
        return _horizontal ? viewport.Width : viewport.Height;
    }

    // The rows that show at a position: from the row at it to the last that starts before the viewport's end.
    private void WriteVisibleRange(float offset)
    {
        if (_count == 0)
        {
            _state.SetVisibleRange(default);
            return;
        }

        float end = offset + ViewportLength();
        int first = IndexAt(offset);
        int last = IndexAt(end);
        if (last > first && PositionOf(last) >= end)
        {
            last--;
        }

        _state.SetVisibleRange(new IndexRange(first, last + 1));
    }

    // A row outside the range stays while it holds the focus, the pointer's capture, or a drag's source, or while its KeepAlive
    // signal is true (design §3.4, "The rows that leave the range").
    private bool Held(Row row)
    {
        NodeHandle focused = _tree.Focus.Focused;
        return (_tree.Nodes.IsAlive(focused) && _tree.IsInSubtree(focused, row.Node))
            || _tree.HoldsPointer?.Invoke(row.Node) == true
            || _tree.KeepsAlive(row.Node);
    }

    // A new row for an index, in a scope of its own under the rows' owner, with the tree as the current one. A render that throws, or
    // that returns a node the box cannot take, is reported, and the row is an empty hidden node.
    private void Make(int index)
    {
        T item = _items[index];
        Signal<T> value = Signal.Create(item);
        Element element = default;
        UiTree? previousTree = UiTree.Current;
        if (!Scope.TryEnter(_rowsOwner, out ScopeRef previousScope))
        {
            throw new InvalidOperationException("The scope of VirtualFor was disposed.");
        }

        UiTree.Current = _tree;
        Computation? running = ReactiveRuntime.Current;
        ReactiveRuntime.Current = null;
        ScopeHandle scope;
        try
        {
            scope = Scope.Create(
                () =>
                {
                    try
                    {
                        element = _render(value);
                        if (element.IsNone || !ReferenceEquals(element.Tree, _tree) || !_tree.Nodes.GetRef(element.Node).Parent.IsNone)
                        {
                            throw new InvalidOperationException("A row of VirtualFor returns a node of the same tree with no parent.");
                        }
                    }
                    catch (Exception exception) when (exception is not InvariantViolationException)
                    {
                        ReactiveRuntime.ReportException(exception);
                        element = Empty();
                    }
                },
                "VirtualFor row"
            );
        }
        finally
        {
            ReactiveRuntime.Current = running;
            UiTree.Current = previousTree;
            Scope.Exit(previousScope);
        }

        var row = new Row(_keys[index], element.Node, scope, value, item) { Index = index, Live = true };
        _rows[row.Key] = row;
        _rowOf[row.Node] = row;
        _order.Add(row);
    }

    // An empty hidden node owned by the current scope.
    private Element Empty()
    {
        NodeHandle node = _tree.CreateNode(new LayoutStyle { Hidden = true });
        UiTree tree = _tree;
        Scope.OnCleanup(() =>
        {
            if (tree.Nodes.IsAlive(node))
            {
                tree.Remove(node);
            }
        });
        return new Element(_tree, node);
    }

    // Takes a row out of the block; Relink takes it out of the box, and its scope is disposed then.
    private void Drop(int at)
    {
        Row row = _order[at];
        _order.RemoveAt(at);
        _ = _rows.Remove(row.Key);
        _ = _rowOf.Remove(row.Node);
        _dropped.Add(row);
    }

    // The box's children become the rows in the order of their indices: every child is taken out in one pass and put back after
    // the one before it, which walks no siblings. The rows dropped are disposed with the box already without them.
    private void Relink()
    {
        if (!_tree.Nodes.IsAlive(_box))
        {
            return;
        }

        _order.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        _tree.DetachChildren(_box);
        foreach (Row row in _dropped)
        {
            Scope.Dispose(row.Scope);
        }

        _dropped.Clear();
        NodeHandle previous = default;
        foreach (Row row in _order)
        {
            if (_tree.Nodes.IsAlive(row.Node))
            {
                _tree.InsertAfter(_box, previous, row.Node);
                previous = row.Node;
            }
        }
    }

    // The lengths by index from the lengths kept by key, and the trees over them.
    private void RebuildLengths()
    {
        if (_extents.Length < _count)
        {
            _extents = new float[_count];
            _lengthTree = new double[_count + 1];
            _countTree = new int[_count + 1];
        }

        Array.Fill(_extents, float.NaN, 0, _count);
        Array.Clear(_lengthTree, 0, _count + 1);
        Array.Clear(_countTree, 0, _count + 1);
        _measuredTotal = 0;
        _measuredRows = 0;
        if (_options.FixedExtent is not null)
        {
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            if (_lengths.TryGetValue(_keys[i], out float length))
            {
                _extents[i] = length;
                _lengthTree[i + 1] += length;
                _countTree[i + 1]++;
                _measuredTotal += length;
                _measuredRows++;
            }
        }

        // Each node takes its sum to the node above it: the trees in O(n).
        for (int i = 1; i <= _count; i++)
        {
            int parent = i + (i & -i);
            if (parent <= _count)
            {
                _lengthTree[parent] += _lengthTree[i];
                _countTree[parent] += _countTree[i];
            }
        }
    }

    private void AddToTrees(int index, double length, int rows)
    {
        for (int i = index + 1; i <= _count; i += i & -i)
        {
            _lengthTree[i] += length;
            _countTree[i] += rows;
        }
    }

    // The measured lengths of the rows before an index, and their number.
    private (double Length, int Rows) SumBefore(int index)
    {
        double length = 0;
        int rows = 0;
        for (int i = Math.Min(index, _count); i > 0; i -= i & -i)
        {
            length += _lengthTree[i];
            rows += _countTree[i];
        }

        return (length, rows);
    }

    // The block goes away: the focus inside its rows is lost, the rows leave the box in one pass, and the box is no list any more.
    private void Teardown()
    {
        if (!_tree.Nodes.IsAlive(_box))
        {
            return;
        }

        NodeHandle focused = _tree.Focus.Focused;
        if (_tree.Nodes.IsAlive(focused) && focused != _box && _tree.IsInSubtree(focused, _box))
        {
            _tree.Focus.Lose(focused);
        }

        _tree.DetachChildren(_box);
        _tree.SetVirtualHost(_box, null);
    }

    // A row made: its key, node, scope, and item's signal; its index now, and whether it is laid out or only kept.
    private sealed class Row(TKey key, NodeHandle node, ScopeHandle scope, Signal<T> value, T item)
    {
        public TKey Key { get; } = key;

        public NodeHandle Node { get; } = node;

        public ScopeHandle Scope { get; } = scope;

        public Signal<T> Value { get; } = value;

        public T Item { get; set; } = item;

        public int Index { get; set; }

        public bool Live { get; set; }
    }
}
