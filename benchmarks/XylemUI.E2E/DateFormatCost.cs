using System.Diagnostics;
using System.Runtime;

namespace XylemUI.E2E;

// The time of one date or time written by a DateFormatter (row LC2, design §4.11, "Dates and times"), with no window: en's and ja's
// yMd and yMMMMEEEEd with a DateOnly, jm in the 12-hour and the 24-hour form with a TimeOnly, jmz with a DateTimeOffset at +9 h, and
// yMd joined with jm (12-hour) with a DateTime, all at 2026-11-22 15:04. Each case is timed two ways: TryFormat into 128 characters
// on the stack, and Of(value) written through the interpolation of a NodeTextWriter over a buffer of 128, as a text node's
// TextBuilder writes it (design §2.13). Each runs 10,000 calls unmeasured, then 7 batches of `calls`; the report gives the
// nanoseconds of one call (the median, least, and most of the batches) and the bytes allocated per call over the 7 batches, which
// must be 0 on NativeAOT. The written lengths are summed into a static field, reported as `sink`, and each batch's sum is checked
// against the case's text, so that no call is optimized away or fails.
internal static class DateFormatCost
{
    private const int WarmUp = 10_000;
    private const int Batches = 7;

    // The written lengths of every batch, so that the calls stay observable (.cursor/rules/bench.mdc, S2).
    private static long _sink;

    // The value of each case, the same every call.
    private enum Value
    {
        DateOnly,
        TimeOnly,
        DateTimeOffset,
        DateTime,
    }

