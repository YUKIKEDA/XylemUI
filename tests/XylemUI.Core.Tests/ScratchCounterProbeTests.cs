using System.Globalization;
using System.Text;

namespace XylemUI.Tests;

// EXPERIMENT (#92): does the allocation counter of a thread that allocates nothing move while other threads allocate,
// collect, and suspend the runtime?
public sealed class ScratchCounterProbeTests
{
    private static readonly object?[] Sink = new object?[64];

    [Theory]
    [InlineData("allocate")]
    [InlineData("collect")]
    [InlineData("precise")]
    public void Counter_OfAThreadThatAllocatesNothing_DoesNotMove(string noise)
    {
        var changes = new long[64];
        int changeCount = 0;
        long reads = 0;
        using var stop = new CancellationTokenSource();
        var reader = new Thread(() =>
        {
            long last = GC.GetAllocatedBytesForCurrentThread();
            long deadline = Environment.TickCount64 + 3_000;
            while (Environment.TickCount64 < deadline)
            {
                long now = GC.GetAllocatedBytesForCurrentThread();
                reads++;
                if (now != last && changeCount < 64)
                {
                    changes[changeCount++] = now - last;
                }

                last = now;
            }
        });
        var noiseThread = new Thread(() =>
        {
            int i = 0;
            while (!stop.IsCancellationRequested)
            {
                Sink[i++ & 63] = new byte[(i & 7) * 100];
                if ((i & 1023) == 0)
                {
                    switch (noise)
                    {
                        case "collect":
                            GC.Collect(i % 3);
                            break;
                        case "precise":
                            _ = GC.GetTotalAllocatedBytes(precise: true);
                            break;
                    }
                }
            }
        });
        reader.Start();
        noiseThread.Start();
        reader.Join();
        stop.Cancel();
        noiseThread.Join();

        if (changeCount != 0)
        {
            var message = new StringBuilder();
            message.Append(CultureInfo.InvariantCulture, $"{noise}: {changeCount} changes in {reads} reads:");
            for (int c = 0; c < changeCount; c++)
            {
                message.Append(CultureInfo.InvariantCulture, $" {changes[c]}");
            }

            Assert.Fail(message.ToString());
        }
    }
}
