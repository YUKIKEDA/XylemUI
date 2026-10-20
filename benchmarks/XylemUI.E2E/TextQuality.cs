using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// The text scene of the rendering comparison (roadmap row T4, docs/benchmarks/t4-glyphs.md), which every framework of
// benchmarks/compare draws the same way. In logical pixels: an 800 x 600 window filled with the background; for Segoe UI, then
// Yu Gothic UI, at 12, 14, 16, and 20 px, a sample line with its top at y and a probe line of 16 "l" with its top at y + 1.8
// size, then y grows by 3.6 size, from y 12; every line starts at x 16. Black on white, or white on black with --dark.
//
// XylemUI does not handle the display's scale yet, so this scene makes the process per-monitor DPI aware and draws in physical
// pixels at the system's scale, as a DPI-aware app does: sizes and positions are the logical ones times the scale, and the
// baseline (top + size) is rounded to a whole pixel. Characters a family lacks take the OS's fallback (FontFallback.Split).
internal static class TextQuality
{
    public const string Sample = "Hamburgefonstiv 0123 日本語のテキスト、ひらがな";
    public const string Probe = "llllllllllllllll";

    public static readonly string[] Families = ["Segoe UI", "Yu Gothic UI"];
    public static readonly float[] Sizes = [12, 14, 16, 20];

    public static int Run(bool dark)
    {
        _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        float scale = GetDpiForSystem() / 96f;
        UiThread.Initialize();
        using var window = new Win32Window("XylemUI text", (int)MathF.Round(800 * scale), (int)MathF.Round(600 * scale));
        window.Show();
        (int width, int height) = window.ClientSize;
        using var renderer = D3D11Renderer.Create(window.Handle, width, height);
        using var fonts = new DirectWriteFontProvider();
        using var rasterizer = new DirectWriteGlyphRasterizer();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();
        TextLanguage japanese = TextLanguage.FromTag("ja");
        Color background = dark ? Color.FromRgb(0x000000) : Color.FromRgb(0xFFFFFF);
        Color ink = dark ? Color.FromRgb(0xFFFFFF) : Color.FromRgb(0x000000);
        var runs = new List<FontRun>();

        void Line(string text, string family, float size, float top)
        {
            FontFallback.Split(fonts, text, japanese, FontRequest.Default with { Family = family }, runs);
            float pen = 16 * scale;
            float baseline = MathF.Round((top + size) * scale);
            foreach (FontRun run in runs)
            {
                string part = text.Substring(run.Start, run.Length);
                pen += list.DrawText(shaper.Shape(run.Font, part, japanese), run.Font, size * scale * run.Scale, atlas, pen, baseline, ink);
            }
        }

        void Draw()
        {
            atlas.BeginFrame();
            list.Reset();
            list.FillRect(0, 0, width, height, background);
            float y = 12;
            foreach (string family in Families)
            {
                foreach (float size in Sizes)
                {
                    Line(Sample, family, size, y);
                    Line(Probe, family, size, y + (1.8f * size));
                    y += 3.6f * size;
                }
            }

            renderer.Render(list, background, atlas);
        }

        Win32MessageLoop.SetFrameClock(renderer.FrameLatencyWaitableObject);
        window.Closed += (_, _) => Win32MessageLoop.Quit();
        Win32MessageLoop.FrameScheduler.RequestFrame(Draw);
        return Win32MessageLoop.Run();
    }
}
