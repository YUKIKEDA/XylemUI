namespace XylemUI;

/// <summary>
/// The reading of characters that the number and the date parsers share (design §4.11, "Parsing" and "Parsing short dates"): the bidi
/// marks they skip, and the digits they read.
/// </summary>
internal static class ParseText
{
    private const char FullWidthZero = (char)0xFF10;

    private const char FullWidthNine = (char)0xFF19;

    private const char LeftToRightMark = (char)0x200E;

    private const char RightToLeftMark = (char)0x200F;

    private const char ArabicLetterMark = (char)0x061C;

    /// <summary>
    /// Copies text without the bidi marks CLDR puts in symbols and patterns (U+200E, U+200F, U+061C), which parsing skips: UTS #35
    /// part 3 says that lenient parsing should ignore bidi format characters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="destination">The characters to write to, at least as long as <paramref name="text"/>.</param>
    /// <returns>The characters written.</returns>
    public static int WithoutBidiMarks(ReadOnlySpan<char> text, Span<char> destination)
    {
        int length = 0;
        foreach (char character in text)
        {
            if (!IsBidiMark(character))
            {
                destination[length++] = character;
            }
        }

        return length;
    }

    /// <summary>
    /// Returns a string without its bidi marks (U+200E, U+200F, U+061C).
    /// </summary>
    /// <param name="text">The string.</param>
    /// <returns>The string without the marks.</returns>
    public static string WithoutBidiMarks(string text)
    {
        Span<char> buffer = text.Length <= 64 ? stackalloc char[64] : new char[text.Length];
        return new string(buffer[..WithoutBidiMarks(text, buffer)]);
    }

    /// <summary>
    /// Returns the value of a locale's digit, an ASCII digit, or a full-width digit (U+FF10 to U+FF19), which parsing reads as the
    /// ASCII digit (design §4.11, "Full-width digits").
    /// </summary>
    /// <param name="character">The character.</param>
    /// <param name="zero">The digit zero of the locale's default numbering system.</param>
    /// <returns>The digit's value, 0 to 9; -1 for another character.</returns>
    public static int DigitValue(char character, char zero)
    {
        if (char.IsAsciiDigit(character))
        {
            return character - '0';
        }

        if (character is >= FullWidthZero and <= FullWidthNine)
        {
            return character - FullWidthZero;
        }

        int offset = character - zero;
        return offset is >= 0 and <= 9 ? offset : -1;
    }

    private static bool IsBidiMark(char character) => character is LeftToRightMark or RightToLeftMark or ArabicLetterMark;
}
