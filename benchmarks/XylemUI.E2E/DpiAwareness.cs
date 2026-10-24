using System.Reflection;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// Whether the process becomes per-monitor DPI aware (V2) when the XylemUI types that can be made before a window are made first
// and used (design §6.1.1, "Windows' display scale", roadmap row W1): the font provider resolves a family and the rasterizer
// draws a glyph, then the first window is made. The awareness can only be set before the process reads a DPI, so a type that
// read one first would leave the process unaware. Run it in a new process.
internal static unsafe class DpiAwareness
{
    public static int Run()
    {
        using (var fonts = new DirectWriteFontProvider())
        using (var rasterizer = new DirectWriteGlyphRasterizer())
        {
            using Font font = OpenFont();
            _ = fonts.ResolveFont("A 日本", 0, TextLanguage.FromTag("ja"), FontRequest.Default);
            _ = rasterizer.Measure(new GlyphKey(font, 2, 24, 0));
        }

        UiThread.Initialize();
        using var window = new Win32Window("XylemUI DPI awareness", 200, 100);
        bool aware = AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) != BOOL.FALSE;
        using var report = new Report("dpi-awareness");
        report.Text("per_monitor_v2", aware ? "yes" : "no");
        report.Number("scale", window.ScaleFactor);
        return aware ? 0 : 1;
    }

    private static Font OpenFont()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XylemUITestSans.ttf")!;
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return new Font(bytes);
    }
}
