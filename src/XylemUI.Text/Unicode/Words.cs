namespace XylemUI.Text.Unicode;

/// <summary>
/// Word boundaries (UAX #29, Unicode 18.0.0, the default rules WB1 to WB999; design §4.9, "The keys"): where a caret moves and a
/// deletion stops when a text field moves by word. With no dictionary, ideographs and hiragana are a word each, and a run of
/// katakana is one word.
/// </summary>
internal static class Words
{
    /// <summary>
    /// Returns the next word boundary after a boundary.
    /// </summary>
    /// <param name="text">The text, in UTF-16.</param>
    /// <param name="start">A word boundary: 0, or a position this method returned.</param>
    /// <returns>The next boundary; the text's length at its end.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> is not inside the text.</exception>
    public static int NextBoundary(ReadOnlySpan<char> text, int start)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, text.Length);
        uint first = UnicodeData.Properties(UnicodeData.CodePointAt(text, start, out int length));
        int position = start + length;

        // What the rules read on the left: the code point just before the position as it is (WB3 to WB3d), the last code point
        // that WB4 does not skip and the one before it (WB6 to WB16), and the regional indicators that end the text so far.
        uint raw = first;
        WordBreak left = UnicodeData.Word(first);
        WordBreak beforeLeft = WordBreak.Other;
        int regional = left == WordBreak.Regional_Indicator ? 1 : 0;
        while (position < text.Length)
        {
            uint next = UnicodeData.Properties(UnicodeData.CodePointAt(text, position, out length));
            WordBreak right = UnicodeData.Word(next);
            WordBreak rawLeft = UnicodeData.Word(raw);
            if (rawLeft == WordBreak.CR && right == WordBreak.LF)
            {
                // WB3
            }
            else if (rawLeft is WordBreak.Newline or WordBreak.CR or WordBreak.LF)
            {
                break; // WB3a
            }
            else if (right is WordBreak.Newline or WordBreak.CR or WordBreak.LF)
            {
                break; // WB3b
            }
            else if (rawLeft == WordBreak.ZWJ && UnicodeData.IsPictographic(next))
            {
                // WB3c
            }
            else if (rawLeft == WordBreak.WSegSpace && right == WordBreak.WSegSpace)
            {
                // WB3d
            }
            else if (right is WordBreak.Extend or WordBreak.Format or WordBreak.ZWJ)
            {
                // WB4: X (Extend | Format | ZWJ)* → X. The left the other rules read stays.
                raw = next;
                position += length;
                continue;
            }
            else if (IsBreak(beforeLeft, left, right, After(text, position + length), regional))
            {
                break;
            }

            raw = next;
            position += length;
            beforeLeft = left;
            left = right;
            regional = right == WordBreak.Regional_Indicator ? regional + 1 : 0;
        }

        return position;
    }

    /// <summary>
    /// Returns the word boundary before a position.
    /// </summary>
    /// <remarks>
    /// It walks the boundaries from the text's start, so it takes time in proportion to the text before the position. A text field
    /// asks once a key; the text of a field is short next to the time of a frame.
    /// </remarks>
    /// <param name="text">The text, in UTF-16.</param>
    /// <param name="index">The position, from 1 to the text's length.</param>
    /// <returns>The last boundary before the position: 0 if there is none other.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not from 1 to the text's length.</exception>
    public static int PreviousBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);
        int previous = 0;
        for (int position = 0; position < index; )
        {
            previous = position;
            position = NextBoundary(text, position);
        }

        return previous;
    }

    // The word break of the first code point at or after a position that WB4 does not skip; Other at the end.
    private static WordBreak After(ReadOnlySpan<char> text, int position)
    {
        while (position < text.Length)
        {
            WordBreak word = UnicodeData.Word(UnicodeData.Properties(UnicodeData.CodePointAt(text, position, out int length)));
            if (word is not (WordBreak.Extend or WordBreak.Format or WordBreak.ZWJ))
            {
                return word;
            }

            position += length;
        }

        return WordBreak.Other;
    }

    // Rules WB5 to WB999 between the left and the right that WB4 leaves, with the one before the left, the one after the right, and
    // the regional indicators that end the left.
    private static bool IsBreak(WordBreak beforeLeft, WordBreak left, WordBreak right, WordBreak afterRight, int regional)
    {
        if (IsLetter(left) && IsLetter(right))
        {
            return false; // WB5
        }

        if (IsLetter(left) && IsMidLetter(right) && IsLetter(afterRight))
        {
            return false; // WB6
        }

        if (IsLetter(beforeLeft) && IsMidLetter(left) && IsLetter(right))
        {
            return false; // WB7
        }

        if (left == WordBreak.Hebrew_Letter && right == WordBreak.Single_Quote)
        {
            return false; // WB7a
        }

        if (left == WordBreak.Hebrew_Letter && right == WordBreak.Double_Quote && afterRight == WordBreak.Hebrew_Letter)
        {
            return false; // WB7b
        }

        if (beforeLeft == WordBreak.Hebrew_Letter && left == WordBreak.Double_Quote && right == WordBreak.Hebrew_Letter)
        {
            return false; // WB7c
        }

        if (
            (left == WordBreak.Numeric || IsLetter(left))
            && (right == WordBreak.Numeric || IsLetter(right))
            && (left == WordBreak.Numeric || right == WordBreak.Numeric)
        )
        {
            return false; // WB8, WB9, WB10
        }

        if (beforeLeft == WordBreak.Numeric && IsMidNumber(left) && right == WordBreak.Numeric)
        {
            return false; // WB11
        }

        if (left == WordBreak.Numeric && IsMidNumber(right) && afterRight == WordBreak.Numeric)
        {
            return false; // WB12
        }

        if (left == WordBreak.Katakana && right == WordBreak.Katakana)
        {
            return false; // WB13
        }

        if ((IsLetter(left) || left is WordBreak.Numeric or WordBreak.Katakana or WordBreak.ExtendNumLet) && right == WordBreak.ExtendNumLet)
        {
            return false; // WB13a
        }

        if (left == WordBreak.ExtendNumLet && (IsLetter(right) || right is WordBreak.Numeric or WordBreak.Katakana))
        {
            return false; // WB13b
        }

        // WB15, WB16: a pair of regional indicators is one word. WB999 breaks everywhere else.
        return !(right == WordBreak.Regional_Indicator && regional % 2 == 1);
    }

    // AHLetter.
    private static bool IsLetter(WordBreak word) => word is WordBreak.ALetter or WordBreak.Hebrew_Letter;

    // MidLetter | MidNumLetQ.
    private static bool IsMidLetter(WordBreak word) => word is WordBreak.MidLetter or WordBreak.MidNumLet or WordBreak.Single_Quote;

    // MidNum | MidNumLetQ.
    private static bool IsMidNumber(WordBreak word) => word is WordBreak.MidNum or WordBreak.MidNumLet or WordBreak.Single_Quote;
}
