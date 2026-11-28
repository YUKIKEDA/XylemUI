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
//
// `hosted` opens the window through the Windows host (row PL1, design §6.0), for the tree scenes (TreeScene): the host's app tier
// (Win32PlatformApp) opens its window tier (Win32PlatformWindow: the window, its UIA provider, the renderer, the swap chain as the
// loop's clock, and TSF), and a window core (WindowCore) connects to it, as XylemApp.OpenWindow does. The core draws on the target
// the host gives it, which the host makes from its renderer through WrapTarget: a TimingTarget that records when Draw and Present
// start and end (Issue #928). The window is made hidden, placed, and shown before the core connects; the host made its renderer
// before it showed, as for an app, whose window shows after its first frame (Issue #907). The plain scene (Build) stays on its own
// window and renderer.
internal sealed class Scene : IDisposable
{
    public const string Line = "Hello, World! 日本語のテキスト";
    public const int Rectangles = 1_000;

    private readonly Font _font;
    private readonly TextShaper _shaper = new();
    private readonly TextLanguage _japanese = TextLanguage.FromTag("ja");
    private readonly TextContext? _text;
    private readonly Win32PlatformApp? _app;

    public Scene(string title, bool useWarp = false, bool gray = false, bool hosted = false)
    {
        if (hosted)
        {
            _font = ReadFont();
            _text = new TextContext(new OneFont(_font));
            _app = new Win32PlatformApp { UseWarp = useWarp, WrapTarget = target => Timing = new TreeScene.TimingTarget(target) };
            Core = new WindowCore(_text, _app.Settings, _app.Clipboard, null, null);
            HostWindow = (Win32PlatformWindow)_app.OpenWindow(Core, title, 800, 600);
            Window = HostWindow.Window;
            Renderer = HostWindow.Renderer;

            // An experiment of the measurement (PL1-e2e, "計測の後の調べ"): XYLEMUI_E2E_NO_UIA=1 takes the host's UIA provider off the
            // window before it shows, so that no UIA client makes the tree's accessibility tree; the report's uia_provider and
            // accessibility_tree read it back.
            if (Environment.GetEnvironmentVariable("XYLEMUI_E2E_NO_UIA") == "1")
            {
                Window.Accessibility = null;
            }

            MonitorChoice.Place(Window.Handle);
            Window.Show();
            MonitorChoice.KeepOnTop(Window.Handle);

            // The host made its renderer at the size the window had where Windows opened it. On a monitor of another scale the
            // window has another size now, which the host's synchronous resize path, not attached yet, did not give the renderer.
            (int clientWidth, int clientHeight) = Window.ClientSize;
            if (Renderer.Width != clientWidth || Renderer.Height != clientHeight)
            {
                Renderer.Resize(clientWidth, clientHeight);
            }

            Renderer.TextRendering = TextRenderingOf(Window, gray);
            Atlas = new GlyphAtlas(_app.CreateRasterizer);
            Core.Connect(HostWindow, Atlas, Clear);
            HostWindow.Attach();

            // The other experiment: XYLEMUI_E2E_NO_OS_POINTER=1 takes the OS's mouse messages away from the core's input, as main's
            // direct window never gave them to the scene's input; the report's os_pointer reads it back.
            if (Environment.GetEnvironmentVariable("XYLEMUI_E2E_NO_OS_POINTER") == "1")
            {
                Window.PointerInputReceived = null;
            }

            if (gray)
            {
                // After the host's own handler, which gives the renderer and the core the window's ClearType again.
                Core.TextRenderingChanged(Renderer.TextRendering);
                Window.TextRenderingChanged += (_, _) =>
                {
                    Renderer.TextRendering = TextRenderingOf(Window, gray);
                    Core.TextRenderingChanged(Renderer.TextRendering);
                };
            }

            return;
        }

        Window = new Win32Window(title, 800, 600);
        MonitorChoice.Place(Window.Handle);
        Window.Show();
        MonitorChoice.KeepOnTop(Window.Handle);
        (int width, int height) = Window.ClientSize;
        Renderer = D3D11Renderer.Create(Window.Handle, width, height, useWarp);
        Renderer.TextRendering = TextRenderingOf(Window, gray);
        Window.TextRenderingChanged += (_, _) => Renderer.TextRendering = TextRenderingOf(Window, gray);
        _font = ReadFont();
        Atlas = new GlyphAtlas(() => new DirectWriteGlyphRasterizer());
    }

