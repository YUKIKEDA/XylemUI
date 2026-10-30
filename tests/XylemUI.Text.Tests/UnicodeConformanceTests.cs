using System.Globalization;
using System.Text;
using XylemUI.Text.LineBreak;
using XylemUI.Text.Unicode;

namespace XylemUI.Text.Tests;

// The Unicode conformance tests of the grapheme clusters and the word boundaries (UAX #29) and the line break opportunities
// (UAX #14) of Unicode 18.0.0 (design §4.1, "Line breaking and grapheme clusters"; §4.9, "The keys"; §8.13; roadmap rows T1 and
// P02b). The test files come from UCD.zip with the tables
// (tools/UnicodeTableGen); each line is one case, and every line passes with no exception.
public sealed class UnicodeConformanceTests
{
    private static readonly Lazy<TheoryData<int, string>> GraphemeLines = new(() => Lines("GraphemeBreakTest.txt"));
    private static readonly Lazy<TheoryData<int, string>> LineBreakLines = new(() => Lines("LineBreakTest.txt"));
    private static readonly Lazy<TheoryData<int, string>> WordLines = new(() => Lines("WordBreakTest.txt"));

    /// <summary>
    /// Gets the cases of GraphemeBreakTest.txt: the line number and the line.
    /// </summary>
    public static TheoryData<int, string> GraphemeCases => GraphemeLines.Value;

    /// <summary>
    /// Gets the cases of LineBreakTest.txt: the line number and the line.
    /// </summary>
    public static TheoryData<int, string> LineBreakCases => LineBreakLines.Value;

    /// <summary>
    /// Gets the cases of WordBreakTest.txt: the line number and the line.
    /// </summary>
    public static TheoryData<int, string> WordCases => WordLines.Value;

    /// <summary>
    /// The grapheme cluster boundaries of one line of GraphemeBreakTest.txt are the ones it marks.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One line of the Unicode 18.0.0 GraphemeBreakTest.txt: code points separated by ÷ (a boundary) and × (none).
    ///
    /// Steps:
    /// - Make the UTF-16 text of the code points; walk its clusters with Graphemes.NextBoundary from 0.
    ///
    /// Expected:
    /// - The positions reached are the ÷ positions of the line, the start and the end included.
    /// </remarks>
    /// <param name="line">The line number, for the report.</param>
    /// <param name="data">The line.</param>
    [Theory]
    [MemberData(nameof(GraphemeCases))]
    public void Graphemes_ConformanceLine_HasItsBoundaries(int line, string data)
    {
        (string text, List<int> expected) = Parse(data);
        var actual = new List<int> { 0 };
        for (int position = 0; position < text.Length; )
        {
            position = Graphemes.NextBoundary(text, position);
            actual.Add(position);
        }

        Assert.True(expected.SequenceEqual(actual), $"line {line}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    /// <summary>
    /// At every position of one line of GraphemeBreakTest.txt, the check of a boundary from the code points around it agrees with
    /// the line.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One line of the Unicode 18.0.0 GraphemeBreakTest.txt.
    ///
    /// Steps:
    /// - Make the UTF-16 text of the code points; call Graphemes.IsBoundary at each position from 0 to its length, the positions
    ///   between the halves of a surrogate pair included.
    ///
    /// Expected:
    /// - True exactly at the ÷ positions of the line.
    /// </remarks>
    /// <param name="line">The line number, for the report.</param>
    /// <param name="data">The line.</param>
    [Theory]
    [MemberData(nameof(GraphemeCases))]
    public void Graphemes_IsBoundaryAtEachPosition_AgreesWithTheLine(int line, string data)
    {
        (string text, List<int> expected) = Parse(data);
        int[] actual = [.. Enumerable.Range(0, text.Length + 1).Where(i => Graphemes.IsBoundary(text, i))];

        Assert.True(expected.SequenceEqual(actual), $"line {line}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    /// <summary>
    /// The word boundaries of one line of WordBreakTest.txt are the ones it marks, walked forward and back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One line of the Unicode 18.0.0 WordBreakTest.txt: code points separated by ÷ (a boundary) and × (none).
    ///
    /// Steps:
    /// - Make the UTF-16 text of the code points; walk its words with Words.NextBoundary from 0, then with Words.PreviousBoundary
    ///   from the end.
    ///
    /// Expected:
    /// - The positions reached forward are the ÷ positions of the line, the start and the end included; the positions reached back
    ///   are the same ones in the other order.
    /// </remarks>
    /// <param name="line">The line number, for the report.</param>
    /// <param name="data">The line.</param>
    [Theory]
    [MemberData(nameof(WordCases))]
    public void Words_ConformanceLine_HasItsBoundaries(int line, string data)
    {
        (string text, List<int> expected) = Parse(data);
        var forward = new List<int> { 0 };
        for (int position = 0; position < text.Length; )
        {
            position = Words.NextBoundary(text, position);
            forward.Add(position);
        }

        var back = new List<int> { text.Length };
        for (int position = text.Length; position > 0; )
        {
            position = Words.PreviousBoundary(text, position);
            back.Add(position);
        }

        back.Reverse();
        Assert.True(expected.SequenceEqual(forward), $"line {line}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", forward)}]");
        Assert.True(expected.SequenceEqual(back), $"line {line}: back, expected [{string.Join(", ", expected)}], got [{string.Join(", ", back)}]");
    }

    /// <summary>
    /// The line break opportunities of one line of LineBreakTest.txt are the ones it marks.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One line of the Unicode 18.0.0 LineBreakTest.txt: code points separated by ÷ (a break allowed) and × (none).
    ///
    /// Steps:
    /// - Make the UTF-16 text of the code points; find its opportunities with LineBreaker.Find.
    ///
    /// Expected:
    /// - The positions found are the ÷ positions of the line (the end included; the start is never one).
    /// </remarks>
    /// <param name="line">The line number, for the report.</param>
    /// <param name="data">The line.</param>
    [Theory]
    [MemberData(nameof(LineBreakCases))]
    public void LineBreaker_ConformanceLine_HasItsOpportunities(int line, string data)
    {
        (string text, List<int> expected) = Parse(data);
        expected.Remove(0);
        int[] actual = [.. new LineBreaker().Find(text).ToArray().Select(b => b.Position)];

        Assert.True(expected.SequenceEqual(actual), $"line {line}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    // The text of a test line and the UTF-16 positions it marks with ÷.
    private static (string Text, List<int> Breaks) Parse(string data)
    {
        var text = new StringBuilder();
        var breaks = new List<int>();
        foreach (string token in data.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "÷")
            {
                breaks.Add(text.Length);
            }
            else if (token != "×")
            {
                text.Append(char.ConvertFromUtf32(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            }
        }

        return (text.ToString(), breaks);
    }

    private static TheoryData<int, string> Lines(string name)
    {
        var data = new TheoryData<int, string>();
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Unicode", name));
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Split('#')[0].Trim();
            if (line.Length > 0)
            {
                data.Add(i + 1, line);
            }
        }

        return data;
    }
}
