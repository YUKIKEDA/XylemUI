using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// Color glyphs drawn by Direct2D (design §4.6, §5.0.3, "Color glyphs", roadmap row T6), with Segoe UI Emoji, which Windows 11
// ships as COLRv1 with COLRv0 layers too. The tests run on the test UI thread, in COM's single-threaded apartment, as an
// application's [STAThread] Main.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class ColorGlyphTests(UiThreadFixture ui)
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
    /// - Measure "😀" at 32 px, step 2, in Gray and in Rgb, with COLRv1 and with COLRv0 only; measure "H" of the test font.
    ///
    /// Expected:
    /// - "😀": a color glyph each time, without a layer in the text's color. "H": not a color glyph, with ink.
    /// </remarks>
    [Fact]
    public void Measure_Emoji_IsAColorGlyph() =>
        ui.Invoke(() =>
        {
            Assert.SkipUnless(File.Exists(EmojiPath), "Windows has no Segoe UI Emoji (seguiemj.ttf).");
            using Font emoji = Font.FromFile(EmojiPath, 0);
            using var test = new Font(TestFontBytes);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            uint grin = GlyphOf(emoji, "😀");

            foreach (bool paintTrees in (bool[])[true, false])
            {
                rasterizer.UsePaintTrees = paintTrees;
                foreach (GlyphCoverage coverage in (GlyphCoverage[])[GlyphCoverage.Gray, GlyphCoverage.Rgb])
                {
                    GlyphBox box = rasterizer.Measure(new GlyphKey(emoji, grin, 32, 2, coverage));
                    Assert.True(box.IsColor, $"{coverage}, paint trees {paintTrees}");
                    Assert.False(box.UsesTextColor, $"{coverage}, paint trees {paintTrees}");
                }
            }

            GlyphBox h = rasterizer.Measure(new GlyphKey(test, 41, 32, 0));
            Assert.False(h.IsColor);
            Assert.True(h.Width > 0);
        });

    /// <summary>
    /// An emoji is drawn in its colors, premultiplied: from its COLRv1 paint tree where Direct2D can draw paint trees, and from its
    /// COLRv0 layers when paint trees are turned off; the two pictures differ.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Segoe UI Emoji (skipped where Windows lacks it) and a rasterizer.
    ///
    /// Steps:
    /// - Measure and rasterize "😀" at 48 px as <see cref="GlyphCoverage.Color"/>, at 4 bytes a pixel; then again with paint trees
    ///   turned off.
    ///
    /// Expected:
    /// - Each box is about the size of the em (32 to 64 px wide and high) and its pixels have color: some are opaque, some pixel's red
    ///   and blue are more than 60 apart, and no channel exceeds the alpha (premultiplied).
    /// - The first drew a paint tree on this machine (Windows 11 26300), the second did not; the pictures differ.
    /// </remarks>
    [Fact]
    public void Rasterize_Emoji_IsDrawnInItsColors() =>
        ui.Invoke(() =>
        {
            Assert.SkipUnless(File.Exists(EmojiPath), "Windows has no Segoe UI Emoji (seguiemj.ttf).");
            using Font emoji = Font.FromFile(EmojiPath, 0);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var key = new GlyphKey(emoji, GlyphOf(emoji, "😀"), 48, 0, GlyphCoverage.Color);

            (GlyphBox paintBox, byte[] paint) = Draw(rasterizer, key);
            bool drewPaintTree = rasterizer.DrewPaintTree;
            rasterizer.UsePaintTrees = false;
            (GlyphBox layersBox, byte[] layers) = Draw(rasterizer, key);

            Assert.True(drewPaintTree);
            Assert.False(rasterizer.DrewPaintTree);
            AssertColored(paintBox, paint);
            AssertColored(layersBox, layers);
            Assert.False(paintBox == layersBox && paint.AsSpan().SequenceEqual(layers), "COLRv1 and COLRv0 drew the same picture.");
        });

    /// <summary>
    /// A thread of a .NET process that did not initialize COM is in the implicit multithreaded apartment, and draws a color glyph:
    /// the process has a multithreaded apartment, so WIC makes its factory there too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Segoe UI Emoji (skipped where Windows lacks it). A thread that undoes the COM initialization .NET made on it
    ///   (<c>CoUninitialize</c>), and initializes it again before it ends.
    ///
    /// Steps:
    /// - On that thread, read the apartment (<c>CoGetApartmentType</c>), then make a rasterizer and measure "😀" at 32 px as
    ///   <see cref="GlyphCoverage.Color"/>.
    ///
    /// Expected:
    /// - The apartment is the multithreaded one, implicitly; the glyph has ink.
    /// </remarks>
    [Fact]
    public void Measure_ColorGlyphOnAThreadWithoutCom_UsesTheImplicitApartment()
    {
        Assert.SkipUnless(File.Exists(EmojiPath), "Windows has no Segoe UI Emoji (seguiemj.ttf).");
        APTTYPE type = default;
        APTTYPEQUALIFIER qualifier = default;
        GlyphBox box = default;
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            CoUninitialize();
            try
            {
                APTTYPE t;
                APTTYPEQUALIFIER q;
                _ = CoGetApartmentType(&t, &q);
                (type, qualifier) = (t, q);
                using Font emoji = Font.FromFile(EmojiPath, 0);
                using var rasterizer = new DirectWriteGlyphRasterizer();
                box = rasterizer.Measure(new GlyphKey(emoji, GlyphOf(emoji, "😀"), 32, 0, GlyphCoverage.Color));
            }
            catch (Exception exception)
            {
                thrown = exception;
            }
            finally
            {
                _ = CoInitializeEx(null, (uint)COINIT.COINIT_MULTITHREADED);
            }
        });
        thread.Start();
        thread.Join();

        Assert.Null(thrown);
        Assert.Equal((APTTYPE.APTTYPE_MTA, APTTYPEQUALIFIER.APTTYPEQUALIFIER_IMPLICIT_MTA), (type, qualifier));
        Assert.True(box.Width > 0 && box.Height > 0, $"{box}");
    }

    private static uint GlyphOf(Font font, string text)
    {
        using var shaper = new TextShaper();
        return shaper.Shape(font, text, TextLanguage.FromTag("en"))[0].GlyphId;
    }

    private static (GlyphBox Box, byte[] Pixels) Draw(DirectWriteGlyphRasterizer rasterizer, GlyphKey key)
    {
        GlyphBox box = rasterizer.Measure(key);
        byte[] pixels = new byte[box.Width * box.Height * 4];
        rasterizer.Rasterize(key, pixels, box.Width * 4);
        return (box, pixels);
    }

    private static void AssertColored(GlyphBox box, byte[] pixels)
    {
        Assert.True(box.IsColor);
        Assert.InRange(box.Width, 32, 64);
        Assert.InRange(box.Height, 32, 64);
        bool opaque = false;
        bool colored = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            (byte b, byte g, byte r, byte a) = (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
            Assert.True(b <= a && g <= a && r <= a, $"pixel {i / 4} is not premultiplied: {r}, {g}, {b}, {a}");
            opaque |= a == 255;
            colored |= Math.Abs(r - b) > 60;
        }

        Assert.True(opaque, "No pixel is opaque.");
        Assert.True(colored, "No pixel has red and blue apart.");
    }
}
