using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// Stores values of <typeparamref name="T"/> in one flat array and hands out generational handles to them (design §2.5).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GetRef"/> returns a reference into the array. When the array grows, a reference taken before the growth still
/// points at the old array and a write through it is lost without an error (Invariant 12). Do not hold a reference across an
/// allocation: copy the links you need, recurse, then call <see cref="GetRef"/> again to write back. A pass that must not allocate
/// runs inside <see cref="ForbidAllocation"/>, so an allocation there stops at once.
/// </para>
/// <para>The arena belongs to the UI thread and has no lock.</para>
/// </remarks>
/// <typeparam name="T">The stored value type.</typeparam>
internal sealed class Arena<T>
    where T : struct
{
    private readonly Stack<int> _freeList = new();
    private Slot[] _slots;
    private int _count;
    private int _forbidAllocationDepth;

    public Arena(int initialCapacity = 256)
    {
        _slots = new Slot[Math.Max(initialCapacity, 1)];
    }

    /// <summary>Gets the number of live values.</summary>
    public int Count => _count - _freeList.Count;

    /// <summary>
    /// Gets the number of times the backing array has grown. A reference from <see cref="GetRef"/> taken while this had another
    /// value no longer points into the arena.
    /// </summary>
    public int GrowthCount { get; private set; }

    /// <summary>Stores a value and returns its handle. Reuses a freed slot first.</summary>
    public Handle<T> Allocate(in T value)
    {
        if (_forbidAllocationDepth != 0)
        {
            Invariant.Fail("An arena allocation happened inside a pass that forbids allocation.");
        }

        if (!_freeList.TryPop(out int index))
        {
            index = _count;
            if (index == _slots.Length)
            {
                Grow();
            }

            _count++;
        }

        ref Slot slot = ref _slots[index];
        slot.Value = value;
        slot.IsAlive = true;
        return new Handle<T>(index, slot.Generation);
    }

    /// <summary>Returns whether the handle points to a live value of its own generation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Handle<T> handle) =>
        (uint)handle.Index < (uint)_count && _slots[handle.Index].IsAlive && _slots[handle.Index].Generation == handle.Generation;

    /// <summary>
    /// Copies the value for a handle that may be stale. Use it where a freed target is normal (timers, animations, IME callbacks).
    /// </summary>
    public bool TryGet(Handle<T> handle, out T value)
    {
        if (!IsAlive(handle))
        {
            value = default;
            return false;
        }

        value = _slots[handle.Index].Value;
        return true;
    }

    /// <summary>
    /// Returns a reference to the value of a live handle. A stale handle here is a broken internal link and fails the invariant.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(Handle<T> handle)
    {
        if (!IsAlive(handle))
        {
            Invariant.Fail("A stale handle was used where a live one is required.");
        }

        return ref _slots[handle.Index].Value;
    }

    /// <summary>Frees a live value. Every handle to it becomes stale, also after the slot is reused.</summary>
    public void Free(Handle<T> handle)
    {
        if (!IsAlive(handle))
        {
            Invariant.Fail("A stale handle was freed.");
        }

        ref Slot slot = ref _slots[handle.Index];
        slot.IsAlive = false;
        slot.Generation++;
        slot.Value = default; // Drops managed references held by the value so the GC can collect them.
        _freeList.Push(handle.Index);
    }

    /// <summary>
    /// Starts a region in which <see cref="Allocate"/> fails the invariant. Dispose the result to end it. Regions nest.
    /// </summary>
    public AllocationForbidden ForbidAllocation()
    {
        _forbidAllocationDepth++;
        return new AllocationForbidden(this);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow()
    {
        Array.Resize(ref _slots, _slots.Length * 2);
        GrowthCount++;
    }

    /// <summary>Ends a region started by <see cref="ForbidAllocation"/>.</summary>
    internal readonly ref struct AllocationForbidden(Arena<T> arena)
    {
        public void Dispose() => arena._forbidAllocationDepth--;
    }

    private struct Slot
    {
        public T Value;
        public ulong Generation;
        public bool IsAlive;
    }
}
