using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace MacProbe;

// Usage: MacProbe [--only <group>] | --list
// Every answer is one line `PROBE <rid>.<group>.<name>: <value>` on stdout, and also appended to the file named by PROBE_SUMMARY.
// Before each step a line `STEP <rid>.<group>.<name>` is printed, so a native crash leaves the step it died in as the last line.
// ci/probe.sh runs one process per group, so a crash in one group does not hide the others.
internal static class Program
{
    private static readonly (string Name, Action Run)[] Groups =
    [
        ("env", HostProbes.Runtime),
        ("frameworks", Frameworks.Probe),
        ("objc", ObjCProbes.Basics),
        ("classreg", ObjCProbes.ClassRegistration),
        ("blocks", BlockProbes.Run),
        ("staticlink", HostProbes.StaticLink),
        ("accesskit", HostProbes.AccessKit),
        ("metal", MetalProbes.Run),
        ("coretext", CoreTextProbes.Run),
        ("display", DisplayProbes.Run),
        ("runloop", RunLoopProbes.Run),
    ];

    private static int Main(string[] args)
    {
        if (args is ["--list"])
        {
            foreach ((string name, _) in Groups)
            {
                Console.WriteLine(name);
            }
            return 0;
        }

        string only = args is ["--only", var group] ? group : "all";
        if (only != "all" && Array.FindIndex(Groups, g => g.Name == only) < 0)
        {
            Console.Error.WriteLine($"unknown group '{only}'; --list names the groups");
            return 2;
        }

        Report.Start();
        Frameworks.LoadAll();
        foreach ((string name, Action run) in Groups)
        {
            if (only != "all" && only != name)
            {
                continue;
            }

            Report.BeginGroup(name);
            nint pool = ObjCRuntime.objc_autoreleasePoolPush();
            try
            {
                run();
            }
            catch (Exception e)
            {
                Report.Value("group", $"FAILED {e.GetType().Name}: {e.Message}");
            }
            ObjCRuntime.objc_autoreleasePoolPop(pool);
            Report.Step("dyld_images", Report.LoadedImages);
            Report.Value("done", "yes");
        }
        return 0;
    }
}

internal sealed class ProbeException(string message) : Exception(message);

internal static class Report
{
    private static readonly Lock Gate = new();
    private static readonly string Rid = RuntimeInformation.RuntimeIdentifier;
    private static string? s_summaryPath;
    private static string s_group = "main";
    private static string? s_step;
    private static long s_stepStart;
    private static int s_stepTimeoutSeconds = 30;

    public static void Start()
    {
        s_summaryPath = Environment.GetEnvironmentVariable("PROBE_SUMMARY");
        if (int.TryParse(Environment.GetEnvironmentVariable("PROBE_STEP_TIMEOUT"), out int seconds) && seconds > 0)
        {
            s_stepTimeoutSeconds = seconds;
        }
        new Thread(Watchdog) { IsBackground = true, Name = "probe-watchdog" }.Start();
    }

    public static void BeginGroup(string group)
    {
        s_group = group;
        Write($"GROUP {Rid}.{group}", summary: false);
    }

    public static void Value(string name, string value) => Write($"PROBE {Rid}.{s_group}.{name}: {value}", summary: true);

    public static void Info(string text) => Write("  " + text, summary: false);

