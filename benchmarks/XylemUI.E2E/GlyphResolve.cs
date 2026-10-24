using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.E2E;

// The step that gets a frame's new glyphs ready (design §5.0.3, "Glyphs seen for the first time", roadmap row T5): the time of
// DisplayList.ResolveGlyphs for a frame with N new glyphs, on 1 to 4 threads. Each sample is a new process, and the fonts are
// opened from their bytes, so DirectWrite shares nothing with other processes and every glyph costs what a glyph never drawn at
// its size costs (docs/benchmarks/t4-glyphs.md). In each process a first frame at another size starts the workers and makes each
// thread's font face, as an app has done by the time it scrolls; the measured frame is the second.
internal static class GlyphResolve
{
    private const string YuGothic = @"C:\Windows\Fonts\YuGothM.ttc";
    private const string Emoji = @"C:\Windows\Fonts\seguiemj.ttf";

    // The parent: `runs` children for each case, and the median of each. The cases: 1 to 8 and 431 new glyphs with the atlas's
    // threshold; 2 to 4 new glyphs kept on the calling thread (`serial`), to see where the threshold belongs; 4 and 64 emoji.
    public static int Run(int runs)
    {
        using var report = new Report("glyph-resolve");
        report.Number("processors", Environment.ProcessorCount);
        foreach (int threads in (int[])[1, 2, 4])
        {
            foreach (int count in (int[])[1, 2, 3, 4, 5, 6, 8, 431])
            {
                Case(report, runs, $"glyphs_{count}_threads_{threads}", $"{count} {threads}");
            }

            foreach (int count in threads == 1 ? [] : (int[])[2, 3, 4])
            {
                Case(report, runs, $"glyphs_{count}_threads_{threads}_serial", $"{count} {threads} --from {count + 1}");
            }

            foreach (int count in (int[])[4, 64])
            {
                Case(report, runs, $"emoji_{count}_threads_{threads}", $"{count} {threads} --emoji");
            }
        }

        return 0;
    }

    private static void Case(Report report, int runs, string name, string arguments)
    {
        var times = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            using JsonDocument child = RunChild("glyph-resolve-child " + arguments);
            times.Add(child.RootElement.GetProperty("resolve_us").GetDouble());
        }

