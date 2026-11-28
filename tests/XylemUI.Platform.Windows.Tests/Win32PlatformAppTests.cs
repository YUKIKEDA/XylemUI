using TerraFX.Interop.Windows;
using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// The app tier of the Windows host (row PL1, design §6.0): the loop's contract, the dispatcher and the synchronization context it
// sets and puts back, OLE, which it initializes when made and uninitializes when disposed, the fonts it owns, and the rasterizers it
// makes. The tests that run the loop wait in
// real time under a watchdog (.cursor/rules/testing.mdc).
[Collection(UiThreadGroup.Name)]
public sealed class Win32PlatformAppTests(UiThreadFixture ui)
{
    /// <summary>
    /// Making the host sets the loop's dispatcher as <see cref="UiDispatcher.Current"/> and its context as the thread's
    /// synchronization context; disposing it puts back the ones it replaced (design §2.12, "Where it is set").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the headless session, which set <see cref="UiDispatcher.Current"/> to its dispatcher. The thread's
    ///   synchronization context is set to a new <see cref="SynchronizationContext"/> for the test, and the one before is put back
    ///   after it.
    ///
    /// Steps:
    /// - Make the host; read its dispatcher, <see cref="UiDispatcher.Current"/>, and the thread's context. Dispose it; read the two
    ///   again.
    ///
    /// Expected:
    /// - The host's dispatcher is the loop's (<see cref="Win32MessageLoop.Dispatcher"/>), and not the session's.
    /// - While the host lives, <see cref="UiDispatcher.Current"/> is the host's dispatcher and the context is its context.
    /// - After the disposal they are the session's dispatcher and the test's context.
    /// </remarks>
    [Fact]
    public void Constructor_SetsItsDispatcherAndContext_DisposeGivesThePreviousOnesBack() =>
        ui.Invoke(() =>
        {
            IUiDispatcher before = UiDispatcher.Current;
            SynchronizationContext? original = SynchronizationContext.Current;
            var mine = new SynchronizationContext();
            UiDispatcher loop = Win32MessageLoop.Dispatcher;
            UiDispatcher dispatcher;
            IUiDispatcher current;
            SynchronizationContext? context;
            IUiDispatcher after;
            SynchronizationContext? contextAfter;
            SynchronizationContext.SetSynchronizationContext(mine);
            try
            {
                using (var app = new Win32PlatformApp())
                {
                    dispatcher = app.Dispatcher;
                    current = UiDispatcher.Current;
                    context = SynchronizationContext.Current;
                }

                after = UiDispatcher.Current;
                contextAfter = SynchronizationContext.Current;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }

            Assert.NotSame(before, loop);
            Assert.Same(loop, dispatcher);
            Assert.Same(dispatcher, current);
            Assert.Same(dispatcher.SynchronizationContext, context);
            Assert.Same(before, after);
            Assert.Same(mine, contextAfter);
        });

    /// <summary>
    /// A second disposal of a host does nothing: it does not take the dispatcher, the context, or OLE from a host made after the
    /// first disposal.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the headless session. The thread's synchronization context is set to a new
    ///   <see cref="SynchronizationContext"/> for the test, and the one before is put back after it.
    ///
    /// Steps:
    /// - Read how many OLE initializations of the thread are not yet balanced (<c>Com.UiThreadInitializations</c>). Make a host
    ///   and dispose it. Make a second host, and dispose the first again. While the second lives, read
    ///   <see cref="UiDispatcher.Current"/>, the thread's context, and the count again.
    ///
    /// Expected:
    /// - <see cref="UiDispatcher.Current"/> is the second host's dispatcher, the context is its context, and the count is one
    ///   more than before: the second host's initialization is still there.
    /// </remarks>
    [Fact]
    public void Dispose_Twice_TakesNothingFromTheNextHost() =>
        ui.Invoke(() =>
        {
            SynchronizationContext? original = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            int before = Com.UiThreadInitializations;
            UiDispatcher dispatcher;
            IUiDispatcher current;
            SynchronizationContext? context;
            int initializations;
            try
            {
                var first = new Win32PlatformApp();
                first.Dispose();
                using var second = new Win32PlatformApp();
                first.Dispose();
                dispatcher = second.Dispatcher;
                current = UiDispatcher.Current;
                context = SynchronizationContext.Current;
                initializations = Com.UiThreadInitializations;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }

            Assert.Same(dispatcher, current);
            Assert.Same(dispatcher.SynchronizationContext, context);
            Assert.Equal(before + 1, initializations);
        });

