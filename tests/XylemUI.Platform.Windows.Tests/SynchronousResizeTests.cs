using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// SetWindowPos sends WM_SIZE to the window procedure before it returns, so these tests drive the synchronous render path from
// inside the window procedure without running the message loop, as the OS modal loop of a resize drag does.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class SynchronousResizeTests(UiThreadFixture ui)
{
    private const uint ResizeFlags = (uint)(SWP.SWP_NOMOVE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE);

    /// <summary>
    /// A size change renders synchronously inside the window procedure, with the new client size, while the loop is not running.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a synchronous render that records its arguments and whether the guard was active; no loop runs.
    ///
    /// Steps:
    /// - Resize the window to 420 x 310 with SetWindowPos.
    ///
    /// Expected:
    /// - The render ran once, before SetWindowPos returned, inside the guard.
    /// - It received the client size that GetClientRect reports afterwards.
    /// - The guard is inactive again.
    /// </remarks>
    [Fact]
    public void SizeChange_WithoutTheLoop_RendersSynchronouslyWithTheClientSize() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("resize", 200, 100);
            var renders = new List<(int Width, int Height, bool Guarded)>();
            window.SynchronousRender = (_, width, height) => renders.Add((width, height, SynchronousRenderGuard.IsActive));

            Resize(window, 420, 310);

            RECT client;
            Assert.True(GetClientRect(window.Hwnd, &client));
            (int width, int height, bool guarded) = Assert.Single(renders);
            Assert.Equal(client.right - client.left, width);
            Assert.Equal(client.bottom - client.top, height);
            Assert.True(guarded);
            Assert.False(SynchronousRenderGuard.IsActive);
        });

    /// <summary>
    /// Inside the OS modal loop of a keyboard resize (WM_ENTERSIZEMOVE to WM_EXITSIZEMOVE), each size change renders synchronously.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A visible window whose hook logs WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE, and whose synchronous render logs the size.
    /// - Key messages queued for the window: Right three times, then Return. The first arrow picks the right edge, the next ones
    ///   move it. A guard thread queues Escape after
    ///   three seconds, so a modal loop that ignored the keys still ends.
    ///
    /// Steps:
    /// - Send WM_SYSCOMMAND with SC_SIZE. DefWindowProc runs its own modal loop, which reads the queued keys; our message loop is
    ///   not running.
    ///
    /// Expected:
    /// - The queued keys ended the loop: the guard thread did not have to send Escape.
    /// - The log starts with the modal loop's enter and ends with its exit.
    /// - Renders happened between them, and the last one is wider than the client area before the loop (the height stays).
    /// </remarks>
    [Fact]
    public void KeyboardResize_InsideTheOsModalLoop_RendersOnEachSizeChange()
    {
        // TestContext.Current does not flow to the fixture's UI thread; read it here.
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
        ui.Invoke(() =>
        {
            using var window = new Win32Window("live resize", 300, 200);
            window.Show();
            var log = new List<string>();
            var sizes = new List<(int Width, int Height)>();
            window.MessageHook = (_, message) =>
            {
                if (message == WM.WM_ENTERSIZEMOVE)
                {
                    log.Add("enter");
                }
                else if (message == WM.WM_EXITSIZEMOVE)
                {
                    log.Add("exit");
                }

                return false;
            };
            window.SynchronousRender = (_, width, height) =>
            {
                log.Add("render");
                sizes.Add((width, height));
            };

            RECT before;
            Assert.True(GetClientRect(window.Hwnd, &before));
            foreach (int key in new[] { VK.VK_RIGHT, VK.VK_RIGHT, VK.VK_RIGHT, VK.VK_RETURN })
            {
                Assert.True(PostMessageW(window.Hwnd, WM.WM_KEYDOWN, (nuint)key, 0));
            }

            HWND hwnd = window.Hwnd;
            using var guard = new ManualResetEventSlim();
            bool escapeSent = false;
            var escape = new Thread(() =>
            {
                if (!guard.Wait(TimeSpan.FromSeconds(3)))
                {
                    escapeSent = true;
                    _ = PostMessageW(hwnd, WM.WM_KEYDOWN, VK.VK_ESCAPE, 0);
                }
            });
            escape.Start();
            _ = SendMessageW(window.Hwnd, WM.WM_SYSCOMMAND, SC.SC_SIZE, 0);
            guard.Set();
            escape.Join();
            output?.WriteLine($"log={string.Join(",", log)} sizes={string.Join(" ", sizes)} escapeSent={escapeSent}");

            Assert.False(escapeSent);
            Assert.Equal("enter", log[0]);
            Assert.Equal("exit", log[^1]);
            Assert.Contains("render", log);
            Assert.All(log[1..^1], entry => Assert.Equal("render", entry));
            (int lastWidth, int lastHeight) = sizes[^1];
            Assert.True(lastWidth > before.right - before.left, $"width {lastWidth} after, {before.right - before.left} before");
            Assert.Equal(before.bottom - before.top, lastHeight);
        });
    }

    /// <summary>
    /// A resize caused by the synchronous render itself is dropped instead of running the pipeline inside itself.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose synchronous render resizes the window again on its first call.
    ///
    /// Steps:
    /// - Resize the window once.
    ///
    /// Expected:
    /// - The render ran once; the nested size change was counted as dropped.
    /// - The guard is inactive afterwards.
    /// </remarks>
    [Fact]
    public void NestedResize_DuringTheRender_IsDropped() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("nested", 200, 100);
            int renders = 0;
            window.SynchronousRender = (w, _, _) =>
            {
                renders++;
                if (renders == 1)
                {
                    Resize(w, 500, 400);
                }
            };

            Resize(window, 300, 200);

            Assert.Equal(1, renders);
            Assert.Equal(1, window.DroppedNestedResizes);
            Assert.False(SynchronousRenderGuard.IsActive);
        });

    /// <summary>
    /// Application callbacks raised during the synchronous render run after it returns, in order.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a Closed handler that logs, and a synchronous render that logs, queues a callback with RunOrDefer,
    ///   and destroys the window (which raises Closed inside the render).
    ///
    /// Steps:
    /// - Resize the window once.
    ///
    /// Expected:
    /// - The log is: render begins, render ends, the queued callback, Closed.
    /// </remarks>
    [Fact]
    public void AppCallbacks_DuringTheRender_RunAfterItInOrder() =>
        ui.Invoke(() =>
        {
            var window = new Win32Window("callbacks", 200, 100);
            var log = new List<string>();
            window.Closed += (_, _) => log.Add("closed");
            window.SynchronousRender = (w, _, _) =>
            {
                log.Add("render begins");
                SynchronousRenderGuard.RunOrDefer(() => log.Add("queued"));
                w.Dispose();
                log.Add("render ends");
            };

            Resize(window, 300, 200);

            Assert.Equal(["render begins", "render ends", "queued", "closed"], log);
            Assert.False(window.IsAlive);
        });

    /// <summary>
    /// An exception from the synchronous render is reported at the window procedure, the guard ends, and the next resize renders.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A callback exception handler that records what it receives.
    /// - A hidden window whose synchronous render throws on its first call only.
    ///
    /// Steps:
    /// - Resize the window twice.
    ///
    /// Expected:
    /// - The handler received the exception once; the guard is inactive after the first resize.
    /// - The render ran on both resizes.
    /// </remarks>
    [Fact]
    public void Render_Throws_IsReportedAndTheNextResizeRenders() =>
        ui.Invoke(() =>
        {
            Action<Exception> previous = Win32MessageLoop.CallbackExceptionHandler;
            var reported = new List<Exception>();
            Win32MessageLoop.CallbackExceptionHandler = reported.Add;
            try
            {
                using var window = new Win32Window("throws", 200, 100);
                int renders = 0;
                window.SynchronousRender = (_, _, _) =>
                {
                    renders++;
                    if (renders == 1)
                    {
                        throw new InvalidOperationException("Thrown from the synchronous render on purpose.");
                    }
                };

                Resize(window, 300, 200);
                Assert.False(SynchronousRenderGuard.IsActive);
                Resize(window, 320, 220);

                Assert.IsType<InvalidOperationException>(Assert.Single(reported));
                Assert.Equal(2, renders);
            }
            finally
            {
                Win32MessageLoop.CallbackExceptionHandler = previous;
            }
        });

    /// <summary>
    /// Every deferred callback runs although an earlier one throws, and the exception surfaces when the path ends.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A synchronous render path with three deferred callbacks; the first throws.
    ///
    /// Steps:
    /// - End the path.
    ///
    /// Expected:
    /// - The end of the path throws the first callback's exception.
    /// - The second and third callbacks ran, in order.
    /// </remarks>
    [Fact]
    public void DeferredCallback_Throws_TheRestStillRun() =>
        ui.Invoke(() =>
        {
            var log = new List<string>();
            var thrown = new InvalidOperationException("Thrown from a deferred callback on purpose.");

            Exception caught = Assert.Throws<InvalidOperationException>(() =>
            {
                using (SynchronousRenderGuard.Enter())
                {
                    SynchronousRenderGuard.RunOrDefer(() => throw thrown);
                    SynchronousRenderGuard.RunOrDefer(() => log.Add("second"));
                    SynchronousRenderGuard.RunOrDefer(() => log.Add("third"));
                }
            });

            Assert.Same(thrown, caught);
            Assert.Equal(["second", "third"], log);
            Assert.False(SynchronousRenderGuard.IsActive);
        });

    /// <summary>
    /// The guard belongs to the UI thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The fixture's thread is the UI thread; the test runs on another thread.
    ///
    /// Steps:
    /// - Call Enter and RunOrDefer on the test thread.
    ///
    /// Expected:
    /// - Both throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Guard_OffTheUiThread_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            using (SynchronousRenderGuard.Enter()) { }
        });
        Assert.Throws<InvalidOperationException>(() => SynchronousRenderGuard.RunOrDefer(DoNothing));
    }

    private static void DoNothing() { }

    private static void Resize(Win32Window window, int width, int height) =>
        Assert.True(SetWindowPos(window.Hwnd, HWND.NULL, 0, 0, width, height, ResizeFlags));
}
