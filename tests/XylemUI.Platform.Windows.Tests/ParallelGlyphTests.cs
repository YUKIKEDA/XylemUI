using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// The new glyphs of a frame rasterized by DirectWrite on 4 threads against 1 (design §5.0.3, "Glyphs seen for the first time",
// roadmap row T5). Microsoft documents nothing about DirectWrite and threads; Skia calls CreateGlyphRunAnalysis from several
// threads without a lock, and XylemUI also registers a loader per rasterizer on the shared factory, so the result is checked
// byte for byte, many times.
[Collection(HeavyWorkGroup.Name)]
public sealed class ParallelGlyphTests
{
    private const int Runs = 200;
    private const int Lines = 40;
    private const int CharactersPerLine = 80;
    private static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    private static readonly string YuGothicPath = Path.Combine(Fonts, "YuGothM.ttc");
    private static readonly string EmojiPath = Path.Combine(Fonts, "seguiemj.ttf");
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Ink = Color.FromRgb(0x202020);

    /// <summary>
    /// A screen of Japanese text and a line of emoji, resolved on 4 threads, gives the same pages and the same display list as on 1
    /// thread, every time.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Yu Gothic and Segoe UI Emoji, opened from their bytes (DirectWrite's in-memory loader; skipped where Windows lacks them),
    ///   and a shaper.
    /// - 40 lines of 80 characters drawn from the font's kana and kanji; every tenth line a heading of 20 px, the others 14 px, times
    ///   the scale; then a line of 40 emoji at 24 px times the scale.
    ///
    /// Steps:
    /// - Make an atlas with 1 thread and new DirectWrite rasterizers; draw the screen at the scales 1, 1.25, and 1.5, one frame
    ///   each, resolving the list's glyphs; after each frame, take the list's commands and glyph instances and a hash of every page
    ///   of every kind.
    /// - 200 times, do the same with a new atlas with 4 threads and new rasterizers. The run on 1 thread is not repeated, since it
    ///   is the same each time: a run took about 0.5 s on 1 thread and 0.3 s on 4 (Debug), and the 200 runs on 4 threads take about a
    ///   minute.
    ///
    /// Expected:
    /// - No exception; the 4-thread atlas started its workers each time; every frame of every run on 4 threads is the same as on 1
    ///   thread.
    /// </remarks>
    [Fact]
    public void ResolveGlyphs_FourThreads_SameAsOneEveryTime()
    {
        Assert.SkipUnless(File.Exists(YuGothicPath) && File.Exists(EmojiPath), "Windows has no Yu Gothic or Segoe UI Emoji.");
        using var gothic = new Font(File.ReadAllBytes(YuGothicPath));
        using var emoji = new Font(File.ReadAllBytes(EmojiPath));
        using var shaper = new TextShaper();
        string[] lines = Screen(gothic, shaper);
        string emojiLine = EmojiLine(emoji, shaper);
        List<string> expected = Draw(gothic, emoji, shaper, lines, emojiLine, threads: 1);
        for (int run = 0; run < Runs; run++)
        {
            AssertSame(expected, Draw(gothic, emoji, shaper, lines, emojiLine, threads: 4), $"run {run}");
        }
    }

    // The three frames of one atlas: per frame, its commands and glyph instances, then a hash of each page.
    private static List<string> Draw(Font gothic, Font emoji, TextShaper shaper, string[] lines, string emojiLine, int threads)
    {
        var frames = new List<string>();
        using var atlas = new GlyphAtlas(() => new DirectWriteGlyphRasterizer(), threads: threads);
        var list = new DisplayList();
        foreach (float scale in (float[])[1, 1.25f, 1.5f])
        {
            atlas.BeginFrame();
            atlas.ClearDirtyRegions();
            list.Reset();
            float baseline = 0;
            for (int line = 0; line < Lines; line++)
            {
                float size = (line % 10 == 0 ? 20 : 14) * scale;
                baseline += MathF.Round(1.5f * size);
                _ = list.DrawText(shaper.Shape(gothic, lines[line], Japanese), gothic, size, atlas, 4.25f, baseline, Ink);
            }

            _ = list.DrawText(shaper.Shape(emoji, emojiLine, Japanese), emoji, 24 * scale, atlas, 4.5f, baseline + (36 * scale), Ink);
            list.ResolveGlyphs(atlas);
            frames.Add(Read(list));
            frames.Add(Pages(atlas));
        }

        Assert.True(threads == 1 || atlas.RunningWorkers is not null, "The 4-thread atlas did not start its workers.");
        return frames;
    }