    public static int Run(int calls)
    {
        if (calls <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calls), calls, "The number of calls in a batch must be positive.");
        }

        using var report = new Report("date-format");
        report.Number("calls", calls);
        report.Number("batches", Batches);
        foreach ((string locale, LocaleData data) in new[] { ("en", CldrLocales.En), ("ja", CldrLocales.Ja) })
        {
            var formats = new (string Name, DateFormatter Format, Value Value)[]
            {
                ("ymd", DateFormatter.FromSkeleton(data, "yMd", use24HourFormat: false), Value.DateOnly),
                ("yMMMMEEEEd", DateFormatter.FromSkeleton(data, "yMMMMEEEEd", use24HourFormat: false), Value.DateOnly),
                ("jm12", DateFormatter.FromSkeleton(data, "jm", use24HourFormat: false), Value.TimeOnly),
                ("jm24", DateFormatter.FromSkeleton(data, "jm", use24HourFormat: true), Value.TimeOnly),
                ("jmz", DateFormatter.FromSkeleton(data, "jmz", use24HourFormat: false), Value.DateTimeOffset),
                ("ymd_jm", DateFormatter.FromSkeletons(data, "yMd", "jm", use24HourFormat: false), Value.DateTime),
            };
            foreach ((string name, DateFormatter format, Value value) in formats)
            {
                var item = new Case(format);
                string key = $"{locale}_{name}";
                switch (value)
                {
                    case Value.DateOnly:
                        Measure(report, key, item.DateOnlyText, item.TryFormatDateOnly, item.OfDateOnly, calls);
                        break;
                    case Value.TimeOnly:
                        Measure(report, key, item.TimeOnlyText, item.TryFormatTimeOnly, item.OfTimeOnly, calls);
                        break;
                    case Value.DateTimeOffset:
                        Measure(report, key, item.DateTimeOffsetText, item.TryFormatDateTimeOffset, item.OfDateTimeOffset, calls);
                        break;
                    default:
                        Measure(report, key, item.DateTimeText, item.TryFormatDateTime, item.OfDateTime, calls);
                        break;
                }
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
        long compiled = JitInfo.GetCompiledMethodCount(currentThread: true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Batches; i++)
        {
            long start = Stopwatch.GetTimestamp();
            sums[i] = batch(calls);
            long end = Stopwatch.GetTimestamp();
            nanoseconds[i] = (end - start) * 1e9 / Stopwatch.Frequency / calls;
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

    // One format with the fixed value of each type, and the buffer and ranges of the writer that the Of path writes into. A case
    // calls only the pair of its format's type: the others throw for a format that does not take them.
    private sealed class Case
    {
        private static readonly DateOnly _date = new(2026, 11, 22);
        private static readonly TimeOnly _clock = new(15, 4);
        private static readonly DateTimeOffset _offset = new(2026, 11, 22, 15, 4, 0, TimeSpan.FromHours(9));
        private static readonly DateTime _dateAndClock = new(2026, 11, 22, 15, 4, 0, DateTimeKind.Unspecified);

        private readonly DateFormatter _format;
        private readonly char[] _text = new char[128];
        private readonly TextRangeWriter _ranges = new();

        public Case(DateFormatter format)
        {
            _format = format;
        }

        public string DateOnlyText => _format.Of(_date).ToString();

        public string TimeOnlyText => _format.Of(_clock).ToString();

        public string DateTimeOffsetText => _format.Of(_offset).ToString();

        public string DateTimeText => _format.Format(_dateAndClock);

        public long TryFormatDateOnly(int calls)
        {
            Span<char> buffer = stackalloc char[128];
            long sum = 0;
            int failures = 0;
            for (int i = 0; i < calls; i++)
            {
                if (!_format.TryFormat(_date, buffer, out int written))
                {
                    failures++;
                }

                sum += written;
            }

            return failures == 0 ? sum : -failures;
        }

        public long TryFormatTimeOnly(int calls)
        {
            Span<char> buffer = stackalloc char[128];
            long sum = 0;
            int failures = 0;
            for (int i = 0; i < calls; i++)
            {
                if (!_format.TryFormat(_clock, buffer, out int written))
                {
                    failures++;
                }

                sum += written;
            }

            return failures == 0 ? sum : -failures;
        }

        public long TryFormatDateTimeOffset(int calls)
        {
            Span<char> buffer = stackalloc char[128];
            long sum = 0;
            int failures = 0;
            for (int i = 0; i < calls; i++)
            {
                if (!_format.TryFormat(_offset, buffer, out int written))
                {
                    failures++;
                }

                sum += written;
            }

            return failures == 0 ? sum : -failures;
        }

        public long TryFormatDateTime(int calls)
        {
            Span<char> buffer = stackalloc char[128];
            long sum = 0;
            int failures = 0;
            for (int i = 0; i < calls; i++)
            {
                if (!_format.TryFormat(_dateAndClock, buffer, out int written))
                {
                    failures++;
                }

                sum += written;
            }

            return failures == 0 ? sum : -failures;
        }

        // A new writer over the same buffer each call, as a text node's write starts; the buffer of 128 never grows.
        public long OfDateOnly(int calls)
        {
            long sum = 0;
            for (int i = 0; i < calls; i++)
            {
                var writer = new NodeTextWriter(_text, _ranges);
                writer.Write($"{_format.Of(_date)}");
                sum += writer.Length;
            }

            return sum;
        }

        public long OfTimeOnly(int calls)
        {
            long sum = 0;
            for (int i = 0; i < calls; i++)
            {
                var writer = new NodeTextWriter(_text, _ranges);
                writer.Write($"{_format.Of(_clock)}");
                sum += writer.Length;
            }

            return sum;
        }

        public long OfDateTimeOffset(int calls)
        {
            long sum = 0;
            for (int i = 0; i < calls; i++)
            {
                var writer = new NodeTextWriter(_text, _ranges);
                writer.Write($"{_format.Of(_offset)}");
                sum += writer.Length;
            }

            return sum;
        }

        public long OfDateTime(int calls)
        {
            long sum = 0;
            for (int i = 0; i < calls; i++)
            {
                var writer = new NodeTextWriter(_text, _ranges);
                writer.Write($"{_format.Of(_dateAndClock)}");
                sum += writer.Length;
            }

            return sum;
        }
    }
}
