using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// Whether Windows asks for less motion (design §2.14, "Reduced motion", row AN1): the value the app tier's settings read, and reading
// it again when a window of the host receives WM_SETTINGCHANGE (design §6.0, "OS settings"). No test changes a setting of Windows: the
// reading again uses a reader the test sets.
[Collection(UiThreadGroup.Name)]
public sealed class ReducedMotionTests(UiThreadFixture ui)
{
    /// <summary>
    /// New settings read whether the client area's animations are off.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Make the settings; read their <see cref="Win32PlatformSettings.ReducesMotion"/>, and <c>SPI_GETCLIENTAREAANIMATION</c>
    ///   straight from <c>SystemParametersInfoW</c>.
    ///
    /// Expected:
    /// - The settings reduce motion exactly when the setting says the client area does not animate.
    /// </remarks>
    [Fact]
    public unsafe void ReducesMotion_NewSettings_IsTheSetting() =>
        ui.Invoke(() =>
        {
            var settings = new Win32PlatformSettings();
            BOOL animates;

            Assert.NotEqual(BOOL.FALSE, SystemParametersInfoW(SPI.SPI_GETCLIENTAREAANIMATION, 0, &animates, 0));
            Assert.Equal(animates == BOOL.FALSE, settings.ReducesMotion);
        });

    /// <summary>
    /// The settings are one object for the windows: Windows sends <c>WM_SETTINGCHANGE</c> to every top-level window, the first
    /// window to receive it reads the change and the settings tell it once, marked as less motion, and the others read no change.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings, two hidden windows whose setting messages read them again, a handler that records the settings' notices, and a
    ///   reader set by the test that returns the opposite of the value the settings read when they were made. The reader is put
    ///   back after the test.
    ///
    /// Steps:
    /// - Send <c>WM_SETTINGCHANGE</c> to the first window, then to the second; then send it to both again.
    ///
    /// Expected:
    /// - One notice, marked as less motion only, and the settings hold the reader's value.
    /// </remarks>
    [Fact]
    public void SettingChange_TwoWindows_TellsTheChangeOnce() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previous = Win32PlatformSettings.ReducesMotionReader;
            try
            {
                var settings = new Win32PlatformSettings();
                using var first = new Win32Window("reduced motion 1", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                using var second = new Win32Window("reduced motion 2", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                bool next = !settings.ReducesMotion;
                var notices = new List<PlatformSettingsChanges>();
                settings.Changed += notices.Add;
                Win32PlatformSettings.ReducesMotionReader = _ => next;

                _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);

                Assert.Equal([PlatformSettingsChanges.ReducesMotion], notices);
                Assert.Equal(next, settings.ReducesMotion);
            }
            finally
            {
                Win32PlatformSettings.ReducesMotionReader = previous;
            }
        });
}
