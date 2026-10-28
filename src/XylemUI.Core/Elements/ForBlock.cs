namespace XylemUI;

/// <summary>
/// What a <c>For</c> keeps (design §2.7, "The shape of V3"): its container, its rows in their order, and the arrays of the diff,
/// which it reuses. An update of the items runs Vue's five steps and moves only the rows outside the longest increasing
/// subsequence; each row is a scope of its own, made under the block's, not under the effect that reads the items.
/// </summary>
/// <typeparam name="T">The type of an item.</typeparam>
/// <typeparam name="TKey">The type of an item's key.</typeparam>
internal sealed class ForBlock<T, TKey>
    where TKey : notnull
{
    private readonly UiTree _tree;
    private readonly NodeHandle _container;
    private readonly Func<T, TKey> _key;
    private readonly Func<IReadOnlySignal<T>, Element>? _render;
    private readonly Func<IReadOnlySignal<T>, IReadOnlySignal<int>, Element>? _renderIndexed;
    private readonly Dictionary<TKey, int> _firstNew = [];
    private ScopeRef _rowsOwner;

    // The rows now, and the arrays the next order is written into; they change places after each update.
    private Rows _now = new();
    private Rows _next = new();

    // The work arrays of the middle of a diff, grown when too short.
    private int[] _sameNext = [];
    private int[] _newToOld = [];
    private bool[] _stays = [];
    private int[] _tails = [];
    private int[] _before = [];
    private int[] _chainPrevious = [];
    private int[] _chainNext = [];
    private int[] _removed = [];
    private int _removedCount;
    private int _chainHead;

    private ForBlock(
        UiTree tree,
        NodeHandle container,
        Func<T, TKey> key,
        Func<IReadOnlySignal<T>, Element>? render,
        Func<IReadOnlySignal<T>, IReadOnlySignal<int>, Element>? renderIndexed
    )
    {
        _tree = tree;
        _container = container;
        _key = key;
        _render = render;
        _renderIndexed = renderIndexed;
    }

    /// <summary>
    /// Makes a block in a scope of its own under the current one, and its first rows. Its scope holds, oldest first, the owner of
    /// the rows, a scope whose cleanup takes every row out of the container in one pass, and the effect that reads the items: a scope
    /// disposes its newest children first, so the effect stops, then the rows leave the container, then they are freed, and freeing
    /// them walks no siblings.
    /// </summary>
    internal static void Create(
        UiTree tree,
        NodeHandle container,
        IReadOnlySignal<IReadOnlyList<T>> items,
        Func<T, TKey> key,
        Func<IReadOnlySignal<T>, Element>? render,
        Func<IReadOnlySignal<T>, IReadOnlySignal<int>, Element>? renderIndexed
    )
    {
        var block = new ForBlock<T, TKey>(tree, container, key, render, renderIndexed);
        _ = Scope.Create(
            () =>
            {
                _ = Scope.Create(() => block._rowsOwner = Scope.CurrentRef, "For rows");
                _ = Scope.Create(() => Scope.OnCleanup(block.Teardown), "For teardown");
                _ = Effect.Create(() => block.Update(items.Get()));
            },
            "For"
        );
    }

    // One update, with no computation reading what the rows read: their own effects follow their signals.
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

    // Vue's patchKeyedChildren (renderer.ts lines 1834 to 2076): the common start, the common end, the rows to make, the rows to
    // remove, and the middle. The keys are compared in the arrays; only the rows made, removed, moved, or given a new value are
    // touched in the tree.
    private void Reconcile(IReadOnlyList<T> items)
    {
        int count = items.Count;
        _next.Ensure(count, _renderIndexed is not null);
        for (int j = 0; j < count; j++)
        {
            _next.Keys[j] = _key(items[j]);
        }

        _removedCount = 0;
        Grow(ref _removed, _now.Count);
        int start = 0;
        int oldEnd = _now.Count - 1;
        int newEnd = count - 1;
        while (start <= oldEnd && start <= newEnd && EqualityComparer<TKey>.Default.Equals(_now.Keys[start], _next.Keys[start]))
        {
            Keep(start, start, items[start]);
            start++;
        }

        while (oldEnd >= start && newEnd >= start && EqualityComparer<TKey>.Default.Equals(_now.Keys[oldEnd], _next.Keys[newEnd]))
        {
            Keep(oldEnd, newEnd, items[newEnd]);
            oldEnd--;
            newEnd--;
        }

        // The common start and end go to the next order as blocks: copied one row at a time, the write barriers of the references
        // took most of a short change's diff (about 11 ns a row of 10,000, PR for Issue #368).
        _now.CopyTo(_next, 0, 0, start);
        _now.CopyTo(_next, oldEnd + 1, newEnd + 1, _now.Count - oldEnd - 1);

        NodeHandle before = start > 0 ? _now.Nodes[start - 1] : default;
        if (start > oldEnd)
        {
            for (int j = start; j <= newEnd; j++)
            {
                Make(j, items[j]);
                _tree.InsertAfter(_container, before, _next.Nodes[j]);
                before = _next.Nodes[j];
            }
        }
        else if (start > newEnd)
        {
            for (int k = start; k <= oldEnd; k++)
            {
                Remove(k, before);
            }
        }
        else
        {
            Middle(items, start, oldEnd, newEnd, before);
        }

        // Freed after the moves, so that a row's cleanup runs with the list in its new order.
        for (int r = 0; r < _removedCount; r++)
        {
            Scope.Dispose(_now.Scopes[_removed[r]]);
        }

        int oldCount = _now.Count;
        (_now, _next) = (_next, _now);
        _now.Count = count;
        _next.Clear(oldCount);
    }

    // The middle (step 5): old rows found by key move to their new places, the others are removed, and the new keys are made. Rows
    // on the longest increasing subsequence of their old places stay; the rest move once, after the row before them in the new order.
    // A chain of the middle's slots, in the order of the tree, gives each row's previous sibling.
    private void Middle(IReadOnlyList<T> items, int start, int oldEnd, int newEnd, NodeHandle outside)
    {
        int newMiddle = newEnd - start + 1;
        int oldMiddle = oldEnd - start + 1;
        Grow(ref _sameNext, newMiddle);
        Grow(ref _newToOld, newMiddle);
        Grow(ref _stays, newMiddle);
        Grow(ref _tails, newMiddle);
        Grow(ref _before, newMiddle);
        Grow(ref _chainPrevious, oldMiddle + newMiddle);
        Grow(ref _chainNext, oldMiddle + newMiddle);

        // The first new place of each key, and for each place the next one of the same key (a key given twice maps in order).
        _firstNew.Clear();
        for (int j = newEnd; j >= start; j--)
        {
            TKey key = _next.Keys[j];
            _sameNext[j - start] = _firstNew.TryGetValue(key, out int later) ? later : -1;
            _firstNew[key] = j;
        }

        Array.Clear(_newToOld, 0, newMiddle);
        Array.Clear(_stays, 0, newMiddle);
        for (int t = 0; t < oldMiddle; t++)
        {
            _chainPrevious[t] = t - 1;
            _chainNext[t] = t + 1 < oldMiddle ? t + 1 : -1;
        }

        _chainHead = oldMiddle > 0 ? 0 : -1;
        bool moved = false;
        int furthest = -1;
        for (int k = start; k <= oldEnd; k++)
        {
            TKey key = _now.Keys[k];
            if (_firstNew.TryGetValue(key, out int j))
            {
                int same = _sameNext[j - start];
                if (same >= 0)
                {
                    _firstNew[key] = same;
                }
                else
                {
                    _ = _firstNew.Remove(key);
                }

                _newToOld[j - start] = k + 1;
                moved |= j < furthest;
                furthest = Math.Max(furthest, j);
                Carry(k, j, items[j]);
            }
            else
            {
                int slot = k - start;
                Remove(k, Previous(_chainPrevious[slot], start, oldMiddle, outside));
                Unchain(slot);
            }
        }

        if (moved)
        {
            MarkLongestIncreasing(newMiddle);
        }

        int previous = -1;
        for (int j = start; j <= newEnd; j++)
        {
            int index = j - start;
            int slot;
            if (_newToOld[index] == 0)
            {
                Make(j, items[j]);
                slot = oldMiddle + index;
                _tree.InsertAfter(_container, Previous(previous, start, oldMiddle, outside), _next.Nodes[j]);
                ChainAfter(previous, slot);
            }
            else
            {
                slot = _newToOld[index] - 1 - start;
                if (moved && !_stays[index])
                {
                    NodeHandle node = _next.Nodes[j];
                    _tree.DetachAfter(_container, Previous(_chainPrevious[slot], start, oldMiddle, outside), node);
                    Unchain(slot);
                    _tree.InsertAfter(_container, Previous(previous, start, oldMiddle, outside), node);
                    ChainAfter(previous, slot);
                    _tree.RowMovedHook?.Invoke(node);
                }
            }

            previous = slot;
        }
    }

    // Vue's getSequence (renderer.ts lines 2608 to 2647): the places, among those with an old row, whose old places increase the
    // longest; they keep their order and stay. O(n log n).
    private void MarkLongestIncreasing(int length)
    {
        int found = 0;
        for (int i = 0; i < length; i++)
        {
            int value = _newToOld[i];
            if (value == 0)
            {
                continue;
            }

            if (found == 0 || _newToOld[_tails[found - 1]] < value)
            {
                _before[i] = found > 0 ? _tails[found - 1] : -1;
                _tails[found++] = i;
                continue;
            }

            int low = 0;
            int high = found - 1;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (_newToOld[_tails[middle]] < value)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (value < _newToOld[_tails[low]])
            {
                _before[i] = low > 0 ? _tails[low - 1] : -1;
                _tails[low] = i;
            }
        }

        for (int i = found > 0 ? _tails[found - 1] : -1, n = found; n > 0; n--)
        {
            _stays[i] = true;
            i = _before[i];
        }
    }

    // The node of a chain slot, or the row before the middle for none.
    private NodeHandle Previous(int slot, int start, int oldMiddle, NodeHandle outside) =>
        slot < 0 ? outside
        : slot < oldMiddle ? _now.Nodes[start + slot]
        : _next.Nodes[start + slot - oldMiddle];

    private void Unchain(int slot)
    {
        int previous = _chainPrevious[slot];
        int next = _chainNext[slot];
        if (previous >= 0)
        {
            _chainNext[previous] = next;
        }
        else
        {
            _chainHead = next;
        }

        if (next >= 0)
        {
            _chainPrevious[next] = previous;
        }
    }

    private void ChainAfter(int previous, int slot)
    {
        int next = previous >= 0 ? _chainNext[previous] : _chainHead;
        _chainPrevious[slot] = previous;
        _chainNext[slot] = next;
        if (previous >= 0)
        {
            _chainNext[previous] = slot;
        }
        else
        {
            _chainHead = slot;
        }

        if (next >= 0)
        {
            _chainPrevious[next] = slot;
        }
    }

    // An old row taken to a new place: its node and scope go along, its value signal takes the item, and its place signal the place.
    private void Carry(int old, int place, T item)
    {
        _next.Nodes[place] = _now.Nodes[old];
        _next.Scopes[place] = _now.Scopes[old];
        _next.Values[place] = _now.Values[old];
        if (_next.Places is { } places)
        {
            places[place] = _now.Places![old];
        }

        Keep(old, place, item);
        _next.Items[place] = _now.Items[old];
    }

    // An old row's signals take its item and its place. The same object as before is the same value: an update keeps most items,
    // and the signal's own comparison (a record's fields) took most of a short change's diff (about 26 ns a row of 10,000). The item
    // kept is compared, not the signal's (`Peek` checks the thread on each call, about 10 ns a row). A new item is written into the
    // old order, which the blocks of the common start and end then copy.
    private void Keep(int old, int place, T item)
    {
        if (default(T) is not null || !ReferenceEquals(_now.Items[old], item))
        {
            _now.Items[old] = item;
            _now.Values[old].Set(item);
        }

        if (place != old && _now.Places is { } places)
        {
            places[old].Set(place);
        }
    }

    // Takes an old row out of the container, given its previous sibling, after its focus; its scope is disposed at the end of the
    // update.
    private void Remove(int old, NodeHandle previous)
    {
        NodeHandle node = _now.Nodes[old];
        _tree.Focus.Lose(node);
        _tree.DetachAfter(_container, previous, node);
        _removed[_removedCount++] = old;
    }

    // A new row in a scope of its own under the rows' owner, with the tree as the current one. A render that throws, or that returns
    // a node the container cannot take, is reported, and the row keeps its place with an empty hidden node.
    private void Make(int place, T item)
    {
        Signal<T> value = Signal.Create(item);
        Signal<int>? index = _renderIndexed is not null ? Signal.Create(place) : null;
        Element element = default;
        UiTree? previousTree = UiTree.Current;
        if (!Scope.TryEnter(_rowsOwner, out ScopeRef previousScope))
        {
            throw new InvalidOperationException("The scope of For was disposed.");
        }

        UiTree.Current = _tree;
        ScopeHandle scope;
        try
        {
            scope = Scope.Create(
                () =>
                {
                    try
                    {
                        element = _renderIndexed is { } indexed ? indexed(value, index!) : _render!(value);
                        if (element.IsNone || !ReferenceEquals(element.Tree, _tree) || !_tree.Nodes.GetRef(element.Node).Parent.IsNone)
                        {
                            throw new InvalidOperationException("A row of For returns a node of the same tree with no parent.");
                        }
                    }
                    catch (Exception exception) when (exception is not InvariantViolationException)
                    {
                        ReactiveRuntime.ReportException(exception);
                        element = Empty();
                    }
                },
                "For row"
            );
        }
        finally
        {
            UiTree.Current = previousTree;
            Scope.Exit(previousScope);
        }

        _next.Nodes[place] = element.Node;
        _next.Scopes[place] = scope;
        _next.Values[place] = value;
        _next.Items[place] = item;
        if (_next.Places is { } places)
        {
            places[place] = index!;
        }
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

    // The block goes away: the focus inside its rows is lost, and the rows leave the container in one pass.
    private void Teardown()
    {
        if (!_tree.Nodes.IsAlive(_container))
        {
            return;
        }

        NodeHandle focused = _tree.Focus.Focused;
        if (_tree.Nodes.IsAlive(focused) && focused != _container && _tree.IsInSubtree(focused, _container))
        {
            _tree.Focus.Lose(focused);
        }

        _tree.DetachChildren(_container);
    }

    private static void Grow<TItem>(ref TItem[] array, int length)
    {
        if (array.Length < length)
        {
            Array.Resize(ref array, Math.Max(length, array.Length * 2));
        }
    }

    // The arrays of one order of the rows.
    private sealed class Rows
    {
        public NodeHandle[] Nodes { get; private set; } = [];

        public TKey[] Keys { get; private set; } = [];

        public Signal<T>[] Values { get; private set; } = [];

        public T[] Items { get; private set; } = [];

        public Signal<int>[]? Places { get; private set; }

        public ScopeHandle[] Scopes { get; private set; } = [];

        public int Count { get; set; }

        public void Ensure(int length, bool places)
        {
            if (Nodes.Length < length)
            {
                int size = Math.Max(length, Nodes.Length * 2);
                Nodes = new NodeHandle[size];
                Keys = new TKey[size];
                Values = new Signal<T>[size];
                Items = new T[size];
                Scopes = new ScopeHandle[size];
                Places = places ? new Signal<int>[size] : null;
            }
            else if (places && Places is null)
            {
                Places = new Signal<int>[Nodes.Length];
            }
        }

        // Copies a run of rows to another order: their nodes, scopes, and signals. The keys of the other order are its own.
        public void CopyTo(Rows target, int from, int to, int length)
        {
            if (length <= 0)
            {
                return;
            }

            Array.Copy(Nodes, from, target.Nodes, to, length);
            Array.Copy(Scopes, from, target.Scopes, to, length);
            Array.Copy(Values, from, target.Values, to, length);
            Array.Copy(Items, from, target.Items, to, length);
            if (Places is not null && target.Places is not null)
            {
                Array.Copy(Places, from, target.Places, to, length);
            }
        }

        // Drops what the arrays hold of an order no longer used, so that the rows freed are not kept alive.
        public void Clear(int count)
        {
            Array.Clear(Nodes, 0, count);
            Array.Clear(Keys, 0, count);
            Array.Clear(Values, 0, count);
            Array.Clear(Items, 0, count);
            Array.Clear(Scopes, 0, count);
            if (Places is not null)
            {
                Array.Clear(Places, 0, count);
            }

            Count = 0;
        }
    }
}
