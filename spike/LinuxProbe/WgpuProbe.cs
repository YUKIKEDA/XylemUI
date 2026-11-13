using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static LinuxProbe.Report;

namespace LinuxProbe;

/// <summary>The functions of native/wgpu_probe.c; only integers, doubles, and pointers to the probe's own buffers cross.</summary>
internal static unsafe partial class WgpuShim
{
    private const string Library = "wgpu_probe";

    [LibraryImport(Library)]
    internal static partial void wp_set_log_level(int logLevel);

    [LibraryImport(Library)]
    internal static partial int wp_instance_create(uint backends);

    [LibraryImport(Library)]
    internal static partial int wp_enumerate_adapters(uint backends, byte* output, int capacity);

    [LibraryImport(Library)]
    internal static partial int wp_request_adapter(int forceFallback, int backendType, byte* output, int capacity);

    [LibraryImport(Library)]
    internal static partial int wp_request_device();

    [LibraryImport(Library)]
    internal static partial int wp_create_targets(uint width, uint height);

    [LibraryImport(Library)]
    internal static partial int wp_frame(
        double r,
        double g,
        double b,
        double a,
        uint x,
        uint y,
        byte* pixel,
        uint* mismatches,
        ulong* encodeNs,
        ulong* waitNs
    );

    [LibraryImport(Library)]
    internal static partial void wp_release_all();

    [LibraryImport(Library)]
    internal static partial byte* wp_last_error();

    [LibraryImport(Library)]
    internal static partial int wp_uncaptured_error_count();
}

/// <summary>One function called straight from C# into wgpu-native's archive, to show that DirectPInvoke reaches it too.</summary>
internal static partial class WgpuNative
{
    [LibraryImport("wgpu_native")]
    internal static partial uint wgpuGetVersion();
}

/// <summary>
/// Instance, adapters, device, and frames that clear an offscreen texture and read it back (question 2 of the report).
/// </summary>
internal static unsafe class WgpuProbe
{
    private const int TextCapacity = 16384;
    private const uint Width = 64;
    private const uint Height = 64;
    private const int WarmFrames = 20;

    // The clear color and the pixel read back: unorm8 of (0.25, 0.5, 0.75, 1.0) is (64, 128, 191, 255), give or take 1.
    private const double ClearR = 0.25;
    private const double ClearG = 0.5;
    private const double ClearB = 0.75;
    private const double ClearA = 1.0;

    public static void Run(string label, string backendsName)
    {
        string p = "wgpu." + label;
        uint backends = backendsName switch
        {
            "vulkan" => 1u,
            "gl" => 2u,
            _ => 0u,
        };
        Probe(p + ".backends", $"{backendsName} (WGPUInstanceBackend mask {backends})");
        Probe(p + ".rss.before_instance", ProcStatus.Memory());

        try
        {
            Probe(p + ".version", $"0x{WgpuNative.wgpuGetVersion():X8}");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // The baseline executable has no archive linked; every call below would fail the same way.
            Failed(p + ".linked", $"{exception.GetType().Name}: {exception.Message}");
            return;
        }

        int logLevel = int.TryParse(Environment.GetEnvironmentVariable("PROBE_WGPU_LOG"), out int level) ? level : 2;
        WgpuShim.wp_set_log_level(logLevel);

        byte[] text = new byte[TextCapacity];
        fixed (byte* buffer = text)
        {
            if (!Step(p + ".instance", () => WgpuShim.wp_instance_create(backends)))
            {
                return;
            }

            Probe(p + ".rss.after_instance", ProcStatus.Memory());

            long start = Stopwatch.GetTimestamp();
            int count = WgpuShim.wp_enumerate_adapters(backends, buffer, TextCapacity);
            long end = Stopwatch.GetTimestamp();
            Probe(p + ".adapters.count", count >= 0 ? $"{count} (enumerated in {Ms(start, end)})" : "FAILED " + LastError());
            string[] lines = Text(buffer).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                Probe($"{p}.adapters[{i}]", lines[i]);
            }

            bool haveDefault = RequestAdapter(p + ".adapter.default", forceFallback: false, buffer);
            bool haveFallback = RequestAdapter(p + ".adapter.fallback", forceFallback: true, buffer);
            string chosen = "fallback";
            if (!haveFallback)
            {
                if (!haveDefault || !RequestAdapter(p + ".adapter.default_again", forceFallback: false, buffer))
                {
                    Probe(p + ".device", "SKIPPED (no adapter)");
                    WgpuShim.wp_release_all();
                    return;
                }

                chosen = "default";
            }

            Probe(p + ".adapter.used_for_device", chosen);
            Probe(p + ".rss.after_adapter", ProcStatus.Memory());

            if (!Step(p + ".device", WgpuShim.wp_request_device))
            {
                WgpuShim.wp_release_all();
                return;
            }

            Probe(p + ".rss.after_device", ProcStatus.Memory());

            if (!Step(p + ".targets", () => WgpuShim.wp_create_targets(Width, Height)))
            {
                WgpuShim.wp_release_all();
                return;
            }

            if (!Frame(p + ".frame.first", report: true, out _))
            {
                WgpuShim.wp_release_all();
                return;
            }

            Probe(p + ".rss.after_first_frame", ProcStatus.Memory());

            var times = new List<double>(WarmFrames);
            for (int i = 0; i < WarmFrames; i++)
            {
                if (!Frame(p + ".frame.warm", report: false, out double milliseconds))
                {
                    break;
                }

                times.Add(milliseconds);
            }

            if (times.Count > 0)
            {
                times.Sort();
                Probe(
                    p + ".frame.warm",
                    $"{times.Count} frames: min {Ms(times[0])}, median {Ms(times[times.Count / 2])}, max {Ms(times[^1])}"
                );
            }

            Probe(p + ".rss.after_warm_frames", ProcStatus.Memory());
            Probe(p + ".uncaptured_errors", WgpuShim.wp_uncaptured_error_count().ToString(CultureInfo.InvariantCulture));
        }

