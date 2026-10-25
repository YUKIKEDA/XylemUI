using System.Numerics;
using TerraFX.Interop.DirectX;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Rendering.D3D11.Tests;

// Hit regions against what the D3D11 renderer draws (roadmap row R4e, design §5.0, "Hit regions and DrawingContext"): a point hits
// a region where the same rounded rectangle, in the same transform and clip, covers half a pixel or more on WARP. And a
// DrawingContext writes text as the display list writes it.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class HitRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly NodeHandle Node = new(1, 1);

    /// <summary>
    /// A point hits a rounded region in a turned rounded clip at 125 % where the same shape, drawn in the same transform and clip,
    /// covers half a pixel or more.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.25.
    ///
    /// Steps:
    /// - Push a rotation by 20 degrees about (90, 60) and the clip (30, 20, 120, 80) with corners of radius 24; fill (50, 30, 100,
    ///   70) with corners of radius 20 in ink and write the same rectangle as the region of a node; pop both. Draw, read every pixel,
    ///   and take each pixel's coverage from its blue channel between the clear color and ink. Test the pixel by its integer
    ///   coordinates, as Win32 gives them.
    ///
    /// Expected:
    /// - Where the coverage is 0.53 or more the pixel hits the node, and where it is 0.47 or less it misses. Pixels where both the
    ///   shape's and the clip's edges pass (a CPU reference covers between 0 and 1 for both) are skipped: the pixel takes the product
    ///   of the two coverages, the hit each one at half. Both hits and misses are checked, at least 50 of them on an edge.
    /// </remarks>
    [Fact]
    public void HitRegion_InATurnedRoundedClip_HitsWhereTheShapeCoversHalf() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("hits", 240, 160);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            const float Scale = 1.25f;
            Matrix3x2 turn = Matrix3x2.CreateRotation(MathF.PI / 9, new Vector2(90, 60));
            var clip = new Vector4(30, 20, 120, 80);
            var shape = new Vector4(50, 30, 100, 70);
            var list = new DisplayList();
            list.Reset(Scale);
            list.PushTransform(turn);
            list.PushClip(clip.X, clip.Y, clip.Z, clip.W, new CornerRadii(24));
            list.FillRect(shape.X, shape.Y, shape.Z, shape.W, new Paint(Ink), new CornerRadii(20));
            list.HitRegion(Node, shape.X, shape.Y, shape.Z, shape.W, new CornerRadii(20));
            list.Pop();
            list.Pop();
            renderer.Draw(list, Clear);

            ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
            Assert.True(Matrix3x2.Invert(turn, out Matrix3x2 back));
            int edges = 0;
            int hits = 0;
            int misses = 0;
            try
            {
                Color[] pixels = renderer.ReadCapturedPixels(capture, width, height);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var center = new Vector2(x + 0.5f, y + 0.5f);
                        Vector2 local = Vector2.Transform(center / Scale, back);
                        float shapeCoverage = ShapeReference.Coverage(local * Scale, shape * Scale, new Vector4(20 * Scale));
                        float clipCoverage = ShapeReference.Coverage(local * Scale, clip * Scale, new Vector4(24 * Scale));
                        if (shapeCoverage is > 0 and < 1 && clipCoverage is > 0 and < 1)
                        {
                            continue;
                        }

                        float coverage = (pixels[(y * width) + x].B - Clear.B) / (float)(Ink.B - Clear.B);
                        if (coverage is > 0.47f and < 0.53f)
                        {
                            continue;
                        }

                        bool hit = list.HitTest(x, y) == Node;
                        Assert.True(hit == coverage >= 0.53f, $"pixel ({x}, {y}): coverage {coverage}, hit {hit}");
                        edges += coverage is > 0.02f and < 0.98f ? 1 : 0;
                        hits += hit ? 1 : 0;
                        misses += hit ? 0 : 1;
                    }
                }
            }
            finally
            {
                _ = capture->Release();
            }

            Assert.True(edges >= 50 && hits > 0 && misses > 0, $"{edges} edge pixels, {hits} hits, {misses} misses");
        });

    /// <summary>
    /// A DrawingContext writes text, in a solid color and with a gradient, as the display list writes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font, a shaper, an atlas with a DirectWrite rasterizer, and two display lists at scale 1.25.
    ///
    /// Steps:
    /// - Through a drawing context on the first, in a rotation ended with using: draw "H日H" at 24 px in ink, and again with a
    ///   linear gradient over the box (10, 40, 120, 30). On the second, the same calls directly. Resolve the glyphs of both; read
    ///   both.
    ///
    /// Expected:
    /// - The two advances are the same, and both lists hold the same commands and glyphs.
    /// </remarks>
    [Fact]
    public void DrawingContext_Text_IsTheDisplayLists() =>
        ui.Invoke(() =>
        {
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            GradientBrush brush = GradientBrush.Linear(90, [new(0, Color.FromRgb(0xE02020)), new(1, Ink)]);
            var viaContext = new DisplayList();
            var direct = new DisplayList();
            viaContext.Reset(1.25f);
            direct.Reset(1.25f);

            var context = new DrawingContext(viaContext, Node);
            float solid;
            float brushed;
            using (context.PushTransform(Matrix3x2.CreateRotation(0.2f)))
            {
                solid = context.DrawText(shaper.Shape(font, "H日H", Japanese), font, 24, atlas, 10, 30, Ink);
                brushed = context.DrawText(shaper.Shape(font, "H日H", Japanese), font, 24, atlas, 10, 60, brush, 10, 40, 120, 30);
            }

            direct.PushTransform(Matrix3x2.CreateRotation(0.2f));
            Assert.Equal(solid, direct.DrawText(shaper.Shape(font, "H日H", Japanese), font, 24, atlas, 10, 30, Ink));
            Assert.Equal(brushed, direct.DrawText(shaper.Shape(font, "H日H", Japanese), font, 24, atlas, 10, 60, brush, 10, 40, 120, 30));
            direct.Pop();
            viaContext.ResolveGlyphs(atlas);
            direct.ResolveGlyphs(atlas);

            Assert.Equal(direct.CommandCount, viaContext.CommandCount);
            Assert.Equal(direct.GlyphCount, viaContext.GlyphCount);
            Assert.Equal(direct.ShapeCount, viaContext.ShapeCount);
            Assert.Equal(Runs(direct), Runs(viaContext));
        });

    // Each glyph run's glyphs and color, and the other commands' tags, in order.
    private static List<string> Runs(DisplayList list)
    {
        var runs = new List<string>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            switch (tag)
            {
                case CommandTag.GlyphRun:
                    GlyphRunCommand run = reader.ReadGlyphRun();
                    runs.Add($"{run.Color} {string.Join(';', run.Glyphs.ToArray())}");
                    break;
                case CommandTag.PushState:
                    runs.Add(reader.ReadState(out _).ToString());
                    break;
                case CommandTag.Pop:
                    reader.ReadPop();
                    runs.Add("Pop");
                    break;
                default:
                    throw new InvalidOperationException($"No reader for {tag}.");
            }
        }

        return runs;
    }
}
