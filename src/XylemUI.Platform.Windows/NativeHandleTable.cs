using System.Diagnostics.CodeAnalysis;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Maps generational integer ids, which native code may hold, to managed objects (design §2.12, Invariant 8).
/// </summary>
/// <remarks>
/// The table holds a strong reference while an id is registered, so the GC never collects the target, and it may move it freely:
/// native code holds only the id. A released id fails to resolve, also after its slot is reused, because the generation moves on.
/// The table belongs to the UI thread and has no lock.
/// </remarks>
internal static class NativeHandleTable
{
    private static readonly Stack<int> _free = new();
    private static Entry[] _entries = new Entry[16];
    private static int _count;

    /// <summary>
    /// Registers a target and returns the id to hand to native code. The id is never zero.
    /// </summary>
    public static nint Register(object target)
    {
        UiThread.VerifyAccess();
        if (nint.Size != sizeof(long))
        {
            // The id packs a 32-bit generation above a 32-bit index.
            throw new PlatformNotSupportedException("XylemUI requires a 64-bit process.");
        }

        int index;
        if (!_free.TryPop(out index))
        {
            index = _count++;
            if (index == _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length * 2);
            }
        }

        ref Entry entry = ref _entries[index];
        entry.Target = target;
        return Pack(index, entry.Generation);
    }

    /// <summary>
    /// Returns the target of a registered id. Fails for zero, for a released id, and for a target of another type.
    /// </summary>
    public static bool TryResolve<T>(nint id, [NotNullWhen(true)] out T? target)
        where T : class
    {
        if (TryFind(id, out int index) && _entries[index].Target is T found)
        {
            target = found;
            return true;
        }

        target = null;
        return false;
    }

    /// <summary>
    /// Releases an id. Later calls to <see cref="TryResolve{T}"/> with it fail. Releasing an id that is not registered does nothing.
    /// </summary>
    public static void Release(nint id)
    {
        UiThread.VerifyAccess();
        if (!TryFind(id, out int index))
        {
            return;
        }

        ref Entry entry = ref _entries[index];
        entry.Target = null;
        entry.Generation++;
        _free.Push(index);
    }

    private static bool TryFind(nint id, out int index)
    {
        // The low 32 bits are index + 1, so that zero (the GWLP_USERDATA default) is never a valid id.
        long value = id;
        index = (int)(uint)value - 1;
        uint generation = (uint)(value >> 32);
        return (uint)index < (uint)_count && _entries[index].Target is not null && _entries[index].Generation == generation;
    }

    private static nint Pack(int index, uint generation) => (nint)(((long)generation << 32) | (uint)(index + 1));

    private struct Entry
    {
        public object? Target;
        public uint Generation;
    }
}
