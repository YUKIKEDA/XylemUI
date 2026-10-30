using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace XylemUI.Text.Unicode;

/// <summary>
/// The Unicode properties the text pipeline reads, from the tables tools/UnicodeTableGen generates (design §4.1, "Line breaking and
/// grapheme clusters"; §8.13). A code point's properties are one 32-bit value of a palette, found through a two-stage table of
/// byte indices. Every table is a span of bytes: it points into the executable's data in every build, where a span of a wider
/// type is an array made at each read in a Debug build.
/// </summary>
/// <remarks>
/// Of the two-stage tables the tool compared for Unicode 18.0.0, blocks of 128 code points were the smallest: 55,256 bytes with
/// the palette of 150 values (54,080 with 112 before the word breaks of row P02b), and every block size took 0.94 to 1.04 ns a
/// look-up (1.1 to 1.4 ns when row T1 measured, which found 147,712 bytes for the smallest table of 32-bit values). The tables are static data of the executable: nothing is allocated or initialized at startup.
/// </remarks>
internal static partial class UnicodeData
{
    private const int LineBreakMask = 0x3F;
    private const int GraphemeShift = 6;
    private const int GraphemeMask = 0xF;
    private const uint PictographicBit = 1u << 10;
    private const uint EastAsianBit = 1u << 11;
    private const int IndicShift = 12;
    private const int IndicMask = 0x3;
    private const int CategoryShift = 14;
    private const int CategoryMask = 0x7;
    private const int WordShift = 17;
    private const int WordMask = 0x1F;

    /// <summary>
    /// Returns the properties of a code point, read by the other members.
    /// </summary>
    /// <param name="codePoint">The code point, from 0 to 0x10FFFF; a lone surrogate is read as its own code point.</param>
    /// <returns>The packed properties.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Properties(int codePoint)
    {
        int block = BinaryPrimitives.ReadUInt16LittleEndian(Stage1[((codePoint >> Shift) * 2)..]);
        int index = Stage2[(block << Shift) | (codePoint & ((1 << Shift) - 1))];
        return BinaryPrimitives.ReadUInt32LittleEndian(Palette[(index * 4)..]);
    }

    /// <summary>
    /// Returns the line break class of packed properties (UAX #14), before rule LB1 resolves AI, CJ, SA, SG, and XX.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns>The class.</returns>
    public static LineBreakClass LineBreak(uint properties) => (LineBreakClass)(properties & LineBreakMask);

    /// <summary>
    /// Returns the grapheme cluster break of packed properties (UAX #29).
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns>The value.</returns>
    public static GraphemeClusterBreak Grapheme(uint properties) => (GraphemeClusterBreak)((properties >> GraphemeShift) & GraphemeMask);

    /// <summary>
    /// Returns whether packed properties have Extended_Pictographic.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns><see langword="true"/> for an extended pictographic.</returns>
    public static bool IsPictographic(uint properties) => (properties & PictographicBit) != 0;

    /// <summary>
    /// Returns whether packed properties have an East Asian width of Fullwidth, Wide, or Halfwidth (UAX #14's $EastAsian).
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns><see langword="true"/> for F, W, or H.</returns>
    public static bool IsEastAsian(uint properties) => (properties & EastAsianBit) != 0;

    /// <summary>
    /// Returns the Indic_Conjunct_Break of packed properties (UAX #29's rule GB9c).
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns>The value.</returns>
    public static IndicConjunctBreak Indic(uint properties) => (IndicConjunctBreak)((properties >> IndicShift) & IndicMask);

    /// <summary>
    /// Returns the general category of packed properties, when it is one the rules read (Pi, Pf, Mn, Mc, Cn).
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns>The category, or <see cref="CategoryOfInterest.Other"/>.</returns>
    public static CategoryOfInterest Category(uint properties) => (CategoryOfInterest)((properties >> CategoryShift) & CategoryMask);

    /// <summary>
    /// Returns the word break of packed properties (UAX #29).
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns>The value.</returns>
    public static WordBreak Word(uint properties) => (WordBreak)((properties >> WordShift) & WordMask);

    /// <summary>
    /// Returns the code point at a position of UTF-16 text and the number of code units it takes.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The position, inside the text.</param>
    /// <param name="length">1, or 2 for a surrogate pair.</param>
    /// <returns>The code point; a lone surrogate is returned as itself.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CodePointAt(ReadOnlySpan<char> text, int index, out int length)
    {
        char c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, text[index + 1]);
        }

        length = 1;
        return c;
    }
}
