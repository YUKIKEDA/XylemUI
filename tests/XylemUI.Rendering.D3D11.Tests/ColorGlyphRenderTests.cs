using TerraFX.Interop.DirectX;
using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Rendering.D3D11.Tests;

// Color glyphs on the D3D11 renderer (roadmap row T6, design §5.0.3, "Color glyphs"): the BGRA atlas pages, the text's alpha
// applied once, and the same draw as grayscale text. The pixels are read back on WARP and compared with an image composed on the
// CPU from the atlas's own pixels: over the background, each glyph in the display list's order, premultiplied "over" with the
// glyph's color scaled by the text's alpha, out = color × α_text + under × (1 − alpha × α_text), rounded to 8 bits after each
// glyph; each channel within 1. Direct2D draws Segoe UI Emoji's glyphs; the grayscale glyph comes from the test font.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class ColorGlyphRenderTests(UiThreadFixture ui)
{
    private static readonly string EmojiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf");
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage English = TextLanguage.FromTag("en");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Paper = Color.FromRgb(0xF0F0F0);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);

    /// <summary>
    /// Every pixel of a color glyph, and the ring around it, is the glyph's premultiplied color scaled by the text's alpha over the
    /// background, the alpha applied once; the frame is one draw with the grayscale text.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer of at least 200 x 100, Segoe UI Emoji (skipped where Windows lacks it), the test font, an atlas, and a
    ///   display list.
    ///
    /// Steps:
    /// - Draw paper (0, 0, 200, 100), "H" of the test font in ink at 32 px (pen 10, baseline 50), "😀" at 32 px in ink (pen 40,
    ///   baseline 50), and "😀" in ink with alpha 128 (pen 100, baseline 50). Read the pixels back before the present.
    ///
    /// Expected:
    /// - Every pixel of each emoji's box grown by 1 is the CPU composite, each channel within 1, and some pixel of the opaque emoji
    ///   has red and blue more than 60 apart.
    /// - The atlas has a color page; the runs are Gray, Color, Color; the frame was one draw.
    /// </remarks>
    [Fact]
    public void Draw_ColorGlyph_IsItsColorTimesTheTextAlpha() =>
        ui.Invoke(() =>
        {
            Assert.SkipUnless(File.Exists(EmojiPath), "Windows has no Segoe UI Emoji (seguiemj.ttf).");
            using var window = new Win32Window("color glyphs", 400, 300);
            (int width, int height) = window.ClientSize;
            Assert.True(width >= 200 && height >= 100);
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using Font emoji = Font.FromFile(EmojiPath, 0);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            var halfInk = new Color(Ink.R, Ink.G, Ink.B, 128);

            list.FillRect(0, 0, 200, 100, Paper);
            _ = list.DrawText(shaper.Shape(font, "H", English), font, 32, atlas, 10, 50, Ink);
            _ = list.DrawText(shaper.Shape(emoji, "😀", English), emoji, 32, atlas, 40, 50, Ink);
            _ = list.DrawText(shaper.Shape(emoji, "😀", English), emoji, 32, atlas, 100, 50, halfInk);
            Painted[] glyphs = ColorGlyphs(list, atlas, out List<GlyphCoverage> kinds);
            renderer.Draw(list, Clear, atlas);

            Assert.Equal([GlyphCoverage.Gray, GlyphCoverage.Color, GlyphCoverage.Color], kinds);
            Assert.Equal(1, atlas.GetPageCount(GlyphCoverage.Color));
            Assert.Equal(1, renderer.LastDrawCount);
            Assert.Equal(2, glyphs.Length);
            ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
            try
            {
                bool colored = false;
                foreach (Painted glyph in glyphs)
                {
                    GlyphInstance at = glyph.At;
                    for (int y = at.Y - 1; y <= at.Y + at.Height; y++)
                    {
                        for (int x = at.X - 1; x <= at.X + at.Width; x++)
                        {
                            Color expected = Composite(glyphs, x, y);
                            Color actual = renderer.ReadCapturedPixel(capture, x, y);
                            bool near =
                                Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1;
                            Assert.True(near, $"pixel ({x}, {y}) near {at}: expected {expected}, got {actual}");
                            colored |= glyph.Alpha == 255 && Math.Abs(actual.R - actual.B) > 60;
                        }
                    }
                }

                Assert.True(colored, "No pixel of the opaque emoji has red and blue apart.");
            }
            finally
            {
                _ = capture->Release();
            }

            renderer.Present();
        });

    /// <summary>
    /// A steady-state frame with emoji and text allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, Segoe UI Emoji (skipped where Windows lacks it), the test font, an atlas, and a display list rewritten each
    ///   frame with a rectangle, "Hello" in the test font, and "😀🎉👍" in ink, at 24 px. Warmed up with 20 frames.
    ///
    /// Steps:
    /// - Run 100 frames, measuring the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes; no glyph was rasterized and the display list never grew.
    /// </remarks>
    [Fact]
    public void SteadyState_FrameWithColorGlyphs_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            Assert.SkipUnless(File.Exists(EmojiPath), "Windows has no Segoe UI Emoji (seguiemj.ttf).");
            using var window = new Win32Window("steady emoji", 400, 300);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using Font emoji = Font.FromFile(EmojiPath, 0);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            for (int i = 0; i < 20; i++)
            {
                Frame(i);
            }

            int rasterized = atlas.RasterizeCount;
            int growth = list.GrowthCount;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                Frame(i);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(rasterized, atlas.RasterizeCount);
            Assert.Equal(growth, list.GrowthCount);

            void Frame(int frame)
            {
                atlas.BeginFrame();
                list.Reset();
                list.FillRect(0, 0, 200, 100, Paper);
                float pen = list.DrawText(shaper.Shape(font, "Hello", English), font, 24, atlas, 10 + (frame % 3), 40, Ink);
                _ = list.DrawText(shaper.Shape(emoji, "😀🎉👍", English), emoji, 24, atlas, 20 + pen, 40, Ink);
                renderer.Render(list, Clear, atlas);
            }
        });

    // The color glyphs of the list in order, each with its BGRA pixels from the atlas, and the kinds of all the runs.
    private static Painted[] ColorGlyphs(DisplayList list, GlyphAtlas atlas, out List<GlyphCoverage> kinds)
    {
        var glyphs = new List<Painted>();
        kinds = [];
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.FillRect)
            {
                _ = reader.ReadFillRect();
                continue;
            }

            GlyphRunCommand run = reader.ReadGlyphRun();
            kinds.Add(run.Coverage);
            if (run.Coverage != GlyphCoverage.Color)
            {
                continue;
            }

            foreach (GlyphInstance glyph in run.Glyphs)
            {
                ReadOnlySpan<byte> page = atlas.GetPagePixels(GlyphCoverage.Color, glyph.Page);
                int stride = atlas.GetPageSize(GlyphCoverage.Color) * 4;
                byte[] pixels = new byte[glyph.Width * glyph.Height * 4];
                for (int y = 0; y < glyph.Height; y++)
                {
                    page.Slice(((glyph.AtlasY + y) * stride) + (glyph.AtlasX * 4), glyph.Width * 4).CopyTo(pixels.AsSpan(y * glyph.Width * 4));
                }

                glyphs.Add(new Painted(glyph, pixels, run.Color.A));
            }
        }

        return [.. glyphs];
    }

    private static Color Composite(Painted[] glyphs, int x, int y)
    {
        Color pixel = Paper;
        foreach (Painted glyph in glyphs)
        {
            (int u, int v) = (x - glyph.At.X, y - glyph.At.Y);
            if (u >= 0 && v >= 0 && u < glyph.At.Width && v < glyph.At.Height)
            {
                int at = ((v * glyph.At.Width) + u) * 4;
                double scale = glyph.Alpha / 255.0;
                double alpha = glyph.Pixels[at + 3] / 255.0 * scale;
                byte Channel(byte color, byte under) => (byte)Math.Round((color * scale) + (under * (1 - alpha)));
                pixel = new Color(Channel(glyph.Pixels[at + 2], pixel.R), Channel(glyph.Pixels[at + 1], pixel.G), Channel(glyph.Pixels[at], pixel.B));
            }
        }

        return pixel;
    }

    private sealed record Painted(GlyphInstance At, byte[] Pixels, byte Alpha);
}