    // Runs one step. An exception becomes `PROBE <step>: FAILED <type>: <message>` and the group goes on with its next step.
    public static void Step(string name, Action body)
    {
        lock (Gate)
        {
            s_step = name;
            s_stepStart = Stopwatch.GetTimestamp();
        }
        Write($"STEP {Rid}.{s_group}.{name}", summary: false);
        try
        {
            body();
        }
        catch (Exception e)
        {
            Value(name, $"FAILED {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            lock (Gate)
            {
                s_step = null;
            }
        }
    }

    public static unsafe void LoadedImages()
    {
        uint count = Libc._dyld_image_count();
        var outside = new List<string>();
        for (uint i = 0; i < count; i++)
        {
            string name = Marshal.PtrToStringUTF8((nint)Libc._dyld_get_image_name(i)) ?? "?";
            Info($"image {name}");
            bool system = name.StartsWith("/usr/lib/", StringComparison.Ordinal) || name.StartsWith("/System/", StringComparison.Ordinal);
            if (!system && name != Environment.ProcessPath)
            {
                outside.Add(name);
            }
        }
        Value("dyld_images", $"{count} loaded; outside /usr/lib and /System, the executable aside: {(outside.Count == 0 ? "none" : string.Join(", ", outside))}");
    }

    private static void Write(string line, bool summary)
    {
        lock (Gate)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
            if (summary && s_summaryPath is not null)
            {
                File.AppendAllText(s_summaryPath, line + "\n");
            }
        }
    }

    // A step that hangs (a GPU wait, nextDrawable, a run loop) is reported and the process ends with 124, so the next group runs.
    private static void Watchdog()
    {
        while (true)
        {
            Thread.Sleep(250);
            string? step;
            long start;
            lock (Gate)
            {
                step = s_step;
                start = s_stepStart;
            }
            if (step is not null && Stopwatch.GetElapsedTime(start).TotalSeconds > s_stepTimeoutSeconds)
            {
                Value(step, $"FAILED hung for more than {s_stepTimeoutSeconds} s; the watchdog ended the process");
                Libc._exit(124);
            }
        }
    }
}

internal static class Frameworks
{
    private static readonly (string Name, string Path)[] All =
    [
        ("libobjc", Paths.ObjC),
        ("libSystem", Paths.LibSystem),
        ("CoreFoundation", Paths.CoreFoundation),
        ("Foundation", Paths.Foundation),
        ("AppKit", Paths.AppKit),
        ("QuartzCore", Paths.QuartzCore),
        ("Metal", Paths.Metal),
        ("CoreText", Paths.CoreText),
        ("CoreGraphics", Paths.CoreGraphics),
        ("CoreVideo", Paths.CoreVideo),
    ];

    private static readonly Dictionary<string, nint> Handles = [];
    private static readonly Dictionary<string, string> Errors = [];

    // Loaded up front: objc_getClass finds a class only once its framework is loaded (a NativeAOT executable links none of them).
    public static void LoadAll()
    {
        foreach ((string name, string path) in All)
        {
            try
            {
                Handles[name] = NativeLibrary.Load(path);
            }
            catch (Exception e)
            {
                Errors[name] = $"{e.GetType().Name}: {e.Message}";
            }
        }
    }

    public static nint Handle(string name) =>
        Handles.TryGetValue(name, out nint handle) ? handle : throw new ProbeException($"{name} did not load: {Errors.GetValueOrDefault(name)}");

    public static void Probe()
    {
        foreach ((string name, string path) in All)
        {
            Report.Value($"load.{name}", Handles.ContainsKey(name) ? $"ok {path}" : $"FAILED {Errors.GetValueOrDefault(name)}");
        }
        Report.Step(
            "classes",
            () =>
            {
                string[] names =
                [
                    "NSObject",
                    "NSString",
                    "NSProcessInfo",
                    "NSApplication",
                    "NSView",
                    "NSScreen",
                    "CAMetalLayer",
                    "CADisplayLink",
                    "MTLTextureDescriptor",
                    "MTLRenderPassDescriptor",
                ];
                Report.Value("classes", string.Join(" ", names.Select(n => $"{n}={(ObjC.Class(n) != 0 ? "yes" : "no")}")));
            }
        );
    }
}

internal static unsafe class ObjC
{
    public static byte[] Utf8z(string text) => System.Text.Encoding.UTF8.GetBytes(text + "\0");

    public static string Utf8(byte* text) => text == null ? "null" : Marshal.PtrToStringUTF8((nint)text) ?? "null";

    public static string Hex(nint value) => $"0x{value:X}";

    public static nint Class(string name)
    {
        fixed (byte* p = Utf8z(name))
        {
            return ObjCRuntime.objc_getClass(p);
        }
    }

