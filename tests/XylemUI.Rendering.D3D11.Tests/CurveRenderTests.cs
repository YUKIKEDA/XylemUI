using System.Numerics;
using XylemUI.Platform.Windows;

namespace XylemUI.Rendering.D3D11.Tests;

// Lines, ellipses, arcs, and sectors on the D3D11 renderer (roadmap row R4b, design §5.0, "The API of lines, ellipses, arcs, and
// the paint of text"). The pixels are read back on WARP and compared with CurveReference over the clear color, at scale 1, where
// logical pixels are physical ones.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed class CurveRenderTests(UiThreadFixture ui)
{
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color Paper = Color.FromRgb(0xF0F0F0);
    private static readonly Color HalfRed = new(0xE0, 0x20, 0x20, 128);
    private static readonly Vector4 Window = new(0, 0, 236, 156);

    /// <summary>
    /// A line is its segment with its caps: flat ones end at the ends, square ones half the width past them, round ones are half
    /// discs; a line of length 0 is only its caps, so nothing with flat ones.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Draw a paper line (30, 40) to (150, 110), 14 wide, with the case's cap, and a paper line of length 0 at (200, 40), 16
    ///   wide, with the same cap; read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference's two lines over the clear color, each channel within 1.
    /// </remarks>
    /// <param name="cap">The cap of both lines.</param>
    [Theory]
    [InlineData(LineCap.Flat)]
    [InlineData(LineCap.Round)]
    [InlineData(LineCap.Square)]
    public void Line_EachCap_IsItsSegmentAndCaps(LineCap cap) =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.DrawLine(30, 40, 150, 110, new Stroke(Paper, 14, cap));
                    list.DrawLine(200, 40, 200, 40, new Stroke(Paper, 16, cap));
                },
                Window,
                p =>
                    Solid(Paper, CurveReference.Line(p, new Vector2(30, 40), new Vector2(150, 110), 7, cap, null), 14)
                    + Solid(Paper, CurveReference.Line(p, new Vector2(200, 40), new Vector2(200, 40), 8, cap, null), 16),
                tolerance: 1
            )
        );

    /// <summary>
    /// A line thinner than a pixel is drawn lighter, by the share of the pixel it covers, not thinner.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Draw paper lines 1, 0.5, and 0.25 wide from (20, 20.3) to (220, 20.3), (20, 40.5) to (220, 40.5), and (20, 60.8) to
    ///   (220, 60.8), and a 0.5 wide one from (20, 80) to (220, 140); read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference over the clear color, each channel within 1; the 0.25 wide line is at most a quarter as dark
    ///   as paper over the clear color plus a step, on every pixel.
    /// </remarks>
    [Fact]
    public void Line_ThinnerThanAPixel_IsLighter() =>
        ui.Invoke(() =>
        {
            (Vector2 A, Vector2 B, float Width)[] lines =
            [
                (new(20, 20.3f), new(220, 20.3f), 1),
                (new(20, 40.5f), new(220, 40.5f), 0.5f),
                (new(20, 60.8f), new(220, 60.8f), 0.25f),
                (new(20, 80), new(220, 140), 0.5f),
            ];
            Vector4 Reference(Vector2 p)
            {
                Vector4 color = Vector4.Zero;
                foreach ((Vector2 a, Vector2 b, float width) in lines)
                {
                    color += Solid(Paper, CurveReference.Line(p, a, b, width / 2, LineCap.Flat, null), width);
                }

                return color;
            }

            _ = ShapeRenderTests.Compare(
                list =>
                {
                    foreach ((Vector2 a, Vector2 b, float width) in lines)
                    {
                        list.DrawLine(a.X, a.Y, b.X, b.Y, new Stroke(Paper, width));
                    }
                },
                Window,
                Reference,
                tolerance: 1
            );
            Assert.True(Reference(new Vector2(100.5f, 60.5f)).W <= 0.25f + (1 / 255f));
        });

    /// <summary>
    /// A dashed line is its dashes, each with the caps, from the first end and moved by the offset; overlapping caps of a
    /// translucent line are painted once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Draw a line from (20, 30) to (220, 120) in the case's color, width, cap, and dash pattern; read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference's dashes over the clear color, each channel within 1.
    /// </remarks>
    /// <param name="cap">The cap.</param>
    /// <param name="width">The width.</param>
    /// <param name="on">The dash's length.</param>
    /// <param name="off">The gap's length.</param>
    /// <param name="offset">The pattern's offset.</param>
    /// <param name="translucent">Whether the line is red with alpha 128 rather than paper.</param>
    [Theory]
    [InlineData(LineCap.Flat, 8, 12, 8, 5, false)]
    [InlineData(LineCap.Round, 8, 0, 10, 0, false)]
    [InlineData(LineCap.Square, 6, 6, 4, -3, false)]
    [InlineData(LineCap.Round, 10, 4, 3, 0, true)]
    public void Line_Dashed_IsItsDashes(LineCap cap, float width, float on, float off, float offset, bool translucent) =>
        ui.Invoke(() =>
        {
            Color color = translucent ? HalfRed : Paper;
            var dash = new Dash(on, off, offset);
            ShapeRenderTests.Compare(
                list => list.DrawLine(20, 30, 220, 120, new Stroke(color, width, cap, dash)),
                Window,
                p => Solid(color, CurveReference.Line(p, new Vector2(20, 30), new Vector2(220, 120), width / 2, cap, dash), width),
                tolerance: 1
            );
        });

    /// <summary>
    /// An ellipse is filled or outlined at its true distance: a thick outline of a long ellipse is as wide everywhere, an outline
    /// whose half width passes the radius has no hole, and a thin one is lighter.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Fill an ellipse at (60, 45) with radii 45 and 25; outline one at (170, 45), radii 55 and 15, 12 wide; outline a circle at
    ///   (40, 120), radius 6, 16 wide; outline an ellipse at (150, 120), radii 60 and 20, 0.5 wide; all in paper. Read every pixel
    ///   of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference's shapes over the clear color, each channel within 1.
    /// </remarks>
    [Fact]
    public void Ellipse_FilledAndOutlined_IsAtItsTrueDistance() =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.FillEllipse(60, 45, 45, 25, Paper);
                    list.DrawEllipse(170, 45, 55, 15, new Stroke(Paper, 12));
                    list.DrawEllipse(40, 120, 6, 6, new Stroke(Paper, 16));
                    list.DrawEllipse(150, 120, 60, 20, new Stroke(Paper, 0.5f));
                },
                Window,
                p =>
                    Solid(Paper, CurveReference.Ellipse(p, new Vector2(60, 45), new Vector2(45, 25)), 0)
                    + Solid(Paper, MathF.Abs(CurveReference.Ellipse(p, new Vector2(170, 45), new Vector2(55, 15))) - 6, 12)
                    + Solid(Paper, MathF.Abs(CurveReference.Ellipse(p, new Vector2(40, 120), new Vector2(6, 6))) - 8, 16)
                    + Solid(Paper, MathF.Abs(CurveReference.Ellipse(p, new Vector2(150, 120), new Vector2(60, 20))) - 0.25f, 0.5f),
                tolerance: 1
            )
        );

    /// <summary>
    /// An ellipse 100 times as wide as it is tall, and one 100 times as tall as it is wide, are still at their true distance: five
    /// Newton steps converge at that ratio.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Fill an ellipse at (118, 15) with radii 110 and 1.1; outline one at (118, 35), radii 110 and 1.1, 3 wide; outline one at
    ///   (30, 100), radii 0.5 and 45, 2 wide; fill one at (60, 100), radii 0.5 and 45. Read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference's shapes over the clear color, each channel within 1.
    /// </remarks>
    [Fact]
    public void Ellipse_HundredToOne_IsAtItsTrueDistance() =>
        ui.Invoke(() =>
            ShapeRenderTests.Compare(
                list =>
                {
                    list.FillEllipse(118, 15, 110, 1.1f, Paper);
                    list.DrawEllipse(118, 35, 110, 1.1f, new Stroke(Paper, 3));
                    list.DrawEllipse(30, 100, 0.5f, 45, new Stroke(Paper, 2));
                    list.FillEllipse(60, 100, 0.5f, 45, Paper);
                },
                Window,
                p =>
                    Solid(Paper, CurveReference.Ellipse(p, new Vector2(118, 15), new Vector2(110, 1.1f)), 0)
                    + Solid(Paper, MathF.Abs(CurveReference.Ellipse(p, new Vector2(118, 35), new Vector2(110, 1.1f))) - 1.5f, 3)
                    + Solid(Paper, MathF.Abs(CurveReference.Ellipse(p, new Vector2(30, 100), new Vector2(0.5f, 45))) - 1, 2)
                    + Solid(Paper, CurveReference.Ellipse(p, new Vector2(60, 100), new Vector2(0.5f, 45)), 0),
                tolerance: 1
            )
        );

    /// <summary>
    /// An arc runs from its start, clockwise for a positive sweep and counterclockwise for a negative one, with its caps and its
    /// dashes laid in its own direction; a sweep of a turn or more is a circle, and a sweep of 0 is only the caps.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Draw a paper arc at (70, 78) with radius 48, 12 wide, with the case's start, sweep, cap, and dashes, and one at (180, 78)
    ///   with radius 40, 30 wide; read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference's arcs over the clear color, each channel within 1.
    /// </remarks>
    /// <param name="start">The start, in degrees.</param>
    /// <param name="sweep">The sweep, in degrees.</param>
    /// <param name="cap">The cap.</param>
    /// <param name="on">The dash's length, or 0 with <paramref name="off"/> 0 for no dashes.</param>
    /// <param name="off">The gap's length.</param>
    /// <param name="offset">The pattern's offset.</param>
    [Theory]
    [InlineData(0, 270, LineCap.Round, 0, 0, 0)]
    [InlineData(90, -120, LineCap.Square, 0, 0, 0)]
    [InlineData(200, 450, LineCap.Flat, 0, 0, 0)]
    [InlineData(30, 0, LineCap.Round, 0, 0, 0)]
    [InlineData(300, -200, LineCap.Round, 8, 5, 2)]
    [InlineData(10, 360, LineCap.Flat, 10, 6, 3)]
    [InlineData(-45, 135, LineCap.Square, 9, 7, -4)]
    public void Arc_StartSweepCapsAndDashes_IsItsArc(float start, float sweep, LineCap cap, float on, float off, float offset) =>
        ui.Invoke(() =>
        {
            Dash? dash = on + off > 0 ? new Dash(on, off, offset) : null;
            float startRadians = start * MathF.PI / 180;
            float sweepRadians = sweep * MathF.PI / 180;
            ShapeRenderTests.Compare(
                list =>
                {
                    list.DrawArc(70, 78, 48, start, sweep, new Stroke(Paper, 12, cap, dash));
                    list.DrawArc(180, 78, 40, start, sweep, new Stroke(Paper, 30, cap, dash));
                },
                Window,
                p =>
                    Solid(Paper, CurveReference.Arc(p, new Vector2(70, 78), 48, startRadians, sweepRadians, 6, cap, dash), 12)
                    + Solid(Paper, CurveReference.Arc(p, new Vector2(180, 78), 40, startRadians, sweepRadians, 15, cap, dash), 30),
                tolerance: 1
            );
        });

    /// <summary>
    /// A dashed circle (an outline with equal radii) is a dashed arc of a whole turn from the top.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Outline a circle at (110, 78) with radius 60, 8 wide, dashes 14 and 6 with offset 4 and round caps, in red with alpha 128;
    ///   read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference's arc from 0 degrees for 360 degrees over the clear color, each channel within 1.
    /// </remarks>
    [Fact]
    public void Ellipse_DashedCircle_IsADashedArcFromTheTop() =>
        ui.Invoke(() =>
        {
            var dash = new Dash(14, 6, 4);
            ShapeRenderTests.Compare(
                list => list.DrawEllipse(110, 78, 60, 60, new Stroke(HalfRed, 8, LineCap.Round, dash)),
                Window,
                p => Solid(HalfRed, CurveReference.Arc(p, new Vector2(110, 78), 60, 0, MathF.Tau, 4, LineCap.Round, dash), 8),
                tolerance: 1
            );
        });

    /// <summary>
    /// A sector is the part of its circle between the center and its arc, for a sweep under and over half a turn and a negative one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Fill paper sectors at (45, 78) with radius 40 from 30 degrees for 100, at (122, 78) radius 35 from 200 degrees for 250, and
    ///   at (198, 78) radius 36 from 60 degrees for -150; read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference's sectors over the clear color, each channel within 1.
    /// </remarks>
    [Fact]
    public void Sector_UnderAndOverHalfATurn_IsItsPart() =>
        ui.Invoke(() =>
        {
            const float Degree = MathF.PI / 180;
            ShapeRenderTests.Compare(
                list =>
                {
                    list.FillSector(45, 78, 40, 30, 100, Paper);
                    list.FillSector(122, 78, 35, 200, 250, Paper);
                    list.FillSector(198, 78, 36, 60, -150, Paper);
                },
                Window,
                p =>
                    Solid(Paper, CurveReference.Sector(p, new Vector2(45, 78), 40, 30 * Degree, 100 * Degree), 0)
                    + Solid(Paper, CurveReference.Sector(p, new Vector2(122, 78), 35, 200 * Degree, 250 * Degree), 0)
                    + Solid(Paper, CurveReference.Sector(p, new Vector2(198, 78), 36, 60 * Degree, -150 * Degree), 0),
                tolerance: 1
            );
        });

    /// <summary>
    /// A gradient on a shape is over the box design §5.0 gives it: a fill's box is the shape's (the whole circle's for a sector), a
    /// stroke's that grown by half the width, and a line's the box of its two ends grown by half the width.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Draw a horizontal line from (20, 20) to (220, 20), 10 wide, with a linear gradient at 90 degrees; outline an ellipse at
    ///   (70, 95) with radii 50 and 35, 10 wide, with a radial one; fill a sector at (180, 95) with radius 45 from 0 degrees for 270
    ///   with a conic one. Read every pixel of the window.
    ///
    /// Expected:
    /// - Each pixel is the reference's coverage of the gradient over its box, over the clear color, each channel within 2 (the
    ///   dither adds at most a step of 8 bits, as R4a's gradients).
    /// </remarks>
    [Fact]
    public void Gradient_OnShapesAndStrokes_IsOverItsBox() =>
        ui.Invoke(() =>
        {
            GradientBrush linear = ShapeRenderTests.Brush("linear 90");
            GradientBrush radial = ShapeRenderTests.Brush("radial");
            GradientBrush conic = ShapeRenderTests.Brush("conic");
            ShapeRenderTests.Compare(
                list =>
                {
                    list.DrawLine(20, 20, 220, 20, new Stroke(linear, 10));
                    list.DrawEllipse(70, 95, 50, 35, new Stroke(radial, 10));
                    list.FillSector(180, 95, 45, 0, 270, conic);
                },
                Window,
                p =>
                    (
                        ShapeReference.Gradient(linear, p, new Vector4(15, 15, 210, 10))
                        * CurveReference.Coverage(CurveReference.Line(p, new Vector2(20, 20), new Vector2(220, 20), 5, LineCap.Flat, null), 10)
                    )
                    + (
                        ShapeReference.Gradient(radial, p, new Vector4(15, 55, 110, 80))
                        * CurveReference.Coverage(MathF.Abs(CurveReference.Ellipse(p, new Vector2(70, 95), new Vector2(50, 35))) - 5, 10)
                    )
                    + (
                        ShapeReference.Gradient(conic, p, new Vector4(135, 50, 90, 90))
                        * CurveReference.Coverage(CurveReference.Sector(p, new Vector2(180, 95), 45, 0, 1.5f * MathF.PI), 0)
                    ),
                tolerance: 2
            );
        });

    /// <summary>
    /// A frame of lines, ellipses, arcs, and sectors, solid, dashed, and with gradients, allocates nothing once the streams have
    /// grown.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, two gradients, and a display list.
    ///
    /// Steps:
    /// - 20 frames to warm up, then 100 frames: reset the list, write 30 of each (a dashed line, a filled ellipse with a gradient, an
    ///   outlined one, a dashed circle, an arc with a gradient, and a sector); draw and present. Measure the bytes allocated on this
    ///   thread.
    ///
    /// Expected:
    /// - 0 bytes, and the list did not grow.
    /// </remarks>
    [Fact]
    public void SteadyState_Curves_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("steady curves", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            GradientBrush linear = ShapeRenderTests.Brush("linear 45");
            GradientBrush conic = ShapeRenderTests.Brush("conic");
            var dashed = new Stroke(Ink, 3, LineCap.Round, new Dash(6, 4));
            void Frame(int frame)
            {
                list.Reset();
                for (int i = 0; i < 30; i++)
                {
                    float x = (i * 9) + (frame % 3);
                    list.DrawLine(x, 10, x + 20, 40, dashed);
                    list.FillEllipse(x + 10, 60, 10, 6, linear);
                    list.DrawEllipse(x + 10, 90, 10, 6, new Stroke(Paper, 2));
                    list.DrawEllipse(x + 10, 120, 8, 8, dashed);
                    list.DrawArc(x + 10, 150, 8, 30, 200, new Stroke(conic, 3, LineCap.Round));
                    list.FillSector(x + 10, 180, 8, 0, 120, Paper);
                }

                renderer.Draw(list, Clear);
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

    // A solid color's premultiplied value at a pixel at `distance` from a shape, for a stroke of `width` (0 for a fill).
    private static Vector4 Solid(Color color, float distance, float width) =>
        ShapeReference.Premultiply(color) * CurveReference.Coverage(distance, width);
}
