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
    /// The pixel at the center of the counter's button is the background the sample gives it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter's window on WARP, hidden; its target reads the pixel at the button's center after each draw, before the present.
    ///
    /// Steps:
    /// - Make the window, which draws the first frame.
    ///
    /// Expected:
    /// - The pixel is <c>CounterApp.ButtonBackground</c>, and the button's text is "+1".
    /// </remarks>
    [Fact]
    public void Button_CenterPixel_IsItsBackground() =>
        ui.Invoke(() =>
        {
            ReadingTarget? reading = null;
            using var window = new DesktopWindow(
                CounterApp.Counter,
                Options,
                useWarp: true,
                target: renderer => reading = new ReadingTarget(renderer)
            );
            (NodeHandle text, NodeHandle button) = Parts(window.Tree);
            reading!.Point = Center(window, button);
            window.Frames.RunFrame();
            Assert.Equal(1, reading.Draws);

            // The first frame drew before the point was known: draw the tree again to read it.
            Invalidation.Invalidate(window.Tree, button, ChangeKind.Color);
            window.Frames.RunFrame();
            Assert.Equal(CounterApp.ButtonBackground, reading.Pixel);
            NodeHandle label = window.Tree.Nodes.GetRef(button).FirstChild;
            Assert.Equal("+1", window.Tree.Nodes.GetRef(label).Text!.Text.ToString());
            Assert.Equal("clicks: 0", window.Tree.Nodes.GetRef(text).Text!.Text.ToString());
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
            using var window = new DesktopWindow(CounterApp.Counter, Options, useWarp: true);
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
    /// close ends the message loop.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter's window on WARP, counted as an open window of the app; the message loop runs under a watchdog.
    ///
    /// Steps:
    /// - Ask the window to close, as its close button does; run the loop until it returns.
    ///
    /// Expected:
    /// - The loop returned before the watchdog; the window raised <c>Closed</c>; the tree has one node, with no child; the root
    ///   scope is not alive.
    /// </remarks>
    [Fact]
    public void Close_DisposesTheRootScope_AndEndsTheLoop() =>
        ui.Invoke(() =>
        {
            using var window = new DesktopWindow(CounterApp.Counter, Options, useWarp: true);
            ScopeHandle scope = window.RootScope;
            Assert.True(Scope.IsAlive(scope));
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            XylemApp.Open(window);

            using (var watchdog = new LoopWatchdog())
            {
                LoopTestHelpers.RunLoop(watchdog, () => watchdog.Expired, window.Window.RequestClose);
                Assert.False(watchdog.Expired);
            }

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
            Assert.Throws<ArgumentException>(() => new DesktopWindow(Elements.Box, new WindowOptions(Width: 0), useWarp: true));
            Assert.Throws<ArgumentException>(() => new DesktopWindow(Elements.Box, new WindowOptions(Height: float.NaN), useWarp: true));
            Assert.Throws<ArgumentException>(() => new DesktopWindow(Elements.Box, new WindowOptions(Width: float.PositiveInfinity), useWarp: true));
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

    // The center of a node's box in the window's physical pixels; the column is at the root's top left, the button in it.
    private static (int X, int Y) Center(DesktopWindow window, NodeHandle node)
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

    // Draws with the renderer and reads one pixel of each draw, before the present makes the back buffer undefined.
    private sealed class ReadingTarget(D3D11Renderer renderer) : IRenderTarget
    {
        public (int X, int Y)? Point { get; set; }

        public Color Pixel { get; private set; }

        public int Draws { get; private set; }

        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas)
        {
            renderer.Draw(list, clearColor, atlas);
            Draws++;
            if (Point is (int x, int y))
            {
                Pixel = renderer.ReadPixel(x, y);
            }
        }

        public void Present() => renderer.Present();
    }
}
