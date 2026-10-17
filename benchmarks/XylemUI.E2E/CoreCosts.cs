using System.Diagnostics;
using System.Reflection;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// Costs of Core paths the roadmap row M1a names, without a window:
//   resize-text: a window resize of a tree of 10,101 nodes (root column, 100 rows of 100 leaves) whose leaves measure a short
//                Japanese text, laid out again at alternating sizes. Once with leaves that shape their text at each measure
//                (no text cache yet), once with leaves that keep the shaped width (a cache hit, design §11.3).
//   glyph-refused: a glyph refused at the atlas's page limit is rasterized again at each request (GlyphAtlas.RefusedCount).
//   dependency-order: each set changes the source of two computed chains that an effect reads; once in the same order every
//                time, once in alternating order ([C, D] then [D, C]), which relinks the effect's edges and makes the chain it
//                stops reading leave its sources and register again. Chains of 1, 10, and 100.
internal static class CoreCosts
{
    private const string Word = "日本語のテキスト";

    public static int ResizeText(int passes)
    {
        UiThread.Initialize();
        using var report = new Report("resize-text");
        using Font font = OpenFont();
        using var shaper = new TextShaper();
        TextLanguage japanese = TextLanguage.FromTag("ja");
        float scale = 14f / font.UnitsPerEm;
        MeasureFunc shaping = (_, _) =>
        {
            long advance = 0;
            foreach (ShapedGlyph glyph in shaper.Shape(font, Word, japanese))
            {
                advance += glyph.XAdvance;
            }

            return MeasureResult.Of(advance * scale, 20);
        };
        float cached = shaping(OptionalSize.None, AvailableSize.MaxContent).Size.Width;
        MeasureFunc cacheHit = (_, _) => MeasureResult.Of(cached, 20);
        foreach ((string name, MeasureFunc measure) in new[] { ("shaping", shaping), ("cached", cacheHit) })
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
            for (int r = 0; r < 100; r++)
            {
                NodeHandle row = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row });
                tree.AppendChild(root, row);
                for (int c = 0; c < 100; c++)
                {
                    tree.AppendChild(row, tree.CreateNode(new LayoutStyle(), measure));
                }
            }

            var times = new List<double>(passes);
            for (int i = 0; i < passes + 5; i++)
            {
                var size = new Size(i % 2 == 0 ? 1_600 : 1_200, i % 2 == 0 ? 2_000 : 1_800);
                long start = Stopwatch.GetTimestamp();
                Invalidation.NotifyWindowResized(tree, root, size);
                LayoutEngine.RunLayoutPass(tree);
                if (i >= 5)
                {
                    times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }

            report.Series($"resize_{name}_ms", times);
        }

        report.Number("nodes", 10_101);
        return 0;
    }

    public static int GlyphRefused(int requests)
    {
        using var report = new Report("glyph-refused");
        using Font font = OpenFont();
        using var shaper = new TextShaper();
        uint[] glyphs = [.. shaper.Shape(font, Word, TextLanguage.FromTag("ja")).ToArray().Select(g => g.GlyphId).Distinct()];

        // 64 px pages of 32 px plots, at most 1 page (a limit of 2): the 8 Japanese glyphs at 24 px fill both pages; "W" is refused.
        var atlas = new GlyphAtlas(pageSize: 64, plotSize: 32, maxPages: 1);
        foreach (uint glyph in glyphs)
        {
            _ = atlas.TryGet(font, glyph, 24, out _);
        }

        var cachedAtlas = new GlyphAtlas();
        _ = cachedAtlas.TryGet(font, 56, 24, out _);
        report.Number("refused_us", Time(requests, () => atlas.TryGet(font, 56, 24, out _)));
        report.Number("cached_us", Time(requests, () => cachedAtlas.TryGet(font, 56, 24, out _)));
        report.Number("refused_count", atlas.RefusedCount);
        return 0;
    }

    public static int DependencyOrder(int sets)
    {
        UiThread.Initialize();
        using var report = new Report("dependency-order");
        foreach (int length in new[] { 1, 10, 100 })
        {
            Signal<int> source = Signal.Create(0);
            Computed<int> c = Chain(source, length);
            Computed<int> d = Chain(source, length);
            Signal<bool> order = Signal.Create(false);
            using Effect effect = Effect.Create(() =>
            {
                _ = order.Get() ? d.Get() + c.Get() : c.Get() + d.Get();
            });
            int i = 0;
            report.Number($"same_order_chain{length}_us", Time(sets, () => source.Set(++i)));
            report.Number(
                $"alternating_order_chain{length}_us",
                Time(
                    sets,
                    () =>
                        ReactiveRuntime.Batch(() =>
                        {
                            source.Set(++i);
                            order.Set(!order.Get());
                        })
                )
            );
        }

        return 0;
    }

    private static Computed<int> Chain(Signal<int> source, int length)
    {
        Computed<int> last = Computed.Create(() => source.Get() + 1);
        for (int i = 1; i < length; i++)
        {
            Computed<int> previous = last;
            last = Computed.Create(() => previous.Get() + 1);
        }

        return last;
    }

    // Microseconds per call: a warm-up of a tenth, then the median of 9 samples of `count` calls each.
    private static double Time(int count, Action call)
    {
        for (int i = 0; i < count / 10; i++)
        {
            call();
        }

        var samples = new List<double>();
        for (int s = 0; s < 9; s++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                call();
            }

            samples.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds / count);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static Font OpenFont()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XylemUITestSans.ttf")!;
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return new Font(bytes);
    }
}
