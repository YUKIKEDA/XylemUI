using System.Diagnostics;
using System.Globalization;

namespace OsAppearance;

/// <summary>Writes the <c>PROBE name: value</c> lines, one per line, from any thread (Console is synchronized).</summary>
internal static class Report
{
    private static readonly long Start = Stopwatch.GetTimestamp();

    public static void Probe(string name, string value) => Console.WriteLine($"PROBE {name}: {OneLine(value)}");

    public static void Failed(string name, Exception exception) => Probe(name, $"FAILED {exception.GetType().Name}: {exception.Message}");

    /// <summary>An HRESULT as 0x%08X, with its name when it is one the probe expects.</summary>
    public static string Hr(int hr)
    {
        string hex = "0x" + ((uint)hr).ToString("X8", CultureInfo.InvariantCulture);
        string? name = (uint)hr switch
        {
            0x00000000 => "S_OK",
            0x00000001 => "S_FALSE",
            0x80004002 => "E_NOINTERFACE",
            0x80004005 => "E_FAIL",
            0x80070057 => "E_INVALIDARG",
            0x80040154 => "REGDB_E_CLASSNOTREG",
            0x800401F0 => "CO_E_NOTINITIALIZED",
            0x80010106 => "RPC_E_CHANGED_MODE",
            0x80070005 => "E_ACCESSDENIED",
            _ => null,
        };
        return name is null ? hex : $"{hex} ({name})";
    }

    /// <summary>The time since the process started and the wall clock, for the event lines.</summary>
    public static string Stamp()
    {
        double seconds = Stopwatch.GetElapsedTime(Start).TotalSeconds;
        return "t="
            + seconds.ToString("F3", CultureInfo.InvariantCulture)
            + "s at "
            + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    public static string Ms(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms";

    public static string Hex(uint value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);

    public static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Text(uint value) => value.ToString(CultureInfo.InvariantCulture);

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
}
