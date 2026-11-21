namespace XylemUI;

/// <summary>
/// A number with the format that writes it, for an interpolation of a <see cref="NodeTextWriter"/> (design §4.11, "Allocation and
/// threads"): <c>$"{format.Of(count)} files"</c> writes the formatted digits straight into the text, with no box and no string. Made
/// by <see cref="NumberFormatter.Of(decimal)"/> and its overloads.
/// </summary>
public readonly struct FormattedNumber : ISpanFormattable
{
    private readonly NumberFormatter? _formatter;
    private readonly decimal _decimal;
    private readonly double _double;
    private readonly bool _isDouble;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormattedNumber"/> struct for a decimal or a whole number.
    /// </summary>
    /// <param name="formatter">The format.</param>
    /// <param name="value">The number.</param>
    internal FormattedNumber(NumberFormatter formatter, decimal value)
    {
        _formatter = formatter;
        _decimal = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FormattedNumber"/> struct for a double.
    /// </summary>
    /// <param name="formatter">The format.</param>
    /// <param name="value">The number.</param>
    internal FormattedNumber(NumberFormatter formatter, double value)
    {
        _formatter = formatter;
        _double = value;
        _isDouble = true;
    }

    /// <summary>
    /// Writes the number with its format's <c>TryFormat</c>. Allocates nothing. The default value writes nothing.
    /// </summary>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <param name="format">Not read: the format is the <see cref="NumberFormatter"/>'s.</param>
    /// <param name="provider">Not read: the locale is the <see cref="NumberFormatter"/>'s.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (_formatter is null)
        {
            charsWritten = 0;
            return true;
        }

        return _isDouble
            ? _formatter.TryFormat(_double, destination, out charsWritten)
            : _formatter.TryFormat(_decimal, destination, out charsWritten);
    }

    /// <summary>
    /// Returns the formatted number as a new string; not for the frame path.
    /// </summary>
    /// <param name="format">The format, which is not read.</param>
    /// <param name="formatProvider">The provider, which is not read.</param>
    /// <returns>The formatted number.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <summary>
    /// Returns the formatted number as a new string; not for the frame path.
    /// </summary>
    /// <returns>The formatted number; empty for the default value.</returns>
    public override string ToString()
    {
        Span<char> buffer = stackalloc char[64];
        if (TryFormat(buffer, out int written, default, null))
        {
            return new string(buffer[..written]);
        }

        char[] grown = new char[256];
        while (!TryFormat(grown, out written, default, null))
        {
            grown = new char[grown.Length * 2];
        }

        return new string(grown, 0, written);
    }
}