    public Win32Window Window { get; }

    // With `hosted`: the window core, connected to the host's window, which it owns with the atlas; the host's window; and the
    // target the host gave the core. Null for the plain scene.
    public WindowCore? Core { get; }

    public Win32PlatformWindow? HostWindow { get; }

    public TreeScene.TimingTarget? Timing { get; private set; }

    public GlyphCoverage Coverage => Renderer.TextRendering.Coverage;

    public bool IsAliased => Renderer.TextRendering.IsAliased;

    public D3D11Renderer Renderer { get; }

    public GlyphAtlas Atlas { get; }

    public DisplayList List { get; } = new();

    // The list as the one list of a frame, for the renderer's Draw with images (row IM1).
    public DisplayList[] Lists => _lists ??= [List];

    // The images of --images (row IM1), made the first time the scene draws them.
    public TextureCache? Images { get; private set; }

    public Color Clear { get; } = Color.FromRgb(0x2A2A2A);

    public static Color Marker { get; } = Color.FromRgb(0x3366CC);

    // The marker's color after an odd number of key presses: the input measurement watches the marker change (row R3,
    // InputWatch). The comparison apps in benchmarks/compare use the same two colors.
    public static Color PressedMarker { get; } = Color.FromRgb(0xCC6633);

    // Whether an odd number of keys went down: the marker is drawn in PressedMarker.
    public bool Pressed { get; set; }

    private Color MarkerNow => Pressed ? PressedMarker : Marker;

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
    private DisplayList[]? _lists;
    private ImageHandle[] _imageHandles = [];

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
    // "zoom-still" the same in a scale that stays 1.25. With `images`, each rectangle is an image of 64 x 64 pixels drawn at 16 x 16
    // (so read from its third mipmap), from 100 images, 10 rectangles after another of each: 100 draws (row IM1).
    public void Build(
        int frame,
        int heavy = 0,
        bool shapes = false,
        bool curves = false,
        bool groups = false,
        int layers = 50,
        string scopes = "",
        int nested = 0,
        bool noText = false,
        bool images = false
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
        List.FillRect(0, 0, 40, 40, MarkerNow);
        if (images && Images is null)
        {
            MakeImages();
        }

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

            if (images)
            {
                List.DrawImage(x, y, 16, 16, _imageHandles[i / 10], new System.Numerics.Vector4(0, 0, 64, 64));
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
        List.FillRect(0, 0, 40, 40, MarkerNow);
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

    // 100 images of 64 x 64, each a gradient of its own hue, opaque.
    private void MakeImages()
    {
        Images = new TextureCache();
        _imageHandles = new ImageHandle[Rectangles / 10];
        for (int n = 0; n < _imageHandles.Length; n++)
        {
            byte[] pixels = new byte[64 * 64 * 4];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    int i = ((y * 64) + x) * 4;
                    pixels[i] = (byte)(x * 4);
                    pixels[i + 1] = (byte)(y * 4);
                    pixels[i + 2] = (byte)(n * 2);
                    pixels[i + 3] = 255;
                }
            }

            _imageHandles[n] = Images.Add(64, 64, pixels);
        }
    }

    private static TextRenderingParameters TextRenderingOf(Win32Window window, bool gray) =>
        gray ? window.TextRendering with { Coverage = GlyphCoverage.Gray, IsAliased = false } : window.TextRendering;

    // The test font, inside the executable.
    private static Font ReadFont()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XylemUITestSans.ttf")!;
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return new Font(bytes);
    }

    public void Dispose()
    {
        if (Core is not null)
        {
            // The core disposes the host's window (which releases the renderer and gives the loop its timer back) and then the atlas;
            // the text tools and the host go after it, in XylemApp.Run's order.
            Core.Dispose();
            _text!.Dispose();
            _shaper.Dispose();
            _font.Dispose();
            _app!.Dispose();
            return;
        }

        Renderer.Dispose();
        _shaper.Dispose();
        Atlas.Dispose();
        _font.Dispose();
        Window.Dispose();
    }

    // The scene's one font for every text of the tree scenes.
    private sealed class OneFont(Font font) : IFontProvider
    {
        public FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request) =>
            new(font, text.Length - start, 1);
    }
}
