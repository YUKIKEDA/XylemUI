using TerraFX.Interop.DirectX;
using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Rendering.D3D11.Tests;

// ClearType glyph runs on the D3D11 renderer (roadmap row T2, design §5.0.3, "ClearType"): the RGBA atlas pages, the dual-source
// blend, the correction of each channel, the BGR swap, and the split of the frame into draws where the blend state changes. The
// pixels are read back and compared with an image composed on the CPU from the atlas's own coverage: over the background, each
// glyph in the display list's order, each channel c out = color_c × α_c + under_c × (1 − α_c) with α_c = color alpha × the
// channel's coverage corrected for the color (Corrected3, GPUI's apply_contrast_and_gamma_correction3), rounded to 8 bits after
// each glyph as the 8-bit target does; each channel within 1. DirectWrite draws the glyphs.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class ClearTypeRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color HalfRed = new(0xCC, 0x33, 0x33, 128);

    /// <summary>
    /// Every pixel of the ClearType glyphs, and the ring around each, is each channel of the text color blended over the background
    /// by that channel's corrected coverage; the coverage is swapped on a BGR monitor; a glyph that arrives in a later frame is
    /// uploaded into its region of the RGBA page.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A renderer on WARP or the default device, at feature level 10.0 or the highest, with the text rendering of the case
    ///   (gamma 1.8, grayscale contrast 1.0, ClearType contrast 0.5, RGB or BGR); the test font; an atlas; a display list.
    ///
    /// Steps:
    /// - Frame 1: the background (0, 0, 200, 100), then "H" in the ink at 32 px in ClearType (pen 10.25, baseline 40). Draw.
    /// - Frame 2: the background, "H日" in the ink (pen 10.25, baseline 40), and "H" in red with alpha 128 (pen 120.5, baseline
    ///   80), both in ClearType. Draw, and read the pixels back before the present.
    ///
    /// Expected:
    /// - The device is WARP when asked, and of feature level 10.0 when asked.
    /// - Every pixel of each glyph's box grown by 1 is the CPU composite, each channel within 1; each glyph has ink.
    /// - The atlas has an RGBA page and no grayscale one; frame 2 uploaded at least one region ("日").
    /// - Some pixel of the first "H" has red and blue more than 20 apart (the blend is per channel).
    /// </remarks>
    /// <param name="useWarp">Whether to use WARP.</param>
    /// <param name="featureLevel10">Whether to ask for feature level 10.0 only.</param>
    /// <param name="isBgr">Whether the monitor's subpixels are BGR.</param>
    /// <param name="ink">The text color, 0xRRGGBB.</param>
    /// <param name="background">The background, 0xRRGGBB.</param>
    [Theory]
    [InlineData(true, false, false, 0x000000, 0xFFFFFF)]
    [InlineData(true, false, false, 0xFFFFFF, 0x000000)]
    [InlineData(true, false, false, 0x2040C0, 0xF0F0F0)]
    [InlineData(true, false, true, 0x2040C0, 0xF0F0F0)]
    [InlineData(true, true, false, 0x000000, 0xFFFFFF)]
    [InlineData(true, true, true, 0xE8E8E8, 0x202020)]
    [InlineData(false, false, false, 0x2040C0, 0xF0F0F0)]
    [InlineData(false, true, false, 0x000000, 0xFFFFFF)]
    public void Draw_ClearTypeRun_EachChannelIsTheBlendOfItsCoverage(bool useWarp, bool featureLevel10, bool isBgr, int ink, int background) =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("cleartype", 400, 300);
            (int width, int height) = window.ClientSize;
            Assert.True(width >= 200 && height >= 100);
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp, featureLevel10);
            var text = new TextRenderingParameters(GlyphCoverage.Rgb, isBgr, Gamma: 1.8f, GrayscaleContrast: 1.0f, ClearTypeContrast: 0.5f);
            renderer.TextRendering = text;
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            Color inkColor = Color.FromRgb((uint)ink);
            Color paper = Color.FromRgb((uint)background);

            list.FillRect(0, 0, 200, 100, paper);
            _ = list.DrawText(shaper.Shape(font, "H", Japanese), font, 32, atlas, 10.25f, 40, inkColor, GlyphCoverage.Rgb);
            list.ResolveGlyphs(atlas);
            renderer.Draw(list, Clear, atlas);
            renderer.Present();
            int regions = renderer.AtlasRegionUploads;
            atlas.BeginFrame();
            list.Reset();
            list.FillRect(0, 0, 200, 100, paper);
            _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 32, atlas, 10.25f, 40, inkColor, GlyphCoverage.Rgb);
            _ = list.DrawText(shaper.Shape(font, "H", Japanese), font, 32, atlas, 120.5f, 80, HalfRed, GlyphCoverage.Rgb);
            list.ResolveGlyphs(atlas);
            Painted[] glyphs = Snapshot(list, atlas);
            renderer.Draw(list, Clear, atlas);

            if (useWarp)
            {
                Assert.True(renderer.IsWarp);
            }

            if (featureLevel10)
            {
                Assert.Equal(D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0, renderer.FeatureLevel);
            }

            Assert.Equal(3, glyphs.Length);
            Assert.Equal((1, 0), (atlas.GetPageCount(GlyphCoverage.Rgb), atlas.GetPageCount(GlyphCoverage.Gray)));
            Assert.True(renderer.AtlasRegionUploads > regions);
            ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
            try
            {
                AssertComposite(renderer, capture, glyphs, paper, text);
                AssertColored(renderer, capture, glyphs[0].At);
            }
            finally
            {
                _ = capture->Release();
            }

            renderer.Present();
        });

    /// <summary>
    /// The frame is split into draws only where the blend state changes: rectangles and grayscale glyphs are one draw, a run of
    /// ClearType glyphs (or several in a row) another.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, an empty atlas, and a display list.
    ///
    /// Steps:
    /// - Write the commands of the case: R a rectangle, G a grayscale run of one glyph, C a ClearType run of one glyph (no ink; the
    ///   draw is counted, not looked at). Draw.
    ///
    /// Expected:
    /// - The number of draws of the case.
    /// </remarks>
    /// <param name="commands">The commands, in order.</param>
    /// <param name="draws">The number of draws.</param>
    [Theory]
    [InlineData("R", 1)]
    [InlineData("RGRG", 1)]
    [InlineData("C", 1)]
    [InlineData("CCC", 1)]
    [InlineData("RGCCGR", 3)]
    [InlineData("GCGC", 4)]
    [InlineData("CRC", 3)]
    public void Draw_BlendStateChanges_SplitTheDraws(string commands, int draws) =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("draws", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            GlyphInstance[] glyph = [new GlyphInstance(0, 0, 1, 1, 0, 0, 0)];
            foreach (char command in commands)
            {
                switch (command)
                {
                    case 'R':
                        list.FillRect(0, 0, 10, 10, HalfRed);
                        break;
                    case 'G':
                        list.GlyphRun(glyph, HalfRed);
                        break;
                    default:
                        list.GlyphRun(glyph, HalfRed, GlyphCoverage.Rgb);
                        break;
                }
            }

            renderer.Draw(list, Clear, atlas);

            Assert.Equal(draws, renderer.LastDrawCount);
            renderer.Present();
        });

    // The glyphs of the list in order, each with the red, green, and blue coverage the atlas has for it now.
    private static Painted[] Snapshot(DisplayList list, GlyphAtlas atlas)
    {
        var glyphs = new List<Painted>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.FillRect)
            {
                _ = reader.ReadFillRect();
                continue;
            }

            GlyphRunCommand run = reader.ReadGlyphRun();
            Assert.Equal(GlyphCoverage.Rgb, run.Coverage);
            foreach (GlyphInstance glyph in run.Glyphs)
            {
                ReadOnlySpan<byte> page = atlas.GetPagePixels(GlyphCoverage.Rgb, glyph.Page);
                int stride = atlas.GetPageSize(GlyphCoverage.Rgb) * 4;
                byte[] coverage = new byte[glyph.Width * glyph.Height * 4];
                for (int y = 0; y < glyph.Height; y++)
                {
                    page.Slice(((glyph.AtlasY + y) * stride) + (glyph.AtlasX * 4), glyph.Width * 4).CopyTo(coverage.AsSpan(y * glyph.Width * 4));
                }

                glyphs.Add(new Painted(glyph, coverage, run.Color));
            }
        }

        return [.. glyphs];
    }

    private static void AssertComposite(D3D11Renderer renderer, ID3D11Texture2D* capture, Painted[] glyphs, Color paper, TextRenderingParameters text)
    {
        foreach (Painted glyph in glyphs)
        {
            GlyphInstance at = glyph.At;
            int inked = 0;
            for (int y = at.Y - 1; y <= at.Y + at.Height; y++)
            {
                for (int x = at.X - 1; x <= at.X + at.Width; x++)
                {
                    Color expected = Composite(glyphs, x, y, paper, text);
                    Color actual = renderer.ReadCapturedPixel(capture, x, y);
                    bool near = Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1;
                    Assert.True(near, $"pixel ({x}, {y}) near {at}: expected {expected}, got {actual}");
                    bool inside = x >= at.X && y >= at.Y && x < at.X + at.Width && y < at.Y + at.Height;
                    if (inside && (Math.Abs(actual.R - paper.R) > 50 || Math.Abs(actual.G - paper.G) > 50 || Math.Abs(actual.B - paper.B) > 50))
                    {
                        inked++;
                    }
                }
            }

            Assert.True(inked > 0, $"{at} drew no ink");
        }
    }

    private static void AssertColored(D3D11Renderer renderer, ID3D11Texture2D* capture, GlyphInstance at)
    {
        for (int y = at.Y; y < at.Y + at.Height; y++)
        {
            for (int x = at.X; x < at.X + at.Width; x++)
            {
                Color pixel = renderer.ReadCapturedPixel(capture, x, y);
                if (Math.Abs(pixel.R - pixel.B) > 20)
                {
                    return;
                }
            }
        }

        Assert.Fail($"No pixel of {at} has red and blue more than 20 apart.");
    }

    private static Color Composite(Painted[] glyphs, int x, int y, Color paper, TextRenderingParameters text)
    {
        Color pixel = paper;
        foreach (Painted glyph in glyphs)
        {
            (int u, int v) = (x - glyph.At.X, y - glyph.At.Y);
            if (u >= 0 && v >= 0 && u < glyph.At.Width && v < glyph.At.Height)
            {
                int at = ((v * glyph.At.Width) + u) * 4;
                (byte r, byte b) = text.IsBgr ? (glyph.Coverage[at + 2], glyph.Coverage[at]) : (glyph.Coverage[at], glyph.Coverage[at + 2]);
                pixel = Blend(pixel, glyph.Color, r, glyph.Coverage[at + 1], b, text);
            }
        }

        return pixel;
    }

    private static Color Blend(Color under, Color over, byte red, byte green, byte blue, TextRenderingParameters text)
    {
        double brightness = ((0.30 * over.R) + (0.59 * over.G) + (0.11 * over.B)) / 255.0;
        double k = text.ClearTypeContrast * Math.Clamp(4 * (0.75 - brightness), 0, 1);
        System.Numerics.Vector4 g = TextGamma.RatiosOf(text.Gamma);
        byte Channel(byte coverage, byte o, byte u)
        {
            double a = coverage / 255.0;
            double c = o / 255.0;
            double contrasted = a * (k + 1) / ((a * k) + 1);
            double corrected = Math.Clamp(
                contrasted + (contrasted * (1 - contrasted) * ((((g.X * c) + g.Y) * contrasted) + ((g.Z * c) + g.W))),
                0,
                1
            );
            double alpha = over.A / 255.0 * corrected;
            return (byte)Math.Round((o * alpha) + (u * (1 - alpha)));
        }

        return new Color(Channel(red, over.R, under.R), Channel(green, over.G, under.G), Channel(blue, over.B, under.B));
    }

    private sealed record Painted(GlyphInstance At, byte[] Coverage, Color Color);
}
