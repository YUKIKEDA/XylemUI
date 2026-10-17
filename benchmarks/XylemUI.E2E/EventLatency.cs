using System.Diagnostics;
using XylemUI.Platform.Windows;

namespace XylemUI.E2E;

// The stand-in for input latency until there is an input layer (design §11.3, typing latency; roadmap row M1a): a worker
// posts a message with its time; on the UI thread it sets a signal; an effect that reads the signal requests a frame; the
// frame builds the scene and presents. Measured from the post to the present call, and to the refresh that showed it (frame
// statistics). One sample at a time, 50 ms apart: the next post waits until the last frame reached the display. The frame
// statistics are read on the UI thread when the worker posts a poll message, every millisecond; a frame requested only to poll
// would wait on the swap chain's waitable object without presenting and hold up the next sample (design §2.11).
internal static class EventLatency
{
    public static int Run(int samples)
    {
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E event latency");
        scene.Renderer.StartMeasuringFrames();
        Win32MessageLoop.SetFrameClock(scene.Renderer.FrameLatencyWaitableObject);
        var toPresent = new List<double>(samples);
        var toDisplay = new List<double>(samples);
        using var shown = new SemaphoreSlim(0);
        Signal<int> value = Signal.Create(0);
        long posted = 0;
        int frame = 0;
        bool waitingForDisplay = false;
        int lost = 0;
        Action step = () =>
        {
            scene.Build(frame++);
            scene.Renderer.Render(scene.List, scene.Clear, scene.Atlas);
            toPresent.Add(Stopwatch.GetElapsedTime(posted).TotalMilliseconds);
            waitingForDisplay = true;
        };
        using Effect requester = Effect.Create(() =>
        {
            if (value.Get() > 0)
            {
                Win32MessageLoop.FrameScheduler.RequestFrame(step);
            }
        });
        UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
        dispatcher.MessageHandler = message =>
        {
            if (message is Ping ping)
            {
                posted = ping.Qpc;
                value.Set(ping.Sequence);
            }
            else if (message is Lost)
            {
                lost++;
                waitingForDisplay = false;
                _ = shown.Release();
            }
            else if (message is Poll)
            {
                if (waitingForDisplay && scene.Renderer.TryReadPresentLatency(out _, out long refresh))
                {
                    toDisplay.Add(Stopwatch.GetElapsedTime(posted, refresh).TotalMilliseconds);
                    waitingForDisplay = false;
                    _ = shown.Release();
                }
            }
            else
            {
                Win32MessageLoop.Quit();
            }
        };

        var worker = new Thread(() =>
        {
            for (int i = 1; i <= samples; i++)
            {
                Thread.Sleep(50);
                long start = Stopwatch.GetTimestamp();
                dispatcher.Post(new Ping(i, start));
                while (!shown.Wait(1))
                {
                    // A present the frame statistics never report (a refresh it shared with another) is a lost sample.
                    if (Stopwatch.GetElapsedTime(start).TotalSeconds > 1)
                    {
                        dispatcher.Post(new Lost());
                        _ = shown.Wait(TimeSpan.FromSeconds(1));
                        break;
                    }

                    dispatcher.Post(new Poll());
                }
            }

            dispatcher.Post(new Done());
        })
        {
            IsBackground = true,
        };
        worker.Start();
        _ = Win32MessageLoop.Run();
        Win32MessageLoop.SetFrameClock(0);

        using var report = new Report("event-latency");
        report.Series("post_to_present_call_ms", toPresent);
        report.Series("post_to_display_ms", toDisplay);
        report.Number("lost_samples", lost);
        return 0;
    }

    private sealed record Ping(int Sequence, long Qpc) : UiMessage;

    private sealed record Poll : UiMessage;

    private sealed record Lost : UiMessage;

    private sealed record Done : UiMessage;
}
