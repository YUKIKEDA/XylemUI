namespace XylemUI.Testing.Tests;

// The OS's settings of a headless app (row PL1, design §6.0, "Headless"): made from the options, and changed by the app as an OS's
// change is read. A change that makes a value differ is told once, marked with that value; one that does not is not told.
public sealed class HeadlessSettingsTests
{
    /// <summary>
    /// Setting the languages to a list equal to the one before, in its tags and their order, tells nothing; a list that differs is
    /// told once, marked as the languages.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings made from options whose languages are "ja-JP, en-US", and a handler that records their notices.
    ///
    /// Steps:
    /// - Set the languages to a new list "ja-JP, en-US"; read the notices. Set them to "en-US, ja-JP"; then to a new list
    ///   "en-US, ja-JP" again.
    ///
    /// Expected:
    /// - No notice after the equal list. After the rest, one notice, marked as the languages only; the settings hold "en-US, ja-JP".
    /// </remarks>
    [Fact]
    public void SetUiLanguages_AnEqualListThenAnother_TellsOnlyTheChange()
    {
        var settings = new HeadlessSettings(new HeadlessOptions { SystemLocales = ["ja-JP", "en-US"] });
        var notices = new List<PlatformSettingsChanges>();
        settings.Changed += notices.Add;

        settings.SetUiLanguages(["ja-JP", "en-US"]);
        int afterEqual = notices.Count;
        settings.SetUiLanguages(["en-US", "ja-JP"]);
        settings.SetUiLanguages(["en-US", "ja-JP"]);

        Assert.Equal(0, afterEqual);
        Assert.Equal([PlatformSettingsChanges.UiLanguages], notices);
        Assert.Equal(["en-US", "ja-JP"], settings.UiLanguages);
    }

    /// <summary>
    /// Setting the 24-hour form to the value it has tells nothing; another value is told once, marked as the 24-hour form.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Settings made from options with the 12-hour form, and a handler that records their notices.
    ///
    /// Steps:
    /// - Set the 12-hour form; read the notices. Set the 24-hour form twice.
    ///
    /// Expected:
    /// - No notice after the same value. After the rest, one notice, marked as the 24-hour form only; the settings hold the 24-hour
    ///   form.
    /// </remarks>
    [Fact]
    public void SetUse24HourFormat_TheSameValueThenAnother_TellsOnlyTheChange()
    {
        var settings = new HeadlessSettings(new HeadlessOptions());
        var notices = new List<PlatformSettingsChanges>();
        settings.Changed += notices.Add;

        settings.SetUse24HourFormat(false);
        int afterSame = notices.Count;
        settings.SetUse24HourFormat(true);
        settings.SetUse24HourFormat(true);

        Assert.Equal(0, afterSame);
        Assert.Equal([PlatformSettingsChanges.Use24HourFormat], notices);
        Assert.True(settings.Use24HourFormat);
    }
}
