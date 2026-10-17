using TerraFX.Interop.DirectX;
using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Rendering.D3D11.Tests;

// Glyph runs on the D3D11 renderer (S8b, design §5.0.2, §5.0.3): the atlas pages as a texture array, the upload of the atlas's
// changes, and the blend of the coverage. The pixels are read back on WARP and compared with an image composed on the CPU from
// the atlas's own coverage: over paper, each glyph in the display list's order, out = color × α + under × (1 − α) with
// α = color alpha × coverage, rounded to 8 bits after each glyph as the 8-bit target does; each channel within 1.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class GlyphRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Paper = Color.FromRgb(0xF0F0F0);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color HalfRed = new(0xCC, 0x33, 0x33, 128);

    /// <summary>
    /// Every pixel of the glyphs, and the ring of pixels around each, is the blend of the text colors over the background by
    /// the glyphs' coverage; a translucent color blends with its alpha times the coverage.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer of at least 200 x 100, the test font, a shaper, an atlas, and a display list.
    ///
    /// Steps:
    /// - Draw a paper rectangle (0, 0, 200, 100), then "H日" in opaque ink at 32 px (pen 10, baseline 40), then "H日" in red with
    ///   alpha 128 (pen 100, baseline 80). Read the pixels back before the present.
    ///
    /// Expected:
    /// - Every pixel of each glyph's box grown by 1 is the CPU composite, each channel within 1.
    /// - Every glyph is drawn: some pixel of its box differs from paper by more than 50 in a channel.
    /// </remarks>
    [Fact]
    public void Draw_GlyphRun_PixelsAreTheBlendOfTheCoverage() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("glyphs", 400, 300);
            (int width, int height) = window.ClientSize;
            Assert.True(width >= 200 && height >= 100);
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            var atlas = new GlyphAtlas();
            var list = new DisplayList();
            list.FillRect(0, 0, 200, 100, Paper);
            _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 32, atlas, 10, 40, Ink);
            _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 32, atlas, 100, 80, HalfRed);
            Painted[] glyphs = Snapshot(list, atlas);

            renderer.Draw(list, Clear, atlas);

            Assert.Equal(4, glyphs.Length);
            AssertDrawn(renderer, glyphs);
            renderer.Present();
        });

    /// <summary>
    /// The display list's order is the painting order: an opaque rectangle written after a glyph run hides the glyphs under it,
    /// and the glyphs outside it are still drawn.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, the test font, a shaper, an atlas, and a display list.
    ///
    /// Steps:
    /// - Draw paper (0, 0, 200, 100), "H日" in ink at 32 px (pen 10, baseline 40), then an opaque blue rectangle over the box of
    ///   the first glyph ("H") only.
    ///
    /// Expected:
    /// - Every pixel of the box of "H" is blue.
    /// - The second glyph ("日") is the CPU composite of its coverage, so it is drawn.
    /// </remarks>
    [Fact]
    public void Draw_RectAfterAGlyphRun_PaintsOverTheGlyphs() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("order", 400, 300);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            var atlas = new GlyphAtlas();
            var list = new DisplayList();
            list.FillRect(0, 0, 200, 100, Paper);
            _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 32, atlas, 10, 40, Ink);
            Painted[] glyphs = Snapshot(list, atlas);
            GlyphInstance h = glyphs[0].At;
            var blue = Color.FromRgb(0x3366CC);
            list.FillRect(h.X, h.Y, h.Width, h.Height, blue);

            renderer.Draw(list, Clear, atlas);

            ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
            try
            {
                for (int y = h.Y; y < h.Y + h.Height; y++)
                {
                    for (int x = h.X; x < h.X + h.Width; x++)
                    {
                        Assert.Equal(blue, renderer.ReadCapturedPixel(capture, x, y));
                    }
                }
            }
            finally
            {
                _ = capture->Release();
            }

            AssertDrawn(renderer, [glyphs[1]]);
            renderer.Present();
        });

    /// <summary>
    /// A translucent rectangle blends over what is under it (the blend state is the same for rectangles and glyphs).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer.
    ///
    /// Steps:
    /// - Draw paper (0, 0, 40, 40), then red with alpha 128 over (10, 10, 20, 20); read (15, 15) and (5, 5).
    ///
    /// Expected:
    /// - (15, 15) is red × 128/255 + paper × 127/255 per channel, within 1; (5, 5) is paper.
    /// </remarks>
    [Fact]
    public void Draw_TranslucentRect_BlendsOverWhatIsUnder() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("blend", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            list.FillRect(0, 0, 40, 40, Paper);
            list.FillRect(10, 10, 20, 20, HalfRed);

            renderer.Draw(list, Clear);

            AssertNear(Blend(Paper, HalfRed, 255), renderer.ReadPixel(15, 15), "(15, 15)");
            Assert.Equal(Paper, renderer.ReadPixel(5, 5));
            renderer.Present();
        });

    /// <summary>
    /// Overwriting an atlas region that an earlier draw read does not change what that draw produced: Direct3D 11 orders the
    /// upload after it, with no fence of ours (design §5.0.2).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and an atlas of one 64 px page with 32 px plots (4 plots, at most 1 page); the 24 px Japanese glyphs
    ///   of the test font take a plot each.
    ///
    /// Steps:
    /// - Frame 1: draw "日本語の" at 24 px (pen 10, baseline 40), so the 4 plots are full; record a copy of the back buffer
    ///   without waiting for it.
    /// - Frame 2: start an atlas frame and draw "テ" (pen 100, baseline 40): it evicts the plot of "日" (the least recently
    ///   used: all were used in frame 1, "日" first) and is uploaded into that region. Record a second copy.
    /// - Read both copies.
    ///
    /// Expected:
    /// - "テ" got the region "日" had (so the second frame overwrote what the first one read).
    /// - The first copy is the composite of frame 1 with the coverage the atlas had then ("日" included); the second is the
    ///   composite of "テ".
    /// </remarks>
    [Fact]
    public void Draw_AtlasRegionOverwrittenAfterADraw_KeepsThatDrawsPixels() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("overwrite", 400, 300);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            var atlas = new GlyphAtlas(pageSize: 64, plotSize: 32, maxPages: 1);
            var list = new DisplayList();
            list.FillRect(0, 0, 200, 100, Paper);
            _ = list.DrawText(shaper.Shape(font, "日本語の", Japanese), font, 24, atlas, 10, 40, Ink);
            Painted[] frame1 = Snapshot(list, atlas);
            renderer.Draw(list, Clear, atlas);
            ID3D11Texture2D* first = renderer.CaptureBackBuffer();
            ID3D11Texture2D* second = null;
            try
            {
                atlas.BeginFrame();
                list.Reset();
                list.FillRect(0, 0, 200, 100, Paper);
                _ = list.DrawText(shaper.Shape(font, "テ", Japanese), font, 24, atlas, 100, 40, Ink);
                Painted[] frame2 = Snapshot(list, atlas);
                renderer.Draw(list, Clear, atlas);
                second = renderer.CaptureBackBuffer();

                (GlyphInstance nichi, GlyphInstance te) = (frame1[0].At, frame2[0].At);
                Assert.Equal((nichi.Page, nichi.AtlasX, nichi.AtlasY), (te.Page, te.AtlasX, te.AtlasY));
                Assert.Equal(1, atlas.EvictionCount);
                AssertComposite(renderer, first, frame1);
                AssertComposite(renderer, second, frame2);
            }
            finally
            {
                _ = first->Release();
                if (second is not null)
                {
                    _ = second->Release();
                }
            }

            renderer.Present();
        });

    /// <summary>
    /// A page the atlas adds beyond its maximum grows the texture array, and its glyph is drawn; once the atlas drops that
    /// page, the array goes back to the maximum. A page is uploaded whole once; the slice that stays is kept, not uploaded again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and an atlas of one 64 px page with 32 px plots, at most 1 page.
    ///
    /// Steps:
    /// - Frame 1: draw "日本語のテ" at 24 px (5 glyphs for 4 plots, all in this frame, so a second page is added).
    /// - Frame 2: draw "日本語の" (page 1 not used). Frame 3: draw "日本語の" (page 1 was dropped by this frame's start).
    ///
    /// Expected:
    /// - After frame 1: 2 slices, 2 whole-page uploads, "テ" is on page 1, and the frame is the CPU composite.
    /// - After frame 3: 1 page and 1 slice, still 2 whole-page uploads, and the frame is the CPU composite.
    /// </remarks>
    [Fact]
    public void Draw_PageBeyondTheMaximum_GrowsAndShrinksTheTextureArray() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("pages", 400, 300);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            var atlas = new GlyphAtlas(pageSize: 64, plotSize: 32, maxPages: 1);
            var list = new DisplayList();

            Painted[] frame1 = DrawFrame(renderer, atlas, list, font, shaper, "日本語のテ");
            Assert.Equal((2, 2), (renderer.AtlasSlices, renderer.AtlasPageUploads));
            Assert.Equal(1, frame1[4].At.Page);
            AssertDrawn(renderer, frame1);

            _ = DrawFrame(renderer, atlas, list, font, shaper, "日本語の");
            Painted[] frame3 = DrawFrame(renderer, atlas, list, font, shaper, "日本語の");
            Assert.Equal(1, atlas.PageCount);
            Assert.Equal((1, 2), (renderer.AtlasSlices, renderer.AtlasPageUploads));
            AssertDrawn(renderer, frame3);
            renderer.Present();
        });

    /// <summary>
    /// A display list with glyph runs needs the atlas they were written from.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list with one glyph run.
    ///
    /// Steps:
    /// - Draw it without an atlas.
    ///
    /// Expected:
    /// - <see cref="ArgumentException"/>.
    /// </remarks>
    [Fact]
    public void Draw_GlyphRunWithoutAnAtlas_Throws() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("no atlas", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            list.GlyphRun([new GlyphInstance(0, 0, 1, 1, 0, 0, 0)], Ink);

            Assert.Throws<ArgumentException>(() => renderer.Draw(list, Clear));
        });

    /// <summary>
    /// A steady-state frame with text — start the atlas frame, write rectangles and text, upload, draw, present — allocates
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, the test font, a shaper, an atlas, and a display list rewritten each frame with 100 rectangles and 10
    ///   lines of "Hello, World! 日本語のテキスト" at 16 px. Warmed up with 20 frames.
    ///
    /// Steps:
    /// - Run 100 frames, measuring the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes; no glyph was rasterized and the display list never grew.
    /// </remarks>
    [Fact]
    public void SteadyState_FrameWithText_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("steady text", 400, 300);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            var atlas = new GlyphAtlas();
            var list = new DisplayList();
            for (int i = 0; i < 20; i++)
            {
                TextFrame(renderer, atlas, list, font, shaper, i);
            }

            int rasterized = atlas.RasterizeCount;
            int growth = list.GrowthCount;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                TextFrame(renderer, atlas, list, font, shaper, i);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(rasterized, atlas.RasterizeCount);
            Assert.Equal(growth, list.GrowthCount);
        });

    private static void TextFrame(D3D11Renderer renderer, GlyphAtlas atlas, DisplayList list, Font font, TextShaper shaper, int frame)
    {
        atlas.BeginFrame();
        list.Reset();
        for (int i = 0; i < 100; i++)
        {
            list.FillRect(((i * 7) + frame) % 190, i % 90, 10, 10, Paper);
        }

        for (int line = 0; line < 10; line++)
        {
            _ = list.DrawText(shaper.Shape(font, "Hello, World! 日本語のテキスト", Japanese), font, 16, atlas, frame % 10, 20 + (line * 20), Ink);
        }

        renderer.Render(list, Clear, atlas);
    }

    private static Painted[] DrawFrame(D3D11Renderer renderer, GlyphAtlas atlas, DisplayList list, Font font, TextShaper shaper, string text)
    {
        atlas.BeginFrame();
        list.Reset();
        list.FillRect(0, 0, 200, 100, Paper);
        _ = list.DrawText(shaper.Shape(font, text, Japanese), font, 24, atlas, 10, 40, Ink);
        Painted[] glyphs = Snapshot(list, atlas);
        renderer.Draw(list, Clear, atlas);
        return glyphs;
    }

    // The glyphs of the list in order, each with the coverage the atlas has for it now.
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
            foreach (GlyphInstance glyph in run.Glyphs)
            {
                ReadOnlySpan<byte> page = atlas.GetPagePixels(glyph.Page);
                byte[] coverage = new byte[glyph.Width * glyph.Height];
                for (int y = 0; y < glyph.Height; y++)
                {
                    page.Slice(((glyph.AtlasY + y) * atlas.PageSize) + glyph.AtlasX, glyph.Width).CopyTo(coverage.AsSpan(y * glyph.Width));
                }

                glyphs.Add(new Painted(glyph, coverage, run.Color));
            }
        }

        return [.. glyphs];
    }

    private static void AssertDrawn(D3D11Renderer renderer, Painted[] glyphs)
    {
        ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
        try
        {
            AssertComposite(renderer, capture, glyphs);
        }
        finally
        {
            _ = capture->Release();
        }
    }

    // Each glyph's box grown by 1 against the CPU composite of all the glyphs over paper; and each glyph has ink.
    private static void AssertComposite(D3D11Renderer renderer, ID3D11Texture2D* capture, Painted[] glyphs)
    {
        foreach (Painted glyph in glyphs)
        {
            GlyphInstance at = glyph.At;
            int inked = 0;
            for (int y = at.Y - 1; y <= at.Y + at.Height; y++)
            {
                for (int x = at.X - 1; x <= at.X + at.Width; x++)
                {
                    Color expected = Composite(glyphs, x, y);
                    Color actual = renderer.ReadCapturedPixel(capture, x, y);
                    AssertNear(expected, actual, $"pixel ({x}, {y}) near {at}");
                    bool inside = x >= at.X && y >= at.Y && x < at.X + at.Width && y < at.Y + at.Height;
                    if (inside && (Math.Abs(actual.R - Paper.R) > 50 || Math.Abs(actual.G - Paper.G) > 50 || Math.Abs(actual.B - Paper.B) > 50))
                    {
                        inked++;
                    }
                }
            }

            Assert.True(inked > 0, $"{at} drew no ink");
        }
    }

    private static Color Composite(Painted[] glyphs, int x, int y)
    {
        Color pixel = Paper;
        foreach (Painted glyph in glyphs)
        {
            (int u, int v) = (x - glyph.At.X, y - glyph.At.Y);
            if (u >= 0 && v >= 0 && u < glyph.At.Width && v < glyph.At.Height)
            {
                pixel = Blend(pixel, glyph.Color, glyph.Coverage[(v * glyph.At.Width) + u]);
            }
        }

        return pixel;
    }

    private static Color Blend(Color under, Color over, byte coverage)
    {
        double alpha = over.A / 255.0 * (coverage / 255.0);
        byte Channel(byte o, byte u) => (byte)Math.Round((o * alpha) + (u * (1 - alpha)));
        return new Color(Channel(over.R, under.R), Channel(over.G, under.G), Channel(over.B, under.B));
    }

    private static void AssertNear(Color expected, Color actual, string where)
    {
        bool near = Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1;
        Assert.True(near, $"{where}: expected {expected}, got {actual}");
    }

    private sealed record Painted(GlyphInstance At, byte[] Coverage, Color Color);
}
