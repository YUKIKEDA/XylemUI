using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace LinuxProbe;

/// <summary>Writes the <c>PROBE name: value</c> lines that ci/run-probe.sh collects into the summary.</summary>
internal static class Report
{
    public static void Probe(string name, string value) => Console.WriteLine($"PROBE {name}: {OneLine(value)}");

    public static void Failed(string name, string reason) => Probe(name, "FAILED " + reason);

    /// <summary>Runs one step; an exception becomes a FAILED line and the probe goes on.</summary>
    public static void Guard(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Failed(name, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static string Ms(long start, long end) =>
        Stopwatch.GetElapsedTime(start, end).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms";

    public static string Ms(double milliseconds) => milliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms";

    public static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    public static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "(unset)";
}

/// <summary>Memory and threads of this process, from /proc/self/status (sizes in kB as the kernel writes them).</summary>
internal static class ProcStatus
{
    private static readonly string[] Keys = ["VmRSS", "RssAnon", "RssFile", "RssShmem", "VmHWM", "Threads"];

    public static string Memory()
    {
        var parts = new List<string>();
        foreach (string line in File.ReadAllLines("/proc/self/status"))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon];
            if (Array.IndexOf(Keys, key) >= 0)
            {
                string value = string.Join(' ', line[(colon + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                parts.Add($"{key}={value}");
            }
        }

        return string.Join(' ', parts);
    }
}

/// <summary>dlopen and dlsym through NativeLibrary, so that a library the probe opens is not in the executable's DT_NEEDED.</summary>
internal static unsafe class Dl
{
    public static nint Open(string name)
    {
        if (!NativeLibrary.TryLoad(name, out nint handle))
        {
            throw new DllNotFoundException($"dlopen({name}) failed");
        }

        return handle;
    }

    public static nint Symbol(nint library, string name) => NativeLibrary.GetExport(library, name);

    /// <summary>A UTF-8 copy of <paramref name="text"/> with the terminating zero C expects.</summary>
    public static byte[] Utf8Z(string text)
    {
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, bytes);
        return bytes;
    }

    public static string Str(byte* text) => text == null ? "(null)" : Marshal.PtrToStringUTF8((nint)text) ?? "(null)";

    // Looked up once, when the class is first used (always an Open before the call whose errno is read), so that reading errno
    // calls nothing that could change it.
    private static readonly delegate* unmanaged<int*> ErrnoLocation = (delegate* unmanaged<int*>)Symbol(Open("libc.so.6"), "__errno_location");

    /// <summary>The calling thread's errno, read right after a call through a function pointer (which does not save it).</summary>
    public static int Errno()
    {
        // __errno_location returns the address of this thread's errno, valid for the thread's lifetime.
        return *ErrnoLocation();
    }
}
