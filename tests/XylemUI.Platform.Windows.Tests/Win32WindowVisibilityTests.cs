using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// Whether the window shows (row IM1's 3b, design §2.11, "Frames of hidden and minimized windows"): read in WM_WINDOWPOSCHANGED from
// the flags of the change and from IsIconic, and told before the WM_SIZE the message sends. The tests open one window on screen.
[Collection(UiThreadGroup.Name)]
public sealed class Win32WindowVisibilityTests(UiThreadFixture ui)
{
    private const string Size = "WM_SIZE";

    /// <summary>
    /// Hiding, showing, minimizing, and restoring a window each tell whether it shows, once, and a window that was never shown
    /// shows; inside <c>WM_WINDOWPOSCHANGED</c>, <c>IsIconic</c> already gives the new state, and the notice comes before the
    /// message's <c>WM_SIZE</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window that was never shown, which records its notices, the <c>WM_SIZE</c> messages among them in order, and what
    ///   <c>IsIconic</c> returns inside each <c>WM_WINDOWPOSCHANGED</c>.
    ///
    /// Steps:
    /// - Read whether it shows. Show it without activating it (<c>SW_SHOWNOACTIVATE</c>), hide it (<c>SW_HIDE</c>), show it,
    ///   minimize it (<c>SW_SHOWMINNOACTIVE</c>), and restore it (<c>SW_SHOWNOACTIVATE</c>); after each, read the records.
    /// - Destroy the window while it shows.
    ///
    /// Expected:
    /// - It shows before it was ever shown, and the first showing tells nothing. Hiding tells false and showing tells true.
    ///   Minimizing tells false, with <c>IsIconic</c> true inside the message, and the notice before the <c>WM_SIZE</c>.
    ///   Restoring tells true, with <c>IsIconic</c> false inside the message, and the notice before the <c>WM_SIZE</c>.
    ///   Destroying tells false: Windows hides a window that shows before it destroys it.
    /// </remarks>
    [Fact]
    public void HideShowMinimizeRestore_EachTellWhetherTheWindowShows() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("visibility", 200, 100);
            var events = new List<string>();
            var iconic = new List<bool>();
            window.VisibilityChanged = shows => events.Add(shows ? "shows" : "stopped");
            window.MessageHook = (_, message) =>
            {
                if (message == WM.WM_WINDOWPOSCHANGED)
                {
                    iconic.Add(IsIconic(window.Hwnd) != BOOL.FALSE);
                }
                else if (message == WM.WM_SIZE)
                {
                    events.Add(Size);
                }

                return false;
            };
            Assert.True(window.IsShowing);

            _ = ShowWindow(window.Hwnd, SW.SW_SHOWNOACTIVATE);
            Assert.True(window.IsShowing);
            Assert.DoesNotContain("shows", events);
            Assert.DoesNotContain("stopped", events);

            events.Clear();
            _ = ShowWindow(window.Hwnd, SW.SW_HIDE);
            Assert.Equal(["stopped"], events.Where(e => e != Size));
            Assert.False(window.IsShowing);

            events.Clear();
            _ = ShowWindow(window.Hwnd, SW.SW_SHOWNOACTIVATE);
            Assert.Equal(["shows"], events.Where(e => e != Size));
            Assert.True(window.IsShowing);

            events.Clear();
            iconic.Clear();
            _ = ShowWindow(window.Hwnd, SW.SW_SHOWMINNOACTIVE);
            Assert.Equal(["stopped", Size], events);
            Assert.All(iconic, Assert.True);
            Assert.NotEmpty(iconic);
            Assert.False(window.IsShowing);

            events.Clear();
            iconic.Clear();
            _ = ShowWindow(window.Hwnd, SW.SW_SHOWNOACTIVATE);
            Assert.Equal(["shows", Size], events);
            Assert.All(iconic, Assert.False);
            Assert.NotEmpty(iconic);
            Assert.True(window.IsShowing);

            events.Clear();
            window.Dispose();
            Assert.Equal(["stopped"], events.Where(e => e != Size));
        });

    /// <summary>
    /// A window that is hidden while minimized stays stopped when it is shown still minimized, and shows when it is restored: it
    /// stops while it is hidden or minimized, and is back when it is neither.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window that records its notices.
    ///
    /// Steps:
    /// - Minimize it (<c>SW_SHOWMINNOACTIVE</c>), hide it (<c>SW_HIDE</c>), show it in its state (<c>SW_SHOWNA</c>), and restore it
    ///   (<c>SW_SHOWNOACTIVATE</c>).
    ///
    /// Expected:
    /// - One notice of false at the minimizing, none at the hiding and the showing, and one of true at the restoring.
    /// </remarks>
    [Fact]
    public void HiddenWhileMinimized_StaysStoppedUntilItIsRestored() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("visibility", 200, 100);
            var notices = new List<bool>();
            window.VisibilityChanged = notices.Add;

            _ = ShowWindow(window.Hwnd, SW.SW_SHOWMINNOACTIVE);
            Assert.Equal([false], notices);
            _ = ShowWindow(window.Hwnd, SW.SW_HIDE);
            _ = ShowWindow(window.Hwnd, SW.SW_SHOWNA);
            Assert.Equal([false], notices);
            Assert.False(window.IsShowing);

            _ = ShowWindow(window.Hwnd, SW.SW_SHOWNOACTIVATE);
            Assert.Equal([false, true], notices);
            Assert.True(window.IsShowing);
        });
}
