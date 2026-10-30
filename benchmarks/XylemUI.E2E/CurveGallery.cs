using System.Numerics;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;

namespace XylemUI.E2E;

// The lines, ellipses, and arcs of roadmap row R4b, for the owner to compare with a browser (design §5.0, "The API of lines,
// ellipses, arcs, and the paint of text"): benchmarks/compare/curves.html draws the same shapes with SVG at the same logical
// positions. A 600 x 420 window of #F3F3F3 with three rows: lines with the three caps, a diagonal line with a gradient, a line of
// a pixel and one of half a pixel, a dashed line and a dotted one; a filled ellipse, an outlined one, a dashed circle, and a circle
// with a radial gradient; an arc with round caps and a gradient from bottom to top, a counterclockwise arc with square caps, a sector, and a
// translucent dashed circle whose round caps overlap. It draws again on a resize and on a change of the display's scale, and
// stays open until it is closed.
internal static class CurveGallery
{
    private static readonly Color Background = Color.FromRgb(0xF3F3F3);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color Red = Color.FromRgb(0xE02020);
    private static readonly Color Blue = Color.FromRgb(0x2040E0);

    public static int Run()
    {
        UiThread.Initialize();
        using var window = new Win32Window("XylemUI curves", 600, 420);
        window.Show();
        (int width, int height) = window.ClientSize;
        using var renderer = D3D11Renderer.Create(window.Handle, width, height);
        var list = new DisplayList();
        GradientBrush linear = GradientBrush.Linear(90, [new(0, Red), new(1, Blue)]);
        GradientBrush radial = GradientBrush.Radial(new Vector2(0.5f), 0.5f, [new(0, Color.FromRgb(0xF0E020)), new(1, Red)]);
        GradientBrush upward = GradientBrush.Linear(0, [new(0, Red), new(1, Blue)]);

        void Draw()
        {
            list.Reset(window.ScaleFactor);
            list.DrawLine(40, 40, 180, 40, new Stroke(Ink, 12));
            list.DrawLine(40, 70, 180, 70, new Stroke(Ink, 12, LineCap.Round));
            list.DrawLine(40, 100, 180, 100, new Stroke(Ink, 12, LineCap.Square));
            list.DrawLine(220, 30, 330, 110, new Stroke(linear, 10, LineCap.Round));
            list.DrawLine(370, 35, 560, 35, new Stroke(Ink, 1));
            list.DrawLine(370, 50, 560, 50, new Stroke(Ink, 0.5f));
            list.DrawLine(370, 75, 560, 75, new Stroke(Ink, 6, LineCap.Flat, new Dash(12, 8)));
            list.DrawLine(370, 100, 560, 100, new Stroke(Ink, 6, LineCap.Round, new Dash(0, 12)));
            list.FillEllipse(100, 200, 60, 40, Red);
            list.DrawEllipse(250, 200, 70, 30, new Stroke(Blue, 10));
            list.DrawEllipse(400, 200, 40, 40, new Stroke(Ink, 6, LineCap.Flat, new Dash(10, 6)));
            list.FillEllipse(520, 200, 40, 40, radial);
            list.DrawArc(100, 330, 40, 0, 270, new Stroke(upward, 10, LineCap.Round));
            list.DrawArc(230, 330, 40, 90, -120, new Stroke(Ink, 10, LineCap.Square));
            list.FillSector(360, 330, 45, 30, 100, Blue);
            list.DrawEllipse(490, 330, 40, 40, new Stroke(new Color(0xE0, 0x20, 0x20, 128), 10, LineCap.Round, new Dash(4, 3)));
            renderer.Draw(list, Background);
            renderer.Present();

            // A callback that presents says so, or the next frame waits on the 16 ms timer in place of the swap chain (design §2.11, I1c).
            Win32MessageLoop.FrameScheduler.FramePresented();
        }

        window.SynchronousRender = (_, newWidth, newHeight) =>
        {
            renderer.Resize(newWidth, newHeight);
            Draw();
        };
        window.Closed += (_, _) => Win32MessageLoop.Quit();
        Win32MessageLoop.SetFrameClock(renderer.FrameLatencyWaitableObject);
        Win32MessageLoop.FrameScheduler.RequestFrame(Draw);
        int exit = Win32MessageLoop.Run();
        Win32MessageLoop.SetFrameClock(0);
        return exit;
    }
}
