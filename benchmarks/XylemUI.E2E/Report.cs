using System.Globalization;
using System.Text.Json;

namespace XylemUI.E2E;

// One JSON object per scenario on standard output, written without reflection (NativeAOT).
internal sealed class Report : IDisposable
{
    private readonly Stream _stream = Console.OpenStandardOutput();
    private readonly Utf8JsonWriter _writer;

    public Report(string scenario)
    {
        _writer = new Utf8JsonWriter(_stream, new JsonWriterOptions { Indented = false });
        _writer.WriteStartObject();
        _writer.WriteString("scenario", scenario);
        _writer.WriteString("build", JitInfo() ? "jit" : "nativeaot");
    }

    public void Number(string name, double value) => _writer.WriteNumber(name, Math.Round(value, 4));

    public void Number(string name, long value) => _writer.WriteNumber(name, value);

    public void Text(string name, string value) => _writer.WriteString(name, value);

    // Median, p95, p99, and max of a series, as name_p50 and so on.
    public void Series(string name, List<double> values)
    {
        if (values.Count == 0)
        {
            _writer.WriteNull(name + "_p50");
            return;
        }

        var sorted = values.Order().ToList();
        Number(name + "_p50", Percentile(sorted, 0.50));
        Number(name + "_p95", Percentile(sorted, 0.95));
        Number(name + "_p99", Percentile(sorted, 0.99));
        Number(name + "_max", sorted[^1]);
        Number(name + "_n", sorted.Count);
    }

    public void Dispose()
    {
        _writer.WriteEndObject();
        _writer.Flush();
        _writer.Dispose();
        _stream.WriteByte((byte)'\n');
        _stream.Flush();
        _stream.Dispose();
    }

    public static double Percentile(List<double> sorted, double p) => sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];

    public static string Format(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static bool JitInfo() => System.Runtime.JitInfo.GetCompiledMethodCount() > 0;
}