        times.Sort();
        report.Number(name + "_us", times[times.Count / 2]);
    }

    // One sample: `count` new glyphs (Yu Gothic at 16 px, or emoji at 32 px) resolved on `threads` threads, handed to the workers
    // from `from` new glyphs on (0: the atlas's threshold).
    public static int Child(int count, int threads, bool emoji, int from)
    {
        using Font font = emoji ? new Font(File.ReadAllBytes(Emoji)) : new Font(File.ReadAllBytes(YuGothic));
        using var shaper = new TextShaper();
        using var atlas = new GlyphAtlas(() => new DirectWriteGlyphRasterizer(), threads: threads);
        atlas.ParallelFrom = from > 0 ? from : GlyphAtlas.ParallelThreshold;
        var list = new DisplayList();
        TextLanguage japanese = TextLanguage.FromTag("ja");
        string text = TextOf(font, shaper, japanese, count, emoji);

        // The first frame, at another size, with enough glyphs for each of 4 threads to draw some, the same for every number of
        // threads.
        atlas.BeginFrame();
        list.Reset();
        _ = list.DrawText(shaper.Shape(font, TextOf(font, shaper, japanese, 32, emoji), japanese), font, 40, atlas, 0, 60, Color.FromRgb(0));
        list.ResolveGlyphs(atlas);

        atlas.BeginFrame();
        list.Reset();
        _ = list.DrawText(shaper.Shape(font, text, japanese), font, emoji ? 32 : 16, atlas, 0, 40, Color.FromRgb(0));
        long start = Stopwatch.GetTimestamp();
        list.ResolveGlyphs(atlas);
        double resolve = Stopwatch.GetElapsedTime(start).TotalMicroseconds;

        using var report = new Report("glyph-resolve-child");
        report.Number("resolve_us", resolve);
        report.Number("rasterized", atlas.RasterizeCount);
        report.Number("glyphs", list.GlyphCount);
        return 0;
    }

    // The steady state of the text of a frame: a screen of 40 lines of 80 characters of Yu Gothic at 150 % (headings of 30 px every
    // tenth line, 21 px otherwise), already shaped and in the atlas, written with DrawText and resolved (nothing is pending). The
    // nanoseconds per glyph: the median of 21 samples of 200 frames each. With `cold`, the frame scene's line instead, once per
    // frame after walking 64 MB to push the code and data out of the caches, as a frame calls it after drawing the frame before:
    // the median of 4,000 frames.
    public static int DrawTextCost(bool cold)
    {
        if (cold)
        {
            return ColdLine();
        }

        using var report = new Report("draw-text");
        using Font font = Font.FromFile(YuGothic);
        using var shaper = new TextShaper();
        using var atlas = new GlyphAtlas(() => new DirectWriteGlyphRasterizer());
        var list = new DisplayList();
        TextLanguage japanese = TextLanguage.FromTag("ja");
        string pool = TextOf(font, shaper, japanese, 1_000, emoji: false);
        var lines = new ShapedGlyph[40][];
        int glyphs = 0;
        for (int line = 0; line < lines.Length; line++)
        {
            var text = new StringBuilder();
            for (int i = 0; i < 80; i++)
            {
                _ = text.Append(pool[(((line * 80) + i) * 7) % pool.Length]);
            }

            lines[line] = shaper.Shape(font, text.ToString(), japanese).ToArray();
            glyphs += lines[line].Length;
        }

        void Frame()
        {
            atlas.BeginFrame();
            list.Reset();
            float baseline = 0;
            for (int line = 0; line < lines.Length; line++)
            {
                float size = line % 10 == 0 ? 30 : 21;
                baseline += MathF.Round(1.5f * size);
                _ = list.DrawText(lines[line], font, size, atlas, 6.25f, baseline, Color.FromRgb(0));
            }

            list.ResolveGlyphs(atlas);
        }

        for (int i = 0; i < 20; i++)
        {
            Frame();
        }

        var samples = new List<double>();
        for (int s = 0; s < 21; s++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 200; i++)
            {
                Frame();
            }

            samples.Add(Stopwatch.GetElapsedTime(start).TotalNanoseconds / (200.0 * glyphs));
        }

        samples.Sort();
        report.Number("glyphs", glyphs);
        report.Number("ns_per_glyph_p50", samples[samples.Count / 2]);
        report.Number("ns_per_glyph_min", samples[0]);
        report.Number("ns_per_glyph_max", samples[^1]);
        return 0;
    }

    private static int ColdLine()
    {
        using var report = new Report("draw-text-cold");
        using Font font = Font.FromFile(YuGothic);
        using var shaper = new TextShaper();
        using var atlas = new GlyphAtlas(() => new DirectWriteGlyphRasterizer());
        var list = new DisplayList();
        ShapedGlyph[] line = shaper.Shape(font, Scene.Line, TextLanguage.FromTag("ja")).ToArray();
        byte[] evict = new byte[64 << 20];
        long sink = 0;
        var times = new List<double>(4_000);
        long[] steps = new long[4];
        for (int frame = 0; frame < 4_100; frame++)
        {
            for (int i = 0; i < evict.Length; i += 64)
            {
                sink += evict[i]++;
            }

            long t0 = Stopwatch.GetTimestamp();
            atlas.BeginFrame();
            long t1 = Stopwatch.GetTimestamp();
            list.Reset();
            long t2 = Stopwatch.GetTimestamp();
            _ = list.DrawText(line, font, 24, atlas, 60, 30, Color.FromRgb(0xE8E8E8));
            long t3 = Stopwatch.GetTimestamp();
            list.ResolveGlyphs(atlas);
            long t4 = Stopwatch.GetTimestamp();
            double nanoseconds = Stopwatch.GetElapsedTime(t0, t4).TotalNanoseconds;
            if (frame >= 100)
            {
                steps[0] += t1 - t0;
                steps[1] += t2 - t1;
                steps[2] += t3 - t2;
                steps[3] += t4 - t3;
            }

            if (frame >= 100)
            {
                times.Add(nanoseconds);
            }
        }

        times.Sort();
        report.Number("glyphs", line.Length);
        report.Number("ns_p50", times[times.Count / 2]);
        report.Number("ns_p25", times[times.Count / 4]);
        report.Number("ns_p75", times[times.Count * 3 / 4]);
        report.Number("sink", sink & 1);
        report.Number("begin_ns", steps[0] * 1e9 / Stopwatch.Frequency / 4000);
        report.Number("reset_ns", steps[1] * 1e9 / Stopwatch.Frequency / 4000);
        report.Number("draw_ns", steps[2] * 1e9 / Stopwatch.Frequency / 4000);
        report.Number("resolve_ns", steps[3] * 1e9 / Stopwatch.Frequency / 4000);
        return 0;
    }

    // `count` characters of the font with distinct glyphs: kanji from U+4E00 on, or emoji from U+1F300 on.
    private static string TextOf(Font font, TextShaper shaper, TextLanguage language, int count, bool emoji)
    {
        var text = new StringBuilder();
        var glyphs = new HashSet<uint>();
        for (int code = emoji ? 0x1F300 : 0x4E00; glyphs.Count < count; code++)
        {
            if (code > (emoji ? 0x1FAFF : 0x9FFF))
            {
                throw new InvalidOperationException($"The font has fewer than {count} glyphs in the range.");
            }

            string character = char.ConvertFromUtf32(code);
            ReadOnlySpan<ShapedGlyph> shaped = shaper.Shape(font, character, language);
            if (shaped.Length == 1 && shaped[0].GlyphId != 0 && glyphs.Add(shaped[0].GlyphId))
            {
                _ = text.Append(character);
            }
        }

        return text.ToString();
    }

    private static JsonDocument RunChild(string arguments)
    {
        using var process = Process.Start(
            new ProcessStartInfo(Environment.ProcessPath!, arguments) { RedirectStandardOutput = true, UseShellExecute = false }
        )!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{arguments} exited with {process.ExitCode}: {output}");
        }

        return JsonDocument.Parse(output);
    }
}
