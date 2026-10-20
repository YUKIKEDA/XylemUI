namespace XylemUI.Text;

/// <summary>
/// Finds the fonts of the OS that render a text, a range at a time (design §4.5): DirectWrite on Windows (roadmap row F1), Core
/// Text on macOS (P06), fontconfig on Linux (P04).
/// </summary>
/// <remarks>
/// The provider owns the fonts it returns and keeps them open while it lives. It is used on one thread, as <see cref="Font"/> is.
/// </remarks>
public interface IFontProvider
{
    /// <summary>
    /// Returns the font for the characters of <paramref name="text"/> from <paramref name="start"/>: the longest range one font
    /// of the OS renders, or, when none renders them, a range of at least one character with no font.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The index of the first character to match.</param>
    /// <param name="language">The language of the text, which picks the glyphs of a Han character (Japanese or Chinese).</param>
    /// <param name="request">The font the text asks for.</param>
    /// <returns>The font, the number of characters it covers (at least 1), and its scale.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> is not an index of <paramref name="text"/>.</exception>
    FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request);
}
