using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The OS's languages (row LC1, design §4.11, "The OS's languages" and "A change of the OS"): read by the app tier's settings when they
// are made and on each WM_SETTINGCHANGE a window of the host receives, whatever setting the message names (design §6.0, "OS settings").
[Collection(UiThreadGroup.Name)]
public sealed class UiLanguagesTests(UiThreadFixture ui)
{
    /// <summary>
    /// Windows gives the list of the user's preferred UI languages, and every entry is a tag that can be read.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - This machine's settings.
    ///
    /// Steps:
    /// - Read the list from Windows.
    ///
    /// Expected:
    /// - At least one tag, none empty, and each one read by <see cref="Locale.TryParse"/>.
    /// </remarks>
    [Fact]
    public void ReadAll_GivesTagsThatCanBeRead()
    {
        _ = ui;
        string[] tags = Win32UiLanguage.ReadAll();

        Assert.NotEmpty(tags);
        Assert.All(tags, static tag => Assert.True(Locale.TryParse(tag, out _), tag));
    }

    /// <summary>
    /// On <c>WM_SETTINGCHANGE</c> to any window of the host, the app tier's settings read the languages again and tell a change
    /// once, when the list changed in its tags or their order, and only then.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A reader set by the test that returns "ja-JP, en-US", in a new array at each read; settings made with it; two hidden
    ///   windows whose setting messages read the settings again, and a handler that records the settings' notices. The reader is
    ///   put back after the test.
    ///
    /// Steps:
    /// - Send <c>WM_SETTINGCHANGE</c> to both windows; make the reader return "en-US, ja-JP" and send it to both; send it to both
    ///   once more.
    ///
    /// Expected:
    /// - No notice after the first round (the list is the same); one notice after the second, marked as the languages only, and the
    ///   settings hold the new list in its order; no new notice after the third.
    /// </remarks>
    [Fact]
    public void SettingChange_TwoWindows_TellsAChangeOnce() =>
        ui.Invoke(() =>
        {
            Func<string[]> previous = Win32PlatformSettings.UiLanguagesReader;
            try
            {
                string[] languages = ["ja-JP", "en-US"];

                // A new array at each read, as Windows' reader gives: a list equal in its tags is the same list.
                Win32PlatformSettings.UiLanguagesReader = () => [.. languages];
                var settings = new Win32PlatformSettings();
                using var first = new Win32Window("languages 1", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                using var second = new Win32Window("languages 2", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                var notices = new List<PlatformSettingsChanges>();
                settings.Changed += notices.Add;

                Send();
                int same = notices.Count;
                languages = ["en-US", "ja-JP"];
                Send();
                Send();

                Assert.Equal(0, same);
                Assert.Equal([PlatformSettingsChanges.UiLanguages], notices);
                Assert.Equal(["en-US", "ja-JP"], settings.UiLanguages);

                void Send()
                {
                    _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                    _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                }
            }
            finally
            {
                Win32PlatformSettings.UiLanguagesReader = previous;
            }
        });
}
