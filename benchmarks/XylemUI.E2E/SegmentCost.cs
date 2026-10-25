using System.Diagnostics;
using System.Text;
using XylemUI.Text.LineBreak;
using XylemUI.Text.Unicode;

namespace XylemUI.E2E;

// The cost of the grapheme clusters (UAX #29) and the line break opportunities (UAX #14) of a screen of text, 40 lines of 80
// characters (row T1, design §4.1): Japanese, English, and English with emoji, per character.
internal static class SegmentCost
{
    private const int ScreenCharacters = 40 * 80;

    private static readonly (string Name, string Sentence)[] Texts =
    [
        (
            "japanese",
            "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。何でも薄暗いじめじめした所でニャーニャー泣いていた事だけは記憶している。"
        ),
        ("english", "The quick brown fox jumps over the lazy dog, and then (quite suddenly) runs off into the woods at 9:45 p.m. "),
        ("emoji", "Party time 🎉 with friends 👨‍👩‍👧 and flags 🇯🇵🇫🇷 — thumbs up 👍🏽 and hearts ❤️ for everyone! "),
    ];

    public static int Run()
    {
        using var report = new Report("segment");
        var breaker = new LineBreaker();
        foreach ((string name, string sentence) in Texts)
        {
            string text = Screen(sentence);
            report.Number($"{name}_line_break_ns_per_char_p50", Median(() => TimeLineBreaks(breaker, text)));
            report.Number($"{name}_grapheme_ns_per_char_p50", Median(() => TimeGraphemes(text)));
        }

        return 0;
    }

    private static string Screen(string sentence)
    {
        var text = new StringBuilder(ScreenCharacters + sentence.Length);
        while (text.Length < ScreenCharacters)
        {
            _ = text.Append(sentence);
        }

        return text.ToString();
    }

    private static double TimeLineBreaks(LineBreaker breaker, string text)
    {
        int found = 0;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 100; i++)
        {
            found += breaker.Find(text).Length;
        }

        double ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds / (100.0 * text.Length);
        return found > 0 ? ns : -1;
    }

    private static double TimeGraphemes(string text)
    {
        int clusters = 0;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 100; i++)
        {
            for (int position = 0; position < text.Length; clusters++)
            {
                position = Graphemes.NextBoundary(text, position);
            }
        }

        double ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds / (100.0 * text.Length);
        return clusters > 0 ? ns : -1;
    }

    private static double Median(Func<double> sample)
    {
        _ = sample();
        var values = new List<double>();
        for (int i = 0; i < 21; i++)
        {
            values.Add(sample());
        }

        values.Sort();
        return values[10];
    }
}
