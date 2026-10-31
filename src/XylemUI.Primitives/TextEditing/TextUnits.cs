using XylemUI.Text.Unicode;

namespace XylemUI.Primitives;

/// <summary>
/// The units a double and a triple click select in a text (design §4.9 and §4.10, "Pointer"): the word around a position, by
/// UAX #29's word boundaries, and the paragraph around it, between hard line breaks.
/// </summary>
internal static class TextUnits
{
    /// <summary>
    /// Returns the word around a position: the word boundaries on either side.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="offset">The position.</param>
    /// <returns>The word, from its start to its end; 0 to 0 in an empty text.</returns>
    public static TextSelection WordAt(ReadOnlySpan<char> text, int offset)
    {
        if (text.IsEmpty)
        {
            return default;
        }

        int at = Math.Clamp(offset, 0, text.Length - 1);
        int start = 0;
        for (int position = 0; position < text.Length; )
        {
            int end = Words.NextBoundary(text, position);
            if (at < end)
            {
                return new TextSelection(start, end);
            }

            start = position = end;
        }

        return new TextSelection(start, text.Length);
    }

    /// <summary>
    /// Returns the paragraph around a position: from the line break before it to the one after it, the breaks left out.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="offset">The position.</param>
    /// <returns>The paragraph, from its start to its end.</returns>
    public static TextSelection ParagraphAt(ReadOnlySpan<char> text, int offset)
    {
        int at = Math.Clamp(offset, 0, text.Length);
        int start = at > 0 ? text[..at].LastIndexOf('\n') + 1 : 0;
        int end = text[at..].IndexOf('\n');
        return new TextSelection(start, end < 0 ? text.Length : at + end);
    }
}
