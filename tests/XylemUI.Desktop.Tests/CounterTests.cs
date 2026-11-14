using Counter;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Desktop.Tests;

// The counter sample in a real window (row V1d, design §2.13, "How it is checked"): the pixels of its button on WARP, a click sent
// as the window's messages, the frames that do and do not present, and what closing the window leaves.
[Collection(UiThreadGroup.Name)]
public sealed class CounterTests(UiThreadFixture ui)
{
    private static readonly WindowOptions Options = new("Counter test", 320, 200);

    /// <summary>
    /// Opening the window runs the frame that the root function's nodes asked for, and leaves none asked for: the first frame did
    /// their work.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter's window on WARP, hidden.
    ///
    /// Steps:
    /// - Make the window; read the frame scheduler.
    ///
    /// Expected:
    /// - One frame built its display list, and the scheduler holds no frame.
    /// </remarks>
    [Fact]
    public void Open_RunsTheFirstFrame_AndLeavesNoneAsked() =>
        ui.Invoke(() =>
        {
            using var window = new TestWindow(CounterApp.Counter, Options, useWarp: true);
            Assert.Equal(1, window.Frames.Builds);
            Assert.False(((Win32FrameScheduler)Win32MessageLoop.FrameScheduler).HasPendingFrame);
        });

    /// <summary>
    /// The window's text fields copy and paste through the clipboard of the host's app tier (design §6.0, "The clipboard"), which
    /// the app gives the window core when it opens the window.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter's window on WARP, hidden, opened as <c>XylemApp.Run</c> opens it, by a host of the tests.
    ///
    /// Steps:
    /// - Read the tree's clipboard and the host's.
    ///
    /// Expected:
    /// - They are the same object.
    /// </remarks>
    [Fact]
    public void Open_GivesTheTreeTheHostsClipboard() =>
        ui.Invoke(() =>
        {
            using var window = new TestWindow(CounterApp.Counter, Options, useWarp: true);
            Assert.Same(window.Host.Clipboard, window.Tree.Clipboard);
        });

    /// <summary>
    /// Disposing a window cancels the frame its tree asked for: the loop's scheduler holds no frame for a window that is gone.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter's window on WARP, hidden. The loop is not run, so a frame that is asked for stays pending.
    ///
    /// Steps:
    /// - Send <c>WM_LBUTTONDOWN</c> and <c>WM_LBUTTONUP</c> at the button's center, which changes the text and asks for a frame;
    ///   read whether the loop's scheduler holds a frame. Dispose the window; read it again.
    ///
    /// Expected:
    /// - A frame is pending before the window is disposed, and none after.
    /// </remarks>
    [Fact]
    public void Dispose_WithAFrameAskedFor_CancelsIt() =>
        ui.Invoke(() =>
        {
            bool before;
            using (var window = new TestWindow(CounterApp.Counter, Options, useWarp: true))
            {
                (_, NodeHandle button) = Parts(window.Tree);
                (int x, int y) = Center(window, button);
                nint point = (y << 16) | (x & 0xFFFF);
                _ = SendMessageW(window.Window.Hwnd, WM.WM_LBUTTONDOWN, MK.MK_LBUTTON, point);
                _ = SendMessageW(window.Window.Hwnd, WM.WM_LBUTTONUP, 0, point);
                before = ((Win32FrameScheduler)Win32MessageLoop.FrameScheduler).HasPendingFrame;
            }

            Assert.True(before);
            Assert.False(((Win32FrameScheduler)Win32MessageLoop.FrameScheduler).HasPendingFrame);
        });

