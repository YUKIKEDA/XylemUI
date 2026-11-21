using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The OS's settings of the Windows host (row PL1, design §6.0, "OS settings"): one object of the app tier, read again on the setting
// messages of the host's windows, which tells each reading that changed values once, marked with them, and after a synchronous render
// when the reading came during one; a reading that throws keeps every value, and one off the UI thread throws. No test changes a
// setting of Windows: the readers stand for them.
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

    /// <summary>
    /// A reading in which one read throws changes no value and tells nothing: every value is read before any is kept.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Readers set by the test, put back after it: no less motion, the default text fields' settings, the languages "ja-JP", and
    ///   the 12-hour form. Settings made with them, and a handler that records the settings' notices.
    ///
    /// Steps:
    /// - Make the first three readers return less motion, a caret 3 wide, and "en-US", and the time reader, read last, throw a
    ///   <see cref="FormatException"/>. Read again.
    ///
    /// Expected:
    /// - The reading throws that exception. The settings still hold no less motion, a caret 1 wide, "ja-JP", and the 12-hour form;
    ///   no notice.
    /// </remarks>
    [Fact]
    public void ReadAgain_AReadThrows_ChangesNothingAndTellsNothing() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previousMotion = Win32PlatformSettings.ReducesMotionReader;
            Func<TextEditingSettings, TextEditingSettings> previousEditing = Win32PlatformSettings.TextEditingReader;
            Func<string[]> previousLanguages = Win32PlatformSettings.UiLanguagesReader;
            Func<bool, bool> previousTimeFormat = Win32PlatformSettings.Use24HourFormatReader;
            try
            {
                var error = new FormatException("The time format throws.");
                Win32PlatformSettings.ReducesMotionReader = static _ => false;
                Win32PlatformSettings.TextEditingReader = static _ => TextEditingSettings.Default;
                Win32PlatformSettings.UiLanguagesReader = static () => ["ja-JP"];
                Win32PlatformSettings.Use24HourFormatReader = static _ => false;
                var settings = new Win32PlatformSettings();
                var notices = new List<PlatformSettingsChanges>();
                settings.Changed += notices.Add;
                Win32PlatformSettings.ReducesMotionReader = static _ => true;
                Win32PlatformSettings.TextEditingReader = static _ => TextEditingSettings.Default with { CaretWidth = 3 };
                Win32PlatformSettings.UiLanguagesReader = static () => ["en-US"];
                Win32PlatformSettings.Use24HourFormatReader = _ => throw error;

                FormatException thrown = Assert.Throws<FormatException>(settings.ReadAgain);

                Assert.Same(error, thrown);
                Assert.False(settings.ReducesMotion);
                Assert.Equal(1f, settings.TextEditing.CaretWidth);
                Assert.Equal(["ja-JP"], settings.UiLanguages);
                Assert.False(settings.Use24HourFormat);
                Assert.Empty(notices);
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
    /// A reading off the UI thread throws before it reads, also when no value changed (design §2.12).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings made on the UI thread with Windows' own readers, and a thread of the test.
    ///
    /// Steps:
    /// - On the test's thread, read again; join the thread.
    ///
    /// Expected:
    /// - The reading threw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void ReadAgain_OffTheUiThread_Throws()
    {
        Win32PlatformSettings? settings = null;
        ui.Invoke(() => settings = new Win32PlatformSettings());
        Exception? thrown = null;
        var worker = new Thread(() => thrown = Record.Exception(settings!.ReadAgain));

        worker.Start();
        worker.Join();

        _ = Assert.IsType<InvalidOperationException>(thrown);
    }
}
