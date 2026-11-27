namespace XylemUI;

/// <summary>
/// A date or a time with the format that writes it, for an interpolation of a <see cref="NodeTextWriter"/> (design §4.11, "Allocation
/// and threads"): <c>$"{format.Of(now)}"</c> writes the formatted date straight into the text, with no box and no string. Made by
/// <see cref="DateFormatter.Of(DateTime)"/> and its overloads, which check the value's type against the format.
/// </summary>
public readonly struct FormattedDate : ISpanFormattable
{
    private readonly DateFormatter? _formatter;
    private readonly DateTime _clock;
    private readonly int _offsetMinutes;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormattedDate"/> struct.
    /// </summary>
    /// <param name="formatter">The format, which has checked that it takes a value of the type.</param>
    /// <param name="clock">The value's date and time; a date at midnight, a time on the 1st of January of the year 1.</param>
    /// <param name="offsetMinutes">The offset from GMT in minutes of a <see cref="DateTimeOffset"/>; 0 for the other types.</param>
    internal FormattedDate(DateFormatter formatter, DateTime clock, int offsetMinutes)
    {
        _formatter = formatter;
        _clock = clock;
        _offsetMinutes = offsetMinutes;
    }

    /// <summary>
    /// Writes the value with its format. Allocates nothing. The default value writes nothing.
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

        return _formatter.TryWrite(_clock, _offsetMinutes, destination, out charsWritten);
    }

    /// <summary>
    /// Returns the formatted value as a new string; not for the frame path.
    /// </summary>
    /// <param name="format">The format, which is not read.</param>
    /// <param name="formatProvider">The provider, which is not read.</param>
    /// <returns>The formatted value.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <summary>
    /// Returns the formatted value as a new string; not for the frame path.
    /// </summary>
    /// <returns>The formatted value; empty for the default value.</returns>
    public override string ToString() => FormattedText.Make(this);
}