    /// <summary>
    /// Disposing the host from a thread that is not the UI thread throws and changes nothing; a later disposal on the UI thread
    /// still puts everything back (design §6.0, <c>IPlatformApp</c>'s <c>Dispose</c>).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the headless session, which set <see cref="UiDispatcher.Current"/> to its dispatcher. The thread's
    ///   synchronization context is set to a new <see cref="SynchronizationContext"/> for the test, and the one before is put back
    ///   after it. A host made on it.
    /// - A new thread in the single-threaded apartment, with no synchronization context.
    ///
    /// Steps:
    /// - Read <see cref="UiDispatcher.Current"/> and how many OLE initializations of the UI thread are not yet balanced
    ///   (<c>Com.UiThreadInitializations</c>), then make the host. On the new thread, dispose the host; then read that thread's
    ///   context and count. On the UI thread, read <see cref="UiDispatcher.Current"/>, the context, and the count; dispose the
    ///   host; read them again.
    ///
    /// Expected:
    /// - The disposal on the new thread threw <see cref="InvalidOperationException"/>; that thread has no context, and its count
    ///   is 0.
    /// - After it, the UI thread still has the host's dispatcher, the host's context, and one more initialization than before.
    /// - After the disposal on the UI thread, the dispatcher is the one read first, the context is the test's, and the count is the
    ///   one read first.
    /// </remarks>
    [Fact]
    public void Dispose_OffTheUiThread_ThrowsAndChangesNothing() =>
        ui.Invoke(() =>
        {
            IUiDispatcher before = UiDispatcher.Current;
            SynchronizationContext? original = SynchronizationContext.Current;
            var mine = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(mine);
            int initializationsBefore = Com.UiThreadInitializations;
            Exception? thrown = null;
            SynchronizationContext? workerContext = new();
            int workerInitializations = -1;
            UiDispatcher dispatcher;
            IUiDispatcher current;
            SynchronizationContext? context;
            int initializations;
            IUiDispatcher after;
            SynchronizationContext? contextAfter;
            int initializationsAfter;
            try
            {
                var app = new Win32PlatformApp();
                var thread = new Thread(() =>
                {
                    thrown = Record(() => DisposeOf(app));
                    workerContext = SynchronizationContext.Current;
                    workerInitializations = Com.UiThreadInitializations;
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                dispatcher = app.Dispatcher;
                current = UiDispatcher.Current;
                context = SynchronizationContext.Current;
                initializations = Com.UiThreadInitializations;
                app.Dispose();
                after = UiDispatcher.Current;
                contextAfter = SynchronizationContext.Current;
                initializationsAfter = Com.UiThreadInitializations;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }

            Assert.IsType<InvalidOperationException>(thrown);
            Assert.Null(workerContext);
            Assert.Equal(0, workerInitializations);
            Assert.Same(dispatcher, current);
            Assert.Same(dispatcher.SynchronizationContext, context);
            Assert.Equal(initializationsBefore + 1, initializations);
            Assert.Same(before, after);
            Assert.Same(mine, contextAfter);
            Assert.Equal(initializationsBefore, initializationsAfter);
        });

    /// <summary>
    /// Making the host initializes OLE on the UI thread once, and disposing it ends that initialization (design §6.0, "The loop,
    /// the dispatcher, and the app's lifetime"; §7, "COM initialization").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the headless session, in the single-threaded apartment. Whatever earlier tests left on it does not
    ///   matter: the test compares counts with the one it reads first.
    ///
    /// Steps:
    /// - Read how many OLE initializations of the thread are not yet balanced (<c>Com.UiThreadInitializations</c>), before the
    ///   host is made, while it lives, and after it is disposed. No documented call tells whether OLE is initialized on a thread;
    ///   <c>RegisterDragDrop</c> returned <c>S_OK</c> on a thread that never called <c>OleInitialize</c> (Issue #908's first
    ///   gate).
    ///
    /// Expected:
    /// - While the host lives the count is one more than before; after it, the count is the one before.
    /// </remarks>
    [Fact]
    public void Constructor_InitializesOle_DisposeUninitializesIt() =>
        ui.Invoke(() =>
        {
            int before = Com.UiThreadInitializations;
            int during;
            using (new Win32PlatformApp())
            {
                during = Com.UiThreadInitializations;
            }

            int after = Com.UiThreadInitializations;

            Assert.Equal(before + 1, during);
            Assert.Equal(before, after);
        });

    /// <summary>
    /// Made on a thread that is not the UI thread, the host throws and changes nothing: neither OLE on that thread, nor its
    /// synchronization context, nor <see cref="UiDispatcher.Current"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A new thread in the single-threaded apartment, where <c>OleInitialize</c> would succeed, with no synchronization context.
    ///   The UI thread is the headless session's, which set <see cref="UiDispatcher.Current"/> to its dispatcher.
    ///
    /// Steps:
    /// - Run an empty action on the UI thread, which starts the session when no test of the group has. Read
    ///   <see cref="UiDispatcher.Current"/>. On the new thread, make the host; then read the thread's context, and how many
    ///   OLE initializations of that thread are not yet balanced (<c>Com.UiThreadInitializations</c>, 0 on a new thread). Read
    ///   <see cref="UiDispatcher.Current"/> again.
    ///
    /// Expected:
    /// - The host threw <see cref="InvalidOperationException"/>, not the one of COM's apartment (<c>RPC_E_CHANGED_MODE</c>).
    /// - The thread has no context, its count is 0, and <see cref="UiDispatcher.Current"/> is the one read before.
    /// </remarks>
    [Fact]
    public void Constructor_OffTheUiThread_ThrowsAndChangesNothing()
    {
        // The fixture starts the session at its first Invoke; when this test is the first of the group, no dispatcher is set yet.
        ui.Invoke(static () => { });
        IUiDispatcher before = UiDispatcher.Current;
        Exception? thrown = null;
        SynchronizationContext? context = new();
        int initializations = -1;
        var thread = new Thread(() =>
        {
            thrown = Record(MakeAndDispose);
            context = SynchronizationContext.Current;
            initializations = Com.UiThreadInitializations;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        var exception = Assert.IsType<InvalidOperationException>(thrown);
        Assert.NotEqual(RPC.RPC_E_CHANGED_MODE, exception.HResult);
        Assert.Null(context);
        Assert.Equal(0, initializations);
        Assert.Same(before, UiDispatcher.Current);
    }

    /// <summary>
    /// <see cref="Win32PlatformApp.Run"/> handles the work posted before it, runs the frames asked for, and returns the code
    /// given to <see cref="Win32PlatformApp.Quit"/> (design §6.0, <c>IPlatformApp.Run</c>).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A host on the UI thread. Before its loop runs, a callback is posted to its dispatcher's context; the callback asks the
    ///   loop's frame scheduler for a frame, whose callback quits the host with 7. The loop's iteration hook quits with 1 after
    ///   5 s.
    ///
    /// Steps:
    /// - Run the host's loop.
    ///
    /// Expected:
    /// - It returns 7 before the watchdog; the posted callback and the frame each ran once.
    /// </remarks>
    [Fact]
    [Trait("Category", "OsTiming")]
    public void Run_HandlesThePostedWorkAndTheFrames_ReturnsTheCodeGivenToQuit() =>
        ui.Invoke(() =>
        {
            using var app = new Win32PlatformApp();
            using var watchdog = new LoopWatchdog();
            int posted = 0;
            int frames = 0;
            Win32MessageLoop.IterationHook = () =>
            {
                if (watchdog.Expired)
                {
                    app.Quit(1);
                }
            };

            int code;
            try
            {
                app.Dispatcher.SynchronizationContext.Post(
                    _ =>
                    {
                        posted++;
                        Win32MessageLoop.FrameScheduler.RequestFrame(() =>
                        {
                            frames++;
                            app.Quit(7);
                        });
                    },
                    null
                );
                code = app.Run();
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
                Win32MessageLoop.FrameScheduler.CancelFrame();
            }

            Assert.False(watchdog.Expired);
            Assert.Equal(7, code);
            Assert.Equal(1, posted);
            Assert.Equal(1, frames);
        });

    /// <summary>
    /// <see cref="Win32PlatformApp.Wake"/>, called from a worker thread, wakes the host's loop where it waits.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A host on the UI thread, with no frame asked for, so that its loop waits on the message queue alone. The loop's iteration
    ///   hook starts a worker at its first iteration, quits with 0 once the worker has set its flag, and quits with 1 after 5 s.
    ///
    /// Steps:
    /// - Run the host's loop. The worker sets its flag, then calls <see cref="Win32PlatformApp.Wake"/>.
    ///
    /// Expected:
    /// - The loop returns 0 before the watchdog.
    /// </remarks>
    [Fact]
    [Trait("Category", "OsTiming")]
    public void Wake_FromAWorker_WakesTheWaitingLoop() =>
        ui.Invoke(() =>
        {
            using var app = new Win32PlatformApp();
            using var watchdog = new LoopWatchdog();
            bool set = false;
            Thread? worker = null;
            Win32MessageLoop.IterationHook = () =>
            {
                if (watchdog.Expired)
                {
                    app.Quit(1);
                    return;
                }

                if (Volatile.Read(ref set))
                {
                    app.Quit(0);
                    return;
                }

                if (worker is null)
                {
                    worker = new Thread(() =>
                    {
                        Volatile.Write(ref set, true);
                        app.Wake();
                    });
                    worker.Start();
                }
            };

            int code;
            try
            {
                code = app.Run();
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
                worker?.Join();
            }

            Assert.False(watchdog.Expired);
            Assert.Equal(0, code);
        });

    /// <summary>
    /// The host's fonts are one provider for the UI thread (design §3.2; §6.0, "The font provider"): the same object before, while,
    /// and after each of two windows is open, still usable after a window is disposed, and disposed with the host.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Windows host whose windows draw on WARP, and two window cores.
    ///
    /// Steps:
    /// - Read the host's fonts. Open a hidden window for the first core and read them; dispose it. Open one for the second core
    ///   and read them; dispose it. Resolve the font of "Abc" with the fonts read first. Dispose the host, then resolve it again.
    ///
    /// Expected:
    /// - Every read is the same object, a <see cref="DirectWriteFontProvider"/>.
    /// - The resolve after the windows were disposed covers the three characters.
    /// - The resolve after the host was disposed throws <see cref="ObjectDisposedException"/>.
    /// </remarks>
    [Fact]
    public void Fonts_AreOneProviderAcrossTwoWindows_AndAreDisposedWithTheHost() =>
        ui.Invoke(() =>
        {
            var app = new Win32PlatformApp { UseWarp = true };
            IFontProvider first;
            IFontProvider whileFirst;
            IFontProvider whileSecond;
            IFontProvider afterBoth;
            FontMatch match;
            try
            {
                using var firstCore = new WindowCore(null, app.Settings, app.Clipboard, null, null);
                using var secondCore = new WindowCore(null, app.Settings, app.Clipboard, null, null);
                first = app.Fonts;
                IPlatformWindow window = app.OpenWindow(firstCore, "first", 200, 100);
                whileFirst = app.Fonts;
                window.Dispose();
                window = app.OpenWindow(secondCore, "second", 200, 100);
                whileSecond = app.Fonts;
                window.Dispose();
                afterBoth = app.Fonts;
                match = first.ResolveFont("Abc", 0, TextLanguage.FromTag("en"), FontRequest.Default);
            }
            finally
            {
                app.Dispose();
            }

            Exception? afterHost = Record(() => first.ResolveFont("Abc", 0, TextLanguage.FromTag("en"), FontRequest.Default));

            _ = Assert.IsType<DirectWriteFontProvider>(first);
            Assert.Same(first, whileFirst);
            Assert.Same(first, whileSecond);
            Assert.Same(first, afterBoth);
            Assert.Equal(3, match.Length);
            _ = Assert.IsType<ObjectDisposedException>(afterHost);
        });

    /// <summary>
    /// <see cref="Win32PlatformApp.CreateRasterizer"/> makes a new DirectWrite rasterizer on each call, so that each thread of a
    /// window's glyph atlas has its own (design §5.0.3, "Glyphs seen for the first time").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Windows host on the UI thread.
    ///
    /// Steps:
    /// - Call <see cref="Win32PlatformApp.CreateRasterizer"/> twice; dispose both rasterizers.
    ///
    /// Expected:
    /// - Both are <see cref="DirectWriteGlyphRasterizer"/>, and they are not the same object.
    /// </remarks>
    [Fact]
    public void CreateRasterizer_ReturnsANewRasterizerOnEachCall() =>
        ui.Invoke(() =>
        {
            using var app = new Win32PlatformApp();
            IGlyphRasterizer first = app.CreateRasterizer();
            IGlyphRasterizer second = app.CreateRasterizer();
            (first as IDisposable)?.Dispose();
            (second as IDisposable)?.Dispose();

            _ = Assert.IsType<DirectWriteGlyphRasterizer>(first);
            _ = Assert.IsType<DirectWriteGlyphRasterizer>(second);
            Assert.NotSame(first, second);
        });

    // Makes the host and disposes it; a helper, so that the thread that makes it off the UI thread on purpose calls no UI-thread-only
    // member in its own lambda (XYLEM100).
    private static void MakeAndDispose()
    {
        using var app = new Win32PlatformApp();
    }

    // Disposes the host; a helper, so that the thread that disposes it off the UI thread on purpose calls no UI-thread-only member
    // in its own lambda (XYLEM100).
    private static void DisposeOf(Win32PlatformApp app) => app.Dispose();

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
