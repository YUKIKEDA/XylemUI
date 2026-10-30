using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The base of signals and computations: a node of the reactive graph that others can read (design §2.2.2). It has no public
/// members.
/// </summary>
/// <remarks>
/// It holds the computations that read it. Each edge also knows its position on the other end, so removing it is a swap with the
/// last edge (the same shape as SolidJS's observers and observer slots). The arrays are reused and only grow.
/// </remarks>
public abstract class ReactiveNode
{
    private ObserverEdge[] _observers = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ReactiveNode"/> class, with no readers.
    /// </summary>
    private protected ReactiveNode() { }

    /// <summary>
    /// Gets the version, advanced each time the value changes. A reader compares it with the version it saw.
    /// </summary>
    internal ulong Version { get; private protected set; }

    /// <summary>
    /// Gets the number of computations that read this node.
    /// </summary>
    internal int ObserverCount { get; private set; }

    /// <summary>
    /// Gets the edges to the computations that read this node. Only the first <see cref="ObserverCount"/> are used.
    /// </summary>
    internal ReadOnlySpan<ObserverEdge> Observers => _observers.AsSpan(0, ObserverCount);

    /// <summary>
    /// Gets the depth of this node: 0 for a signal. A hint for the order of effects, never the basis of correctness.
    /// </summary>
    internal abstract uint Depth { get; }

    /// <summary>
    /// Registers a read of this node by the computation that is running, if any.
    /// </summary>
    private protected void TrackRead()
    {
        if (ReactiveRuntime.Current is { } reader)
        {
            reader.RegisterDependency(this);
        }
    }

    /// <summary>
    /// Marks every computation that reads this node dirty.
    /// </summary>
    private protected void MarkObserversDirty()
    {
        for (int i = 0; i < ObserverCount; i++)
        {
            _observers[i].Observer.MarkDirty();
        }
    }

    /// <summary>
    /// Adds a computation that reads this node.
    /// </summary>
    /// <param name="observer">The computation.</param>
    /// <param name="slotInObserver">The position of the matching source edge in the computation's sources.</param>
    /// <returns>The position of the new edge in <see cref="Observers"/>.</returns>
    internal int AddObserver(Computation observer, int slotInObserver)
    {
        if (ObserverCount == _observers.Length)
        {
            GrowObservers();
        }

        int slot = ObserverCount++;
        _observers[slot] = new ObserverEdge(observer, slotInObserver);
        if (slot == 0)
        {
            OnFirstObserver();
        }

        return slot;
    }

    /// <summary>
    /// Removes the edge at a position in <see cref="Observers"/>. The last edge takes its place, and its computation learns the new
    /// position.
    /// </summary>
    /// <param name="slot">The edge's position in <see cref="Observers"/>.</param>
    internal void RemoveObserver(int slot)
    {
        int last = --ObserverCount;
        if (slot != last)
        {
            ObserverEdge moved = _observers[last];
            _observers[slot] = moved;
            moved.Observer.MoveSourceSlot(moved.SlotInObserver, slot);
        }

        _observers[last] = default;
        if (last == 0)
        {
            OnLastObserverRemoved();
        }
    }

    /// <summary>
    /// Called when the first reader is added. A computed value registers with its sources here.
    /// </summary>
    private protected virtual void OnFirstObserver() { }

    /// <summary>
    /// Called when the last reader is removed. A computed value leaves its sources here.
    /// </summary>
    private protected virtual void OnLastObserverRemoved() { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowObservers() => Array.Resize(ref _observers, Math.Max(4, _observers.Length * 2));
}
