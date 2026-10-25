// Shows a Win32 window drawn with D3D11 (roadmap rows S1, Issue #4, S7, Issue #34, and S8b, Issue #51).
// With --auto, it checks with the public API and exits:
//   S1: a compacting collection while the window is alive, then the close goes through the window procedure and the handle
//       table; a Closed handler that throws is reported instead of crashing; a thread-pool thread touching the window is rejected.
//   S7: 120 frames of 1,000 rectangles paced by the swap chain's waitable object; one pixel read back; the CPU time of building the
//       display list, of Draw (read, upload, issue), and of Present, and the interval between frames, as medians; the time to
//       create the renderer (device, swap chain, and the shaders compiled at run time).
//   S8b: a line of text in every frame (shaped, put in the atlas, drawn as a glyph run); its first glyph has ink on the screen.
//   R4d: the first frame's rectangles in a rounded clip; the time of its Draw, which waits for the clips' shaders.
//   F1: a second line through the font fallback: what the test font lacks takes the fonts of Windows, and an unassigned
//       character the bundled font's .notdef; the families of its runs, and the time to split it the first time and again
//       (XylemUI.E2E's font-fallback scenario measures the memory: Process would add about 0.29 MB to this executable).
// Without --auto, the rectangles move until the user closes the window; a resize redraws from WM_SIZE.
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using XylemUI;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;
using XylemUI.Text;

const int AutoFrames = 120;
const int Rectangles = 1_000;
const string Text = "Hello, World! 日本語のテキスト";
const string FallbackText = "Ελληνικά 中文 한국어 ͸ 😀";
bool auto = args.Contains("--auto");

UiThread.Initialize();

int reported = 0;
Win32MessageLoop.CallbackExceptionHandler = _ => reported++;

// 800 x 600 logical pixels: 1,000 x 750 on the screen at 125 %.
using var window = new Win32Window("XylemUI HelloWindow", 800, 600);
bool closed = false;
window.Closed += (_, _) =>
{
    closed = true;
    Win32MessageLoop.Quit();
};
window.Show();

(int width, int height) = window.ClientSize;
long createStart = Stopwatch.GetTimestamp();
using var renderer = D3D11Renderer.Create(window.Handle, width, height);

// The window reads how its text is drawn (ClearType where Windows and the monitor allow it, roadmap row T2) and reads it again
// when a setting of Windows changes; the renderer and the text take it.
renderer.TextRendering = window.TextRendering;
window.TextRenderingChanged += (_, _) => renderer.TextRendering = window.TextRendering;
double createMilliseconds = Stopwatch.GetElapsedTime(createStart).TotalMilliseconds;
var clear = Color.FromRgb(0x2A2A2A);
var accent = Color.FromRgb(0x3366CC);
var list = new DisplayList();
using Stream fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XylemUITestSans.ttf")!;
byte[] fontBytes = new byte[fontStream.Length];
fontStream.ReadExactly(fontBytes);
using var font = new Font(fontBytes);
using var shaper = new TextShaper();
TextLanguage japanese = TextLanguage.FromTag("ja");
using var atlas = new GlyphAtlas(() => new DirectWriteGlyphRasterizer());
using var fonts = new DirectWriteFontProvider();
var fallbackRuns = new List<FontRun>();
long splitStart = Stopwatch.GetTimestamp();
FontFallback.Split(fonts, FallbackText, japanese, FontRequest.Default, fallbackRuns);
double firstSplitMicroseconds = Stopwatch.GetElapsedTime(splitStart).TotalMicroseconds;
splitStart = Stopwatch.GetTimestamp();
FontFallback.Split(fonts, FallbackText, japanese, FontRequest.Default, fallbackRuns);
double splitMicroseconds = Stopwatch.GetElapsedTime(splitStart).TotalMicroseconds;
var families = new System.Text.StringBuilder();
foreach (FontRun run in fallbackRuns)
{
    families.Append(families.Length == 0 ? string.Empty : ", ").Append(run.Font.FamilyName).Append(' ').Append(run.Length);
}

string fallbackFamilies = families.ToString();
string ink = "not checked";
var build = new List<double>();
var draw = new List<double>();
var present = new List<double>();
var interval = new List<double>();
long lastFrame = 0;
int frames = 0;
string pixel = "not checked";

window.SynchronousRender = (_, newWidth, newHeight) =>
{
    renderer.Resize(newWidth, newHeight);
    BuildFrame(frames, newWidth, newHeight);
    renderer.Render(list, clear, atlas);
};

Action? frame = null;
frame = () =>
{
    long t0 = Stopwatch.GetTimestamp();
    if (lastFrame != 0)
    {
        interval.Add(Stopwatch.GetElapsedTime(lastFrame, t0).TotalMilliseconds);
    }

    lastFrame = t0;
    BuildFrame(frames, renderer.Width, renderer.Height);
    long t1 = Stopwatch.GetTimestamp();
    renderer.Draw(list, clear, atlas);
    long t2 = Stopwatch.GetTimestamp();
    if (frames == 5)
    {
        // The first rectangle of a frame always covers (0, 0) to (40, 40); the text is in the band above the moving rectangles.
        pixel = renderer.ReadPixel(20, 20) == accent ? "ok" : "wrong";
        ink = HasInk(renderer, list, clear) ? "ok" : "wrong";
    }

    long t3 = Stopwatch.GetTimestamp();
    renderer.Present();
    long t4 = Stopwatch.GetTimestamp();
    build.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds);
    draw.Add(Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
    present.Add(Stopwatch.GetElapsedTime(t3, t4).TotalMilliseconds);
    frames++;
    if (!auto || frames < AutoFrames)
    {
        Win32MessageLoop.FrameScheduler.RequestFrame(frame!);
    }
    else
    {
        window.RequestClose();
    }
};

