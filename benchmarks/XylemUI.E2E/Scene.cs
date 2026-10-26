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

    public Font Font => _font;

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
    // ellipse, and an arc with a gradient (row R4b), and 125 runs of 8 glyphs (1,000 glyphs) are drawn with a gradient. With
    // `groups`, the rectangles are 100 cards of 10, each a group of opacity 0.8; the first `layers` of the odd cards (at most 50)
    // have an 11th rectangle over their first, so that they are layers and the other cards give their alpha to their children
    // (row R4c). `scopes` puts the scene in clips and transforms (row R4d): "cards" draws the groups' layout without its groups,
    // and "rect" and "rounded" clip each card of it to a rectangle a little shorter than the card, with radius 0 or 6; "rotated" turns each card
    // about its middle by an angle that changes with the frame; "nested" puts the 1,000 rectangles in `nested` rounded clips of
    // the window, each turned a thousandth of a radian more than the one around it so that none is taken into another, and
    // "nested-heavy" adds four translucent rectangles of the window inside them; "zoom"
    // draws 10 lines of text in a scale that changes every frame, so that every frame draws its glyphs at a new size, and
    // "zoom-still" the same in a scale that stays 1.25.
    public void Build(
        int frame,
        int heavy = 0,
        bool shapes = false,
        bool curves = false,
        bool groups = false,
        int layers = 50,
        string scopes = "",
        int nested = 0,
        bool noText = false
    )
    {
        if (scopes.Length > 0)
        {
            BuildScopes(frame, scopes, nested);
            return;
        }

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
            if (groups)
            {
                // A card of one row of ten rectangles, 17 pixels apart; an odd card that is a layer has one more over its first. (Two rows
                // would make every card a layer: the second row's second rectangle meets the union of the ones before it.)
                int g = i / 10;
                int j = i % 10;
                float cardX = ((g * 97) + (frame * 3)) % Math.Max(1, width - 180);
                float cardY = 50 + ((g * 37) % Math.Max(1, height - 80));
                if (j == 0)
                {
                    List.PushOpacity(0.8f);
                    if (g % 2 == 1 && g / 2 < layers)
                    {
                        List.FillRect(cardX + 4, cardY + 4, 16, 16, Color.FromRgb(0x3366CC));
                    }
                }

                List.FillRect(cardX + (j * 17), cardY, 16, 16, (i & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66));
                if (j == 9)
                {
                    List.Pop();
                }

                continue;
            }

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

        if (!noText)
        {
            _ = List.DrawText(_shaper.Shape(_font, Line, _japanese), _font, 24, Atlas, 60, 30, Color.FromRgb(0xE8E8E8), Coverage, IsAliased);
        }

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

    private void BuildScopes(int frame, string scopes, int nested)
    {
        Atlas.BeginFrame();
        float scale = Window.ScaleFactor;
        List.Reset(scale);
        int width = (int)(Renderer.Width / scale);
        int height = (int)(Renderer.Height / scale);
        List.FillRect(0, 0, 40, 40, Marker);
        var middle = new System.Numerics.Vector2(width / 2f, height / 2f);
        if (scopes is "nested" or "nested-heavy")
        {
            for (int k = 0; k < nested; k++)
            {
                List.PushTransform(System.Numerics.Matrix3x2.CreateRotation(0.001f, middle));
                List.PushClip(10 + k, 10 + k, width - 20 - (2 * k), height - 20 - (2 * k), new CornerRadii(40));
            }
        }

        for (int i = 0; i < Rectangles; i++)
        {
            Color color = (i & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66);
            if (scopes is "cards" or "rect" or "rounded" or "rotated")
            {
                int g = i / 10;
                int j = i % 10;
                float cardX = ((g * 97) + (frame * 3)) % Math.Max(1, width - 180);
                float cardY = 50 + ((g * 37) % Math.Max(1, height - 80));
                if (j == 0 && scopes != "cards")
                {
                    if (scopes == "rotated")
                    {
                        List.PushTransform(System.Numerics.Matrix3x2.CreateRotation(0.01f * ((frame + g) % 60), new(cardX + 85, cardY + 8)));
                    }
                    else
                    {
                        List.PushClip(cardX, cardY + 2, 165, 12, new CornerRadii(scopes == "rounded" ? 6 : 0));
                    }
                }

                List.FillRect(cardX + (j * 17), cardY, 16, 16, color);
                if (j == 9 && scopes != "cards")
                {
                    List.Pop();
                }

                continue;
            }

            float x = ((i * 37) + (frame * 3)) % Math.Max(1, width - 20);
            float y = 50 + ((i * 11) % Math.Max(1, height - 70));
            List.FillRect(x, y, 16, 16, color);
        }

        if (scopes is "nested" or "nested-heavy")
        {
            // "nested-heavy" covers each pixel four times more: the clip's cost per pixel against a layer's composite.
            for (int k = 0; scopes == "nested-heavy" && k < 4; k++)
            {
                List.FillRect(0, 0, width, height, new Color(0x10, 0x20, 0x30, 40));
            }

            for (int k = 0; k < 2 * nested; k++)
            {
                List.Pop();
            }
        }

        if (scopes is "zoom" or "zoom-still")
        {
            float zoom = scopes == "zoom" ? 1.25f + (0.25f * MathF.Sin(frame * 0.05f)) : 1.25f;
            List.PushTransform(System.Numerics.Matrix3x2.CreateScale(zoom, new System.Numerics.Vector2(60, 60)));
            for (int line = 0; line < 10; line++)
            {
                _ = List.DrawText(_shaper.Shape(_font, Line, _japanese), _font, 16, Atlas, 60, 80 + (line * 20), Color.FromRgb(0xE8E8E8));
            }

            List.Pop();
        }
        else
        {
            _ = List.DrawText(_shaper.Shape(_font, Line, _japanese), _font, 24, Atlas, 60, 30, Color.FromRgb(0xE8E8E8), Coverage, IsAliased);
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
