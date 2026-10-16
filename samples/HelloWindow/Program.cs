// Shows an empty Win32 window (roadmap row S1, Issue #4).
// With --auto, it checks the S1 questions with the public API and exits:
//   2. a compacting collection while the window is alive, then the close goes through the window procedure and the handle table;
//   3. a Closed handler (which runs inside the window procedure) throws, and the exception is reported instead of crashing;
//   4. a thread-pool thread touching the window is rejected.
// Without --auto, the window stays until the user closes it.
using System.Runtime;
using XylemUI;
using XylemUI.Platform.Windows;

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
    window.RequestClose();
}

int exitCode = Win32MessageLoop.Run();

// PublishAot turns RuntimeFeature.IsDynamicCode* off under `dotnet run` too, so ask the JIT whether it has compiled anything.
string build = JitInfo.GetCompiledMethodCount() > 0 ? "jit" : "nativeaot";
Console.WriteLine($"HelloWindow build={build} closed={closed} reported={reported} offThread={offThread} exit={exitCode}");

bool passed = closed && (!auto || (reported == 1 && offThread == "rejected"));
return passed ? exitCode : 1;
