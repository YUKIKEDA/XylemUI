using System.Diagnostics;
using XylemUI.Platform.Windows;

namespace XylemUI.Rendering.D3D11.Tests;

// The swap chain's frame latency waitable object as the message loop's frame clock (S7, design §2.11).
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
[Trait("Category", "OsTiming")]
public sealed class FrameClockTests(UiThreadFixture ui)
{
    /// <summary>
    /// A frame that returns without presenting leaves the swap chain's object unsignaled; the next requested frame still runs,
    /// after the wait's timeout.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer whose waitable object is the loop's frame clock. A frame callback that asks for the next frame until 6
    ///   ran; frames 2 and 3 return without presenting, the others render.
    ///
    /// Steps:
    /// - Run the loop and request the first frame.
    ///
    /// Expected:
    /// - All 6 frames ran (the watchdog did not fire); the gap after each frame that did not present is at least the timeout.
    /// </remarks>
    [Fact]
    public void FrameWithoutPresent_DoesNotStopTheNextFrames()
    {
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
        ui.Invoke(() =>
        {
            using var window = new Win32Window("skip", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            var starts = new List<long>();
            Action? frame = null;
            frame = () =>
            {
                starts.Add(Stopwatch.GetTimestamp());
                int index = starts.Count;
                if (index != 2 && index != 3)
                {
                    renderer.Render(list, Color.FromRgb(0x2A2A2A));
                }

                if (index < 6)
                {
                    Win32MessageLoop.FrameScheduler.RequestFrame(frame!);
                }
            };

            Win32MessageLoop.SetFrameClock(renderer.FrameLatencyWaitableObject);
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(watchdog, () => starts.Count == 6, () => Win32MessageLoop.FrameScheduler.RequestFrame(frame));
                Assert.False(watchdog.Expired);
            }
            finally
            {
                Win32MessageLoop.SetFrameClock(0);
            }

            TimeSpan afterSecond = Stopwatch.GetElapsedTime(starts[1], starts[2]);
            TimeSpan afterThird = Stopwatch.GetElapsedTime(starts[2], starts[3]);
            output?.WriteLine($"gap after a frame without present: {afterSecond.TotalMilliseconds:F1} ms, {afterThird.TotalMilliseconds:F1} ms");
            Assert.Equal(6, starts.Count);
            Assert.True(afterSecond >= TimeSpan.FromMilliseconds(Win32FrameScheduler.SwapChainWaitTimeoutMilliseconds - 5));
            Assert.True(afterThird >= TimeSpan.FromMilliseconds(Win32FrameScheduler.SwapChainWaitTimeoutMilliseconds - 5));
        });
    }

    /// <summary>
    /// With the swap chain's waitable object as the clock, requested frames run and present; once no frame is requested, the
    /// loop presents nothing more.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer whose waitable object is the loop's frame clock. A frame callback that renders and asks for the next
    ///   frame until 10 frames ran, and records the CPU time of each render.
    ///
    /// Steps:
    /// - Run the loop; request the first frame; after the 10th frame, keep the loop running for 300 ms more (woken by a
    ///   watchdog-safe thread) with no frame requested.
    ///
    /// Expected:
    /// - With a frame requested, the loop waits on the swap chain's object and the built-in timer is not running.
    /// - Exactly 10 frames ran and presented; none during the idle 300 ms.
    /// </remarks>
    [Fact]
    public void SwapChainClock_RunsRequestedFramesAndNothingWhenIdle()
    {
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
        ui.Invoke(() =>
        {
            using var window = new Win32Window("clock", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            list.FillRect(10, 10, 50, 30, Color.FromRgb(0x3366CC));
            var renderTimes = new List<double>();
            int frames = 0;
            long idleSince = 0;
            Action? frame = null;
            frame = () =>
            {
                long start = Stopwatch.GetTimestamp();
                renderer.Render(list, Color.FromRgb(0x2A2A2A));
                renderTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                frames++;
                if (frames < 10)
                {
                    Win32MessageLoop.FrameScheduler.RequestFrame(frame!);
                }
                else
                {
                    idleSince = Stopwatch.GetTimestamp();
                }
            };

            Win32MessageLoop.SetFrameClock(renderer.FrameLatencyWaitableObject);
            var scheduler = (Win32FrameScheduler)Win32MessageLoop.FrameScheduler;
            bool waitsOnTheSwapChain = false;
            bool timerRanWithAFrame = false;
            Thread? waker = null;
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () =>
                    {
                        if (idleSince != 0 && waker is null)
                        {
                            // Nothing else wakes an idle loop; this thread wakes it once after 300 ms so it can stop.
                            waker = new Thread(() =>
                            {
                                Thread.Sleep(300);
                                Win32MessageLoop.Wake();
                            });
                            waker.Start();
                        }

                        return idleSince != 0 && Stopwatch.GetElapsedTime(idleSince) >= TimeSpan.FromMilliseconds(300);
                    },
                    () =>
                    {
                        Win32MessageLoop.FrameScheduler.RequestFrame(frame);
                        waitsOnTheSwapChain = scheduler.WaitHandle == (TerraFX.Interop.Windows.HANDLE)renderer.FrameLatencyWaitableObject;
                        timerRanWithAFrame = scheduler.IsClockRunning;
                    }
                );

                Assert.False(watchdog.Expired);
            }
            finally
            {
                waker?.Join();
                Win32MessageLoop.SetFrameClock(0);
            }

            renderTimes.Sort();
            output?.WriteLine($"frames={frames}");
            output?.WriteLine($"render (draw + present) ms: median {renderTimes[renderTimes.Count / 2]:F3}, max {renderTimes[^1]:F3}");
            Assert.True(waitsOnTheSwapChain);
            Assert.False(timerRanWithAFrame);
            Assert.Equal(10, frames);
            Assert.Equal(10, renderTimes.Count);
        });
    }
}
