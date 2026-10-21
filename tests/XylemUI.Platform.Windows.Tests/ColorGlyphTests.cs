using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// Color glyphs drawn from their COLRv0 layers (design §4.6, §5.0.3, "Color glyphs", roadmap row T6), with Segoe UI Emoji, which
// Windows ships with COLRv0 layers (and COLRv1 paint trees, which are not drawn, row T9).
public sealed class ColorGlyphTests
{
    private static readonly string EmojiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf");
    private static readonly byte[] TestFontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly byte[] ColorFontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestColor.ttf"));

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
    ///   apart, and the opaque pixels hold at least 3 colors (the face, the eyes, the mouth); no channel exceeds the alpha
    ///   (premultiplied).
    /// - This also watches the font: the drawing relies on Segoe UI Emoji keeping COLRv0 layers. If an update of Windows drops them,
    ///   DirectWrite gives no color runs, the glyph is drawn as one outline in the text's color, and the colors fail here.
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
            var colors = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                (byte b, byte g, byte r, byte a) = (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
                Assert.True(b <= a && g <= a && r <= a, $"{size} px, pixel {i / 4} is not premultiplied: {r}, {g}, {b}, {a}");
                opaque |= a == 255;
                colored |= Math.Abs(r - b) > 60;
                if (a == 255)
                {
                    _ = colors.Add((r << 16) | (g << 8) | b);
                }
            }

            Assert.True(opaque, $"{size} px: no pixel is opaque.");
            Assert.True(colored || size < 48, $"{size} px: no pixel has red and blue apart.");
            Assert.True(colors.Count >= 3 || size < 48, $"{size} px: the opaque pixels hold {colors.Count} colors.");
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

    /// <summary>
    /// A COLRv0 layer in the text's color (palette index 0xFFFF) is read from the font: the glyph uses the text's color, its layer is
    /// drawn in the key's text color, and its other layer keeps the palette's color; a glyph without such a layer does not use it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The color test font (<c>XylemUITestColor.ttf</c>: "H" is "H" in the text's color under "I" in red; "O" is "O" in blue) and a
    ///   rasterizer.
    ///
    /// Steps:
    /// - Measure "H" and "O" at 48 px in Gray. Rasterize "H" as Color in opaque green and in opaque blue, and "O" as Color in green.
    ///
    /// Expected:
    /// - "H" is a color glyph that uses the text's color; "O" is a color glyph that does not.
    /// - "H" in green has opaque green pixels (the "H" layer) and opaque red pixels (the "I" layer over it); "H" in blue has opaque
    ///   blue pixels instead of the green ones, and the same red pixels; neither has a pixel of the other text color.
    /// - "O" in green has opaque blue pixels and no green one.
    /// </remarks>
    [Fact]
    public void Rasterize_LayerInTheTextColor_TakesTheTextColor()
    {
        using var font = new Font(ColorFontBytes);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        const uint H = 41;
        const uint O = 48;
        const uint Green = 0xFF00FF00;
        const uint Blue = 0xFFFF0000;

        GlyphBox h = rasterizer.Measure(new GlyphKey(font, H, 48, 0));
        GlyphBox o = rasterizer.Measure(new GlyphKey(font, O, 48, 0));
        HashSet<(int R, int G, int B)> hGreen = OpaqueColors(rasterizer, new GlyphKey(font, H, 48, 0, GlyphCoverage.Color, Green));
        HashSet<(int R, int G, int B)> hBlue = OpaqueColors(rasterizer, new GlyphKey(font, H, 48, 0, GlyphCoverage.Color, Blue));
        HashSet<(int R, int G, int B)> oGreen = OpaqueColors(rasterizer, new GlyphKey(font, O, 48, 0, GlyphCoverage.Color, Green));

        Assert.True(h.IsColor && h.UsesTextColor, $"{h}");
        Assert.True(o.IsColor && !o.UsesTextColor, $"{o}");
        Assert.Contains((0, 255, 0), hGreen);
        Assert.Contains((255, 0, 0), hGreen);
        Assert.DoesNotContain((0, 0, 255), hGreen);
        Assert.Contains((0, 0, 255), hBlue);
        Assert.Contains((255, 0, 0), hBlue);
        Assert.DoesNotContain((0, 255, 0), hBlue);
        Assert.Contains((0, 0, 255), oGreen);
        Assert.DoesNotContain((0, 255, 0), oGreen);
    }

    // The colors of the opaque pixels of a color glyph, as red, green, and blue.
    private static HashSet<(int R, int G, int B)> OpaqueColors(DirectWriteGlyphRasterizer rasterizer, GlyphKey key)
    {
        GlyphBox box = rasterizer.Measure(key);
        byte[] pixels = new byte[box.Width * box.Height * 4];
        rasterizer.Rasterize(key, pixels, box.Width * 4);
        var colors = new HashSet<(int R, int G, int B)>();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 255)
            {
                _ = colors.Add((pixels[i + 2], pixels[i + 1], pixels[i]));
            }
        }

        return colors;
    }

    private static uint GlyphOf(Font font, string text)
    {
        using var shaper = new TextShaper();
        return shaper.Shape(font, text, TextLanguage.FromTag("en"))[0].GlyphId;
    }
}
