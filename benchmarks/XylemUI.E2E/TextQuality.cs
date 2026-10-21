using System.Diagnostics;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// The text scene of the rendering comparison (roadmap row T4, docs/benchmarks/t4-glyphs.md), which every framework of
// benchmarks/compare draws the same way. In logical pixels: an 800 x 600 window filled with the background; for Segoe UI, then
// Yu Gothic UI, at 12, 14, 16, and 20 px, a sample line with its top at y and a probe line of 16 "l" with its top at y + 1.8
// size, then y grows by 3.6 size, from y 12; every line starts at x 16. Black on white, or white on black with --dark. The text
// is drawn as the window's TextRendering says (ClearType where Windows and the monitor allow it, roadmap row T2), or in grayscale
// with --gray, as T4 drew it. Under them, at y, the emoji line (row T6): the emoji in Segoe UI Emoji at 16, 24, 32, and 48 px,
// each with its top at y and its left at x 16, 120, 270, and 470. With --frames N it draws N frames instead of waiting for the window to close, and reports the CPU
// time of building the display list and of Draw, the GPU time, and the draws of a frame (the first 10 frames left out), with the
// atlas's pages of each kind and their bytes, and the process's private memory, at the end.
//
// XylemUI does not handle the display's scale yet, so this scene makes the process per-monitor DPI aware and draws in physical
// pixels at the window's scale, as a DPI-aware app does: sizes and positions are the logical ones times the scale, and the
// baseline (top + size) is rounded to a whole pixel. Characters a family lacks take the OS's fallback (FontFallback.Split).
internal static class TextQuality
{
    public const string Sample = "Hamburgefonstiv 0123 日本語のテキスト、ひらがな";
    public const string Probe = "llllllllllllllll";
    public const string Emoji = "😀🎉👍❤️🇯🇵";

    public static readonly string[] Families = ["Segoe UI", "Yu Gothic UI"];
    public static readonly float[] Sizes = [12, 14, 16, 20];
    public static readonly float[] EmojiSizes = [16, 24, 32, 48];
    public static readonly float[] EmojiLefts = [16, 120, 270, 470];

    public static int Run(bool dark, bool gray, int frames = 0)
    {
        _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        UiThread.Initialize();
        using var window = new Win32Window("XylemUI text", 800, 600);

        // The window's own DPI: the system DPI stays the one of the sign-in when the display's scale changes after it.
        var handle = (HWND)window.Handle;
        float scale = GetDpiForWindow(handle) / 96f;
        _ = SetWindowPos(handle, HWND.NULL, 0, 0, (int)MathF.Round(800 * scale), (int)MathF.Round(600 * scale), SWP.SWP_NOMOVE | SWP.SWP_NOZORDER);
        window.Show();
        (int width, int height) = window.ClientSize;
        using var renderer = D3D11Renderer.Create(window.Handle, width, height);
        TextRenderingParameters rendering = gray ? window.TextRendering with { Coverage = GlyphCoverage.Gray } : window.TextRendering;
        renderer.TextRendering = rendering;
        using var fonts = new DirectWriteFontProvider();
        using var rasterizer = new DirectWriteGlyphRasterizer();
        using var shaper = new TextShaper();
        var atlas = new GlyphAtlas(rasterizer);
        var list = new DisplayList();
        TextLanguage japanese = TextLanguage.FromTag("ja");
        Color background = dark ? Color.FromRgb(0x000000) : Color.FromRgb(0xFFFFFF);
        Color ink = dark ? Color.FromRgb(0xFFFFFF) : Color.FromRgb(0x000000);
        var runs = new List<FontRun>();
        var build = new List<double>(frames);
        var draw = new List<double>(frames);
        var gpu = new List<double>(frames);
        long start = 0;

        void Line(string text, string family, float size, float top, float left = 16)
        {
            FontFallback.Split(fonts, text, japanese, FontRequest.Default with { Family = family }, runs);
            float pen = left * scale;
            float baseline = MathF.Round((top + size) * scale);
            foreach (FontRun run in runs)
            {
                string part = text.Substring(run.Start, run.Length);
                pen += list.DrawText(
                    shaper.Shape(run.Font, part, japanese),
                    run.Font,
                    size * scale * run.Scale,
                    atlas,
                    pen,
                    baseline,
                    ink,
                    rendering.Coverage
                );
            }
        }

        void Draw()
        {
            start = Stopwatch.GetTimestamp();
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

            for (int i = 0; i < EmojiSizes.Length; i++)
            {
                Line(Emoji, "Segoe UI Emoji", EmojiSizes[i], y, EmojiLefts[i]);
            }

            long built = Stopwatch.GetTimestamp();
            renderer.Draw(list, background, atlas);
            long drawn = Stopwatch.GetTimestamp();
            renderer.Present();
            if (frames == 0)
            {
                return;
            }

            build.Add(Stopwatch.GetElapsedTime(start, built).TotalMilliseconds);
            draw.Add(Stopwatch.GetElapsedTime(built, drawn).TotalMilliseconds);
            if (renderer.TryReadGpuMilliseconds(out double milliseconds))
            {
                gpu.Add(milliseconds);
            }

            if (build.Count < frames)
            {
                Win32MessageLoop.FrameScheduler.RequestFrame(Draw);
            }
            else
            {
                Win32MessageLoop.Quit();
            }
        }

        if (frames > 0)
        {
            renderer.StartMeasuringFrames();
        }

        Win32MessageLoop.SetFrameClock(renderer.FrameLatencyWaitableObject);
        window.Closed += (_, _) => Win32MessageLoop.Quit();
        Win32MessageLoop.FrameScheduler.RequestFrame(Draw);
        int exit = Win32MessageLoop.Run();
        Win32MessageLoop.SetFrameClock(0);
        if (frames > 0)
        {
            const int Warm = 10;
            using var report = new Report("text");
            report.Text("gpu", renderer.IsWarp ? "warp" : "hardware");
            report.Text("coverage", rendering.Coverage.ToString());
            report.Number("glyphs", list.GlyphCount);
            report.Number("draws", renderer.LastDrawCount);
            report.Number("private_mb", Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0);
            foreach (GlyphCoverage kind in (GlyphCoverage[])[GlyphCoverage.Gray, GlyphCoverage.Rgb, GlyphCoverage.Color])
            {
                int pages = atlas.GetPageCount(kind);
                report.Number($"atlas_pages_{kind}".ToLowerInvariant(), pages);
                report.Number(
                    $"atlas_bytes_{kind}".ToLowerInvariant(),
                    (long)pages * atlas.GetPageSize(kind) * atlas.GetPageSize(kind) * GlyphAtlas.BytesPerPixel(kind)
                );
            }

            report.Series("build_ms", [.. build.Skip(Warm)]);
            report.Series("draw_ms", [.. draw.Skip(Warm)]);
            report.Series("gpu_ms", [.. gpu.Skip(Warm)]);
        }

        return exit;
    }
}
