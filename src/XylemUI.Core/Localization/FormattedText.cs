namespace XylemUI;

/// <summary>
/// Makes the string of a formatted value, for the <c>ToString</c> of <see cref="FormattedNumber"/>, <see cref="FormattedDate"/>, and
/// <see cref="FormattedDateRange"/>; not for the frame path.
/// </summary>
internal static class FormattedText
{
    /// <summary>
    /// Writes a value into a buffer on the stack, or into larger arrays until it fits, and returns the text as a new string.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string Make<T>(in T value)
        where T : struct, ISpanFormattable
    {
        Span<char> buffer = stackalloc char[64];
        if (value.TryFormat(buffer, out int written, default, null))
        {
            return new string(buffer[..written]);
        }

        // A number takes at most 999 integer and 999 fraction digits, so its text (with its group separators and affixes) is some
        // 2,500 characters at most, and the buffer stops growing at 4,096.
        char[] grown = new char[256];
        while (!value.TryFormat(grown, out written, default, null))
        {
            grown = new char[grown.Length * 2];
        }

        return new string(grown, 0, written);
    }
}
