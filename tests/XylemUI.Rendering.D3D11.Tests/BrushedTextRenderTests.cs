using System.Numerics;
using TerraFX.Interop.DirectX;
using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Rendering.D3D11.Tests;

// Text painted with a gradient on the D3D11 renderer (roadmap row R4b, design §5.0, "The API of lines, ellipses, arcs, and the
// paint of text"). As GlyphRenderTests and ClearTypeRenderTests compose glyphs on the CPU from the atlas's coverage, these
// compose them with the gradient's color at each pixel's center for the text's color, which also corrects the coverage; each
// channel within 2, as the gradient's dither adds at most a step of 8 bits (R4a). DirectWrite draws the glyphs.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class BrushedTextRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Paper = Color.FromRgb(0xF0F0F0);

    /// <summary>
    /// Each pixel of brushed text is the blend of the gradient's color there, over its box, by the coverage corrected for that
    /// color: for grayscale coverage with one alpha, for ClearType with one per channel; a translucent stop blends with its alpha.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer with gamma 1.8, grayscale contrast 1.0, and ClearType contrast 0.5; the test font; an atlas; a display list.
    ///
    /// Steps:
    /// - Draw paper (0, 0, 220, 100), then "H日H" at 32 px (pen 10.25, baseline 45) with a linear gradient at 90 degrees from
    ///   red to blue with alpha 160 over the box (10, 10, 180, 40), in the case's coverage. Read the pixels back before the present.
    ///
    /// Expected:
    /// - Every pixel of each glyph's box grown by 1 is the CPU composite with the gradient's color at the pixel's center, each
    ///   channel within 2; each glyph has ink.
    /// - The frame is two draws: paper, then the text with its own shader.
    /// </remarks>
    /// <param name="coverage">The coverage of the text.</param>
    [Theory]
    [InlineData(GlyphCoverage.Gray)]
    [InlineData(GlyphCoverage.Rgb)]
    public void Draw_BrushedText_IsTheBlendOfTheGradientAtEachPixel(GlyphCoverage coverage) =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("brushed text", 400, 300);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var text = new TextRenderingParameters(GlyphCoverage.Rgb, false, Gamma: 1.8f, GrayscaleContrast: 1.0f, ClearTypeContrast: 0.5f);
            renderer.TextRendering = text;
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            GradientBrush brush = GradientBrush.Linear(90, [new(0, Color.FromRgb(0xE02020)), new(1, new Color(0x20, 0x40, 0xE0, 160))]);
            var box = new Vector4(10, 10, 180, 40);

            list.FillRect(0, 0, 220, 100, Paper);
            _ = list.DrawText(shaper.Shape(font, "H日H", Japanese), font, 32, atlas, 10.25f, 45, brush, box.X, box.Y, box.Z, box.W, coverage);
            list.ResolveGlyphs(atlas);
            Painted[] glyphs = Snapshot(list, atlas, coverage);
            renderer.Draw(list, Clear, atlas);

            Assert.Equal(3, glyphs.Length);
            Assert.Equal(2, renderer.LastDrawCount);
            ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
            try
            {
                foreach (Painted glyph in glyphs)
                {
                    GlyphInstance at = glyph.At;
                    int inked = 0;
                    for (int y = at.Y - 1; y <= at.Y + at.Height; y++)
                    {
                        for (int x = at.X - 1; x <= at.X + at.Width; x++)
                        {
                            Vector4 paint = ShapeReference.Gradient(brush, new Vector2(x + 0.5f, y + 0.5f), box);
                            Color expected = Composite(glyphs, x, y, paint, coverage, text);
                            Color actual = renderer.ReadCapturedPixel(capture, x, y);
                            bool near =
                                Math.Abs(expected.R - actual.R) <= 2 && Math.Abs(expected.G - actual.G) <= 2 && Math.Abs(expected.B - actual.B) <= 2;
                            Assert.True(near, $"pixel ({x}, {y}) near {at}: expected {expected}, got {actual}");
                            bool inside = x >= at.X && y >= at.Y && x < at.X + at.Width && y < at.Y + at.Height;
                            if (inside && (Math.Abs(actual.R - Paper.R) > 50 || Math.Abs(actual.B - Paper.B) > 50))
                            {
                                inked++;
                            }
                        }
                    }

                    Assert.True(inked > 0, $"{at} drew no ink");
                }
            }
            finally
            {
                _ = capture->Release();
            }

            renderer.Present();
        });

    /// <summary>
    /// A frame of brushed text, grayscale and ClearType, allocates nothing once its glyphs are in the atlas.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, the test font, a shaper, an atlas, a gradient, and a display list.
    ///
    /// Steps:
    /// - 20 frames to warm up, then 100 frames: reset the list, write "H日" with the gradient in grayscale and in ClearType and a
    ///   line in it; resolve, draw, and present. Measure the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes, and the list did not grow.
    /// </remarks>
    [Fact]
    public void SteadyState_BrushedText_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("steady brushed text", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            GradientBrush brush = ShapeRenderTests.Brush("linear 45");
            ShapedGlyph[] glyphs = shaper.Shape(font, "H日", Japanese).ToArray();
            void Frame()
            {
                atlas.BeginFrame();
                list.Reset();
                _ = list.DrawText(glyphs, font, 24, atlas, 10, 40, brush, 0, 0, 200, 50);
                _ = list.DrawText(glyphs, font, 24, atlas, 10, 90, brush, 0, 50, 200, 50, GlyphCoverage.Rgb);
                list.DrawLine(10, 120, 200, 120, new Stroke(brush, 3));
                list.ResolveGlyphs(atlas);
                renderer.Draw(list, Clear, atlas);
                renderer.Present();
            }

            for (int i = 0; i < 20; i++)
            {
                Frame();
            }

            int growth = list.GrowthCount;
            PerformanceAssert.DoesNotAllocate(100, _ => Frame());
            Assert.Equal(growth, list.GrowthCount);
        });

    // The glyphs of the list in order, each with the coverage the atlas has for it now: one byte a pixel, or four for ClearType.
    private static Painted[] Snapshot(DisplayList list, GlyphAtlas atlas, GlyphCoverage coverage)
    {
        var glyphs = new List<Painted>();
        DisplayListReader reader = list.GetReader();
        int bytes = coverage == GlyphCoverage.Rgb ? 4 : 1;
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.FillRect)
            {
                _ = reader.ReadFillRect();
                continue;
            }

            GlyphRunCommand run = reader.ReadGlyphRun();
            Assert.Equal(0, run.Brush);
            foreach (GlyphInstance glyph in run.Glyphs)
            {
                ReadOnlySpan<byte> page = atlas.GetPagePixels(coverage, glyph.Page);
                int stride = atlas.GetPageSize(coverage) * bytes;
                byte[] cells = new byte[glyph.Width * glyph.Height * bytes];
                for (int y = 0; y < glyph.Height; y++)
                {
                    page.Slice(((glyph.AtlasY + y) * stride) + (glyph.AtlasX * bytes), glyph.Width * bytes)
                        .CopyTo(cells.AsSpan(y * glyph.Width * bytes));
                }

                glyphs.Add(new Painted(glyph, cells));
            }
        }

        return [.. glyphs];
    }

    // The pixel (x, y) over paper with every glyph that covers it blended in the gradient's color there (premultiplied `paint`).
    private static Color Composite(Painted[] glyphs, int x, int y, Vector4 paint, GlyphCoverage coverage, TextRenderingParameters text)
    {
        var pixel = new Vector3(Paper.R, Paper.G, Paper.B);
        Vector3 color = paint.W > 0 ? new Vector3(paint.X, paint.Y, paint.Z) / paint.W : Vector3.Zero;
        foreach (Painted glyph in glyphs)
        {
            (int u, int v) = (x - glyph.At.X, y - glyph.At.Y);
            if (u >= 0 && v >= 0 && u < glyph.At.Width && v < glyph.At.Height)
            {
                int at = (v * glyph.At.Width) + u;
                Vector3 alpha =
                    coverage == GlyphCoverage.Rgb
                        ? new Vector3(
                            Corrected(glyph.Coverage[at * 4] / 255f, color.X, text.ClearTypeContrast, Brightness(color), text),
                            Corrected(glyph.Coverage[(at * 4) + 1] / 255f, color.Y, text.ClearTypeContrast, Brightness(color), text),
                            Corrected(glyph.Coverage[(at * 4) + 2] / 255f, color.Z, text.ClearTypeContrast, Brightness(color), text)
                        )
                        : new Vector3(Corrected(glyph.Coverage[at] / 255f, Brightness(color), text.GrayscaleContrast, Brightness(color), text));
                alpha *= paint.W;
                pixel = Vector3.Round((color * 255 * alpha) + (pixel * (Vector3.One - alpha)));
            }
        }

        return new Color((byte)pixel.X, (byte)pixel.Y, (byte)pixel.Z);
    }

    private static float Brightness(Vector3 color) => (0.30f * color.X) + (0.59f * color.Y) + (0.11f * color.Z);

    // The shaders' correction of a coverage (QuadShaders, GPUI's apply_contrast_and_gamma_correction): the contrast from the
    // color's brightness, the gamma from `channel` (the brightness for grayscale, the channel's color for ClearType).
    private static float Corrected(float coverage, float channel, float contrast, float brightness, TextRenderingParameters text)
    {
        float k = contrast * Math.Clamp(4 * (0.75f - brightness), 0, 1);
        float contrasted = coverage * (k + 1) / ((coverage * k) + 1);
        Vector4 g = TextGamma.RatiosOf(text.Gamma);
        return Math.Clamp(contrasted + (contrasted * (1 - contrasted) * ((((g.X * channel) + g.Y) * contrasted) + ((g.Z * channel) + g.W))), 0, 1);
    }

    private sealed record Painted(GlyphInstance At, byte[] Coverage);
}
