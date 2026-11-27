using System.Text;

namespace XylemUI;

/// <summary>
/// Compiles a date pattern of CLDR into its pieces (design §4.11, "Dates and times"). Text in quotes is literal, two quotes are one
/// quote character inside or outside quotes, and each run of one ASCII letter outside quotes is a field. tools/CldrTableGen checks
/// that every field of a generated pattern is one <see cref="DateFormatter"/> writes and quotes the letters of the joining patterns;
/// <see cref="DateFormatter"/> fails the invariant on any other field.
/// </summary>
internal static class DatePattern
{
    /// <summary>
    /// Compiles a pattern.
    /// </summary>
    /// <param name="pattern">The pattern, such as <c>EEE, MMM d</c>.</param>
    /// <returns>The pieces, in order.</returns>
    public static DatePatternItem[] Compile(string pattern)
    {
        var items = new List<DatePatternItem>();
        var literal = new StringBuilder();
        bool quoted = false;
        int i = 0;
        while (i < pattern.Length)
        {
            char character = pattern[i];
            int length = 1;
            while (i + length < pattern.Length && pattern[i + length] == character)
            {
                length++;
            }

            if (character == '\'')
            {
                // Each pair of quotes is a quote character; a quote left over opens or closes the quoted text.
                literal.Append('\'', length / 2);
                quoted ^= length % 2 == 1;
            }
            else if (!quoted && char.IsAsciiLetter(character))
            {
                Flush(items, literal);
                items.Add(new DatePatternItem(character, length, string.Empty));
            }
            else
            {
                literal.Append(character, length);
            }

            i += length;
        }

        Flush(items, literal);
        return [.. items];
    }

    private static void Flush(List<DatePatternItem> items, StringBuilder literal)
    {
        if (literal.Length > 0)
        {
            items.Add(new DatePatternItem('\0', 0, literal.ToString()));
            _ = literal.Clear();
        }
    }
}
