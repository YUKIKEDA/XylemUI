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
    /// Returns whether a position is a grapheme cluster boundary, from the code points around it: the two on its sides, and those
    /// before it that the rules looking back read (the regional indicators before it for GB12 and GB13, the Extend* before a ZWJ
    /// for GB11, and the InCB Extend* after a linker for GB9c). It does not walk the text from its start, so a font run's end is
    /// checked in the time of the cluster around it (PR #285's review).
    /// </summary>
    /// <param name="text">The text, in UTF-16.</param>
    /// <param name="index">The position, from 0 to the text's length.</param>
    /// <returns><see langword="true"/> at the start, the end, and between two clusters; <see langword="false"/> inside a cluster and
    /// between the two halves of a surrogate pair.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the text.</exception>
    public static bool IsBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);
        if (index == 0 || index == text.Length)
        {
            return true; // GB1, GB2
        }

        if (char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
        {
            return false;
        }

        int before = PreviousStart(text, index);
        uint previous = UnicodeData.Properties(UnicodeData.CodePointAt(text, before, out _));
        uint next = UnicodeData.Properties(UnicodeData.CodePointAt(text, index, out _));

        // The regional indicators that end the text before the position.
        int regional = 0;
        for (int k = index; k > 0; )
        {
            k = PreviousStart(text, k);
            if (UnicodeData.Grapheme(UnicodeData.Properties(UnicodeData.CodePointAt(text, k, out _))) != GraphemeClusterBreak.Regional_Indicator)
            {
                break;
            }

            regional++;
        }

        // An extended pictographic, Extend*, and a ZWJ just before the position (GB11).
        bool pictographicZwj = false;
        if (UnicodeData.Grapheme(previous) == GraphemeClusterBreak.ZWJ)
        {
            int k = before;
            uint properties = 0;
            while (k > 0)
            {
                k = PreviousStart(text, k);
                properties = UnicodeData.Properties(UnicodeData.CodePointAt(text, k, out _));
                if (UnicodeData.Grapheme(properties) != GraphemeClusterBreak.Extend)
                {
                    break;
                }
            }

            pictographicZwj = k < before && UnicodeData.IsPictographic(properties);
        }

        // An InCB linker and InCB Extend* just before the position (GB9c).
        bool linker = false;
        for (int k = index; k > 0; )
        {
            k = PreviousStart(text, k);
            IndicConjunctBreak indic = UnicodeData.Indic(UnicodeData.Properties(UnicodeData.CodePointAt(text, k, out _)));
            if (indic != IndicConjunctBreak.Extend)
            {
                linker = indic == IndicConjunctBreak.Linker;
                break;
            }
        }

        return IsBreak(previous, next, regional, pictographicZwj, linker);
    }

    /// <summary>
    /// Returns the start of the grapheme cluster that ends at a position: the boundary before it.
    /// </summary>
    /// <param name="text">The text, in UTF-16.</param>
    /// <param name="index">The position, from 1 to the text's length.</param>
    /// <returns>The last boundary before the position: 0 if there is none other.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not from 1 to the text's length.</exception>
    public static int PreviousBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);
        for (int position = index - 1; position > 0; position--)
        {
            if (IsBoundary(text, position))
            {
                return position;
            }
        }

        return 0;
    }

    // The start of the code point that ends at `index`.
    private static int PreviousStart(ReadOnlySpan<char> text, int index) =>
        index >= 2 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2]) ? index - 2 : index - 1;

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