Win32MessageLoop.SetFrameClock(renderer.FrameLatencyWaitableObject);
Win32MessageLoop.FrameScheduler.RequestFrame(frame);

string offThread = "not checked";
if (auto)
{
    window.Closed += (_, _) => throw new InvalidOperationException("Thrown from a Closed handler on purpose (S1 question 3).");

    offThread = Task.Run(() =>
        {
            try
            {
                window.Show();
                return "accepted";
            }
            catch (InvalidOperationException)
            {
                return "rejected";
            }
        })
        .GetAwaiter()
        .GetResult();

    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
}

int exitCode = Win32MessageLoop.Run();
Win32MessageLoop.SetFrameClock(0);

// PublishAot turns RuntimeFeature.IsDynamicCode* off under `dotnet run` too, so ask the JIT whether it has compiled anything.
string runtime = JitInfo.GetCompiledMethodCount() > 0 ? "jit" : "nativeaot";
Console.WriteLine(
    $"HelloWindow build={runtime} gpu={(renderer.IsWarp ? "warp" : "hardware")} create={createMilliseconds:F1}ms frames={frames} pixel={pixel} "
        + $"text={ink} closed={closed} "
        + $"reported={reported} offThread={offThread} exit={exitCode}"
);
Console.WriteLine($"fallback runs: {fallbackFamilies}; split {firstSplitMicroseconds:F1} us the first time, {splitMicroseconds:F1} us again");
if (frames > 0)
{
    Console.WriteLine(
        $"frame ms (median of {frames}): build {Median(build):F3}, draw {Median(draw):F3}, present {Median(present):F3}, interval {Median(interval):F3}"
    );
    Console.WriteLine($"firstDraw={draw[0]:F1}ms (in a rounded clip: the wait for the clips' shaders, row R4d)");
}

bool checks = pixel == "ok" && ink == "ok";
bool passed = closed && (!auto || (reported == 1 && offThread == "rejected" && frames == AutoFrames && checks));
return passed ? exitCode : 1;

// 1,000 rectangles in a grid, shifted by the frame number; the first one stays at (0, 0) so the pixel check knows its color. A
// line of text in the band above them. Everything is in logical pixels; the display list multiplies them by the window's scale
// (design §6.1.1, "Windows' display scale"), and width and height are the client area's physical pixels.
void BuildFrame(int frame, int physicalWidth, int physicalHeight)
{
    float scale = window.ScaleFactor;
    int width = (int)(physicalWidth / scale);
    int height = (int)(physicalHeight / scale);
    atlas.BeginFrame();
    list.Reset(scale);
    list.FillRect(0, 0, 40, 40, Color.FromRgb(0x3366CC));

    // The first frame draws the rectangles in a rounded clip, whose shaders the renderer compiles on its threads from its creation:
    // its Draw waits for whatever is left of that (row R4d), which firstDraw reports.
    if (frame == 0)
    {
        list.PushClip(0, 45, width, height - 45, new CornerRadii(12));
    }

    for (int i = 1; i < Rectangles; i++)
    {
        float x = ((i * 37) + (frame * 3)) % Math.Max(1, width - 20);
        float y = 50 + ((i * 11) % Math.Max(1, height - 70));
        list.FillRect(x, y, 16, 16, (i & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66));
    }

    if (frame == 0)
    {
        list.Pop();
    }

    GlyphCoverage coverage = renderer.TextRendering.Coverage;
    bool aliased = renderer.TextRendering.IsAliased;
    _ = list.DrawText(shaper.Shape(font, Text, japanese), font, 24, atlas, 60, 30, Color.FromRgb(0xE8E8E8), coverage, aliased);

    // The fallback's runs one after another on one pen, each shaped with its own font at the size its scale gives.
    float pen = 420;
    foreach (FontRun run in fallbackRuns)
    {
        int size = (int)MathF.Round(24 * run.Scale);
        ReadOnlySpan<char> runText = FallbackText.AsSpan(run.Start, run.Length);
        pen += list.DrawText(shaper.Shape(run.Font, runText, japanese), run.Font, size, atlas, pen, 30, Color.FromRgb(0xE8E8E8), coverage, aliased);
    }

    // The glyphs seen for the first time, rasterized together on the atlas's workers.
    list.ResolveGlyphs(atlas);
}

// Some pixel of the box of the frame's first glyph is far from the clear color.
static bool HasInk(D3D11Renderer renderer, DisplayList list, Color clear)
{
    GlyphInstance glyph = default;
    DisplayListReader reader = list.GetReader();
    while (reader.TryNext(out CommandTag tag))
    {
        if (tag == CommandTag.FillRect)
        {
            _ = reader.ReadFillRect();
        }
        else
        {
            GlyphRunCommand run = reader.ReadGlyphRun();
            glyph = glyph.Width == 0 ? run.Glyphs[0] : glyph;
        }
    }

    for (int y = glyph.Y; y < glyph.Y + glyph.Height; y++)
    {
        for (int x = glyph.X; x < glyph.X + glyph.Width; x++)
        {
            Color color = renderer.ReadPixel(x, y);
            if (Math.Abs(color.R - clear.R) > 100)
            {
                return true;
            }
        }
    }

    return false;
}

static double Median(List<double> values)
{
    values.Sort();
    return values[values.Count / 2];
}
