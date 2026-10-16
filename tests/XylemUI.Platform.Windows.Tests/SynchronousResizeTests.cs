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
    /// - Key messages queued for the window: Right three times, then Return. The first arrow picks the right edge, and the
    ///   next ones move it.
    /// - A guard thread queues Escape after three seconds, so a modal loop that ignored the keys still ends.
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
    /// A resize caused by the synchronous render itself is not rendered inside it; its final size is rendered once afterwards.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose synchronous render logs the size and, on its first call, resizes the window twice more.
    ///
    /// Steps:
    /// - Resize the window once.
    ///
    /// Expected:
    /// - Two renders: the first size, then the final size (the client size GetClientRect reports), and nothing in between.
    /// - The two nested size changes were counted; the guard is inactive afterwards.
    /// </remarks>
    [Fact]
    public void NestedResize_DuringTheRender_RendersTheFinalSizeAfterIt() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("nested", 200, 100);
            var sizes = new List<(int Width, int Height, bool Guarded)>();
            window.SynchronousRender = (w, width, height) =>
            {
                sizes.Add((width, height, SynchronousRenderGuard.IsActive));
                if (sizes.Count == 1)
                {
                    Resize(w, 450, 350);
                    Resize(w, 500, 400);
                }
            };

            Resize(window, 300, 200);

            RECT client;
            Assert.True(GetClientRect(window.Hwnd, &client));
            Assert.Equal(2, sizes.Count);
            Assert.NotEqual((sizes[0].Width, sizes[0].Height), (sizes[1].Width, sizes[1].Height));
            Assert.Equal((client.right - client.left, client.bottom - client.top), (sizes[1].Width, sizes[1].Height));
            Assert.All(sizes, size => Assert.True(size.Guarded));
            Assert.Equal(2, window.DroppedNestedResizes);
            Assert.False(SynchronousRenderGuard.IsActive);
        });

    /// <summary>
    /// A window resized while another window renders synchronously is rendered at its final size after that render.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two hidden windows with synchronous renders that log; the first one's render resizes the second.
    ///
    /// Steps:
    /// - Resize the first window.
    ///
    /// Expected:
    /// - The log is: the first window, then the second window at its final client size.
    /// </remarks>
    [Fact]
    public void OtherWindowResize_DuringARender_RendersAfterIt() =>
        ui.Invoke(() =>
        {
            using var first = new Win32Window("first", 200, 100);
            using var second = new Win32Window("second", 200, 100);
            var log = new List<string>();
            first.SynchronousRender = (_, _, _) =>
            {
                log.Add("first");
                Resize(second, 360, 260);
            };
            second.SynchronousRender = (_, width, height) => log.Add($"second {width}x{height}");

            Resize(first, 300, 200);

            RECT client;
            Assert.True(GetClientRect(second.Hwnd, &client));
            Assert.Equal(["first", $"second {client.right - client.left}x{client.bottom - client.top}"], log);
        });

    /// <summary>
    /// Minimizing a window does not render it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A shown window whose synchronous render counts its calls, set after the window was shown.
    ///
    /// Steps:
    /// - Minimize the window (WM_SIZE with SIZE_MINIMIZED).
    ///
    /// Expected:
    /// - No render.
    /// </remarks>
    [Fact]
    public void Minimize_DoesNotRender() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("minimize", 200, 100);
            window.Show();
            int renders = 0;
            window.SynchronousRender = (_, _, _) => renders++;

            _ = ShowWindow(window.Hwnd, SW.SW_MINIMIZE);

            Assert.True(IsIconic(window.Hwnd));
            Assert.Equal(0, renders);
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
    /// The guard counts depth: an inner scope's end runs nothing, and the outermost end runs every deferred callback in order.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An outer and an inner scope, each deferring one callback that logs.
    ///
    /// Steps:
    /// - End the inner scope, then the outer one.
    ///
    /// Expected:
    /// - Nothing ran at the inner end, and the guard was still active.
    /// - Both ran at the outer end, in the order they were deferred.
    /// </remarks>
    [Fact]
    public void NestedScopes_InnerEnd_RunsNothing() =>
        ui.Invoke(() =>
        {
            var log = new List<string>();
            using (SynchronousRenderGuard.Enter())
            {
                SynchronousRenderGuard.RunOrDefer(() => log.Add("outer"));
                using (SynchronousRenderGuard.Enter())
                {
                    SynchronousRenderGuard.RunOrDefer(() => log.Add("inner"));
                }

                Assert.Empty(log);
                Assert.True(SynchronousRenderGuard.IsActive);
            }

            Assert.Equal(["outer", "inner"], log);
            Assert.False(SynchronousRenderGuard.IsActive);
        });

    /// <summary>
    /// Outside a synchronous render, RunOrDefer runs the callback at once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No scope is open.
    ///
    /// Steps:
    /// - Call RunOrDefer with a callback that logs, then log after the call.
    ///
    /// Expected:
    /// - The callback's entry comes before the entry after the call.
    /// </remarks>
    [Fact]
    public void RunOrDefer_OutsideARender_RunsAtOnce() =>
        ui.Invoke(() =>
        {
            var log = new List<string>();
            SynchronousRenderGuard.RunOrDefer(() => log.Add("callback"));
            log.Add("after the call");

            Assert.Equal(["callback", "after the call"], log);
        });

    /// <summary>
    /// Ending scopes out of nesting order fails the invariant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release builds end the process instead).
    /// - An outer and an inner scope.
    ///
    /// Steps:
    /// - End the outer scope first; then end both in order to restore the guard.
    ///
    /// Expected:
    /// - Ending the outer scope first throws <see cref="InvariantViolationException"/>, and the guard is inactive after the
    ///   scopes end in order.
    /// </remarks>
    [Fact]
    public void Scopes_EndedOutOfOrder_FailTheInvariant() =>
        ui.Invoke(() =>
        {
            RequireDebugBuild();
            SynchronousRenderGuard.Scope outer = SynchronousRenderGuard.Enter();
            SynchronousRenderGuard.Scope inner = SynchronousRenderGuard.Enter();
            InvariantViolationException? caught = null;
            try
            {
                // A scope is a ref struct, so it cannot be captured by Assert.Throws's lambda.
                try
                {
                    outer.Dispose();
                }
                catch (InvariantViolationException exception)
                {
                    caught = exception;
                }

                Assert.NotNull(caught);
            }
            finally
            {
                inner.Dispose();
                outer.Dispose();
            }

            Assert.False(SynchronousRenderGuard.IsActive);
        });

    /// <summary>
    /// When several deferred callbacks throw, the end of the scope throws an AggregateException with all of them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scope with three deferred callbacks; the first and the third throw.
    ///
    /// Steps:
    /// - End the scope.
    ///
    /// Expected:
    /// - An <see cref="AggregateException"/> with the two exceptions, in order; the second callback ran.
    /// </remarks>
    [Fact]
    public void DeferredCallbacks_SeveralThrow_ThrowAllTogether() =>
        ui.Invoke(() =>
        {
            var first = new InvalidOperationException("first");
            var third = new InvalidOperationException("third");
            bool secondRan = false;

            var aggregate = Assert.Throws<AggregateException>(() =>
            {
                using (SynchronousRenderGuard.Enter())
                {
                    SynchronousRenderGuard.RunOrDefer(() => throw first);
                    SynchronousRenderGuard.RunOrDefer(() => secondRan = true);
                    SynchronousRenderGuard.RunOrDefer(() => throw third);
                }
            });

            Assert.True(secondRan);
            Assert.Equal([first, third], aggregate.InnerExceptions);
        });

    /// <summary>
    /// When the render throws and a deferred callback throws too, the window procedure reports both.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A callback exception handler that records what it receives.
    /// - A hidden window whose synchronous render defers a throwing callback and then throws.
    ///
    /// Steps:
    /// - Resize the window.
    ///
    /// Expected:
    /// - The handler received one <see cref="AggregateException"/> with the render's exception first and the callback's second.
    /// </remarks>
    [Fact]
    public void RenderAndDeferredCallback_BothThrow_BothAreReported() =>
        ui.Invoke(() =>
        {
            Action<Exception> previous = Win32MessageLoop.CallbackExceptionHandler;
            var reported = new List<Exception>();
            Win32MessageLoop.CallbackExceptionHandler = reported.Add;
            try
            {
                using var window = new Win32Window("both throw", 200, 100);
                var fromRender = new InvalidOperationException("from the render");
                var fromCallback = new InvalidOperationException("from the deferred callback");
                window.SynchronousRender = (_, _, _) =>
                {
                    SynchronousRenderGuard.RunOrDefer(() => throw fromCallback);
                    throw fromRender;
                };

                Resize(window, 300, 200);

                var aggregate = Assert.IsType<AggregateException>(Assert.Single(reported));
                Assert.Equal([fromRender, fromCallback], aggregate.InnerExceptions);
            }
            finally
            {
                Win32MessageLoop.CallbackExceptionHandler = previous;
            }
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

    private static void RequireDebugBuild()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
    }

    private static void DoNothing() { }

    private static void Resize(Win32Window window, int width, int height) =>
        Assert.True(SetWindowPos(window.Hwnd, HWND.NULL, 0, 0, width, height, ResizeFlags));
}
