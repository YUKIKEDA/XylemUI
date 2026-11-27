using System.Diagnostics;
using System.Runtime;

namespace XylemUI.E2E;

// The time of one number written by a NumberFormatter (row LC2, design §4.11, "Numbers"), with no window: en's and ja's decimal,
// percent, USD, and short compact formats, each with a decimal and a double that write a typical text (1,234.5; 25.6 %; 1,234.5 USD;
// 1,234,567 compact). Each case is timed two ways: TryFormat into 64 characters on the stack, and Of(value) written through the
// interpolation of a NodeTextWriter over a buffer of 64, as a text node's TextBuilder writes it (design §2.13). Each runs 10,000 calls
// unmeasured, then 7 batches of `calls`; the report gives the nanoseconds of one call (the median, least, and most of the batches)
// and the bytes allocated per call over the 7 batches, which must be 0 on NativeAOT. The written lengths are summed into a static
// field, reported as `sink`, and each batch's sum is checked against the case's text, so that no call is optimized away or fails.
internal static class NumberFormatCost
{
    private const int WarmUp = 10_000;
    private const int Batches = 7;

    // The written lengths of every batch, so that the calls stay observable (.cursor/rules/bench.mdc, S2).
    private static long _sink;

    public static int Run(int calls)
    {
        if (calls <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calls), calls, "The number of calls in a batch must be positive.");
        }

        using var report = new Report("number-format");
        report.Number("calls", calls);
        report.Number("batches", Batches);
        foreach ((string locale, LocaleData data) in new[] { ("en", CldrLocales.En), ("ja", CldrLocales.Ja) })
        {
            var formats = new (string Name, NumberFormatter Format, decimal Decimal, double Double)[]
            {
                ("decimal", data.DecimalFormat, 1234.5m, 1234.5),
                ("percent", data.PercentFormat, 0.256m, 0.256),
                ("currency_usd", NumberFormatter.Currency(data, "USD"), 1234.5m, 1234.5),
                ("compact_short", NumberFormatter.Compact(data, CompactStyle.Short), 1234567m, 1234567.0),
            };
            foreach ((string name, NumberFormatter format, decimal decimalValue, double doubleValue) in formats)
            {
                var item = new Case(format, decimalValue, doubleValue);
                string key = $"{locale}_{name}";
                Measure(report, $"{key}_decimal", item.DecimalText, item.TryFormatDecimal, item.OfDecimal, calls);
                Measure(report, $"{key}_double", item.DoubleText, item.TryFormatDouble, item.OfDouble, calls);
            }
        }

        report.Number("sink", _sink);
        return 0;
    }

    // Reports the case's text and both of its ways.
    private static void Measure(Report report, string key, string text, Func<int, long> tryFormat, Func<int, long> of, int calls)
    {
        report.Text($"{key}_text", text);
        Time(report, $"{key}_tryformat", text.Length, tryFormat, calls);
        Time(report, $"{key}_of", text.Length, of, calls);
    }

    // The nanoseconds of one call in each batch, and the bytes allocated over the batches. The window of the bytes holds the batches
    // and their clock reads only: the times go into an array made before it, and are sorted after it.
    private static void Time(Report report, string name, int length, Func<int, long> batch, int calls)
    {
        long expected = (long)length * calls;
        if (batch(WarmUp) != (long)length * WarmUp)
        {
            throw new InvalidOperationException($"The warm-up of {name} wrote another length than its text's {length}.");
        }

        var nanoseconds = new double[Batches];
        var sums = new long[Batches];

        // Read before the window: the first read runs Stopwatch's static constructor, which put 40 bytes into the first case's
        // window on NativeAOT.
        long frequency = Stopwatch.Frequency;
        long compiled = JitInfo.GetCompiledMethodCount(currentThread: true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Batches; i++)
        {
            long start = Stopwatch.GetTimestamp();
            sums[i] = batch(calls);
            long end = Stopwatch.GetTimestamp();
            nanoseconds[i] = (end - start) * 1e9 / frequency / calls;
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        long compiledInWindow = JitInfo.GetCompiledMethodCount(currentThread: true) - compiled;
        foreach (long sum in sums)
        {
            if (sum != expected)
            {
                throw new InvalidOperationException($"A batch of {name} wrote {sum} characters, not {expected}.");
            }

            _sink += sum;
        }

        Array.Sort(nanoseconds);
        report.Number($"{name}_ns_p50", nanoseconds[Batches / 2]);
        report.Number($"{name}_ns_min", nanoseconds[0]);
        report.Number($"{name}_ns_max", nanoseconds[^1]);
        report.Number($"{name}_bytes", bytes);
        report.Number($"{name}_bytes_per_call", (double)bytes / ((long)calls * Batches));
        report.Number($"{name}_jit_compiled", compiledInWindow);
    }

    // One format with its two values, and the buffer and ranges of the writer that the Of path writes into.
    private sealed class Case
    {
        private readonly NumberFormatter _format;
        private readonly decimal _decimal;
        private readonly double _double;
        private readonly char[] _text = new char[64];
        private readonly TextRangeWriter _ranges = new();

        public Case(NumberFormatter format, decimal decimalValue, double doubleValue)
        {
            _format = format;
            _decimal = decimalValue;
            _double = doubleValue;
            DecimalText = _format.Format(_decimal);
            DoubleText = _format.Format(_double);
        }

        public string DecimalText { get; }

        public string DoubleText { get; }

        public long TryFormatDecimal(int calls)
        {
            Span<char> buffer = stackalloc char[64];
            long sum = 0;
            int failures = 0;
            for (int i = 0; i < calls; i++)
            {
                if (!_format.TryFormat(_decimal, buffer, out int written))
                {
                    failures++;
                }

                sum += written;
            }

            return failures == 0 ? sum : -failures;
        }

        public long TryFormatDouble(int calls)
        {
            Span<char> buffer = stackalloc char[64];
            long sum = 0;
            int failures = 0;
            for (int i = 0; i < calls; i++)
            {
                if (!_format.TryFormat(_double, buffer, out int written))
                {
                    failures++;
                }

                sum += written;
            }

            return failures == 0 ? sum : -failures;
        }

        // A new writer over the same buffer each call, as a text node's write starts; the buffer of 64 never grows.
        public long OfDecimal(int calls)
        {
            long sum = 0;
            for (int i = 0; i < calls; i++)
            {
                var writer = new NodeTextWriter(_text, _ranges);
                writer.Write($"{_format.Of(_decimal)}");
                sum += writer.Length;
            }

            return sum;
        }

        public long OfDouble(int calls)
        {
            long sum = 0;
            for (int i = 0; i < calls; i++)
            {
                var writer = new NodeTextWriter(_text, _ranges);
                writer.Write($"{_format.Of(_double)}");
                sum += writer.Length;
            }

            return sum;
        }
    }
}
