using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// Whether Windows asks for less motion (design §2.14, "Reduced motion", row AN1): the value a new window reads, and reading it again
// when a setting of Windows changes. No test changes a setting of Windows: the reading again uses a reader the test sets.
[Collection(UiThreadGroup.Name)]
public sealed class ReducedMotionTests(UiThreadFixture ui)
{
    /// <summary>
    /// A new window reads whether the client area's animations are off.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window.
    ///
    /// Steps:
    /// - Read the window's <see cref="Win32Window.ReducesMotion"/>, and <c>SPI_GETCLIENTAREAANIMATION</c> straight from
    ///   <c>SystemParametersInfoW</c>.
    ///
    /// Expected:
    /// - The window reduces motion exactly when the setting says the client area does not animate.
    /// </remarks>
    [Fact]
    public unsafe void ReducesMotion_NewWindow_IsTheSetting() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("reduced motion", 200, 100);
            BOOL animates;

            Assert.NotEqual(BOOL.FALSE, SystemParametersInfoW(SPI.SPI_GETCLIENTAREAANIMATION, 0, &animates, 0));
            Assert.Equal(animates == BOOL.FALSE, window.ReducesMotion);
        });

    /// <summary>
    /// Each window that receives <c>WM_SETTINGCHANGE</c> reads the value again and raises
    /// <see cref="Win32Window.ReducesMotionChanged"/> when its own value changed, so every window follows the setting, not only
    /// the first one Windows sends the message to.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two hidden windows, a handler on each that counts the event, and a reader set by the test that returns the opposite of the
    ///   value the windows read when they were created. The reader is put back after the test.
    ///
    /// Steps:
    /// - Send <c>WM_SETTINGCHANGE</c> to the first window, then to the second; then send it to both again.
    ///
    /// Expected:
    /// - After the first two messages: both windows hold the reader's value, and each raised the event once. After the second
    ///   round: no new event.
    /// </remarks>
    [Fact]
    public void SettingChange_EachWindowReadsAgainAndRaisesWhenChanged() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previous = Win32Window.ReducesMotionReader;
            try
            {
                using var first = new Win32Window("reduced motion 1", 200, 100);
                using var second = new Win32Window("reduced motion 2", 200, 100);
                bool next = !first.ReducesMotion;
                int firstRaised = 0;
                int secondRaised = 0;
                first.ReducesMotionChanged += (_, _) => firstRaised++;
                second.ReducesMotionChanged += (_, _) => secondRaised++;
                Win32Window.ReducesMotionReader = _ => next;

                _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                (int, int) once = (firstRaised, secondRaised);
                _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);

                Assert.Equal((1, 1), once);
                Assert.Equal((1, 1), (firstRaised, secondRaised));
                Assert.Equal(next, first.ReducesMotion);
                Assert.Equal(next, second.ReducesMotion);
            }
            finally
            {
                Win32Window.ReducesMotionReader = previous;
            }
        });
}
