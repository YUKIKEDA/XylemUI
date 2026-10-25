namespace XylemUI.Text.Unicode;

/// <summary>
/// Extended grapheme clusters (UAX #29, Unicode 18.0.0; design §4.1, "Line breaking and grapheme clusters"): the units a font run,
/// a caret, and an emergency break never cut.
/// </summary>
internal static class Graphemes
{
    /// <summary>
    /// Returns the end of the grapheme cluster that starts at a position.
    /// </summary>
    /// <param name="text">The text, in UTF-16.</param>
    /// <param name="start">The start of a cluster: 0, or the end of the cluster before it.</param>
    /// <returns>The position after the cluster; the text's length at its end.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> is not inside the text.</exception>
    public static int NextBoundary(ReadOnlySpan<char> text, int start)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, text.Length);
        int position = start;
        uint previous = UnicodeData.Properties(UnicodeData.CodePointAt(text, position, out int length));
        position += length;

        // What the rules that look back read: the regional indicators that end the cluster so far (GB12, GB13), whether it ends with
        // an extended pictographic and Extend* (GB11) or with that and a ZWJ, and whether it ends with an InCB linker and InCB
        // Extend* (GB9c).
        int regional = UnicodeData.Grapheme(previous) == GraphemeClusterBreak.Regional_Indicator ? 1 : 0;
        bool pictographic = UnicodeData.IsPictographic(previous);
        bool pictographicZwj = false;
        bool linker = UnicodeData.Indic(previous) == IndicConjunctBreak.Linker;
        while (position < text.Length)
        {
            uint next = UnicodeData.Properties(UnicodeData.CodePointAt(text, position, out length));
            if (IsBreak(previous, next, regional, pictographicZwj, linker))
            {
                break;
            }

            GraphemeClusterBreak kind = UnicodeData.Grapheme(next);
            regional = kind == GraphemeClusterBreak.Regional_Indicator ? regional + 1 : 0;
            pictographicZwj = pictographic && kind == GraphemeClusterBreak.ZWJ;
            pictographic = UnicodeData.IsPictographic(next) || (pictographic && kind == GraphemeClusterBreak.Extend);
            IndicConjunctBreak indic = UnicodeData.Indic(next);
            linker = indic == IndicConjunctBreak.Linker || (linker && indic == IndicConjunctBreak.Extend);
            previous = next;
            position += length;
        }

        return position;
    }

    /// <summary>
    /// Returns whether a position is a grapheme cluster boundary.
    /// </summary>
    /// <param name="text">The text, in UTF-16.</param>
    /// <param name="index">The position, from 0 to the text's length.</param>
    /// <returns><see langword="true"/> at the start, the end, and between two clusters.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the text.</exception>
    public static bool IsBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);
        int position = 0;
        while (position < index)
        {
            position = NextBoundary(text, position);
        }

        return position == index;
    }

    // Rules GB3 to GB999 between two code points, with what the rules that look back need.
    private static bool IsBreak(uint previous, uint next, int regional, bool pictographicZwj, bool linker)
    {
        GraphemeClusterBreak left = UnicodeData.Grapheme(previous);
        GraphemeClusterBreak right = UnicodeData.Grapheme(next);
        if (left == GraphemeClusterBreak.CR && right == GraphemeClusterBreak.LF)
        {
            return false; // GB3
        }

        if (left is GraphemeClusterBreak.Control or GraphemeClusterBreak.CR or GraphemeClusterBreak.LF)
        {
            return true; // GB4
        }

        if (right is GraphemeClusterBreak.Control or GraphemeClusterBreak.CR or GraphemeClusterBreak.LF)
        {
            return true; // GB5
        }

        bool hangul = (left, right) switch
        {
            (GraphemeClusterBreak.L, GraphemeClusterBreak.L or GraphemeClusterBreak.V or GraphemeClusterBreak.LV or GraphemeClusterBreak.LVT) => true, // GB6
            (GraphemeClusterBreak.LV or GraphemeClusterBreak.V, GraphemeClusterBreak.V or GraphemeClusterBreak.T) => true, // GB7
            (GraphemeClusterBreak.LVT or GraphemeClusterBreak.T, GraphemeClusterBreak.T) => true, // GB8
            _ => false,
        };
        if (hangul)
        {
            return false;
        }

        if (right is GraphemeClusterBreak.Extend or GraphemeClusterBreak.ZWJ or GraphemeClusterBreak.SpacingMark)
        {
            return false; // GB9, GB9a
        }

        if (left == GraphemeClusterBreak.Prepend)
        {
            return false; // GB9b
        }

        if (linker && UnicodeData.Indic(next) == IndicConjunctBreak.Consonant)
        {
            return false; // GB9c: \p{InCB=Linker} \p{InCB=Extend}* × \p{InCB=Consonant}
        }

        if (pictographicZwj && UnicodeData.IsPictographic(next))
        {
            return false; // GB11
        }

        // GB12, GB13: a pair of regional indicators is one cluster.
        return !(right == GraphemeClusterBreak.Regional_Indicator && regional % 2 == 1);
    }
}
