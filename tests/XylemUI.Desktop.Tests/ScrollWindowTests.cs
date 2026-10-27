using System.Numerics;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The scroll container in a real window (row V2, design §3.4.1, "The scroll container"): its pixels on WARP before and after it
// scrolls, and the wheel's messages of Windows that scroll it.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class ScrollWindowTests(UiThreadFixture ui)
{
    private static readonly Color First = Color.FromRgb(0xCC3333);
    private static readonly Color Below = Color.FromRgb(0x3366CC);
    private static readonly Color Gray = Color.FromRgb(0x808080);

    /// <summary>
    /// A box below the scroll view's height is clipped, and drawn where the view shows it once it scrolls.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 200 on WARP, hidden: a scroll view 100 high holding a column of five boxes 50 high, the first red, the
    ///   fourth blue, the others gray; the window behind is white.
    ///
    /// Steps:
    /// - Read the pixels at (20, 25) and (20, 175) in logical pixels. Scroll to (0, 150); draw; read (20, 25).
    ///
    /// Expected:
    /// - Red at (20, 25) and white at (20, 175), where the fourth box would be without the clip; blue at (20, 25) after the scroll.
    /// </remarks>
    [Fact]
    public void ClippedBox_IsDrawnOnceScrolledIntoView() =>
        ui.Invoke(() =>
        {
            var state = new ScrollState();
            ReadingTarget? reading = null;
            using var window = new DesktopWindow(
                () => Column(ScrollView(Column(Box5()), state).Height(100)),
                new WindowOptions("Scroll pixels", 200, 200),
                useWarp: true,
                target: renderer => reading = new ReadingTarget(renderer)
            );
            float scale = window.Window.ScaleFactor;

            Assert.Equal(First, Read(window, reading!, 20, 25, scale));
            Assert.Equal(Color.FromRgb(0xFFFFFF), Read(window, reading!, 20, 175, scale));

            state.ScrollTo(new Vector2(0, 150));
            Assert.Equal(Below, Read(window, reading!, 20, 25, scale));
        });

    /// <summary>
    /// A notch of the vertical wheel scrolls the view under the pointer by the lines Windows sets times 100/3, a notch of the
    /// horizontal wheel scrolls across by the characters set, and the next frame presents.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 200 on WARP, hidden: a scroll view 100 high holding ten boxes 50 high (largest position 400), and a box
    ///   100 high that scrolls across holding a row 2,000 wide; the wheel settings of this machine; the message loop under a
    ///   watchdog.
    ///
    /// Steps:
    /// - Send WM_MOUSEWHEEL of −120 (down) at the screen point of the view's (20, 25); run the loop until a frame presents. Send
    ///   WM_MOUSEHWHEEL of +120 (right) over the other box.
    ///
    /// Expected:
    /// - The view is at (0, min(lines × 100/3, 400)) and a frame presented; the other box is at (min(characters × 100/3, 1,800), 0).
    /// </remarks>
    [Fact]
    public void WheelMessages_ScrollByTheSettingsOfWindows() =>
        ui.Invoke(() =>
        {
            var vertical = new ScrollState();
            var across = new ScrollState();
            using var window = new DesktopWindow(
                () =>
                    Column(
                        ScrollView(Column(Boxes(10)), vertical).Height(100),
                        Box(Row(Box().Width(2_000).Height(10)).Width(2_000))
                            .Overflow(Overflow.Scroll, Overflow.Visible)
                            .ScrollState(across)
                            .Height(100)
                    ),
                new WindowOptions("Scroll wheel", 200, 200),
                useWarp: true
            );
            uint lines;
            uint chars;
            Assert.True(SystemParametersInfoW(SPI.SPI_GETWHEELSCROLLLINES, 0, &lines, 0) != BOOL.FALSE);
            Assert.True(SystemParametersInfoW(SPI.SPI_GETWHEELSCROLLCHARS, 0, &chars, 0) != BOOL.FALSE);
            float scale = window.Window.ScaleFactor;
            int presents = window.Frames.PresentCount;

            using (var watchdog = new LoopWatchdog())
            {
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () => window.Frames.PresentCount > presents,
                    () => SendWheel(window, WM.WM_MOUSEWHEEL, -120, 20 * scale, 25 * scale)
                );
                Assert.False(watchdog.Expired);
            }

            Assert.Equal(Math.Min(lines * 100f / 3, 400), vertical.Offset.Get().Y, 2);
            SendWheel(window, WM.WM_MOUSEHWHEEL, 120, 20 * scale, 150 * scale);
            Assert.Equal(Math.Min(chars * 100f / 3, 1_800), across.Offset.Get().X, 2);
        });

    // Draws the tree again with the pixel at (x, y) logical pixels read, and returns it.
    private static Color Read(DesktopWindow window, ReadingTarget reading, float x, float y, float scale)
    {
        reading.Point = ((int)(x * scale), (int)(y * scale));
        Invalidation.Invalidate(window.Tree, window.Tree.Root, ChangeKind.Color);
        window.Frames.RunFrame();
        return reading.Pixel;
    }

    // Sends a wheel message of the amount at a client point in physical pixels, given as Windows gives it: in screen coordinates.
    private static void SendWheel(DesktopWindow window, uint message, int amount, float x, float y)
    {
        var point = new POINT { x = (int)x, y = (int)y };
        _ = ClientToScreen(window.Window.Hwnd, &point);
        nint lParam = (nint)(((uint)(ushort)(short)point.y << 16) | (ushort)(short)point.x);
        _ = SendMessageW(window.Window.Hwnd, message, unchecked((nuint)(uint)(amount << 16)), lParam);
    }

    private static Element[] Box5()
    {
        Element[] boxes = Boxes(5);
        _ = boxes[0].Background(new Paint(First));
        _ = boxes[3].Background(new Paint(Below));
        return boxes;
    }

    private static Element[] Boxes(int count)
    {
        var boxes = new Element[count];
        for (int i = 0; i < count; i++)
        {
            boxes[i] = Box().Height(50).Background(new Paint(Gray));
        }

        return boxes;
    }
}
