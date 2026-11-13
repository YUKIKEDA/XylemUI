using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The OS's languages of a window (row LC1, design §4.11, "The OS's languages" and "A change of the OS"): read when the window is
// created and on each WM_SETTINGCHANGE, whatever setting the message names.
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
    /// On <c>WM_SETTINGCHANGE</c> each window reads the languages again and raises its event when the list changed, and only
    /// then.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A reader set by the test that returns "ja-JP, en-US"; two hidden windows made with it, and a handler on each that counts
    ///   the event. The reader is put back after the test.
    ///
    /// Steps:
    /// - Send <c>WM_SETTINGCHANGE</c> to both windows; make the reader return "en-US, ja-JP" and send it to both; send it to both
    ///   once more.
    ///
    /// Expected:
    /// - No event after the first round (the list is the same); one event from each window after the second, and both hold the
    ///   new list in its order; no new event after the third.
    /// </remarks>
    [Fact]
    public void SettingChange_EachWindowReadsAgainAndRaisesWhenChanged() =>
        ui.Invoke(() =>
        {
            Func<string[]> previous = Win32Window.UiLanguagesReader;
            try
            {
                string[] languages = ["ja-JP", "en-US"];
                Win32Window.UiLanguagesReader = () => languages;
                using var first = new Win32Window("languages 1", 200, 100);
                using var second = new Win32Window("languages 2", 200, 100);
                int firstRaised = 0;
                int secondRaised = 0;
                first.UiLanguagesChanged += (_, _) => firstRaised++;
                second.UiLanguagesChanged += (_, _) => secondRaised++;

                Send();
                (int, int) same = (firstRaised, secondRaised);
                languages = ["en-US", "ja-JP"];
                Send();
                (int, int) changed = (firstRaised, secondRaised);
                Send();

                Assert.Equal((0, 0), same);
                Assert.Equal((1, 1), changed);
                Assert.Equal((1, 1), (firstRaised, secondRaised));
                Assert.Equal(["en-US", "ja-JP"], first.UiLanguages);
                Assert.Equal(["en-US", "ja-JP"], second.UiLanguages);

                void Send()
                {
                    _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                    _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                }
            }
            finally
            {
                Win32Window.UiLanguagesReader = previous;
            }
        });
}