    /// <summary>
    /// A press and a release of the left button at the button's center, sent as the window's messages, make the text "clicks: 1",
    /// and the next frame lays it out and presents it; frames with nothing changed present nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter's window on WARP, hidden, its frames paced by the swap chain; the message loop runs under a watchdog.
    ///
    /// Steps:
    /// - Send <c>WM_LBUTTONDOWN</c> and <c>WM_LBUTTONUP</c> at the button's center in physical pixels; run the loop until a frame
    ///   presents. Then ask for three frames one after another with nothing changed, and run the loop until they ran.
    ///
    /// Expected:
    /// - The text is "clicks: 1" after the messages, and the frame that follows presents and leaves the text laid out. The three
    ///   frames present nothing.
    /// </remarks>
    [Fact]
    public void Click_SentAsMessages_CountsAndIsDrawnInTheNextFrame() =>
        ui.Invoke(() =>
        {
            using var window = new TestWindow(CounterApp.Counter, Options, useWarp: true);
            (NodeHandle text, NodeHandle button) = Parts(window.Tree);
            (int x, int y) = Center(window, button);
            int presents = window.Frames.PresentCount;

            using (var watchdog = new LoopWatchdog())
            {
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () => window.Frames.PresentCount > presents,
                    () =>
                    {
                        nint point = (y << 16) | (x & 0xFFFF);
                        _ = SendMessageW(window.Window.Hwnd, WM.WM_LBUTTONDOWN, MK.MK_LBUTTON, point);
                        _ = SendMessageW(window.Window.Hwnd, WM.WM_LBUTTONUP, 0, point);
                        Assert.Equal("clicks: 1", window.Tree.Nodes.GetRef(text).Text!.Text.ToString());
                    }
                );
                Assert.False(watchdog.Expired);
            }

            Assert.Equal(presents + 1, window.Frames.PresentCount);
            ref UiNode leaf = ref window.Tree.Nodes.GetRef(text);
            Assert.Equal(DirtyFlags.None, (leaf.Dirty.SelfDirty | leaf.Dirty.SubtreeDirty) & DirtyFlags.Layout);
            Assert.True(leaf.Text!.Shaped!.Valid);

            int clean = window.Frames.PresentCount;
            int ran = 0;
            void Frame()
            {
                window.Frames.RunFrame();
                if (++ran < 3)
                {
                    Win32MessageLoop.FrameScheduler.RequestFrame(Frame);
                }
            }

            using (var watchdog = new LoopWatchdog())
            {
                LoopTestHelpers.RunLoop(watchdog, () => ran == 3, () => Win32MessageLoop.FrameScheduler.RequestFrame(Frame));
                Assert.False(watchdog.Expired);
            }

            Assert.Equal(clean, window.Frames.PresentCount);
        });

    /// <summary>
    /// Closing the window disposes its root scope: no node but the root box is left, the scope is not alive, and the last window's
    /// close ends the message loop through the host's contract (<see cref="IPlatformApp.Quit"/>, design §6.0), with 0.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter's window on WARP, counted as an open window of an app whose host records each <c>Quit</c> and then quits the
    ///   message loop with its code; the message loop runs under a watchdog.
    ///
    /// Steps:
    /// - Ask the window to close, as its close button does; run the loop until it returns.
    ///
    /// Expected:
    /// - The loop returned before the watchdog; the host was asked to quit once, with 0; the window raised <c>Closed</c>; the tree
    ///   has one node, with no child; the root scope is not alive.
    /// </remarks>
    [Fact]
    public void Close_DisposesTheRootScope_AndEndsTheLoop() =>
        ui.Invoke(() =>
        {
            using var window = new TestWindow(CounterApp.Counter, Options, useWarp: true);
            ScopeHandle scope = window.RootScope;
            Assert.True(Scope.IsAlive(scope));
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            XylemApp.Open(window.Core, window.Host);

            using (var watchdog = new LoopWatchdog())
            {
                LoopTestHelpers.RunLoop(watchdog, () => watchdog.Expired, window.Window.RequestClose);
                Assert.False(watchdog.Expired);
            }

            Assert.Equal([0], window.Host.Quits);
            Assert.True(closed);
            Assert.Equal(1, window.Tree.Nodes.Count);
            Assert.True(window.Tree.Nodes.GetRef(window.Tree.Root).FirstChild.IsNone);
            Assert.False(Scope.IsAlive(scope));
        });

    /// <summary>
    /// <c>XylemApp.Run</c> opens the window, runs the root function once, and returns 0 when the window closes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root function that counts its runs and makes a box; the message loop's iteration hook asks the window of the run's title
    ///   to close, and quits after 5 s.
    ///
    /// Steps:
    /// - Run the app.
    ///
    /// Expected:
    /// - It returns 0 before the watchdog, with the root function run once.
    /// </remarks>
    [Fact]
    public void Run_ReturnsWhenTheWindowCloses() =>
        ui.Invoke(() =>
        {
            int runs = 0;
            using var watchdog = new LoopWatchdog();
            Win32MessageLoop.IterationHook = () =>
            {
                if (watchdog.Expired)
                {
                    Win32MessageLoop.Quit(1);
                    return;
                }

                AskToClose("Run test");
            };

            int code;
            try
            {
                code = XylemApp.Run(
                    () =>
                    {
                        runs++;
                        return Elements.Box();
                    },
                    new WindowOptions("Run test", 100, 80)
                );
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
            }

            Assert.False(watchdog.Expired);
            Assert.Equal(0, code);
            Assert.Equal(1, runs);
        });

    /// <summary>
    /// The root function runs with the UI thread's synchronization context, the one the message loop runs with: an element it
    /// makes that keeps the context (an image, whose decode's result is posted to it, design §5.0.4, "Decoding threads") has the
    /// loop's, as an element made later has (Issue #785).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The thread has no synchronization context, as an app's <c>Main</c> has none. The message loop's iteration hook reads the
    ///   context at its first iteration, asks the window to close, and quits after 5 s.
    ///
    /// Steps:
    /// - Run the app with a root function that reads the current context.
    ///
    /// Expected:
    /// - The root function's context is the loop's, and not null. After the run the thread has none again.
    /// </remarks>
    [Fact]
    public void Run_RootFunction_HasTheLoopsSynchronizationContext() =>
        ui.Invoke(() =>
        {
            SynchronizationContext? before = SynchronizationContext.Current;
            SynchronizationContext? inRoot = null;
            SynchronizationContext? inLoop = null;
            SynchronizationContext? after;
            using var watchdog = new LoopWatchdog();
            Win32MessageLoop.IterationHook = () =>
            {
                inLoop ??= SynchronizationContext.Current;
                if (watchdog.Expired)
                {
                    Win32MessageLoop.Quit(1);
                    return;
                }

                AskToClose("Run context test");
            };

            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                _ = XylemApp.Run(
                    () =>
                    {
                        inRoot = SynchronizationContext.Current;
                        return Elements.Box();
                    },
                    new WindowOptions("Run context test", 100, 80)
                );
                after = SynchronizationContext.Current;
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
                SynchronizationContext.SetSynchronizationContext(before);
            }

            Assert.False(watchdog.Expired);
            Assert.NotNull(inRoot);
            Assert.Same(inLoop, inRoot);
            Assert.Null(after);
        });

    /// <summary>
    /// A loop that the app ended with its window open (<c>Win32MessageLoop.Quit</c>) does not keep the next <c>XylemApp.Run</c> from
    /// running: the run counts no window of the one before (PR #340's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The message loop's iteration hook quits with 5 at its first iteration in the first run, and asks the window of the second
    ///   run's title to close in the second; both quit after 5 s.
    ///
    /// Steps:
    /// - Run the app twice.
    ///
    /// Expected:
    /// - The first run returns 5, and the second 0, both before the watchdog.
    /// </remarks>
    [Fact]
    public void Run_AfterALoopTheAppQuit_RunsAgain() =>
        ui.Invoke(() =>
        {
            using var watchdog = new LoopWatchdog();
            int first;
            int second;
            try
            {
                Win32MessageLoop.IterationHook = () => Win32MessageLoop.Quit(watchdog.Expired ? 1 : 5);
                first = XylemApp.Run(Elements.Box, new WindowOptions("Run quit test", 100, 80));
                Win32MessageLoop.IterationHook = () =>
                {
                    if (watchdog.Expired)
                    {
                        Win32MessageLoop.Quit(1);
                        return;
                    }

                    AskToClose("Run again test");
                };
                second = XylemApp.Run(Elements.Box, new WindowOptions("Run again test", 100, 80));
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
            }

            Assert.False(watchdog.Expired);
            Assert.Equal((5, 0), (first, second));
        });

    /// <summary>
    /// <c>XylemApp.Run</c> sets <see cref="UiDispatcher.Current"/> to the loop's dispatcher before the root function runs, and gives
    /// back the one it replaced when it returns (design §2.12): the headless session's, whose UI thread runs this test.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the headless session, which set <see cref="UiDispatcher.Current"/> to its dispatcher. The message loop's
    ///   iteration hook reads <see cref="UiDispatcher.Current"/> at its first iteration, asks the window to close, and quits after
    ///   5 s.
    ///
    /// Steps:
    /// - Read <see cref="UiDispatcher.Current"/>; run the app with a root function that reads it; read it after the run.
    ///
    /// Expected:
    /// - Before the run it is the session's dispatcher (<see cref="HeadlessSession.Dispatcher"/>).
    /// - In the root function and in the loop it is the loop's (<see cref="Win32MessageLoop.Dispatcher"/>).
    /// - After the run it is the session's again.
    /// </remarks>
    [Fact]
    public void Run_SetsCurrentToTheLoopsDispatcher_AndGivesThePreviousOneBack() =>
        ui.Invoke(() =>
        {
            IUiDispatcher before = UiDispatcher.Current;
            IUiDispatcher? inRoot = null;
            IUiDispatcher? inLoop = null;
            using var watchdog = new LoopWatchdog();
            Win32MessageLoop.IterationHook = () =>
            {
                inLoop ??= UiDispatcher.Current;
                if (watchdog.Expired)
                {
                    Win32MessageLoop.Quit(1);
                    return;
                }

                AskToClose("Run dispatcher test");
            };

            try
            {
                _ = XylemApp.Run(
                    () =>
                    {
                        inRoot = UiDispatcher.Current;
                        return Elements.Box();
                    },
                    new WindowOptions("Run dispatcher test", 100, 80)
                );
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
            }

            Assert.False(watchdog.Expired);
            Assert.Same(HeadlessSession.Dispatcher, before);
            Assert.Same(Win32MessageLoop.Dispatcher, inRoot);
            Assert.Same(Win32MessageLoop.Dispatcher, inLoop);
            Assert.Same(before, UiDispatcher.Current);
        });

    /// <summary>
    /// Called where no dispatcher is set, as from an app's <c>Main</c>, <c>XylemApp.Run</c> sets the loop's, and leaves none set
    /// when it returns.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - <see cref="UiDispatcher.Current"/> is cleared for the test (exchanged with <see langword="null"/>) and given back after it.
    ///   The message loop's iteration hook asks the window to close, and quits after 5 s.
    ///
    /// Steps:
    /// - Run the app with a root function that reads <see cref="UiDispatcher.Current"/>; read it after the run.
    ///
    /// Expected:
    /// - In the root function it is the loop's (<see cref="Win32MessageLoop.Dispatcher"/>).
    /// - After the run, reading it throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Run_WithNoDispatcherSet_SetsTheLoopsAndLeavesNoneAfter() =>
        ui.Invoke(() =>
        {
            IUiDispatcher? inRoot = null;
            Exception? after;
            using var watchdog = new LoopWatchdog();
            Win32MessageLoop.IterationHook = () =>
            {
                if (watchdog.Expired)
                {
                    Win32MessageLoop.Quit(1);
                    return;
                }

                AskToClose("Run no dispatcher test");
            };

            IUiDispatcher? session = UiDispatcher.Exchange(null);
            try
            {
                _ = XylemApp.Run(
                    () =>
                    {
                        inRoot = UiDispatcher.Current;
                        return Elements.Box();
                    },
                    new WindowOptions("Run no dispatcher test", 100, 80)
                );
                after = Record.Exception(() => UiDispatcher.Current);
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
                _ = UiDispatcher.Exchange(session);
            }

            Assert.False(watchdog.Expired);
            Assert.Same(Win32MessageLoop.Dispatcher, inRoot);
            Assert.IsType<InvalidOperationException>(after);
        });

    /// <summary>
    /// A worker that posts through <see cref="UiDispatcher.Current"/> while <c>XylemApp.Run</c> runs reaches the receiver on the UI
    /// thread, through the loop's queue.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root function that adds a receiver of <c>Ping</c> that records the number and whether it runs on the UI thread. The
    ///   message loop's iteration hook starts a worker thread at its first iteration, asks the window to close once the receiver
    ///   ran, and quits after 5 s.
    ///
    /// Steps:
    /// - Run the app. The worker reads <see cref="UiDispatcher.Current"/> and posts <c>Ping</c> 3 to it.
    ///
    /// Expected:
    /// - The worker read the loop's dispatcher (<see cref="Win32MessageLoop.Dispatcher"/>).
    /// - The receiver got 3, on the UI thread, and the run returned 0 before the watchdog.
    /// </remarks>
    [Fact]
    public void Run_AWorkerPostsThroughCurrent_TheReceiverGetsItOnTheUiThread() =>
        ui.Invoke(() =>
        {
            var received = new List<int>();
            bool onUiThread = false;
            IUiDispatcher? read = null;
            Thread? worker = null;
            using var watchdog = new LoopWatchdog();
            Win32MessageLoop.IterationHook = () =>
            {
                if (watchdog.Expired)
                {
                    Win32MessageLoop.Quit(1);
                    return;
                }

                if (worker is null)
                {
                    worker = new Thread(() =>
                    {
                        IUiDispatcher current = UiDispatcher.Current;
                        Volatile.Write(ref read, current);
                        current.Post(new Ping(3));
                    });
                    worker.Start();
                }

                if (received.Count > 0)
                {
                    AskToClose("Run post test");
                }
            };

            int code;
            try
            {
                code = XylemApp.Run(
                    () =>
                    {
                        UiMessage.On<Ping>(ping =>
                        {
                            received.Add(ping.Number);
                            onUiThread = UiThread.CheckAccess();
                        });
                        return Elements.Box();
                    },
                    new WindowOptions("Run post test", 100, 80)
                );
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
                worker?.Join();
            }

            Assert.False(watchdog.Expired);
            Assert.Equal(0, code);
            Assert.Same(Win32MessageLoop.Dispatcher, Volatile.Read(ref read));
            Assert.Equal([3], received);
            Assert.True(onUiThread);
        });

    /// <summary>
    /// A root function that throws leaves <see cref="UiDispatcher.Current"/> as it was before <c>XylemApp.Run</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the headless session, which set <see cref="UiDispatcher.Current"/> to its dispatcher.
    ///
    /// Steps:
    /// - Read <see cref="UiDispatcher.Current"/>; run the app with a root function that throws a <see cref="FormatException"/>;
    ///   read it again.
    ///
    /// Expected:
    /// - The run throws that exception, and <see cref="UiDispatcher.Current"/> is the one read before.
    /// </remarks>
    [Fact]
    public void Run_RootFunctionThrows_GivesThePreviousDispatcherBack() =>
        ui.Invoke(() =>
        {
            IUiDispatcher before = UiDispatcher.Current;
            var thrown = new FormatException("Thrown from the root function on purpose.");

            FormatException caught = Assert.Throws<FormatException>(() =>
                XylemApp.Run(() => throw thrown, new WindowOptions("Run throw test", 100, 80))
            );

            Assert.Same(thrown, caught);
            Assert.Same(before, UiDispatcher.Current);
        });

    /// <summary>
    /// When the loop ends, <c>XylemApp.Run</c> disposes the window before the host (design §6.0, "The order of creation", step 8):
    /// the cleanups of the window's root scope still run with the host's dispatcher and synchronization context, which the host puts
    /// back after them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the headless session, which set <see cref="UiDispatcher.Current"/> to its dispatcher. The message loop's
    ///   iteration hook quits with 5 at its first iteration, with the window still open, so that the run's disposal of the window
    ///   disposes the root scope; after 5 s it quits with 1.
    ///
    /// Steps:
    /// - Run the app with a root function that adds a cleanup to its scope; the cleanup reads <see cref="UiDispatcher.Current"/> and
    ///   <see cref="SynchronizationContext.Current"/>. Read <see cref="UiDispatcher.Current"/> after the run.
    ///
    /// Expected:
    /// - The run returns 5 before the watchdog, and the cleanup ran once.
    /// - In the cleanup the dispatcher is the loop's (<see cref="Win32MessageLoop.Dispatcher"/>) and the context is its context.
    /// - After the run the dispatcher is the session's again.
    /// </remarks>
    [Fact]
    public void Run_LoopEndsWithTheWindowOpen_DisposesTheWindowBeforeTheHost() =>
        ui.Invoke(() =>
        {
            IUiDispatcher before = UiDispatcher.Current;
            SynchronizationContext? context = SynchronizationContext.Current;
            int cleanups = 0;
            IUiDispatcher? inCleanup = null;
            SynchronizationContext? contextInCleanup = null;
            using var watchdog = new LoopWatchdog();
            Win32MessageLoop.IterationHook = () => Win32MessageLoop.Quit(watchdog.Expired ? 1 : 5);

            int code;
            try
            {
                code = XylemApp.Run(
                    () =>
                    {
                        Scope.OnCleanup(() =>
                        {
                            cleanups++;
                            inCleanup = UiDispatcher.Current;
                            contextInCleanup = SynchronizationContext.Current;
                        });
                        return Elements.Box();
                    },
                    new WindowOptions("Run order test", 100, 80)
                );
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
                SynchronizationContext.SetSynchronizationContext(context);
            }

            Assert.False(watchdog.Expired);
            Assert.Equal(5, code);
            Assert.Equal(1, cleanups);
            Assert.Same(Win32MessageLoop.Dispatcher, inCleanup);
            Assert.Same(Win32MessageLoop.Dispatcher.SynchronizationContext, contextInCleanup);
            Assert.Same(before, UiDispatcher.Current);
        });

    /// <summary>
    /// The window's options are checked before a window is made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread.
    ///
    /// Steps:
    /// - Make a window of width 0, of height NaN, and of width +∞.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentException"/>.
    /// </remarks>
    [Fact]
    public void Options_OutOfRange_Throw() =>
        ui.Invoke(() =>
        {
            Assert.Throws<ArgumentException>(() => new TestWindow(Elements.Box, new WindowOptions(Width: 0), useWarp: true));
            Assert.Throws<ArgumentException>(() => new TestWindow(Elements.Box, new WindowOptions(Height: float.NaN), useWarp: true));
            Assert.Throws<ArgumentException>(() => new TestWindow(Elements.Box, new WindowOptions(Width: float.PositiveInfinity), useWarp: true));
        });

    // Posts WM_CLOSE to the XylemUI window of the title, if there is one, as its close button does.
    private static unsafe void AskToClose(string title)
    {
        // The strings are pinned for the call; FindWindowW reads them and keeps neither.
        fixed (char* className = "XylemUI.Window")
        {
            fixed (char* name = title)
            {
                HWND found = FindWindowW(className, name);
                if (found != HWND.NULL)
                {
                    _ = PostMessageW(found, WM.WM_CLOSE, 0, 0);
                }
            }
        }
    }

    // The counter's text and button: the column's first and second child.
    private static (NodeHandle Text, NodeHandle Button) Parts(UiTree tree)
    {
        NodeHandle column = tree.Nodes.GetRef(tree.Root).FirstChild;
        NodeHandle text = tree.Nodes.GetRef(column).FirstChild;
        return (text, tree.Nodes.GetRef(text).NextSibling);
    }

    private sealed record Ping(int Number) : UiMessage;

    // A host that records the codes it is asked to quit with, and quits the message loop with each, as the Windows host does.
    private sealed class QuitRecorder : IPlatformApp
    {
        public List<int> Quits { get; } = [];

        public UiDispatcher Dispatcher => Win32MessageLoop.Dispatcher;

        public int Run() => Win32MessageLoop.Run();

        public void Quit(int exitCode)
        {
            Quits.Add(exitCode);
            Win32MessageLoop.Quit(exitCode);
        }

        public void Wake() => Win32MessageLoop.Wake();

        public void Dispose() { }
    }

    // The center of a node's box in the window's physical pixels; the column is at the root's top left, the button in it.
    private static (int X, int Y) Center(TestWindow window, NodeHandle node)
    {
        NodeHandle column = window.Tree.Nodes.GetRef(node).Parent;
        Rect columnBox = window.Tree.GetRect(column);
        Rect box = window.Tree.GetRect(node);
        float scale = window.Window.ScaleFactor;
        return (
            (int)MathF.Round((columnBox.X + box.X + (box.Width / 2)) * scale),
            (int)MathF.Round((columnBox.Y + box.Y + (box.Height / 2)) * scale)
        );
    }
}
