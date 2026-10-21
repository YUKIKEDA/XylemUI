using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// Color glyphs drawn from their COLRv0 layers (design §4.6, §5.0.3, "Color glyphs", roadmap row T6), with Segoe UI Emoji, which
// Windows ships with COLRv0 layers (and COLRv1 paint trees, which are not drawn, row T9).
public sealed class ColorGlyphTests
{
    private static readonly string EmojiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf");
    private static readonly byte[] TestFontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));

    /// <summary>
    /// An emoji asked for as grayscale or ClearType is a color glyph without a layer in the text's color; a glyph of a font without
    /// color is not a color glyph.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Segoe UI Emoji (skipped where Windows lacks it), the test font, and a rasterizer.
    ///
    /// Steps:
    /// - Measure "😀" at 32 px, step 2, in Gray and in Rgb; measure "H" of the test font.
    ///
    /// Expected:
    /// - "😀": a color glyph each time, without a layer in the text's color. "H": not a color glyph, with ink.
    /// </remarks>
    [Fact]
    public void Measure_Emoji_IsAColorGlyph()
    {
        Assert.SkipUnless(File.Exists(EmojiPath), "Windows has no Segoe UI Emoji (seguiemj.ttf).");
        using Font emoji = Font.FromFile(EmojiPath, 0);
        using var test = new Font(TestFontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        uint grin = GlyphOf(emoji, "😀");

        foreach (GlyphCoverage coverage in (GlyphCoverage[])[GlyphCoverage.Gray, GlyphCoverage.Rgb])
        {
            GlyphBox box = rasterizer.Measure(new GlyphKey(emoji, grin, 32, 2, coverage));
            Assert.True(box.IsColor, $"{coverage}");
            Assert.False(box.UsesTextColor, $"{coverage}");
        }

        GlyphBox h = rasterizer.Measure(new GlyphKey(test, 41, 32, 0));
        Assert.False(h.IsColor);
        Assert.True(h.Width > 0);
    }

    /// <summary>
    /// An emoji is drawn in its colors, premultiplied, about the size of the em; a small emoji drawn first is drawn too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Segoe UI Emoji (skipped where Windows lacks it).
    ///
    /// Steps:
    /// - For 12, 17.3, and 48 px, a new rasterizer measures and rasterizes "😀" as <see cref="GlyphCoverage.Color"/>, at 4 bytes a
    ///   pixel.
    ///
    /// Expected:
    /// - Each box is between 0.7 and 1.3 em wide and high; some pixels are opaque; at 48 px some pixel's red and blue are more than 60
    ///   apart; no channel exceeds the alpha (premultiplied).
    /// </remarks>
    [Fact]
    public void Rasterize_Emoji_IsDrawnInItsColors()
    {
        Assert.SkipUnless(File.Exists(EmojiPath), "Windows has no Segoe UI Emoji (seguiemj.ttf).");
        using Font emoji = Font.FromFile(EmojiPath, 0);
        uint grin = GlyphOf(emoji, "😀");
        foreach (float size in (float[])[12, 17.3f, 48])
        {
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var key = new GlyphKey(emoji, grin, size, 0, GlyphCoverage.Color);
            GlyphBox box = rasterizer.Measure(key);
            byte[] pixels = new byte[box.Width * box.Height * 4];
            rasterizer.Rasterize(key, pixels, box.Width * 4);

            Assert.True(box.IsColor);
            Assert.InRange(box.Width, size * 0.7f, size * 1.3f);
            Assert.InRange(box.Height, size * 0.7f, size * 1.3f);
            bool opaque = false;
            bool colored = false;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                (byte b, byte g, byte r, byte a) = (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
                Assert.True(b <= a && g <= a && r <= a, $"{size} px, pixel {i / 4} is not premultiplied: {r}, {g}, {b}, {a}");
                opaque |= a == 255;
                colored |= Math.Abs(r - b) > 60;
            }

            Assert.True(opaque, $"{size} px: no pixel is opaque.");
            Assert.True(colored || size < 48, $"{size} px: no pixel has red and blue apart.");
        }
    }

    /// <summary>
    /// A glyph that is not a color glyph, asked for as <see cref="GlyphCoverage.Color"/>, is its outline in the key's text color.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and a rasterizer.
    ///
    /// Steps:
    /// - Rasterize "H" at 32 px as Color with the text color opaque red (255, 0, 0).
    ///
    /// Expected:
    /// - Every pixel is red at its alpha (blue and green 0, red equal to alpha), and some are opaque.
    /// </remarks>
    [Fact]
    public void Rasterize_GlyphWithoutColor_IsItsOutlineInTheTextColor()
    {
        using var font = new Font(TestFontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        var key = new GlyphKey(font, 41, 32, 0, GlyphCoverage.Color, TextColor: 0xFF0000FF);

        GlyphBox box = rasterizer.Measure(key);
        byte[] pixels = new byte[box.Width * box.Height * 4];
        rasterizer.Rasterize(key, pixels, box.Width * 4);

        Assert.True(box.Width > 0 && box.Height > 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Assert.Equal([0, 0, pixels[i + 3], pixels[i + 3]], pixels.AsSpan(i, 4).ToArray());
        }

        Assert.Contains(pixels.Where((_, i) => i % 4 == 3), alpha => alpha == 255);
    }

    private static uint GlyphOf(Font font, string text)
    {
        using var shaper = new TextShaper();
        return shaper.Shape(font, text, TextLanguage.FromTag("en"))[0].GlyphId;
    }
}