    public static nint RequireClass(string name)
    {
        nint cls = Class(name);
        return cls != 0 ? cls : throw new ProbeException($"class {name} not found");
    }

    public static nint Protocol(string name)
    {
        fixed (byte* p = Utf8z(name))
        {
            return ObjCRuntime.objc_getProtocol(p);
        }
    }

    public static nint Sel(string name)
    {
        fixed (byte* p = Utf8z(name))
        {
            return ObjCRuntime.sel_registerName(p);
        }
    }

    // An unrecognized selector raises an NSException, which cannot cross the managed frames and would end the process. Every send
    // goes through this check first, so a wrong selector becomes a FAILED line instead of a crash.
    public static nint Check(nint receiver, string selector)
    {
        if (receiver == 0)
        {
            throw new ProbeException($"nil receiver for {selector}");
        }
        nint sel = Sel(selector);
        if (ObjCRuntime.class_respondsToSelector(ObjCRuntime.object_getClass(receiver), sel) == 0)
        {
            throw new ProbeException($"{ClassName(receiver)} does not respond to {selector}");
        }
        return sel;
    }

    public static string ClassName(nint obj) => Utf8(ObjCRuntime.class_getName(ObjCRuntime.object_getClass(obj)));

    public static nint Id(nint receiver, string selector) => Msg.Id(receiver, Check(receiver, selector));

    public static nint Id(nint receiver, string selector, nint a) => Msg.IdId(receiver, Check(receiver, selector), a);

    public static nint IdU(nint receiver, string selector, nuint a) => Msg.IdUInt(receiver, Check(receiver, selector), a);

    public static void Void(nint receiver, string selector) => Msg.Void(receiver, Check(receiver, selector));

    public static void VoidId(nint receiver, string selector, nint a) => Msg.VoidId(receiver, Check(receiver, selector), a);

    public static void SetU(nint receiver, string selector, nuint a) => Msg.VoidUInt(receiver, Check(receiver, selector), a);

    public static nuint U(nint receiver, string selector) => Msg.UInt(receiver, Check(receiver, selector));

    public static ulong U64(nint receiver, string selector) => Msg.UInt64(receiver, Check(receiver, selector));

    public static double Double(nint receiver, string selector) => Msg.Double(receiver, Check(receiver, selector));

    public static bool Bool(nint receiver, string selector) => Msg.Bool(receiver, Check(receiver, selector)) != 0;

    // [[Class alloc] init], +1 retained.
    public static nint New(string className)
    {
        nint obj = Id(Id(RequireClass(className), "alloc"), "init");
        return obj != 0 ? obj : throw new ProbeException($"[[{className} alloc] init] returned nil");
    }

    // [[NSString alloc] initWithCharacters:length:], +1 retained.
    public static nint NSString(string text)
    {
        nint obj = Id(RequireClass("NSString"), "alloc");
        fixed (char* p = text)
        {
            return Msg.IdCharsLength(obj, Check(obj, "initWithCharacters:length:"), p, (nuint)text.Length);
        }
    }

    // A CFStringRef or NSString* (toll-free bridged) read back as UTF-16.
    public static string? Text(nint str)
    {
        if (str == 0)
        {
            return null;
        }
        nint length = CF.CFStringGetLength(str);
        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            CF.CFStringGetCharacters(str, new CFRange(0, length), p);
        }
        return new string(buffer);
    }

    // A CFStringRef, +1 retained.
    public static nint CFStr(string text)
    {
        fixed (char* p = text)
        {
            return CF.CFStringCreateWithCharacters(0, p, text.Length);
        }
    }

    public static string ErrorText(nint error) => error == 0 ? "no NSError" : Text(Id(error, "localizedDescription")) ?? "no description";

    // The value of an exported data symbol that holds a pointer (kCFRunLoopDefaultMode, kCTFontURLAttribute).
    public static nint ReadExport(nint library, string name) => *(nint*)NativeLibrary.GetExport(library, name);
}
