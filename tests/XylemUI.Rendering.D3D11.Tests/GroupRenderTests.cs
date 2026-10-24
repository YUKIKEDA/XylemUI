using System.Numerics;
using TerraFX.Interop.DirectX;
using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Rendering.D3D11.Tests;

// Opacity groups and layers on the D3D11 renderer (roadmap row R4c, design §5.0, "The API of layers, clips, transforms, and
// DrawingContext"). The pixels are read back on WARP and compared with a CPU reference: a group whose children are apart gives
// each child's color its alpha; a group whose children overlap is the children drawn over one another, then taken times the
// alpha, which is what a layer composites.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class GroupRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color Paper = Color.FromRgb(0xF0F0F0);
    private static readonly Color HalfRed = new(0xE0, 0x20, 0x20, 128);
    private static readonly Vector4 Window = new(0, 0, 236, 156);

    /// <summary>
    /// A group whose children are apart draws each child with its color times the alpha, in one draw and with no layer.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push opacity 0.5; fill (20, 20, 60, 40) with ink and (100, 20, 60, 40) with paper; pop. Read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is each rectangle's color times 0.5 over the clear color, each channel within 1; one draw, and no layer.
    /// </remarks>
    [Fact]
    public void Group_ApartChildren_TakeTheAlphaWithoutALayer() =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushOpacity(0.5f);
                    list.FillRect(20, 20, 60, 40, Ink);
                    list.FillRect(100, 20, 60, 40, Paper);
                    list.Pop();
                },
                Window,
                p => (Rect(p, new Vector4(20, 20, 60, 40), Ink) + Rect(p, new Vector4(100, 20, 60, 40), Paper)) * 0.5f,
                tolerance: 1,
                check: renderer => Assert.Equal((1, 0), (renderer.LastDrawCount, renderer.LayerCount))
            )
        );

    /// <summary>
    /// A group whose children overlap is a layer: where they overlap, the pixel is the children drawn over one another, times the
    /// alpha, not each child times the alpha blended twice.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push opacity 0.5; fill (20, 20, 80, 60) with ink and (60, 40, 80, 60) with red of alpha 128; pop. Read every pixel.
    ///
    /// Expected:
    /// - Each pixel is (red over ink) times 0.5 over the clear color, each channel within 1; one layer, drawn in two draws (the
    ///   children into the layer, and the layer into the back buffer).
    /// </remarks>
    [Fact]
    public void Group_OverlappingChildren_AreALayer() =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushOpacity(0.5f);
                    list.FillRect(20, 20, 80, 60, Ink);
                    list.FillRect(60, 40, 80, 60, HalfRed);
                    list.Pop();
                },
                Window,
                p => Over(Rect(p, new Vector4(60, 40, 80, 60), HalfRed), Rect(p, new Vector4(20, 20, 80, 60), Ink)) * 0.5f,
                tolerance: 1,
                check: renderer => Assert.Equal((2, 1), (renderer.LastDrawCount, renderer.LayerCount))
            )
        );

    /// <summary>
    /// Nested groups: a layer inside a group whose children are apart is composited with both alphas, and a group whose children
    /// are apart inside a layer gives its alpha to its children inside the layer.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Case "layer inside": push 0.5; fill (10, 10, 40, 40) with paper; push 0.6; fill (70, 10, 60, 60) with ink and
    ///   (100, 40, 60, 60) with red of alpha 128; pop; pop.
    /// - Case "apart inside": push 0.5; fill (10, 10, 210, 130) with paper; push 0.5; fill (30, 30, 40, 40) and (120, 30, 40, 40)
    ///   with ink; pop; pop.
    /// - Read every pixel of the window.
    ///
    /// Expected:
    /// - "layer inside": paper times 0.5, and (red over ink) times 0.3. "apart inside": (ink times 0.5 over paper) times 0.5. Each
    ///   over the clear color, each channel within 1; one layer.
    /// </remarks>
    /// <param name="layerInside">Whether the case is "layer inside", rather than "apart inside".</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Group_Nested_TakesBothAlphas(bool layerInside) =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushOpacity(0.5f);
                    if (layerInside)
                    {
                        list.FillRect(10, 10, 40, 40, Paper);
                        list.PushOpacity(0.6f);
                        list.FillRect(70, 10, 60, 60, Ink);
                        list.FillRect(100, 40, 60, 60, HalfRed);
                    }
                    else
                    {
                        list.FillRect(10, 10, 210, 130, Paper);
                        list.PushOpacity(0.5f);
                        list.FillRect(30, 30, 40, 40, Ink);
                        list.FillRect(120, 30, 40, 40, Ink);
                    }

                    list.Pop();
                    list.Pop();
                },
                Window,
                p =>
                    layerInside
                        ? (Rect(p, new Vector4(10, 10, 40, 40), Paper) * 0.5f)
                            + (Over(Rect(p, new Vector4(100, 40, 60, 60), HalfRed), Rect(p, new Vector4(70, 10, 60, 60), Ink)) * 0.3f)
                        : Over(
                            (Rect(p, new Vector4(30, 30, 40, 40), Ink) + Rect(p, new Vector4(120, 30, 40, 40), Ink)) * 0.5f,
                            Rect(p, new Vector4(10, 10, 210, 130), Paper)
                        ) * 0.5f,
                tolerance: 1,
                check: renderer => Assert.Equal(1, renderer.LayerCount)
            )
        );

    /// <summary>
    /// Inside a layer, every kind of command is drawn in its place (the renderer moves each into the layer's pixels), and in a
    /// group whose children are apart, gradients take the alpha too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Case "layer": push 0.5; an ellipse at (190, 50), radii 30 and 20, 4 wide, in paper; the outer shadow of (60, 30, 80, 50)
    ///   with radius 10, offset (4, 6), blur 12; fill that rectangle with a linear gradient, radius 10, and a 3 wide ink border; a
    ///   line from (20, 120) to (200, 140), 6 wide, with the gradient; pop.
    /// - Case "apart": push 0.5; the same ellipse, rectangle, and line without the shadow, in that order, so that none meets the
    ///   union of the ones before it; pop.
    /// - Read every pixel of the window.
    ///
    /// Expected:
    /// - "layer": (shape over shadow) and the line and the ellipse, times 0.5; "apart": each times 0.5; over the clear color, each
    ///   channel within 2 (the gradient's dither). One layer for "layer", none for "apart".
    /// </remarks>
    /// <param name="layer">Whether the case is "layer".</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Group_EveryKind_IsInItsPlaceAndTakesTheAlpha(bool layer) =>
        ui.Invoke(() =>
        {
            GradientBrush brush = ShapeRenderTests.Brush("linear 45");
            var rect = new Vector4(60, 30, 80, 50);
            var radii = new Vector4(10);
            var shadow = new BoxShadow(new Color(0, 0, 0, 100), new Vector2(4, 6), 12);
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushOpacity(0.5f);
                    list.DrawEllipse(190, 50, 30, 20, new Stroke(Paper, 4));
                    if (layer)
                    {
                        list.DrawShadow(60, 30, 80, 50, new CornerRadii(10), shadow);
                    }

                    list.FillRect(60, 30, 80, 50, brush, new CornerRadii(10), new Border(new BorderWidths(3), Ink));
                    list.DrawLine(20, 120, 200, 140, new Stroke(brush, 6));
                    list.Pop();
                },
                Window,
                p =>
                {
                    Vector4 fill = ShapeReference.Gradient(brush, p, rect);
                    Vector4 shape = ShapeReference.Shape(p, rect, radii, fill, new Vector4(3), ShapeReference.Premultiply(Ink));
                    Vector4 under = layer
                        ? ShapeReference.Premultiply(shadow.Color)
                            * ShapeReference.Shadow(p, new Vector4(64, 36, 80, 50), radii, 6, rect, radii, inset: false)
                        : Vector4.Zero;
                    var a = new Vector2(20, 120);
                    var b = new Vector2(200, 140);
                    Vector4 line =
                        ShapeReference.Gradient(brush, p, new Vector4(17, 117, 186, 26))
                        * CurveReference.Coverage(CurveReference.Line(p, a, b, 3, LineCap.Flat, null), 6);
                    Vector4 ellipse =
                        ShapeReference.Premultiply(Paper)
                        * CurveReference.Coverage(MathF.Abs(CurveReference.Ellipse(p, new Vector2(190, 50), new Vector2(30, 20))) - 2, 4);
                    return (Over(shape, under) + line + ellipse) * 0.5f;
                },
                tolerance: 2,
                check: renderer => Assert.Equal(layer ? 1 : 0, renderer.LayerCount)
            );
        });

    /// <summary>
    /// A layer is its bounds cut to the target: one that reaches far past the window is drawn where it is in the window, and one
    /// wholly outside it draws nothing and makes no layer, while the commands after it are drawn.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push 0.5; fill (-1000, 20, 1200, 40) with ink and (100, 30, 40, 40) with paper; pop. Push 0.5; fill (5000, 10, 50, 50) and
    ///   (5020, 20, 50, 50) with ink; pop. Fill (10, 100, 30, 30) with paper. Read every pixel of the window.
    ///
    /// Expected:
    /// - (paper over ink) times 0.5 in the first group's rectangles, paper in the last one, over the clear color, each channel
    ///   within 1; one layer, whose texture is no larger than the window rounded up to 64 pixels.
    /// </remarks>
    [Fact]
    public void Layer_PastOrOutsideTheTarget_IsCutToIt() =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushOpacity(0.5f);
                    list.FillRect(-1000, 20, 1200, 40, Ink);
                    list.FillRect(100, 30, 40, 40, Paper);
                    list.Pop();
                    list.PushOpacity(0.5f);
                    list.FillRect(5000, 10, 50, 50, Ink);
                    list.FillRect(5020, 20, 50, 50, Ink);
                    list.Pop();
                    list.FillRect(10, 100, 30, 30, Paper);
                },
                Window,
                p =>
                    (Over(Rect(p, new Vector4(100, 30, 40, 40), Paper), Rect(p, new Vector4(-1000, 20, 1200, 40), Ink)) * 0.5f)
                    + Rect(p, new Vector4(10, 100, 30, 30), Paper),
                tolerance: 1,
                check: renderer =>
                {
                    Assert.Equal(1, renderer.LayerCount);
                    Assert.True(renderer.LayerBytes <= 256L * 192 * 4, $"the layer holds {renderer.LayerBytes} bytes");
                }
            )
        );

    /// <summary>
    /// When the pool of layer textures is full, a group that needs a layer gives its alpha to its children instead, and the
    /// refusal is counted.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer whose layer textures may hold 0 bytes, and a display list at scale 1.
    ///
    /// Steps:
    /// - Push 0.5; fill (20, 20, 80, 60) with ink and (60, 40, 80, 60) with paper; pop. Read every pixel.
    ///
    /// Expected:
    /// - Each pixel is (paper times 0.5) over (ink times 0.5) over the clear color, each channel within 1; no layer, one refusal.
    /// </remarks>
    [Fact]
    public void Layer_PoolFull_GivesTheAlphaToTheChildren() =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushOpacity(0.5f);
                    list.FillRect(20, 20, 80, 60, Ink);
                    list.FillRect(60, 40, 80, 60, Paper);
                    list.Pop();
                },
                Window,
                p => Over(Rect(p, new Vector4(60, 40, 80, 60), Paper) * 0.5f, Rect(p, new Vector4(20, 20, 80, 60), Ink) * 0.5f),
                tolerance: 1,
                setup: renderer => renderer.LayerCapacityBytes = 0,
                check: renderer => Assert.Equal((0, 1), (renderer.LayerCount, renderer.LayersRefused))
            )
        );

    /// <summary>
    /// Text in a group is a layer and is drawn in grayscale even where the window draws ClearType: the pixels are those of the
    /// same text drawn in grayscale with its color's alpha times the group's, without a group.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two WARP renderers with ClearType text rendering, the test font, a shaper, an atlas each, and a display list each.
    ///
    /// Steps:
    /// - First: paper (0, 0, 200, 100); push 0.5; "H日" in ink at 32 px in ClearType (pen 10.25, baseline 50); pop.
    /// - Second: paper; "H日" in ink with alpha 128 at 32 px in grayscale.
    /// - Read both frames' pixels of (0, 0, 200, 100).
    ///
    /// Expected:
    /// - The first list's run is grayscale, and its group is a layer; each pixel of the two frames is within 1.
    /// </remarks>
    [Fact]
    public void Group_WithText_IsAGrayscaleLayer()
    {
        Color[] grouped = TextFrame(grouped: true);
        Color[] plain = TextFrame(grouped: false);
        for (int i = 0; i < grouped.Length; i++)
        {
            Color a = grouped[i];
            Color b = plain[i];
            bool near = Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1;
            Assert.True(near, $"pixel ({i % 200}, {i / 200}): grouped {a}, plain {b}");
        }
    }

    /// <summary>
    /// A frame with layers, groups whose children are apart, and text in a group allocates nothing once its textures and streams
    /// are made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, the test font, a shaper, an atlas, and a display list.
    ///
    /// Steps:
    /// - 20 frames to warm up, then 100 frames: reset; 10 groups of two overlapping rectangles, 10 of two apart, and "H日" in a group;
    ///   resolve, draw, and present. Measure the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes; the list did not grow; the layer textures are the same ones.
    /// </remarks>
    [Fact]
    public void SteadyState_Groups_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("steady groups", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            ShapedGlyph[] glyphs = shaper.Shape(font, "H日", Japanese).ToArray();
            void Frame(int frame)
            {
                atlas.BeginFrame();
                list.Reset();
                for (int i = 0; i < 10; i++)
                {
                    float x = (i * 25) + (frame % 3);
                    list.PushOpacity(0.5f);
                    list.FillRect(x, 10, 20, 20, Ink);
                    list.FillRect(x + 10, 20, 20, 20, Paper);
                    list.Pop();
                    list.PushOpacity(0.7f);
                    list.FillRect(x, 60, 8, 8, Ink);
                    list.FillRect(x + 12, 60, 8, 8, Paper);
                    list.Pop();
                }

                list.PushOpacity(0.8f);
                _ = list.DrawText(glyphs, font, 24, atlas, 10, 140, Ink, GlyphCoverage.Rgb);
                list.Pop();
                list.ResolveGlyphs(atlas);
                renderer.Draw(list, Clear, atlas);
                renderer.Present();
            }

            for (int i = 0; i < 20; i++)
            {
                Frame(i);
            }

            int growth = list.GrowthCount;
            (int layers, long bytes) = (renderer.LayerCount, renderer.LayerBytes);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                Frame(i);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(growth, list.GrowthCount);
            Assert.Equal((layers, bytes), (renderer.LayerCount, renderer.LayerBytes));
        });

    // The frame of Group_WithText_IsAGrayscaleLayer, read back.
    private Color[] TextFrame(bool grouped)
    {
        Color[] pixels = new Color[200 * 100];
        ui.Invoke(() =>
        {
            using var window = new Win32Window("group text", 400, 300);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            renderer.TextRendering = new TextRenderingParameters(
                GlyphCoverage.Rgb,
                false,
                Gamma: 1.8f,
                GrayscaleContrast: 1.0f,
                ClearTypeContrast: 0.5f
            );
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            list.FillRect(0, 0, 200, 100, Paper);
            if (grouped)
            {
                list.PushOpacity(0.5f);
                _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 32, atlas, 10.25f, 50, Ink, GlyphCoverage.Rgb);
                list.Pop();
            }
            else
            {
                _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 32, atlas, 10.25f, 50, new Color(Ink.R, Ink.G, Ink.B, 128));
            }

            list.ResolveGlyphs(atlas);
            if (grouped)
            {
                DisplayListReader reader = list.GetReader();
                var tags = new List<CommandTag>();
                while (reader.TryNext(out CommandTag tag))
                {
                    tags.Add(tag);
                    switch (tag)
                    {
                        case CommandTag.FillRect:
                            _ = reader.ReadFillRect();
                            break;
                        case CommandTag.PushOpacity:
                            Assert.True(reader.ReadGroup().Layer);
                            break;
                        case CommandTag.Pop:
                            reader.ReadPop();
                            break;
                        default:
                            Assert.Equal(GlyphCoverage.Gray, reader.ReadGlyphRun().Coverage);
                            break;
                    }
                }

                Assert.Equal([CommandTag.FillRect, CommandTag.PushOpacity, CommandTag.GlyphRun, CommandTag.Pop], tags);
            }

            renderer.Draw(list, Clear, atlas);
            ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
            try
            {
                for (int y = 0; y < 100; y++)
                {
                    for (int x = 0; x < 200; x++)
                    {
                        pixels[(y * 200) + x] = renderer.ReadCapturedPixel(capture, x, y);
                    }
                }
            }
            finally
            {
                _ = capture->Release();
            }

            renderer.Present();
        });
        return pixels;
    }

    // A solid rectangle with whole-pixel edges: its premultiplied color inside, nothing outside.
    private static Vector4 Rect(Vector2 p, Vector4 rect, Color color) =>
        p.X >= rect.X && p.X < rect.X + rect.Z && p.Y >= rect.Y && p.Y < rect.Y + rect.W ? ShapeReference.Premultiply(color) : Vector4.Zero;

    // Premultiplied `top` over `bottom`.
    private static Vector4 Over(Vector4 top, Vector4 bottom) => top + (bottom * (1 - top.W));
}
