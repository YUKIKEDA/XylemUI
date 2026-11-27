namespace XylemUI;

/// <summary>
/// A range of two dates with the format that writes it, for an interpolation of a <see cref="NodeTextWriter"/> (design §4.11,
/// "Allocation and threads"): <c>$"{format.OfRange(start, end)}"</c> writes the formatted range straight into the text, with no box
/// and no string. Made by <see cref="DateFormatter.OfRange(DateOnly, DateOnly)"/> and its overload, which check the values' type and
/// order against the format.
/// </summary>
public readonly struct FormattedDateRange : ISpanFormattable
{
    private readonly DateFormatter? _formatter;
    private readonly DateTime _start;
    private readonly DateTime _end;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormattedDateRange"/> struct.
    /// </summary>
    /// <param name="formatter">The format, which has checked that it takes the values and has read its range patterns.</param>
    /// <param name="start">The start of the range; a date at midnight.</param>
    /// <param name="end">The end of the range, not before <paramref name="start"/>.</param>
    internal FormattedDateRange(DateFormatter formatter, DateTime start, DateTime end)
    {
        _formatter = formatter;
        _start = start;
        _end = end;
    }

    /// <summary>
    /// Writes the range with its format. Allocates nothing. The default value writes nothing.
    /// </summary>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <param name="format">Not read: the format is the <see cref="DateFormatter"/>'s.</param>
    /// <param name="provider">Not read: the locale is the <see cref="DateFormatter"/>'s.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (_formatter is null)
        {
            charsWritten = 0;
            return true;
        }

        return _formatter.TryWriteRange(_start, _end, destination, out charsWritten);
    }

    /// <summary>
    /// Returns the formatted range as a new string; not for the frame path.
    /// </summary>
    /// <param name="format">The format, which is not read.</param>
    /// <param name="formatProvider">The provider, which is not read.</param>
    /// <returns>The formatted range.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <summary>
    /// Returns the formatted range as a new string; not for the frame path.
    /// </summary>
    /// <returns>The formatted range; empty for the default value.</returns>
    public override string ToString() => FormattedText.Make(this);
}
