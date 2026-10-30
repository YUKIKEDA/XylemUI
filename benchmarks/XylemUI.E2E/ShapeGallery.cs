using System.Numerics;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;

namespace XylemUI.E2E;

// The shapes of roadmap row R4a, for the owner to compare with a browser (design §5.0, "Shapes, brushes, and shadows"):
// benchmarks/compare/shapes.html draws the same shapes with CSS at the same logical positions. A 600 x 480 window of #F3F3F3 with
// four rows: a rounded rectangle with a different radius at each corner and a border of a different width on each edge, a card
// with an outer shadow, and an inset shadow; four gradients (linear 45 degrees, radial, conic, Oklab); a fade to transparent, hard
// stops, and a translucent card over a shadow, whose shadow does not show under it; a red fill under a translucent blue
// border, which shows the fill under the border, and a radius past half the box's height, which CSS scales down. It draws again on a resize and on a change of
// the display's scale, and stays open until it is closed.
internal static class ShapeGallery
{
    private static readonly Color Background = Color.FromRgb(0xF3F3F3);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color White = Color.FromRgb(0xFFFFFF);
    private static readonly Color Red = Color.FromRgb(0xE02020);
    private static readonly Color Blue = Color.FromRgb(0x2040E0);

    public static int Run()
    {
        UiThread.Initialize();
        using var window = new Win32Window("XylemUI shapes", 600, 480);
        window.Show();
        (int width, int height) = window.ClientSize;
        using var renderer = D3D11Renderer.Create(window.Handle, width, height);
        var list = new DisplayList();
        GradientBrush linear = GradientBrush.Linear(45, [new(0, Red), new(1, Blue)]);
        GradientBrush radial = GradientBrush.Radial(new Vector2(0.3f, 0.6f), 0.7f, [new(0, Red), new(1, Blue)]);
        GradientBrush conic = GradientBrush.Conic(new Vector2(0.5f), 30, [new(0, Red), new(0.5f, Color.FromRgb(0xF0E020)), new(1, Red)]);
        GradientBrush oklab = GradientBrush.Linear(90, [new(0, Red), new(1, Blue)], GradientColorSpace.Oklab);
        GradientBrush fade = GradientBrush.Linear(90, [new(0, Red), new(1, new Color(0xE0, 0x20, 0x20, 0))]);
        GradientBrush hard = GradientBrush.Linear(90, [new(0.25f, Red), new(0.5f, Red), new(0.5f, Blue), new(0.75f, Blue)]);

        void Draw()
        {
            list.Reset(window.ScaleFactor);
            list.FillRect(30, 30, 140, 90, White, new CornerRadii(0, 8, 20, 30), new Border(new BorderWidths(1, 4, 8, 2), Ink));
            var card = new CornerRadii(12);
            list.DrawShadow(210, 30, 140, 90, card, new BoxShadow(new Color(0, 0, 0, 89), new Vector2(0, 6), 16));
            list.FillRect(210, 30, 140, 90, White, card);
            list.FillRect(390, 30, 140, 90, Background, card);
            list.DrawShadow(390, 30, 140, 90, card, new BoxShadow(new Color(0, 0, 0, 102), new Vector2(2, 3), 10, 1, Inset: true));
            list.FillRect(30, 170, 110, 80, linear, new CornerRadii(8));
            list.FillRect(160, 170, 110, 80, radial, new CornerRadii(8));
            list.FillRect(290, 170, 110, 80, conic, new CornerRadii(8));
            list.FillRect(420, 170, 110, 80, oklab, new CornerRadii(8));
            list.FillRect(30, 290, 140, 70, fade, default);
            list.FillRect(210, 290, 140, 70, hard, default);
            list.DrawShadow(390, 290, 140, 70, card, new BoxShadow(new Color(0, 0, 0, 115), new Vector2(0, 8), 20));
            list.FillRect(390, 290, 140, 70, new Color(0xFF, 0xFF, 0xFF, 128), card);
            list.FillRect(30, 400, 140, 50, Red, new CornerRadii(10), new Border(new BorderWidths(8), new Color(0x20, 0x40, 0xE0, 128)));
            list.FillRect(210, 400, 140, 50, Blue, new CornerRadii(40));
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