        WgpuShim.wp_release_all();
        Probe(p + ".rss.after_release", ProcStatus.Memory());
    }

    private static bool Step(string name, Func<int> call)
    {
        long start = Stopwatch.GetTimestamp();
        int result = call();
        long end = Stopwatch.GetTimestamp();
        if (result != 0)
        {
            Failed(name, $"returned {result} after {Ms(start, end)}: {LastError()}");
            return false;
        }

        Probe(name, $"ok in {Ms(start, end)}");
        return true;
    }

    private static bool RequestAdapter(string name, bool forceFallback, byte* buffer)
    {
        long start = Stopwatch.GetTimestamp();
        int result = WgpuShim.wp_request_adapter(forceFallback ? 1 : 0, 0, buffer, TextCapacity);
        long end = Stopwatch.GetTimestamp();
        if (result != 0)
        {
            Failed(name, $"returned {result} after {Ms(start, end)}: {LastError()}");
            return false;
        }

        Probe(name, $"{Text(buffer)} (in {Ms(start, end)})");
        return true;
    }

    private static bool Frame(string name, bool report, out double milliseconds)
    {
        byte* pixel = stackalloc byte[4];
        uint mismatches;
        ulong encodeNs;
        ulong waitNs;
        long start = Stopwatch.GetTimestamp();
        int result = WgpuShim.wp_frame(ClearR, ClearG, ClearB, ClearA, 10, 10, pixel, &mismatches, &encodeNs, &waitNs);
        long end = Stopwatch.GetTimestamp();
        milliseconds = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
        if (result != 0)
        {
            Failed(name, $"returned {result} after {Ms(start, end)}: {LastError()}");
            return false;
        }

        if (report)
        {
            Probe(name, $"{Ms(start, end)} (record+submit {Ms(encodeNs / 1e6)}, submit to mapped {Ms(waitNs / 1e6)})");
            Probe(
                name + ".pixel",
                $"({pixel[0]}, {pixel[1]}, {pixel[2]}, {pixel[3]}) at (10, 10), expected about (64, 128, 191, 255); "
                    + $"{mismatches} of {Width * Height} pixels off by more than 1"
            );
        }
        else if (mismatches != 0)
        {
            Failed(name + ".pixel", $"{mismatches} of {Width * Height} pixels off by more than 1");
        }

        return true;
    }

    private static string LastError() => Dl.Str(WgpuShim.wp_last_error());

    private static string Text(byte* buffer) => Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(buffer));
}
