using System.Numerics;
using TerraFX.Interop.DirectX;
using XylemUI.Platform.Windows;

namespace XylemUI.Rendering.D3D11.Tests;

// Rounded rectangles, borders, gradients, and shadows on the D3D11 renderer (roadmap row R4, design §5.0, "Shapes, brushes, and
// shadows"). The pixels are read back on WARP and compared with ShapeReference over the clear color.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class ShapeRenderTests(UiThreadFixture ui)
{
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Paper = Color.FromRgb(0xF0F0F0);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);

    /// <summary>
    /// A rounded rectangle with a different radius at each corner and a border of a different width on each edge is its rounded
    /// shape, filled inside its border.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Fill (20, 10, 120, 70) with paper, corner radii 0, 8, 20, 30, and an ink border 1, 4, 8, 2 wide; read every pixel of the
    ///   rectangle grown by 2.
    ///
    /// Expected:
    /// - Each pixel is the reference's shape over the clear color, each channel within 1.
    /// </remarks>
    [Fact]
    public void Shape_RoundedWithABorder_IsItsShape() =>
        ui.Invoke(() =>
        {
            var rect = new Vector4(20, 10, 120, 70);
            var radii = new Vector4(0, 8, 20, 30);
            var widths = new Vector4(1, 4, 8, 2);
            Compare(
                list => list.FillRect(20, 10, 120, 70, Paper, new CornerRadii(0, 8, 20, 30), new Border(new BorderWidths(1, 4, 8, 2), Ink)),
                rect,
                p => ShapeReference.Shape(p, rect, radii, ShapeReference.Premultiply(Paper), widths, ShapeReference.Premultiply(Ink)),
                tolerance: 1
            );
        });

    /// <summary>
    /// Each kind of gradient fills its shape with the colors at the fractions of the shape's box that design §5.0 gives.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Fill a 60 x 40 rectangle with <paramref name="kind"/> of red to blue (or the case's stops), and read it back.
    ///
    /// Expected:
    /// - Each pixel is the reference's gradient over the clear color, each channel within 2: the dither adds at most one step of 8
    ///   bits, and the rounding one more.
    /// </remarks>
    /// <param name="kind">The case.</param>
    [Theory]
    [InlineData("linear 0")]
    [InlineData("linear 45")]
    [InlineData("linear 90")]
    [InlineData("radial")]
    [InlineData("conic")]
    [InlineData("oklab")]
    [InlineData("fade")]
    [InlineData("hard")]
    public void Gradient_EachKind_IsItsColors(string kind)
    {
        int largest = 0;
        ui.Invoke(() =>
        {
            GradientBrush brush = Brush(kind);
            var rect = new Vector4(30, 20, 60, 40);
            largest = Compare(
                list => list.FillRect(30, 20, 60, 40, brush, default),
                rect,
                p => ShapeReference.Gradient(brush, p, rect) * ShapeReference.Coverage(p, rect, Vector4.Zero),
                tolerance: 2
            );
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{kind}: largest difference {largest}");
    }

    /// <summary>
    /// A shadow is the Gaussian blur of its rounded rectangle, with CSS's sigma (half the blur radius), and an outer shadow is not
    /// drawn under its shape.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer and a display list at scale 1.
    ///
    /// Steps:
    /// - Draw the shadow of (60, 30, 80, 50) with corner radius 12, in black at alpha 160: <paramref name="kind"/> (outer, moved
    ///   by (6, 4), blur 16, spread 3; or inset, moved by (4, 6), blur 12, spread 2); read the pixels of the quad.
    ///
    /// Expected:
    /// - Each pixel is within 1 of the reference, which integrates the Gaussian along y numerically where the shader takes four
    ///   samples and approximates erf (1 for both cases when written); the largest difference is written in the test's output.
    /// - For the outer shadow, the pixels well inside the shape are the clear color.
    /// </remarks>
    /// <param name="kind">"outer" or "inset".</param>
    [Theory]
    [InlineData("outer")]
    [InlineData("inset")]
    public void Shadow_OuterAndInset_IsTheBlurOfItsShape(string kind)
    {
        int largest = 0;
        ui.Invoke(() =>
        {
            bool inset = kind == "inset";
            var color = new Color(0, 0, 0, 160);
            var shadow = inset ? new BoxShadow(color, new Vector2(4, 6), 12, 2, Inset: true) : new BoxShadow(color, new Vector2(6, 4), 16, 3);
            var element = new Vector4(60, 30, 80, 50);
            var radii = new Vector4(12);
            float sigma = shadow.BlurRadius / 2;
            float spread = inset ? -shadow.Spread : shadow.Spread;
            var blurred = new Vector4(
                element.X + shadow.Offset.X - spread,
                element.Y + shadow.Offset.Y - spread,
                element.Z + (2 * spread),
                element.W + (2 * spread)
            );
            var blurredRadii = new Vector4(MathF.Max(0, 12 + spread));
            Vector4 area = inset
                ? element
                : new Vector4(blurred.X - (3 * sigma), blurred.Y - (3 * sigma), blurred.Z + (6 * sigma), blurred.W + (6 * sigma));
            largest = Compare(
                list => list.DrawShadow(60, 30, 80, 50, new CornerRadii(12), shadow),
                area,
                p => ShapeReference.Premultiply(color) * ShapeReference.Shadow(p, blurred, blurredRadii, sigma, element, radii, inset),
                tolerance: 1
            );
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{kind} shadow: largest difference {largest}");
    }

    /// <summary>
    /// A steady-state frame of shapes, gradients, and shadows allocates nothing, with its brushes made once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, a display list, and a linear and a conic brush; 20 frames of warm-up.
    ///
    /// Steps:
    /// - 100 frames: reset the list, write 50 rounded rectangles with the two brushes (each brush on many of them), a border, and
    ///   an outer and an inset shadow each; draw and present. Measure the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes, and the list did not grow.
    /// </remarks>
    [Fact]
    public void SteadyState_ShapesAndShadows_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("steady shapes", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            GradientBrush linear = Brush("linear 45");
            GradientBrush conic = Brush("conic");
            void Frame(int frame)
            {
                list.Reset();
                for (int i = 0; i < 50; i++)
                {
                    float x = (i * 13) + (frame % 3);
                    var radii = new CornerRadii(6);
                    list.DrawShadow(x, 20, 40, 30, radii, new BoxShadow(new Color(0, 0, 0, 100), new Vector2(2, 3), 8));
                    list.FillRect(x, 20, 40, 30, (i & 1) == 0 ? linear : conic, radii, new Border(new BorderWidths(1), Ink));
                    list.DrawShadow(x, 20, 40, 30, radii, new BoxShadow(new Color(0, 0, 0, 60), Vector2.One, 4, Inset: true));
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

    private static GradientBrush Brush(string kind)
    {
        GradientStop[] redToBlue = [new(0, Color.FromRgb(0xE02020)), new(1, Color.FromRgb(0x2040E0))];
        return kind switch
        {
            "linear 0" => GradientBrush.Linear(0, redToBlue),
            "linear 45" => GradientBrush.Linear(45, redToBlue),
            "linear 90" => GradientBrush.Linear(
                90,
                [new(0, Color.FromRgb(0xE02020)), new(0.5f, Color.FromRgb(0x20C040)), new(1, Color.FromRgb(0x2040E0))]
            ),
            "radial" => GradientBrush.Radial(new Vector2(0.3f, 0.6f), 0.7f, redToBlue),
            "conic" => GradientBrush.Conic(
                new Vector2(0.5f),
                30,
                [new(0, Color.FromRgb(0xE02020)), new(0.5f, Color.FromRgb(0xF0E020)), new(1, Color.FromRgb(0xE02020))]
            ),
            "oklab" => GradientBrush.Linear(90, redToBlue, GradientColorSpace.Oklab),
            "fade" => GradientBrush.Linear(90, [new(0, Color.FromRgb(0xE02020)), new(1, new Color(0xE0, 0x20, 0x20, 0))]),
            _ => GradientBrush.Linear(
                90,
                [
                    new(0.25f, Color.FromRgb(0xE02020)),
                    new(0.5f, Color.FromRgb(0xE02020)),
                    new(0.5f, Color.FromRgb(0x2040E0)),
                    new(0.75f, Color.FromRgb(0x2040E0)),
                ]
            ),
        };
    }

    // Draws the frame `write` writes over the clear color, reads every pixel of `area` grown by 2, and compares it with the
    // reference over the clear color. Returns the largest difference of a channel.
    private static int Compare(Action<DisplayList> write, Vector4 area, Func<Vector2, Vector4> reference, int tolerance)
    {
        using var window = new Win32Window("shapes", 240, 160);
        (int width, int height) = window.ClientSize;
        using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
        var list = new DisplayList();
        write(list);
        renderer.Draw(list, Clear);
        ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
        int largest = 0;
        try
        {
            for (int y = Math.Max(0, (int)area.Y - 2); y < Math.Min(height, (int)(area.Y + area.W) + 2); y++)
            {
                for (int x = Math.Max(0, (int)area.X - 2); x < Math.Min(width, (int)(area.X + area.Z) + 2); x++)
                {
                    Color expected = ShapeReference.Over(reference(new Vector2(x + 0.5f, y + 0.5f)), Clear);
                    Color actual = renderer.ReadCapturedPixel(capture, x, y);
                    int difference = Math.Max(
                        Math.Abs(expected.R - actual.R),
                        Math.Max(Math.Abs(expected.G - actual.G), Math.Abs(expected.B - actual.B))
                    );
                    largest = Math.Max(largest, difference);
                    Assert.True(difference <= tolerance, $"pixel ({x}, {y}): expected {expected}, got {actual}");
                }
            }
        }
        finally
        {
            _ = capture->Release();
        }

        renderer.Present();
        return largest;
    }
}
