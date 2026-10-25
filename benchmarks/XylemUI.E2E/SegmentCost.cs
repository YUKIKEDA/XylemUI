using System.Diagnostics;
using System.Text;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using XylemUI.Text.LineBreak;
using XylemUI.Text.Unicode;

namespace XylemUI.E2E;

// The cost of the grapheme clusters (UAX #29) and the line break opportunities (UAX #14) of a screen of text, 40 lines of 80
// characters (row T1, design §4.1): Japanese, English, and English with emoji, per character. And the split of the screen into
// font runs with the fonts of Windows, which checks each run's end against the grapheme clusters (PR #285's review). And the
// screen shaped once at 16 px, wrapped at 640 px (Pass1), laid out (Pass2), and wrapped again at 480 px (row T1b).
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
        using var provider = new DirectWriteFontProvider();
        var runs = new List<FontRun>();
        TextLanguage japanese = TextLanguage.FromTag("ja");
        foreach ((string name, string sentence) in Texts)
        {
            string text = Screen(sentence);
            report.Number($"{name}_line_break_ns_per_char_p50", Median(() => TimeLineBreaks(breaker, text)));
            report.Number($"{name}_grapheme_ns_per_char_p50", Median(() => TimeGraphemes(text)));
            report.Number($"{name}_split_us_p50", Median(() => TimeSplit(provider, text, japanese, runs)));
            report.Number($"{name}_runs", runs.Count);
            Wrapping(report, name, text, runs, japanese);
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

    private static void Wrapping(Report report, string name, string text, List<FontRun> runs, TextLanguage language)
    {
        using var shaper = new TextShaper();
        var breaker = new LineBreaker();
        var shaped = new ShapingResult();
        var lines = new LineBreakResult();
        var laid = new LineLayoutResult();
        FontRun[] fontRuns = [.. runs];
        report.Number($"{name}_shape_us_p50", Median(() => Time(() => shaped.Shape(text, fontRuns, 16, language, shaper, breaker))));
        report.Number($"{name}_wrap_us_p50", Median(() => Time(() => shaped.Wrap(640, shaper, lines))));
        report.Number($"{name}_lines", lines.Lines.Length);
        report.Number($"{name}_lay_us_p50", Median(() => Time(() => shaped.Lay(lines, shaper, laid))));
        report.Number($"{name}_rewrap_us_p50", Median(() => Time(() => shaped.Wrap(480, shaper, lines))));
    }

    private static double Time(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 20; i++)
        {
            action();
        }

        return Stopwatch.GetElapsedTime(start).TotalMicroseconds / 20;
    }

    private static double TimeSplit(DirectWriteFontProvider provider, string text, TextLanguage language, List<FontRun> runs)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 20; i++)
        {
            FontFallback.Split(provider, text, language, FontRequest.Default, runs);
        }

        return Stopwatch.GetElapsedTime(start).TotalMicroseconds / 20;
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