    private static string Read(DisplayList list)
    {
        var text = new StringBuilder();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.FillRect)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"{reader.ReadFillRect()}\n");
                continue;
            }

            GlyphRunCommand run = reader.ReadGlyphRun();
            _ = text.Append(CultureInfo.InvariantCulture, $"run {run.Coverage} {run.Color} {run.Glyphs.Length}:");
            foreach (GlyphInstance glyph in run.Glyphs)
            {
                _ = text.Append(
                    CultureInfo.InvariantCulture,
                    $" {glyph.X},{glyph.Y},{glyph.Width},{glyph.Height},{glyph.Page},{glyph.AtlasX},{glyph.AtlasY}"
                );
            }

            _ = text.Append('\n');
        }

        return text.ToString();
    }

    private static string Pages(GlyphAtlas atlas)
    {
        var text = new StringBuilder();
        foreach (GlyphCoverage coverage in (GlyphCoverage[])[GlyphCoverage.Gray, GlyphCoverage.Rgb, GlyphCoverage.Color])
        {
            for (int page = 0; page < atlas.GetPageCount(coverage); page++)
            {
                _ = text.Append(
                    CultureInfo.InvariantCulture,
                    $"{coverage} {page} {Convert.ToHexString(SHA256.HashData(atlas.GetPagePixels(coverage, page)))}\n"
                );
            }
        }

        return text.ToString();
    }

    private static void AssertSame(List<string> expected, List<string> actual, string what)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i] == actual[i], $"{what}: frame {(i / 2) + 1} differs in its {(i % 2 == 0 ? "display list" : "pages")}.");
        }
    }

    // 40 lines of 80 characters: hiragana, katakana, and the first 1,000 kanji from U+4E00 the font has, picked by a fixed
    // pseudo-random sequence, so that characters repeat across lines as in running text.
    private static string[] Screen(Font font, TextShaper shaper)
    {
        var pool = new List<string>();
        AddCharacters(font, shaper, pool, 0x3041, 0x3093, int.MaxValue);
        AddCharacters(font, shaper, pool, 0x30A1, 0x30F6, int.MaxValue);
        AddCharacters(font, shaper, pool, 0x4E00, 0x9FFF, 1_000);
        var lines = new string[Lines];
        long state = 1;
        for (int line = 0; line < Lines; line++)
        {
            var text = new StringBuilder();
            for (int i = 0; i < CharactersPerLine; i++)
            {
                state = ((state * 1_103_515_245) + 12_345) & 0x7FFF_FFFF;
                _ = text.Append(pool[(int)(state % pool.Count)]);
            }

            lines[line] = text.ToString();
        }

        return lines;
    }

    // 40 emoji from U+1F600 on that the font has.
    private static string EmojiLine(Font font, TextShaper shaper)
    {
        var pool = new List<string>();
        AddCharacters(font, shaper, pool, 0x1F600, 0x1F6FF, 40);
        return string.Concat(pool);
    }

    private static void AddCharacters(Font font, TextShaper shaper, List<string> pool, int first, int last, int most)
    {
        int added = 0;
        for (int code = first; code <= last && added < most; code++)
        {
            string character = char.ConvertFromUtf32(code);
            ReadOnlySpan<ShapedGlyph> shaped = shaper.Shape(font, character, Japanese);
            if (shaped.Length == 1 && shaped[0].GlyphId != 0)
            {
                pool.Add(character);
                added++;
            }
        }
    }
}
