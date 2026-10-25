using System.Numerics;
using TerraFX.Interop.DirectX;
using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Rendering.D3D11.Tests;

// Clips and transforms on the D3D11 renderer (roadmap row R4d, design §5.0, "The API of layers, clips, transforms, and
// DrawingContext"). The pixels are read back on WARP and compared with a CPU reference: a command's coverage at the pixel, taken
// where the pixel lies before the transform, times the coverage of each clip at the pixel (a rectangle's: inside or not; a rounded
// clip's: by the distance to its edge, as a shape's own). Text is compared with text drawn without the clip or the transform:
// over a frame, a clip's coverage c makes a pixel c times the pixel with the text and 1 - c times the one without it, and a turn by
// a right angle about a whole pixel, or a scale of 2, makes the pixels those of the text turned or drawn twice as large.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class ClipRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly string EmojiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf");
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color Paper = Color.FromRgb(0xF0F0F0);
    private static readonly Color HalfRed = new(0xE0, 0x20, 0x20, 128);

    // The shadows' color: the one ShapeRenderTests' shadows are within 1 of their reference in.
    private static readonly Color Shade = new(0, 0, 0, 160);
    private static readonly Vector4 Window = new(0, 0, 236, 156);

    /// <summary>
    /// A rectangle that clips draws a rectangle only inside it, on whole pixels: the clip is the four planes of the vertex shader.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push the clip (40, 30, 100, 60) with no radius; fill (10, 10, 200, 120) with ink; pop. Read every pixel of the window.
    ///
    /// Expected:
    /// - Ink inside (40, 30, 100, 60) and the clear color elsewhere, each channel within 1.
    /// </remarks>
    [Fact]
    public void Clip_Rectangle_CutsARectangle() =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushClip(40, 30, 100, 60, default);
                    list.FillRect(10, 10, 200, 120, Ink);
                    list.Pop();
                },
                Window,
                p => Inside(p, new Vector4(40, 30, 100, 60)) ? ShapeReference.Premultiply(Ink) : Vector4.Zero,
                tolerance: 1
            )
        );

    /// <summary>
    /// A rounded clip cuts a rectangle by the distance to its edge, antialiased as a rounded rectangle's own edge.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push the clip (40, 30, 100, 60) with radii 24, 8, 0, and 30; fill (10, 10, 200, 120) with ink; pop. Read every pixel.
    ///
    /// Expected:
    /// - Ink times the rounded rectangle's coverage at each pixel over the clear color, each channel within 1.
    /// </remarks>
    [Fact]
    public void Clip_Rounded_CutsByTheDistance() =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushClip(40, 30, 100, 60, new CornerRadii(24, 8, 0, 30));
                    list.FillRect(10, 10, 200, 120, Ink);
                    list.Pop();
                },
                Window,
                p => ShapeReference.Premultiply(Ink) * ShapeReference.Coverage(p, new Vector4(40, 30, 100, 60), new Vector4(24, 8, 0, 30)),
                tolerance: 1
            )
        );

    /// <summary>
    /// Every kind of command but text is cut by a rectangle and by a rounded clip: a rounded rectangle with a border, an outer
    /// shadow, a line, a filled ellipse, the outline of an ellipse, an arc, and a sector.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push the clip (24, 14, 188, 128), with radius 0 in one case and 44 in the other; draw, apart from one another: the rounded
    ///   rectangle (10, 10, 60, 50) of radius 12 in paper with a 4 wide ink border; an outer shadow of (100, 18, 40, 30), radius 6,
    ///   blur 10, in black of alpha 160; a line from (168, 10) to (226, 66), 8 wide with round caps, in paper; an ellipse at (40, 112), radii 30
    ///   and 22, in ink; the outline of an ellipse at (118, 112), radii 30 and 22, 6 wide, in paper; an arc at (196, 112), radius
    ///   24, from 300 to 150 degrees, 6 wide with square caps, in red of alpha 128; and a sector at (118, 70), radius 14, from 0 to
    ///   250 degrees, in paper. Pop. Read every pixel.
    ///
    /// Expected:
    /// - The sum of the commands' references, each times the clip's coverage at the pixel, over the clear color, each channel
    ///   within 1.
    /// </remarks>
    /// <param name="radius">The clip's radius.</param>
    [Theory]
    [InlineData(0f)]
    [InlineData(44f)]
    public void Clip_EveryKind_IsCut(float radius) =>
        ui.Invoke(() =>
        {
            var clip = new Vector4(24, 14, 188, 128);
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushClip(clip.X, clip.Y, clip.Z, clip.W, new CornerRadii(radius));
                    list.FillRect(10, 10, 60, 50, Paper, new CornerRadii(12), new Border(new BorderWidths(4), Ink));
                    list.DrawShadow(100, 18, 40, 30, new CornerRadii(6), new BoxShadow(Shade, Vector2.Zero, 10));
                    list.DrawLine(168, 10, 226, 66, new Stroke(Paper, 8, LineCap.Round));
                    list.FillEllipse(40, 112, 30, 22, Ink);
                    list.DrawEllipse(118, 112, 30, 22, new Stroke(Paper, 6));
                    list.DrawArc(196, 112, 24, 300, 210, new Stroke(HalfRed, 6, LineCap.Square));
                    list.FillSector(118, 70, 14, 0, 250, Paper);
                    list.Pop();
                },
                Window,
                p =>
                {
                    Vector4 content =
                        ShapeReference.Shape(
                            p,
                            new Vector4(10, 10, 60, 50),
                            new Vector4(12),
                            ShapeReference.Premultiply(Paper),
                            new Vector4(4),
                            ShapeReference.Premultiply(Ink)
                        )
                        + (
                            ShapeReference.Premultiply(Shade)
                            * ShapeReference.Shadow(
                                p,
                                new Vector4(100, 18, 40, 30),
                                new Vector4(6),
                                5,
                                new Vector4(100, 18, 40, 30),
                                new Vector4(6),
                                inset: false
                            )
                        )
                        + Solid(Paper, CurveReference.Line(p, new Vector2(168, 10), new Vector2(226, 66), 4, LineCap.Round, null), 8)
                        + Solid(Ink, CurveReference.Ellipse(p, new Vector2(40, 112), new Vector2(30, 22)), 0)
                        + Solid(Paper, MathF.Abs(CurveReference.Ellipse(p, new Vector2(118, 112), new Vector2(30, 22))) - 3, 6)
                        + Solid(HalfRed, CurveReference.Arc(p, new Vector2(196, 112), 24, Radians(300), Radians(210), 3, LineCap.Square, null), 6)
                        + Solid(Paper, CurveReference.Sector(p, new Vector2(118, 70), 14, 0, Radians(250)), 0);
                    float coverage =
                        radius > 0 ? ShapeReference.Coverage(p, clip, new Vector4(radius))
                        : Inside(p, clip) ? 1
                        : 0;
                    return content * coverage;
                },
                tolerance: 1
            );
        });

    /// <summary>
    /// Grayscale, ClearType, and color glyphs are cut by a rounded clip as every other command is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - WARP renderers with ClearType text rendering, the test font, Segoe UI Emoji (its glyph skipped where Windows lacks it), an
    ///   atlas, and a display list each.
    ///
    /// Steps:
    /// - Three frames of paper (0, 0, 220, 120): without text; with "H日" in grayscale at (10.25, 50), the same in ClearType at
    ///   (10.25, 100), and an emoji at (140, 80), all 40 px in ink; and with the same text inside the clip (30, 20, 150, 90) of
    ///   radius 36. Read every pixel of (0, 0, 220, 120).
    ///
    /// Expected:
    /// - Each pixel of the clipped frame is the frame with the text times the clip's coverage, plus the frame without it times one
    ///   less the coverage; each channel within 1. Its runs are ClearType where the text asked for it.
    /// </remarks>
    [Fact]
    public void Clip_GlyphsOfEachKind_AreCut()
    {
        var clip = new Vector4(30, 20, 150, 90);
        Color[] plain = TextFrame(220, 120, text: false, clip: null);
        Color[] text = TextFrame(220, 120, text: true, clip: null);
        Color[] clipped = TextFrame(220, 120, text: true, clip: clip);
        for (int y = 0; y < 120; y++)
        {
            for (int x = 0; x < 220; x++)
            {
                float coverage = ShapeReference.Coverage(new Vector2(x + 0.5f, y + 0.5f), clip, new Vector4(36));
                AssertNear(Lerp(plain[(y * 220) + x], text[(y * 220) + x], coverage), clipped[(y * 220) + x], x, y);
            }
        }
    }

    /// <summary>
    /// A rectangle in a rotation is the rotated rectangle, with its edges antialiased by the distance before the rotation.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push the rotation by 30 degrees about (118, 78); fill (68, 48, 100, 60) with ink; pop. Read every pixel.
    ///
    /// Expected:
    /// - Ink times the coverage, at the pixel turned back by the rotation, of the rectangle; over the clear color, each channel
    ///   within 1.
    /// </remarks>
    [Fact]
    public void Transform_Rotation_TurnsARectangle() =>
        ui.Invoke(() =>
        {
            Matrix3x2 rotation = Matrix3x2.CreateRotation(MathF.PI / 6, new Vector2(118, 78));
            Matrix3x2.Invert(rotation, out Matrix3x2 back);
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushTransform(rotation);
                    list.FillRect(68, 48, 100, 60, Ink);
                    list.Pop();
                },
                Window,
                p =>
                    ShapeReference.Premultiply(Ink) * ShapeReference.Coverage(Vector2.Transform(p, back), new Vector4(68, 48, 100, 60), Vector4.Zero),
                tolerance: 1
            );
        });

    /// <summary>
    /// In a rotation and a scale, a rounded rectangle with a border, a line, and an ellipse turn and grow, with their distances
    /// taken before the transform and scaled to physical pixels for the antialiasing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push the scale by 1.5 about (118, 78) and then the rotation by 25 degrees about it; draw the rounded rectangle (88, 58, 40,
    ///   30) of radius 8 in paper with a 3 wide ink border, the line from (70, 40) to (100, 50), 4 wide with flat caps, in paper,
    ///   and the ellipse at (150, 100), radii 12 and 8, in ink; pop; pop. Read every pixel.
    ///
    /// Expected:
    /// - Each command's reference at the pixel taken back through the transform, its distances times 1.5, over the clear color;
    ///   each channel within 1.
    /// </remarks>
    [Fact]
    public void Transform_RotationAndScale_TurnsAndGrowsEachKind() =>
        ui.Invoke(() =>
        {
            Matrix3x2 transform =
                Matrix3x2.CreateRotation(25 * MathF.PI / 180, new Vector2(118, 78)) * Matrix3x2.CreateScale(1.5f, new Vector2(118, 78));
            Matrix3x2.Invert(transform, out Matrix3x2 back);
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushTransform(Matrix3x2.CreateScale(1.5f, new Vector2(118, 78)));
                    list.PushTransform(Matrix3x2.CreateRotation(25 * MathF.PI / 180, new Vector2(118, 78)));
                    list.FillRect(88, 58, 40, 30, Paper, new CornerRadii(8), new Border(new BorderWidths(3), Ink));
                    list.DrawLine(70, 40, 100, 50, new Stroke(Paper, 4));
                    list.FillEllipse(150, 100, 12, 8, Ink);
                    list.Pop();
                    list.Pop();
                },
                Window,
                p =>
                {
                    Vector2 q = Vector2.Transform(p, back);
                    Vector4 shape = ScaledShape(q, new Vector4(88, 58, 40, 30), 8, 3, 1.5f);
                    Vector4 line = Solid(Paper, 1.5f * CurveReference.Line(q, new Vector2(70, 40), new Vector2(100, 50), 2, LineCap.Flat, null), 6);
                    Vector4 ellipse = Solid(Ink, 1.5f * CurveReference.Ellipse(q, new Vector2(150, 100), new Vector2(12, 8)), 0);
                    return shape + line + ellipse;
                },
                tolerance: 1
            );
        });

    /// <summary>
    /// A rounded clip in a rotation turns with it, and so does the rectangle it cuts.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push the rotation by -20 degrees about (118, 78); push the clip (58, 38, 120, 80) with radius 20 at each corner; fill
    ///   (40, 20, 160, 120) with ink; pop; pop. Read every pixel.
    ///
    /// Expected:
    /// - Ink times the clip's coverage at the pixel turned back (the rectangle covers the clip), over the clear color, each channel
    ///   within 1.
    /// </remarks>
    [Fact]
    public void Clip_RoundedInARotation_TurnsWithIt() =>
        ui.Invoke(() =>
        {
            Matrix3x2 rotation = Matrix3x2.CreateRotation(-MathF.PI / 9, new Vector2(118, 78));
            Matrix3x2.Invert(rotation, out Matrix3x2 back);
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushTransform(rotation);
                    list.PushClip(58, 38, 120, 80, new CornerRadii(20));
                    list.FillRect(40, 20, 160, 120, Ink);
                    list.Pop();
                    list.Pop();
                },
                Window,
                p =>
                    ShapeReference.Premultiply(Ink)
                    * ShapeReference.Coverage(Vector2.Transform(p, back), new Vector4(58, 38, 120, 80), new Vector4(20)),
                tolerance: 1
            );
        });

    /// <summary>
    /// In a move by whole physical pixels at 125 %, a clip's edges and the edges of a rectangle given the same logical edges lie on
    /// the same pixels: every pixel is the rectangle's or the clear color's, with no line or gap at the clip's edge.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.25.
    ///
    /// Steps:
    /// - Push the move by (8, 4) logical pixels (10 and 5 physical); push the clip (20.3, 10.7, 60.2, 40.1); fill the same rectangle
    ///   with ink, and (0, 0, 200, 100) with half red behind it first; pop; pop. Read every pixel.
    ///
    /// Expected:
    /// - Inside the rectangle on the pixels of its edges (each logical edge times 1.25 to the nearest pixel, then moved), ink over
    ///   half red; elsewhere the clear color: half red is cut by the clip, which lies on the same pixels. Each channel within 1.
    /// </remarks>
    [Fact]
    public void Clip_InAWholePixelMove_LiesOnTheRectanglesPixels()
    {
        {
            float left = MathF.Round(20.3f * 1.25f) + 10;
            float top = MathF.Round(10.7f * 1.25f) + 5;
            float right = MathF.Round((20.3f + 60.2f) * 1.25f) + 10;
            float bottom = MathF.Round((10.7f + 40.1f) * 1.25f) + 5;
            var snapped = new Vector4(left, top, right - left, bottom - top);
            CompareAt(
                1.25f,
                list =>
                {
                    list.PushTransform(Matrix3x2.CreateTranslation(8, 4));
                    list.PushClip(20.3f, 10.7f, 60.2f, 40.1f, default);
                    list.FillRect(0, 0, 200, 100, HalfRed);
                    list.FillRect(20.3f, 10.7f, 60.2f, 40.1f, Ink);
                    list.Pop();
                    list.Pop();
                },
                p => Inside(p, snapped) ? ShapeReference.Premultiply(Ink) : Vector4.Zero
            );
        }
    }

    /// <summary>
    /// Past <see cref="DisplayList.MaxRoundedClips"/> rounded clips, the commands are drawn into a layer with the last clip, and the
    /// layer is composited with the clips around it: the pixels are the product of every clip's coverage.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - For k from 0 to MaxRoundedClips: push the rotation by 4k degrees about (118, 78), then the rounded clip (20 + 8k, 14 + 6k,
    ///   180 - 10k, 120 - 12k) of radius 30 + 4k; none is inside another. Fill (0, 0, 236, 156) with ink; pop as many times as
    ///   pushed. Read every pixel.
    ///
    /// Expected:
    /// - Ink times the product of the clips' coverages at the pixel (each radius fitted to half the shorter side, as CSS fits it),
    ///   over the clear color, each channel within 1; one layer.
    /// </remarks>
    [Fact]
    public void Clip_PastTheLimit_IsALayer() =>
        ui.Invoke(() =>
        {
            const int Count = DisplayList.MaxRoundedClips + 1;
            ShapeRenderTests.Compare(
                list =>
                {
                    for (int k = 0; k < Count; k++)
                    {
                        list.PushTransform(Matrix3x2.CreateRotation(4 * k * MathF.PI / 180, new Vector2(118, 78)));
                        list.PushClip(20 + (8 * k), 14 + (6 * k), 180 - (10 * k), 120 - (12 * k), new CornerRadii(30 + (4 * k)));
                    }

                    list.FillRect(0, 0, 236, 156, Ink);
                    for (int k = 0; k < 2 * Count; k++)
                    {
                        list.Pop();
                    }
                },
                Window,
                p =>
                {
                    float coverage = 1;
                    Matrix3x2 transform = Matrix3x2.Identity;
                    for (int k = 0; k < Count; k++)
                    {
                        transform = Matrix3x2.CreateRotation(4 * k * MathF.PI / 180, new Vector2(118, 78)) * transform;
                        Matrix3x2.Invert(transform, out Matrix3x2 back);

                        // CSS fits a radius past half the shorter side to it (with five clips, the last one's 46 to 36).
                        var rect = new Vector4(20 + (8 * k), 14 + (6 * k), 180 - (10 * k), 120 - (12 * k));
                        float radius = MathF.Min(30 + (4 * k), MathF.Min(rect.Z, rect.W) / 2);
                        coverage *= ShapeReference.Coverage(Vector2.Transform(p, back), rect, new Vector4(radius));
                    }

                    return ShapeReference.Premultiply(Ink) * coverage;
                },
                tolerance: 1,
                check: renderer => Assert.Equal(1, renderer.LayerCount)
            );
        });

    /// <summary>
    /// A group whose children overlap, in a rotation and a rounded clip, is a layer cut to the clip: its pixels are the children
    /// over one another times the group's alpha and the clip's coverage, and its texture is no larger than the clip's bounds.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Push the rotation by 15 degrees about (118, 78); push the clip (68, 48, 100, 60) of radius 16; push opacity 0.5; fill
    ///   (0, 0, 236, 120) with ink and (40, 40, 160, 100) with red of alpha 128; pop three times. Read every pixel.
    ///
    /// Expected:
    /// - (red over ink) times 0.5 times the clip's coverage at the pixel turned back, over the clear color, each channel within 1;
    ///   one layer, whose texture holds at most 192 x 128 pixels (the turned clip's bounds rounded up to 64).
    /// </remarks>
    [Fact]
    public void Group_InAClipAndARotation_IsALayerCutToTheClip() =>
        ui.Invoke(() =>
        {
            Matrix3x2 rotation = Matrix3x2.CreateRotation(15 * MathF.PI / 180, new Vector2(118, 78));
            Matrix3x2.Invert(rotation, out Matrix3x2 back);
            ShapeRenderTests.Compare(
                list =>
                {
                    list.PushTransform(rotation);
                    list.PushClip(68, 48, 100, 60, new CornerRadii(16));
                    list.PushOpacity(0.5f);
                    list.FillRect(0, 0, 236, 120, Ink);
                    list.FillRect(40, 40, 160, 100, HalfRed);
                    list.Pop();
                    list.Pop();
                    list.Pop();
                },
                Window,
                p =>
                {
                    Vector2 q = Vector2.Transform(p, back);
                    Vector4 children = Over(
                        Inside(q, new Vector4(40, 40, 160, 100)) ? ShapeReference.Premultiply(HalfRed) : Vector4.Zero,
                        Inside(q, new Vector4(0, 0, 236, 120)) ? ShapeReference.Premultiply(Ink) : Vector4.Zero
                    );
                    return children * 0.5f * ShapeReference.Coverage(q, new Vector4(68, 48, 100, 60), new Vector4(16));
                },
                tolerance: 1,
                check: renderer =>
                    Assert.True(
                        renderer.LayerCount == 1 && renderer.LayerBytes <= 192 * 128 * 4,
                        $"{renderer.LayerCount} layers of {renderer.LayerBytes} bytes"
                    )
            );
        });

    /// <summary>
    /// Text turned by a right angle about a whole pixel is sampled between texels where every pixel's center falls on a texel's:
    /// its pixels are those of the same text drawn without the turn, turned.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - WARP renderers, the test font, an atlas, and a display list each.
    ///
    /// Steps:
    /// - Two frames of 120 x 120: "H日" in grayscale, 32 px, ink, at (10.25, 50), over paper (0, 0, 120, 120); and the same with the
    ///   text in the rotation by 90 degrees about (60, 60). Read both frames' pixels.
    ///
    /// Expected:
    /// - Each pixel (x, y) of the turned frame is the pixel (y, 119 - x) of the plain one, each channel within 1.
    /// </remarks>
    [Fact]
    public void Transform_RightAngle_TurnsTheText()
    {
        Color[] plain = TurnedTextFrame(turned: false);
        Color[] turned = TurnedTextFrame(turned: true);
        for (int y = 0; y < 120; y++)
        {
            for (int x = 0; x < 120; x++)
            {
                AssertNear(plain[((119 - x) * 120) + y], turned[(y * 120) + x], x, y);
            }
        }
    }

    /// <summary>
    /// Text in a scale of 2 is drawn at twice its size in the atlas, at the text's scale: its pixels are those of the text drawn at
    /// twice the size and twice the position with no transform.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - WARP renderers, the test font, an atlas, and a display list each.
    ///
    /// Steps:
    /// - Two frames of 220 x 120: "H日" in grayscale, 20 px, ink, at (5.25, 25) in the scale by 2 about the origin; and the same at
    ///   40 px at (10.5, 50) with no transform. Read both frames' pixels; and the first frame's state.
    ///
    /// Expected:
    /// - The two frames' pixels are the same, each channel within 1; the state's text scale is 2.
    /// </remarks>
    [Fact]
    public void Transform_Scale_DrawsTheTextLarger()
    {
        Color[] scaled = ScaledTextFrame(scaled: true);
        Color[] large = ScaledTextFrame(scaled: false);
        for (int y = 0; y < 120; y++)
        {
            for (int x = 0; x < 220; x++)
            {
                AssertNear(large[(y * 220) + x], scaled[(y * 220) + x], x, y);
            }
        }
    }

    /// <summary>
    /// ClearType text in a move by whole physical pixels stays ClearType and is the same text moved; in a move by part of a pixel
    /// it is grayscale.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - WARP renderers with ClearType text rendering, the test font, an atlas, and a display list each.
    ///
    /// Steps:
    /// - Frames of 200 x 100 of paper (0, 0, 200, 100) and "H日" in ClearType, 32 px, ink, at (10.25, 50): with no move; in the move
    ///   by (30, 20); and in the move by (30.5, 20). Read the first two frames' pixels, and every frame's runs.
    ///
    /// Expected:
    /// - The moved frame's pixel (x + 30, y + 20) is the plain frame's (x, y), each channel within 1. The runs of the first two are
    ///   ClearType, those of the third grayscale.
    /// </remarks>
    [Fact]
    public void Transform_WholePixelMove_KeepsClearType()
    {
        Color[] plain = MovedTextFrame(Vector2.Zero, GlyphCoverage.Rgb);
        Color[] moved = MovedTextFrame(new Vector2(30, 20), GlyphCoverage.Rgb);
        _ = MovedTextFrame(new Vector2(30.5f, 20), GlyphCoverage.Gray);
        for (int y = 0; y < 80; y++)
        {
            for (int x = 0; x < 170; x++)
            {
                AssertNear(plain[(y * 200) + x], moved[((y + 20) * 200) + x + 30], x, y);
            }
        }
    }

    /// <summary>
    /// A frame with clips, rounded clips, a clip's layer, transforms, text in a transform, and a group in a clip allocates nothing
    /// once its textures and streams are made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, the test font, an atlas, and a display list.
    ///
    /// Steps:
    /// - 20 frames to warm up, then 100 frames: reset; ten rotated, rounded clips of a rectangle and a line each; a rectangle clip
    ///   of text; six nested rounded clips; a group of two overlapping rectangles in a clip; resolve, draw, and present. Measure the
    ///   bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes; the list did not grow.
    /// </remarks>
    [Fact]
    public void SteadyState_ClipsAndTransforms_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("steady clips", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            ShapedGlyph[] text = shaper.Shape(font, "H日", Japanese).ToArray();
            void Frame(int frame)
            {
                atlas.BeginFrame();
                list.Reset();
                for (int k = 0; k < 10; k++)
                {
                    list.PushTransform(Matrix3x2.CreateRotation(0.1f * k, new Vector2(150, 100)));
                    list.PushClip(10 * k, 10, 60, 40, new CornerRadii(8));
                    list.FillRect(10 * k, 10, 80, 50, Ink);
                    list.DrawLine(10 * k, 10, (10 * k) + 50, 50, new Stroke(Paper, 2));
                    list.Pop();
                    list.Pop();
                }

                list.PushClip(20, 100, 120, 60, default);
                list.PushTransform(Matrix3x2.CreateScale(1.5f));
                _ = list.DrawText(text, font, 20, atlas, 15, 90, Ink);
                list.Pop();
                list.Pop();
                for (int k = 0; k <= DisplayList.MaxRoundedClips; k++)
                {
                    list.PushTransform(Matrix3x2.CreateRotation(0.05f * k, new Vector2(150, 100)));
                    list.PushClip(150 + (4 * k), 20 + (4 * k), 120, 80, new CornerRadii(20 + k));
                }

                list.FillRect(140, 10, 150, 100, HalfRed);
                for (int k = 0; k <= DisplayList.MaxRoundedClips; k++)
                {
                    list.Pop();
                    list.Pop();
                }

                list.PushClip(200, 120, 80, 60, new CornerRadii(10));
                list.PushOpacity(0.5f);
                list.FillRect(190, 110, 60, 60, Ink);
                list.FillRect(220, 130, 60, 60, Paper);
                list.Pop();
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
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                Frame(i);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(growth, list.GrowthCount);
        });

    private static bool Inside(Vector2 p, Vector4 rect) => p.X >= rect.X && p.X < rect.X + rect.Z && p.Y >= rect.Y && p.Y < rect.Y + rect.W;

    private static float Radians(float degrees) => degrees * MathF.PI / 180;

    private static Vector4 Over(Vector4 top, Vector4 bottom) => top + (bottom * (1 - top.W));

    // A solid color covering what a curve covers at the distance d, with the stroke's width (0 for a fill).
    private static Vector4 Solid(Color color, float distance, float width) =>
        ShapeReference.Premultiply(color) * CurveReference.Coverage(distance, width);

    // The rounded rectangle of paper with an ink border at the point q before a transform that scales distances by `scale`.
    private static Vector4 ScaledShape(Vector2 q, Vector4 rect, float radius, float border, float scale)
    {
        float outer = Math.Clamp(0.5f - (scale * ShapeReference.Distance(q, rect, new Vector4(radius))), 0, 1);
        var innerRect = new Vector4(rect.X + border, rect.Y + border, rect.Z - (2 * border), rect.W - (2 * border));
        float inner = Math.Clamp(0.5f - (scale * ShapeReference.Distance(q, innerRect, new Vector4(radius - border))), 0, 1);
        Vector4 ink = ShapeReference.Premultiply(Ink);
        Vector4 paper = ShapeReference.Premultiply(Paper);
        Vector4 fill = Vector4.Lerp(ink + (paper * (1 - ink.W)), paper, inner);
        return fill * outer;
    }

    private static Color Lerp(Color without, Color with, float coverage) =>
        new(
            (byte)MathF.Round(without.R + ((with.R - without.R) * coverage)),
            (byte)MathF.Round(without.G + ((with.G - without.G) * coverage)),
            (byte)MathF.Round(without.B + ((with.B - without.B) * coverage)),
            255
        );

    private static void AssertNear(Color expected, Color actual, int x, int y)
    {
        bool near = Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1;
        Assert.True(near, $"pixel ({x}, {y}): expected {expected}, got {actual}");
    }

    // A frame like ShapeRenderTests.Compare's at another scale, compared over the whole window.
    private void CompareAt(float scale, Action<DisplayList> write, Func<Vector2, Vector4> reference) =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("clips at scale", 240, 160);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            list.Reset(scale);
            write(list);
            renderer.Draw(list, Clear);
            Color[] pixels = Capture(renderer, width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    AssertNear(ShapeReference.Over(reference(new Vector2(x + 0.5f, y + 0.5f)), Clear), pixels[(y * width) + x], x, y);
                }
            }

            renderer.Present();
        });

    // The frame of Clip_GlyphsOfEachKind_AreCut: paper, with the text or not, inside the clip of radius 36 when there is one.
    private Color[] TextFrame(int width, int height, bool text, Vector4? clip)
    {
        Color[] pixels = [];
        ui.Invoke(() =>
        {
            using var window = new Win32Window("clipped text", 400, 300);
            using var renderer = D3D11Renderer.Create(window.Handle, window.ClientSize.Width, window.ClientSize.Height, useWarp: true);
            renderer.TextRendering = new TextRenderingParameters(
                GlyphCoverage.Rgb,
                false,
                Gamma: 1.8f,
                GrayscaleContrast: 1.0f,
                ClearTypeContrast: 0.5f
            );
            using var font = new Font(FontBytes);
            using Font? emoji = File.Exists(EmojiPath) ? Font.FromFile(EmojiPath, 0) : null;
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            list.FillRect(0, 0, width, height, Paper);
            if (text)
            {
                if (clip is { } edges)
                {
                    list.PushClip(edges.X, edges.Y, edges.Z, edges.W, new CornerRadii(36));
                }

                _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 40, atlas, 10.25f, 50, Ink);
                _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 40, atlas, 10.25f, 100, Ink, GlyphCoverage.Rgb);
                if (emoji is not null)
                {
                    _ = list.DrawText(shaper.Shape(emoji, "😀", Japanese), emoji, 40, atlas, 140, 80, Ink);
                }

                if (clip is not null)
                {
                    list.Pop();
                }
            }

            list.ResolveGlyphs(atlas);
            AssertRuns(list, expectClearType: text);
            renderer.Draw(list, Clear, atlas);
            pixels = Capture(renderer, width, height);
            renderer.Present();
        });
        return pixels;
    }

    // The frame of Transform_RightAngle_TurnsTheText.
    private Color[] TurnedTextFrame(bool turned)
    {
        Color[] pixels = [];
        ui.Invoke(() =>
        {
            using var window = new Win32Window("turned text", 300, 200);
            using var renderer = D3D11Renderer.Create(window.Handle, window.ClientSize.Width, window.ClientSize.Height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            list.FillRect(0, 0, 120, 120, Paper);
            if (turned)
            {
                list.PushTransform(Matrix3x2.CreateRotation(MathF.PI / 2, new Vector2(60, 60)));
            }

            _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 32, atlas, 10.25f, 50, Ink);
            if (turned)
            {
                list.Pop();
            }

            list.ResolveGlyphs(atlas);
            renderer.Draw(list, Clear, atlas);
            pixels = Capture(renderer, 120, 120);
            renderer.Present();
        });
        return pixels;
    }

    // The frame of Transform_Scale_DrawsTheTextLarger.
    private Color[] ScaledTextFrame(bool scaled)
    {
        Color[] pixels = [];
        ui.Invoke(() =>
        {
            using var window = new Win32Window("scaled text", 300, 200);
            using var renderer = D3D11Renderer.Create(window.Handle, window.ClientSize.Width, window.ClientSize.Height, useWarp: true);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            list.FillRect(0, 0, 220, 120, Paper);
            if (scaled)
            {
                list.PushTransform(Matrix3x2.CreateScale(2));
                _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 20, atlas, 5.25f, 25, Ink);
                list.Pop();
            }
            else
            {
                _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 40, atlas, 10.5f, 50, Ink);
            }

            list.ResolveGlyphs(atlas);
            if (scaled)
            {
                DisplayListReader reader = list.GetReader();
                while (reader.TryNext(out CommandTag tag))
                {
                    if (tag == CommandTag.PushState)
                    {
                        Assert.Equal(2, reader.ReadState(out _).TextScale);
                    }
                    else
                    {
                        Skip(ref reader, tag);
                    }
                }
            }

            renderer.Draw(list, Clear, atlas);
            pixels = Capture(renderer, 220, 120);
            renderer.Present();
        });
        return pixels;
    }

    // The frame of Transform_WholePixelMove_KeepsClearType, whose runs are `coverage`.
    private Color[] MovedTextFrame(Vector2 move, GlyphCoverage coverage)
    {
        Color[] pixels = [];
        ui.Invoke(() =>
        {
            using var window = new Win32Window("moved text", 300, 200);
            using var renderer = D3D11Renderer.Create(window.Handle, window.ClientSize.Width, window.ClientSize.Height, useWarp: true);
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
            list.PushTransform(Matrix3x2.CreateTranslation(move));
            _ = list.DrawText(shaper.Shape(font, "H日", Japanese), font, 32, atlas, 10.25f, 50, Ink, GlyphCoverage.Rgb);
            list.Pop();
            list.ResolveGlyphs(atlas);
            DisplayListReader reader = list.GetReader();
            while (reader.TryNext(out CommandTag tag))
            {
                if (tag == CommandTag.GlyphRun)
                {
                    Assert.Equal(coverage, reader.ReadGlyphRun().Coverage);
                }
                else
                {
                    Skip(ref reader, tag);
                }
            }

            renderer.Draw(list, Clear, atlas);
            pixels = Capture(renderer, 200, 100);
            renderer.Present();
        });
        return pixels;
    }

    // The runs of a list of text: one of them ClearType when the text asked for it, as a clip does not make text grayscale.
    private static void AssertRuns(DisplayList list, bool expectClearType)
    {
        bool clearType = false;
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.GlyphRun)
            {
                clearType |= reader.ReadGlyphRun().Coverage == GlyphCoverage.Rgb;
            }
            else
            {
                Skip(ref reader, tag);
            }
        }

        Assert.Equal(expectClearType, clearType);
    }

    private static void Skip(ref DisplayListReader reader, CommandTag tag)
    {
        switch (tag)
        {
            case CommandTag.FillRect:
                _ = reader.ReadFillRect();
                break;
            case CommandTag.GlyphRun:
                _ = reader.ReadGlyphRun();
                break;
            case CommandTag.PushState:
                _ = reader.ReadState(out _);
                break;
            case CommandTag.PushOpacity:
                _ = reader.ReadGroup();
                break;
            default:
                reader.ReadPop();
                break;
        }
    }

    private static Color[] Capture(D3D11Renderer renderer, int width, int height)
    {
        ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
        try
        {
            return renderer.ReadCapturedPixels(capture, width, height);
        }
        finally
        {
            _ = capture->Release();
        }
    }
}
