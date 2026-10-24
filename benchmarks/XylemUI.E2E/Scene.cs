using System.Reflection;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;
using XylemUI.Text;

namespace XylemUI.E2E;

// The app every framework builds for the comparison (roadmap row M1a, docs/benchmarks/m1-baseline.md): an 800 x 600 window (logical
// pixels, row W1)
// cleared to #2A2A2A, a marker rectangle #3366CC at (0, 0, 40, 40) of the client area (the startup measurement watches the
// screen for it), 1,000 rectangles in a grid that move with the frame, and one line of Japanese text at 24 px at (60, 30).
// `heavy` adds full-window translucent rectangles to load the GPU. The text is drawn as the window's TextRendering says (roadmap
// row T2), so it is ClearType where Windows and the monitor allow it.
internal sealed class Scene : IDisposable
{
    public const string Line = "Hello, World! 日本語のテキスト";
    public const int Rectangles = 1_000;

    private readonly Font _font;
    private readonly TextShaper _shaper = new();
    private readonly TextLanguage _japanese = TextLanguage.FromTag("ja");

    public Scene(string title, bool useWarp = false, bool gray = false)
    {
        Window = new Win32Window(title, 800, 600);
        Window.Show();
        (int width, int height) = Window.ClientSize;
        Renderer = D3D11Renderer.Create(Window.Handle, width, height, useWarp);
        Renderer.TextRendering = TextRenderingOf(Window, gray);
        Window.TextRenderingChanged += (_, _) => Renderer.TextRendering = TextRenderingOf(Window, gray);
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XylemUITestSans.ttf")!;
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        _font = new Font(bytes);
        Atlas = new GlyphAtlas(() => new DirectWriteGlyphRasterizer());
    }

    public Win32Window Window { get; }

    public GlyphCoverage Coverage => Renderer.TextRendering.Coverage;

    public bool IsAliased => Renderer.TextRendering.IsAliased;

    public D3D11Renderer Renderer { get; }

    public GlyphAtlas Atlas { get; }

    public DisplayList List { get; } = new();

    public Color Clear { get; } = Color.FromRgb(0x2A2A2A);

    public static Color Marker { get; } = Color.FromRgb(0x3366CC);

    // A linear gradient for the shapes scene, made once (row R4).
    private static readonly GradientBrush ShapeGradient = GradientBrush.Linear(
        135,
        [new(0, Color.FromRgb(0xCC3333)), new(1, Color.FromRgb(0x3366CC))]
    );

    // A gradient for the curves scene, and the glyphs it draws with it, shaped once (row R4b).
    private static readonly GradientBrush CurveGradient = GradientBrush.Linear(
        90,
        [new(0, Color.FromRgb(0xE8E8E8)), new(1, Color.FromRgb(0x3366CC))]
    );

    private ShapedGlyph[]? _brushedGlyphs;

    // The rectangles move with the frame number, so that every frame draws something new. The scene is in logical pixels, drawn at
    // the window's scale (row W1). With `shapes`, each rectangle is a rounded one with a gradient and a border over a shadow (row
    // R4): two instances where the plain scene has one. With `curves`, each is a dashed line with round caps, the outline of an
    // ellipse, and an arc with a gradient (row R4b), and 125 runs of 8 glyphs (1,000 glyphs) are drawn with a gradient.
    public void Build(int frame, int heavy = 0, bool shapes = false, bool curves = false)
    {
        Atlas.BeginFrame();
        float scale = Window.ScaleFactor;
        List.Reset(scale);
        int width = (int)(Renderer.Width / scale);
        int height = (int)(Renderer.Height / scale);
        List.FillRect(0, 0, 40, 40, Marker);
        for (int i = 0; i < Rectangles; i++)
        {
            float x = ((i * 37) + (frame * 3)) % Math.Max(1, width - 20);
            float y = 50 + ((i * 11) % Math.Max(1, height - 70));
            if (curves)
            {
                List.DrawLine(x, y, x + 16, y + 12, new Stroke(Color.FromRgb(0xCC3333), 2, LineCap.Round, new Dash(4, 3)));
                List.DrawEllipse(x + 8, y + 8, 8, 5, new Stroke(Color.FromRgb(0x33AA66), 1.5f));
                List.DrawArc(x + 8, y + 8, 6, frame % 360, 270, new Stroke(CurveGradient, 2, LineCap.Round));
                continue;
            }

            if (shapes)
            {
                var radii = new CornerRadii(4);
                List.DrawShadow(x, y, 16, 16, radii, new BoxShadow(new Color(0, 0, 0, 90), new System.Numerics.Vector2(0, 2), 6));
                List.FillRect(x, y, 16, 16, ShapeGradient, radii, new Border(new BorderWidths(1), Color.FromRgb(0xE8E8E8)));
                continue;
            }

            List.FillRect(x, y, 16, 16, (i & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66));
        }

        _ = List.DrawText(_shaper.Shape(_font, Line, _japanese), _font, 24, Atlas, 60, 30, Color.FromRgb(0xE8E8E8), Coverage, IsAliased);
        if (curves)
        {
            _brushedGlyphs ??= _shaper.Shape(_font, "日本語のテキスト", _japanese).ToArray();
            for (int i = 0; i < 125; i++)
            {
                float x = (i % 5) * 150;
                float baseline = 80 + ((i / 5) * 20);
                _ = List.DrawText(_brushedGlyphs, _font, 16, Atlas, x, baseline, CurveGradient, x, baseline - 16, 140, 20, Coverage, IsAliased);
            }
        }

        for (int i = 0; i < heavy; i++)
        {
            List.FillRect(0, 0, width, height, new Color(0x10, 0x20, 0x30, 8));
        }

        List.ResolveGlyphs(Atlas);
    }

    private static TextRenderingParameters TextRenderingOf(Win32Window window, bool gray) =>
        gray ? window.TextRendering with { Coverage = GlyphCoverage.Gray, IsAliased = false } : window.TextRendering;

    public void Dispose()
    {
        Renderer.Dispose();
        _shaper.Dispose();
        Atlas.Dispose();
        _font.Dispose();
        Window.Dispose();
    }
}
