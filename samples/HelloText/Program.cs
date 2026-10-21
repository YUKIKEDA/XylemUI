// Shapes and rasterizes text with HarfBuzz and FreeType (roadmap row S8a, Issue #44). Under NativeAOT both libraries are linked
// into this executable; under the JIT they load as DLLs. With --auto, it checks with the public API and exits:
//   the glyph ids of "Hello, World!" and of a missing character (.notdef, glyph 0), the atlas regions, and the time of one text
//   update (shape + atlas lookups of cached glyphs) and of the first rasterization, as medians.
// With --atlas-sizing, it runs the measurement of the atlas's sizes instead (AtlasSizing.cs, roadmap row S8b).
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using XylemUI.Platform.Windows;
using XylemUI.Text;

if (args.Length >= 2 && args[0] == "--atlas-sizing")
{
    int face = args.Length >= 3 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0;
    double scale = args.Length >= 4 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1.5;
    int step = args.Length >= 5 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 1;
    return AtlasSizing.Run(args[1], face, scale, step, args.Length >= 6 ? args[5] : Path.Combine("docs", "design.ja.md"));
}

bool auto = args.Contains("--auto");
const string Text = "Hello, World! 日本語のテキスト";

using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("XylemUITestSans.ttf");
if (resource is null)
{
    Console.Error.WriteLine("The font resource is missing.");
    return 1;
}

byte[] bytes = new byte[resource.Length];
resource.ReadExactly(bytes);

long openStart = Stopwatch.GetTimestamp();
using var font = new Font(bytes);
double openMilliseconds = Stopwatch.GetElapsedTime(openStart).TotalMilliseconds;
using var shaper = new TextShaper();
TextLanguage japanese = TextLanguage.FromTag("ja");
using var rasterizer = new DirectWriteGlyphRasterizer();
var atlas = new GlyphAtlas(rasterizer);

// The first pass rasterizes every glyph; later passes find them in the atlas.
long rasterizeStart = Stopwatch.GetTimestamp();
int inked = 0;
foreach (ShapedGlyph glyph in shaper.Shape(font, Text, japanese))
{
    if (atlas.TryGet(new GlyphKey(font, glyph.GlyphId, 24, 0), out AtlasGlyph region) && region.Width > 0)
    {
        inked++;
    }
}

double firstPassMilliseconds = Stopwatch.GetElapsedTime(rasterizeStart).TotalMilliseconds;
int rasterized = atlas.RasterizeCount;

uint[] hello = shaper.Shape(font, "Hello, World!", japanese).ToArray().Select(g => g.GlyphId).ToArray();
uint[] missing = shaper.Shape(font, "A€B", japanese).ToArray().Select(g => g.GlyphId).ToArray();
bool helloOk = hello.SequenceEqual(new uint[] { 41, 70, 77, 77, 80, 13, 1, 56, 80, 83, 77, 69, 2 });
bool missingOk = missing.SequenceEqual(new uint[] { 34, 0, 35 });

// One text update in steady state: start an atlas frame, shape the text again, and look its glyphs up. Repeated inside a sample to rise above the clock.
var samples = new List<double>();
long sink = 0;
for (int round = 0; round < 50; round++)
{
    long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < 1_000; i++)
    {
        atlas.BeginFrame();
        foreach (ShapedGlyph glyph in shaper.Shape(font, Text, japanese))
        {
            _ = atlas.TryGet(new GlyphKey(font, glyph.GlyphId, 24, 0), out AtlasGlyph region);
            sink += glyph.XAdvance + region.Width;
        }
    }

    samples.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds / 1_000);
}

samples.Sort();
string runtime = JitInfo.GetCompiledMethodCount() > 0 ? "jit" : "nativeaot";
Console.WriteLine(
    $"HelloText build={runtime} hello={(helloOk ? "ok" : "wrong")} notdef={(missingOk ? "ok" : "wrong")} glyphs={rasterized} "
        + $"inked={inked} steadyRasterize={atlas.RasterizeCount - rasterized}"
);
Console.WriteLine(
    $"ms: open font {openMilliseconds:F3}, first pass (shape + rasterize {rasterized}) {firstPassMilliseconds:F3}; "
        + $"us per update (shape {Text.Length} chars + lookups, median of 50): {samples[samples.Count / 2]:F2} [{sink % 7}]"
);

bool passed = !auto || (helloOk && missingOk && atlas.RasterizeCount == rasterized);
return passed ? 0 : 1;
