namespace XylemUI;

/// <summary>
/// Scratch space for runs that nest on one thread, taken and given back last in, first out: a flex container's run takes its
/// span, and the runs of its items, inside it, take theirs above.
/// </summary>
/// <typeparam name="T">The element; the caller writes each element before it reads it.</typeparam>
internal sealed class ScratchStack<T>
    where T : struct
{
    private T[] _buffer = [];

    /// <summary>
    /// Gets the number of elements taken. Give it to <see cref="PopTo"/> to give back what was taken after it.
    /// </summary>
    public int Top { get; private set; }

    /// <summary>
    /// Returns <paramref name="count"/> elements above the ones taken. When the buffer is full, a larger one replaces it; the
    /// spans taken before stay in the old buffer, which they keep alive until they are given back.
    /// </summary>
    /// <param name="count">The number of elements to take.</param>
    /// <returns>The elements taken; their contents are not cleared.</returns>
    public Span<T> Push(int count)
    {
        if (Top + count > _buffer.Length)
        {
            _buffer = new T[Math.Max(_buffer.Length * 2, Top + count)];
        }

        Span<T> span = _buffer.AsSpan(Top, count);
        Top += count;
        return span;
    }

    /// <summary>
    /// Gives back the elements taken after <paramref name="top"/>, a value <see cref="Top"/> had before.
    /// </summary>
    /// <param name="top">The value of <see cref="Top"/> to go back to.</param>
    public void PopTo(int top) => Top = top;
}
