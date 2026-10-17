// Shows a Win32 window drawn with D3D11 (roadmap rows S1, Issue #4, and S7, Issue #34).
// With --auto, it checks with the public API and exits:
//   S1: a compacting collection while the window is alive, then the close goes through the window procedure and the handle
//       table; a Closed handler that throws is reported instead of crashing; a thread-pool thread touching the window is rejected.
//   S7: 120 frames of 1,000 rectangles paced by the swap chain's waitable object; one pixel read back; the CPU time of building the
//       display list, of Draw (read, upload, issue), and of Present, and the interval between frames, as medians.
// Without --auto, the rectangles move until the user closes the window; a resize redraws from WM_SIZE.
using System.Diagnostics;
using System.Runtime;
using XylemUI;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;

const int AutoFrames = 120;
const int Rectangles = 1_000;
bool auto = args.Contains("--auto");

UiThread.Initialize();

int reported = 0;
Win32MessageLoop.CallbackExceptionHandler = _ => reported++;

using var window = new Win32Window("XylemUI HelloWindow", 800, 600);
bool closed = false;
window.Closed += (_, _) =>
{
    closed = true;
    Win32MessageLoop.Quit();
};
window.Show();

(int width, int height) = window.ClientSize;
using var renderer = D3D11Renderer.Create(window.Handle, width, height);
var clear = Color.FromRgb(0x2A2A2A);
var accent = Color.FromRgb(0x3366CC);
var list = new DisplayList();
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
    BuildFrame(list, frames, newWidth, newHeight);
    renderer.Render(list, clear);
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
    BuildFrame(list, frames, renderer.Width, renderer.Height);
    long t1 = Stopwatch.GetTimestamp();
    renderer.Draw(list, clear);
    long t2 = Stopwatch.GetTimestamp();
    if (frames == 5)
    {
        // The first rectangle of a frame always covers (0, 0) to (40, 40).
        pixel = renderer.ReadPixel(20, 20) == accent ? "ok" : "wrong";
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
    $"HelloWindow build={runtime} gpu={(renderer.IsWarp ? "warp" : "hardware")} frames={frames} pixel={pixel} closed={closed} "
        + $"reported={reported} offThread={offThread} exit={exitCode}"
);
if (frames > 0)
{
    Console.WriteLine(
        $"frame ms (median of {frames}): build {Median(build):F3}, draw {Median(draw):F3}, present {Median(present):F3}, interval {Median(interval):F3}"
    );
}

bool passed = closed && (!auto || (reported == 1 && offThread == "rejected" && frames == AutoFrames && pixel == "ok"));
return passed ? exitCode : 1;

// 1,000 rectangles in a grid, shifted by the frame number; the first one stays at (0, 0) so the pixel check knows its color.
static void BuildFrame(DisplayList list, int frame, int width, int height)
{
    list.Reset();
    list.FillRect(0, 0, 40, 40, Color.FromRgb(0x3366CC));
    for (int i = 1; i < Rectangles; i++)
    {
        float x = ((i * 37) + (frame * 3)) % Math.Max(1, width - 20);
        float y = 50 + ((i * 11) % Math.Max(1, height - 70));
        list.FillRect(x, y, 16, 16, (i & 1) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x33AA66));
    }
}

static double Median(List<double> values)
{
    values.Sort();
    return values[values.Count / 2];
}
