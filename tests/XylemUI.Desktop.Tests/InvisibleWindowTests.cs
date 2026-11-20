using Counter;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Testing.XUnit;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The frames of a window that is hidden or minimized (row IM1's 3b, design §2.11, "Frames of hidden and minimized windows"): a
// headless app's SetVisible, and a real window's minimizing through to its frames.
[Collection(UiThreadGroup.Name)]
public sealed class InvisibleWindowTests(UiThreadFixture ui)
{
    private static readonly WindowOptions Options = new("Invisible window test", 320, 200);

    /// <summary>
    /// A hidden headless app's animation does not advance and the pump settles; the first frame after the app shows again moves the
    /// animation to the clock's time, which ends one whose time has passed (design §12.4, "A hidden window"; §2.14).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of a box whose root makes an animation of 100 ms, at a frame interval of 1/60 s.
    ///
    /// Steps:
    /// - Animate to 1 and run one frame, which starts the move. Hide the app, pump until settled, and read the value.
    /// - Advance the clock by 1 s. Show the app and pump until settled.
    /// - Animate to 0 and run one frame. Hide the app, pump until settled, and dispose it.
    ///
    /// Expected:
    /// - Hidden, the pump runs the one frame the animation asked for, which does nothing, and settles: the value is still 0 and
    ///   nothing was presented. Shown, the pump runs one frame, whose time is past the move's 100 ms, and the value is 1.
    ///   Hidden again, the value is still 1 and the disposal does not throw with the animation unfinished: no frame and no
    ///   timer is left.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task HiddenApp_DoesNotAdvanceItsAnimation_UntilItShows()
    {
        Animation animation = null!;
        HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            animation = Animation.Create(TimeSpan.FromMilliseconds(100));
            return Box();
        });
        int presents = app.PresentCount;

        animation.AnimateTo(1);
        app.Frames.RunFrame();
        app.SetVisible(false);
        int hidden = app.PumpAndSettle();
        Assert.Equal((1, 0f, presents), (hidden, animation.Value.Get(), app.PresentCount));

        app.Time.Advance(TimeSpan.FromSeconds(1));
        app.SetVisible(true);
        int shown = app.PumpAndSettle();
        Assert.Equal((1, 1f), (shown, animation.Value.Get()));

        animation.AnimateTo(0);
        app.Frames.RunFrame();
        app.SetVisible(false);
        _ = app.PumpAndSettle();
        Assert.Equal(1, animation.Value.Get());
        await app.DisposeAsync();
    }

    /// <summary>
    /// <see cref="HeadlessApp.SetVisible"/> on an app that is not running throws, as the other entries do.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app that was disposed.
    ///
    /// Steps:
    /// - Call SetVisible(false).
    ///
    /// Expected:
    /// - <see cref="ObjectDisposedException"/>, which is an <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SetVisible_OnAnAppThatIsNotRunning_Throws()
    {
        HeadlessApp app = await HeadlessApp.StartAsync(() => Box());
        await app.DisposeAsync();

        _ = Assert.ThrowsAny<InvalidOperationException>(() => app.SetVisible(false));
    }

    /// <summary>
    /// A real window's frames stop while it is minimized and run again when it is restored: the window's notice reaches its frames
    /// (design §2.11, "Platform and Desktop").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The counter's window on WARP, never shown, its first frame presented.
    ///
    /// Steps:
    /// - Minimize the window without activating it (<c>SW_SHOWMINNOACTIVE</c>). Set the background of the root's child, and run
    ///   the frames' <c>RunFrame</c>.
    /// - Restore the window without activating it (<c>SW_SHOWNOACTIVATE</c>).
    ///
    /// Expected:
    /// - Minimized, the change asks the scheduler for no frame and the frame presents nothing. The restore's size message renders
    ///   at once, so the present count is higher when <c>ShowWindow</c> returns.
    /// </remarks>
    [Fact]
    public void MinimizedWindow_PresentsNothing_UntilItIsRestored() =>
        ui.Invoke(() =>
        {
            using var window = new TestWindow(CounterApp.Counter, Options, useWarp: true);
            var scheduler = (Win32FrameScheduler)Win32MessageLoop.FrameScheduler;
            NodeHandle column = window.Tree.Nodes.GetRef(window.Tree.Root).FirstChild;
            int presents = window.Frames.PresentCount;
            Assert.True(window.Window.IsShowing);

            _ = ShowWindow(window.Window.Hwnd, SW.SW_SHOWMINNOACTIVE);
            Assert.False(window.Window.IsShowing);
            window.Tree.SetBackground(column, new Paint(Color.FromRgb(0xCC3333)));
            window.Frames.RunFrame();

            Assert.False(scheduler.HasPendingFrame);
            Assert.Equal(presents, window.Frames.PresentCount);

            _ = ShowWindow(window.Window.Hwnd, SW.SW_SHOWNOACTIVATE);

            Assert.True(window.Window.IsShowing);
            Assert.True(window.Frames.PresentCount > presents);
        });
}
