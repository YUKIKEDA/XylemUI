using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The OS's settings of the Windows host (row PL1, design §6.0, "OS settings"): one object of the app tier, read again on the setting
// messages of the host's windows, which tells each reading that changed values once, marked with them, and after a synchronous render
// when the reading came during one. No test changes a setting of Windows: the readers stand for them.
[Collection(UiThreadGroup.Name)]
public sealed class Win32PlatformSettingsTests(UiThreadFixture ui)
{
    /// <summary>
    /// One <c>WM_SETTINGCHANGE</c> that changes every value is told once, marked with every value; the same message to a second
    /// window of the host, which reads the same values, tells nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Readers set by the test, put back after it: no less motion, the default text fields' settings, the languages "ja-JP", and
    ///   the 12-hour form. Settings made with them; two hidden windows whose setting messages read them again; a handler that
    ///   records the settings' notices.
    ///
    /// Steps:
    /// - Make the readers return less motion, a caret 3 wide, "en-US", and the 24-hour form. Send <c>WM_SETTINGCHANGE</c> to the
    ///   first window, then to the second.
    ///
    /// Expected:
    /// - One notice, marked with less motion, the text fields' settings, the languages, and the 24-hour form; the settings hold the
    ///   new values.
    /// </remarks>
    [Fact]
    public void SettingChange_EveryValueChanged_TellsOnceWithEveryMark() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previousMotion = Win32PlatformSettings.ReducesMotionReader;
            Func<TextEditingSettings, TextEditingSettings> previousEditing = Win32PlatformSettings.TextEditingReader;
            Func<string[]> previousLanguages = Win32PlatformSettings.UiLanguagesReader;
            Func<bool, bool> previousTimeFormat = Win32PlatformSettings.Use24HourFormatReader;
            try
            {
                bool reduces = false;
                TextEditingSettings editing = TextEditingSettings.Default;
                string[] languages = ["ja-JP"];
                bool use24HourForm = false;
                Win32PlatformSettings.ReducesMotionReader = _ => reduces;
                Win32PlatformSettings.TextEditingReader = _ => editing;
                Win32PlatformSettings.UiLanguagesReader = () => languages;
                Win32PlatformSettings.Use24HourFormatReader = _ => use24HourForm;
                var settings = new Win32PlatformSettings();
                using var first = new Win32Window("settings 1", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                using var second = new Win32Window("settings 2", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                var notices = new List<PlatformSettingsChanges>();
                settings.Changed += notices.Add;
                reduces = true;
                editing = TextEditingSettings.Default with { CaretWidth = 3 };
                languages = ["en-US"];
                use24HourForm = true;

                _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);

                PlatformSettingsChanges every =
                    PlatformSettingsChanges.ReducesMotion
                    | PlatformSettingsChanges.TextEditing
                    | PlatformSettingsChanges.UiLanguages
                    | PlatformSettingsChanges.Use24HourFormat;
                Assert.Equal([every], notices);
                Assert.True(settings.ReducesMotion);
                Assert.Equal(3f, settings.TextEditing.CaretWidth);
                Assert.Equal(["en-US"], settings.UiLanguages);
                Assert.True(settings.Use24HourFormat);
            }
            finally
            {
                Win32PlatformSettings.ReducesMotionReader = previousMotion;
                Win32PlatformSettings.TextEditingReader = previousEditing;
                Win32PlatformSettings.UiLanguagesReader = previousLanguages;
                Win32PlatformSettings.Use24HourFormatReader = previousTimeFormat;
            }
        });

    /// <summary>
    /// Readings that change values during a synchronous render are told after the render, once, marked with what each changed
    /// (design §2.11.1): an app's handler does not run inside the render.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Readers set by the test, put back after it: no less motion and the 12-hour form. Settings made with them, and a handler that
    ///   records the settings' notices.
    ///
    /// Steps:
    /// - Enter a synchronous render. Make the motion reader return less motion and read again; make the time reader return the
    ///   24-hour form and read again; count the notices. Leave the render.
    ///
    /// Expected:
    /// - No notice inside the render; after it, one notice marked with less motion and the 24-hour form.
    /// </remarks>
    [Fact]
    public void ReadAgain_DuringASynchronousRender_TellsOnceAfterIt() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previousMotion = Win32PlatformSettings.ReducesMotionReader;
            Func<bool, bool> previousTimeFormat = Win32PlatformSettings.Use24HourFormatReader;
            try
            {
                bool reduces = false;
                bool use24HourForm = false;
                Win32PlatformSettings.ReducesMotionReader = _ => reduces;
                Win32PlatformSettings.Use24HourFormatReader = _ => use24HourForm;
                var settings = new Win32PlatformSettings();
                var notices = new List<PlatformSettingsChanges>();
                settings.Changed += notices.Add;

                SynchronousRenderGuard.Scope render = SynchronousRenderGuard.Enter();
                reduces = true;
                settings.ReadAgain();
                use24HourForm = true;
                settings.ReadAgain();
                int inside = notices.Count;
                render.Dispose();

                Assert.Equal(0, inside);
                Assert.Equal([PlatformSettingsChanges.ReducesMotion | PlatformSettingsChanges.Use24HourFormat], notices);
            }
            finally
            {
                Win32PlatformSettings.ReducesMotionReader = previousMotion;
                Win32PlatformSettings.Use24HourFormatReader = previousTimeFormat;
            }
        });
}
