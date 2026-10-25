using XylemUI.Text.LineBreak;
using XylemUI.Text.Unicode;

namespace XylemUI.Text.Tests;

// The edge cases of the grapheme clusters and the line break opportunities (roadmap row T1, design §4.1, "Line breaking and
// grapheme clusters"), the version of the generated tables, and their steady state. The expected values come from UAX #14 and
// UAX #29 as written for Unicode 18.0.0, not from the code.
public sealed class SegmentationTests
{
    /// <summary>
    /// Each hard line break of rules LB4 and LB5 ends a line there: VT, FF, U+2028, and U+2029 (BK), CR, LF, NEL (NL), and CR LF
    /// as one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A line breaker.
    ///
    /// Steps:
    /// - Find the opportunities of "a" + the break + "b" for each of VT, FF, U+2028, U+2029, CR, LF, and NEL, and of "a\r\nb".
    ///
    /// Expected:
    /// - A mandatory opportunity after the break (at 2), none between "a" and the break, and the end (3) mandatory. For CR LF, one
    ///   mandatory opportunity after the LF (at 3), none between CR and LF.
    /// </remarks>
    /// <param name="hardBreak">The hard line break.</param>
    [Theory]
    [InlineData('\u000B')]
    [InlineData('\u000C')]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData('\u0085')]
    public void LineBreaker_HardBreak_EndsTheLineAfterIt(char hardBreak)
    {
        var breaker = new LineBreaker();

        LineBreakOpportunity[] breaks = breaker.Find($"a{hardBreak}b").ToArray();
        LineBreakOpportunity[] crlf = breaker.Find("a\r\nb").ToArray();

        Assert.Equal([new LineBreakOpportunity(2, true), new LineBreakOpportunity(3, true)], breaks);
        Assert.Equal([new LineBreakOpportunity(3, true), new LineBreakOpportunity(4, true)], crlf);
    }

    /// <summary>
    /// Spaces and words: a break is allowed after the spaces before a word and is not mandatory; the end is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A line breaker.
    ///
    /// Steps:
    /// - Find the opportunities of "ab  cd" and of "日本語".
    ///
    /// Expected:
    /// - "ab  cd": an allowed opportunity at 4 (after both spaces, LB7 and LB18) and the mandatory end at 6. "日本語": allowed at 1
    ///   and 2 between the ideographs (LB31) and the end at 3.
    /// </remarks>
    [Fact]
    public void LineBreaker_SpacesAndIdeographs_AllowBreaks()
    {
        var breaker = new LineBreaker();

        Assert.Equal([new LineBreakOpportunity(4, false), new LineBreakOpportunity(6, true)], breaker.Find("ab  cd").ToArray());
        Assert.Equal(
            [new LineBreakOpportunity(1, false), new LineBreakOpportunity(2, false), new LineBreakOpportunity(3, true)],
            breaker.Find("日本語").ToArray()
        );
    }

    /// <summary>
    /// Small kana resolve to NS (strict, LB1's default): no break before a small kana or a prolonged sound mark.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A line breaker.
    ///
    /// Steps:
    /// - Find the opportunities of "キャッシュー" (ki, small ya, small tsu, shi, small yu, the prolonged sound mark).
    ///
    /// Expected:
    /// - Only before "シ" (at 3) and the end (at 6): none before the small kana and the prolonged sound mark (CJ, NS by LB21).
    /// </remarks>
    [Fact]
    public void LineBreaker_SmallKana_AreStrict()
    {
        int[] positions = [.. new LineBreaker().Find("キャッシュー").ToArray().Select(b => b.Position)];

        Assert.Equal([3, 6], positions);
    }

    /// <summary>
    /// Neither a grapheme cluster nor a line break opportunity falls inside an emoji ZWJ sequence, a variation sequence, an emoji
    /// with a skin tone, a flag, or a surrogate pair.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A line breaker, and the texts: the family 👨‍👩‍👧 (three emoji joined by ZWJ, 8 code units), ❤️ (U+2764 U+FE0F), 👍🏽
    ///   (a thumb with a medium skin tone, 4 code units), 🇯🇵 (two regional indicators, 4 code units), and 𠀋 (U+2000B, a pair).
    ///
    /// Steps:
    /// - Walk each text's clusters, and find its line break opportunities.
    ///
    /// Expected:
    /// - Each text is one cluster, and its only opportunity is its end.
    /// </remarks>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("👨‍👩‍👧")]
    [InlineData("❤️")]
    [InlineData("👍🏽")]
    [InlineData("🇯🇵")]
    [InlineData("𠀋")]
    public void Segmentation_EmojiSequencesAndPairs_AreNotCut(string text)
    {
        Assert.Equal(text.Length, Graphemes.NextBoundary(text, 0));
        Assert.Equal([text.Length], new LineBreaker().Find(text).ToArray().Select(b => b.Position));
    }

    /// <summary>
    /// Two flags are two clusters, with a break allowed between them (GB12 and GB13, LB30a); a lone surrogate is its own cluster.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text 🇯🇵🇫🇷 (four regional indicators), and "a" + U+D800 (a lone high surrogate) + "b".
    ///
    /// Steps:
    /// - Walk the clusters of each; find the line break opportunities of the flags.
    ///
    /// Expected:
    /// - The flags end at 4 and 8, with an opportunity at 4. The second text has clusters ending at 1, 2, and 3.
    /// </remarks>
    [Fact]
    public void Segmentation_TwoFlagsAndALoneSurrogate_AreSeparateClusters()
    {
        const string Flags = "🇯🇵🇫🇷";
        string lone = "a\uD800b";

        Assert.Equal(4, Graphemes.NextBoundary(Flags, 0));
        Assert.Equal(8, Graphemes.NextBoundary(Flags, 4));
        Assert.Equal([4, 8], new LineBreaker().Find(Flags).ToArray().Select(b => b.Position));
        Assert.Equal(1, Graphemes.NextBoundary(lone, 0));
        Assert.Equal(2, Graphemes.NextBoundary(lone, 1));
        Assert.Equal(3, Graphemes.NextBoundary(lone, 2));
        Assert.True(Graphemes.IsBoundary(lone, 2));
        Assert.False(Graphemes.IsBoundary(Flags, 2));
    }

    /// <summary>
    /// The generated tables are of the Unicode version that vendor/versions.lock pins.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The repository's vendor/versions.lock, found above the test's directory.
    ///
    /// Steps:
    /// - Read the version of its unicode-ucd line.
    ///
    /// Expected:
    /// - UnicodeData.Version is that version (18.0.0).
    /// </remarks>
    [Fact]
    public void UnicodeData_Version_IsTheLocked()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "XylemUI.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.NotNull(directory);
        string locked = File.ReadLines(Path.Combine(directory, "vendor", "versions.lock"))
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Single(fields => fields.Length >= 2 && fields[0] == "unicode-ucd")[1];

        Assert.Equal(UnicodeData.Version, locked);
        Assert.Equal("18.0.0", locked);
    }

    /// <summary>
    /// Finding the line break opportunities and walking the grapheme clusters of a text allocate nothing once the line breaker's
    /// arrays have grown.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A line breaker that has found the opportunities of the text once, and a text of Japanese, English, and emoji.
    ///
    /// Steps:
    /// - 1,000 times: find the opportunities, and walk every cluster.
    ///
    /// Expected:
    /// - 0 bytes on this thread.
    /// </remarks>
    [Fact]
    public void SteadyState_Segmentation_AllocatesNothing()
    {
        const string Text = "日本語のテキストと English words, 👨‍👩‍👧 and 🇯🇵 — mixed.";
        var breaker = new LineBreaker();
        int sum = 0;

        void Run(int index)
        {
            sum += breaker.Find(Text).Length;
            for (int position = 0; position < Text.Length; )
            {
                position = Graphemes.NextBoundary(Text, position);
            }
        }

        Run(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            Run(i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sum > 0);
    }
}
