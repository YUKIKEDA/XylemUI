namespace XylemUI;

/// <summary>
/// Writes into a span and remembers whether anything did not fit, for the <c>TryFormat</c> of <see cref="NumberFormatter"/> and
/// <see cref="DateFormatter"/>.
/// </summary>
internal ref struct CharWriter
{
    private readonly Span<char> _destination;
    private int _length;
    private bool _full;

    /// <summary>
    /// Initializes a new instance of the <see cref="CharWriter"/> struct at the start of a span.
    /// </summary>
    /// <param name="destination">The characters to write to.</param>
    public CharWriter(Span<char> destination)
    {
        _destination = destination;
    }

    /// <summary>
    /// Writes a character, or remembers that it did not fit.
    /// </summary>
    /// <param name="value">The character.</param>
    public void Append(char value)
    {
        if (_length < _destination.Length)
        {
            _destination[_length++] = value;
        }
        else
        {
            _full = true;
        }
    }

    /// <summary>
    /// Writes characters, or remembers that they did not fit.
    /// </summary>
    /// <param name="value">The characters.</param>
    public void Append(ReadOnlySpan<char> value)
    {
        if (value.TryCopyTo(_destination[_length..]))
        {
            _length += value.Length;
        }
        else
        {
            _full = true;
        }
    }

    /// <summary>
    /// Ends the write.
    /// </summary>
    /// <param name="charsWritten">The characters written; 0 when something did not fit.</param>
    /// <returns><see langword="false"/> when something did not fit.</returns>
    public readonly bool TryFinish(out int charsWritten)
    {
        charsWritten = _full ? 0 : _length;
        return !_full;
    }
}
