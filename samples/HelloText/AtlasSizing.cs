using System.Diagnostics;
using System.Globalization;
using XylemUI.Platform.Windows;
using XylemUI.Text;

// The measurement that picks the atlas's page size, plot size, and page limit (roadmap row S8b, design §5.0.3): a Japanese
// screen scrolled one line per frame through a long text, for each configuration. A screen is 40 lines of 80 characters; every
// tenth line is a heading of 20 px, the others body text of 14 px, both times the display scale and not rounded (150 % makes them
// 30 and 21 px, 125 % 25 and 17.5 px). The text is shaped line by line and every glyph is put in the atlas at the subpixel step
// of its pen, each line starting on a whole pixel, as DisplayList.DrawText does (roadmap row T4). DirectWrite draws the glyphs.
//
//   HelloText --atlas-sizing <font file> [face index] [scale] [lines per frame] [text file] [--cleartype]
//
// With --cleartype the glyphs are ClearType (roadmap row T2): 4 bytes a pixel, so a page is 4 × pageSize² bytes.
//
// One line per frame is a slow scroll; 40 lines per frame (a screen) is the worst case, every frame a new screen.
//
// It prints, per configuration: the GPU memory of the pages at the end (pageSize² bytes per page, 4 times that for ClearType) and
// the most pages at once;
// the glyphs rasterized in the first frame; and, over the scrolling frames, the rasterizations and evictions per frame (median,
// p95, max), the frames that rasterized more than 50 glyphs, and the time of the whole run.
internal static class AtlasSizing
{
    private const int LinesPerScreen = 40;
    private const int CharactersPerLine = 80;

    public static int Run(string fontPath, int faceIndex, double scale, int step, string textPath, GlyphCoverage coverage)
    {
        string[] lines = Lines(File.ReadAllText(textPath));
        using var font = new Font(File.ReadAllBytes(fontPath), faceIndex);
        using var shaper = new TextShaper();
        using var rasterizer = new DirectWriteGlyphRasterizer();
        TextLanguage japanese = TextLanguage.FromTag("ja");
        Console.WriteLine(
            $"atlas sizing: {Path.GetFileName(fontPath)} face {faceIndex}, scale {scale}, {coverage}, {step} lines per frame, {lines.Length} lines of {CharactersPerLine} from "
                + $"{Path.GetFileName(textPath)}, {LinesPerScreen} lines per screen"
        );
        Console.WriteLine("page plot max | MB end, pages max | first | raster/frame med p95 max | evict/frame med p95 max | frames > 50 | ms");
        float body = (float)(14 * scale);
        float heading = (float)(20 * scale);
        foreach ((int page, int plot, int maxPages) in Configurations())
        {
            var atlas = new GlyphAtlas(rasterizer, page, plot, maxPages);
            var rasterized = new List<int>();
            var evicted = new List<int>();
            int first = 0;
            int mostPages = 0;
            long start = Stopwatch.GetTimestamp();
            for (int top = 0; top + LinesPerScreen <= lines.Length; top += step)
            {
                atlas.BeginFrame();
                atlas.ClearDirtyRegions();
                int rasterBefore = atlas.RasterizeCount;
                int evictBefore = atlas.EvictionCount;
                for (int line = top; line < top + LinesPerScreen; line++)
                {
                    float size = line % 10 == 0 ? heading : body;
                    float unit = size / font.UnitsPerEm;
                    long pen = 0;
                    foreach (ShapedGlyph glyph in shaper.Shape(font, lines[line], japanese))
                    {
                        _ = GlyphPosition.Snap((pen + glyph.XOffset) * unit, out int subpixel);
                        _ = atlas.TryGet(new GlyphKey(font, glyph.GlyphId, size, subpixel, coverage), out _);
                        pen += glyph.XAdvance;
                    }
                }

                mostPages = Math.Max(mostPages, atlas.GetPageCount(coverage));
                if (top == 0)
                {
                    first = atlas.RasterizeCount - rasterBefore;
                    continue;
                }

                rasterized.Add(atlas.RasterizeCount - rasterBefore);
                evicted.Add(atlas.EvictionCount - evictBefore);
            }

            double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            int heavy = rasterized.Count(count => count > 50);
            double megabytes = (double)atlas.GetPageCount(coverage) * page * page * GlyphAtlas.BytesPerPixel(coverage) / (1024 * 1024);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{P(page, 4)} {P(plot, 4)} {P(maxPages, 3)} | {P(megabytes.ToString("F1", CultureInfo.InvariantCulture), 6)} {P(mostPages, 3)} | {P(first, 5)} | {Stats(rasterized)} | {Stats(evicted)} | {P(heavy, 5)} | {P(milliseconds.ToString("F0", CultureInfo.InvariantCulture), 6)}"
                )
            );
        }

        return 0;
    }

    private static IEnumerable<(int Page, int Plot, int MaxPages)> Configurations()
    {
        foreach (int maxPages in new[] { 1, 2, 4 })
        {
            yield return (1024, 128, maxPages);
            yield return (1024, 256, maxPages);
            yield return (1024, 512, maxPages);
        }

        yield return (2048, 256, 1);
        yield return (2048, 512, 1);
        yield return (512, 128, 4);
        yield return (512, 256, 4);
    }

    // The text cut into lines of a fixed length, with line breaks and runs of spaces taken out.
    private static string[] Lines(string text)
    {
        var flat = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c) || (flat.Length > 0 && flat[^1] != ' '))
            {
                _ = flat.Append(char.IsWhiteSpace(c) ? ' ' : c);
            }
        }

        var lines = new List<string>();
        int start = 0;
        while (start + CharactersPerLine <= flat.Length)
        {
            // A line does not end between the two halves of a surrogate pair.
            int length = char.IsHighSurrogate(flat[start + CharactersPerLine - 1]) ? CharactersPerLine - 1 : CharactersPerLine;
            lines.Add(flat.ToString(start, length));
            start += length;
        }

        return [.. lines];
    }

    private static string Stats(List<int> values)
    {
        var sorted = values.Order().ToList();
        int median = sorted[sorted.Count / 2];
        int p95 = sorted[(int)(sorted.Count * 0.95)];
        return string.Create(CultureInfo.InvariantCulture, $"{P(median, 3)} {P(p95, 3)} {P(sorted[^1], 4)}");
    }

    private static string P(object value, int width) => (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).PadLeft(width);
}
