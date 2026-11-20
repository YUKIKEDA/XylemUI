using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// Windows' 24-hour form (row LC2, design §4.11, "The locale of the formats" and "The key of the 24-hour form"): whether the user's
// time format holds H outside single quotes, read up to the length GetLocaleInfoEx returns, when the window is created and on each
// WM_SETTINGCHANGE.
[Collection(UiThreadGroup.Name)]
public sealed class Win32TimeFormatTests(UiThreadFixture ui)
{
    /// <summary>
    /// A time format is in 24-hour form when it holds <c>H</c> outside single quotes, as Microsoft's "Hour, Minute, and Second Format
    /// Pictures" reads quotes (design §4.11, "The locale of the formats"); only the characters the call wrote are read, so an
    /// <c>H</c> left past the terminating null does not count (PR #854's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Buffers as <c>GetLocaleInfoEx</c> leaves them, each with the count it returns (the terminating null included): Windows'
    ///   12-hour and 24-hour defaults; <c>H:mm</c>; <c>h:mm tt</c>; <c>h'H'mm</c> (an H between quotes); <c>'H'H:mm</c> (one between
    ///   quotes and one after); <c>h'o''H'mm</c> (a doubled quote inside quotes, which keeps the H quoted); <c>h''H:mm</c> (a
    ///   doubled quote outside quotes, which quotes nothing); <c>h:mm 'H</c> (a quote not closed); the empty format; a 12-hour
    ///   format with "HH" past its null; a 24-hour format with "h" past its null.
    ///
    /// Steps:
    /// - Read each buffer.
    ///
    /// Expected:
    /// - The row's value: an H between quotes or past the null never counts.
    /// </remarks>
    /// <param name="buffer">What the call left in the buffer.</param>
    /// <param name="written">What the call returned.</param>
    /// <param name="expected">Whether the format is in 24-hour form.</param>
    [Theory]
    [InlineData("h:mm:ss tt\0", 11, false)]
    [InlineData("HH:mm:ss\0", 9, true)]
    [InlineData("H:mm\0", 5, true)]
    [InlineData("h:mm tt\0", 8, false)]
    [InlineData("h'H'mm\0", 7, false)]
    [InlineData("'H'H:mm\0", 8, true)]
    [InlineData("h'o''H'mm\0", 10, false)]
    [InlineData("h''H:mm\0", 8, true)]
    [InlineData("h:mm 'H\0", 8, false)]
    [InlineData("\0", 1, false)]
    [InlineData("h:mm:ss tt\0HH:mm:ss\0", 11, false)]
    [InlineData("HH:mm\0h:mm tt\0", 6, true)]
    public void Uses24Hour_ReadsOnlyTheCharactersWritten(string buffer, int written, bool expected)
    {
        _ = ui;
        Assert.Equal(expected, Win32TimeFormat.Uses24Hour(buffer, written));
    }

    /// <summary>
    /// Windows gives this machine's time format, so the read does not fall back on the value before.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - This machine's settings.
    ///
    /// Steps:
    /// - Read the 24-hour form with <see langword="false"/> as the value before, then with <see langword="true"/>.
    ///
    /// Expected:
    /// - The two reads are equal.
    /// </remarks>
    [Fact]
    public void ReadUse24HourFormat_WindowsGivesAFormat_DoesNotKeepTheValueBefore()
    {
        _ = ui;
        Assert.Equal(Win32TimeFormat.ReadUse24HourFormat(false), Win32TimeFormat.ReadUse24HourFormat(true));
    }

    /// <summary>
    /// On <c>WM_SETTINGCHANGE</c> each window reads the 24-hour form again and raises its event when the value changed, and only
    /// then.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A reader set by the test that returns the 12-hour form; two hidden windows made with it, and a handler on each that counts
    ///   the event. The reader is put back after the test.
    ///
    /// Steps:
    /// - Send <c>WM_SETTINGCHANGE</c> to both windows; make the reader return the 24-hour form and send it to both; send it to
    ///   both once more.
    ///
    /// Expected:
    /// - Both windows start in the 12-hour form. No event after the first round; one event from each window after the second,
    ///   and both hold the 24-hour form; no new event after the third.
    /// </remarks>
    [Fact]
    public void SettingChange_EachWindowReadsAgainAndRaisesWhenChanged() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previous = Win32Window.Use24HourFormatReader;
            try
            {
                bool use24HourForm = false;
                Win32Window.Use24HourFormatReader = _ => use24HourForm;
                using var first = new Win32Window("time format 1", 200, 100);
                using var second = new Win32Window("time format 2", 200, 100);
                (bool, bool) start = (first.Use24HourFormat, second.Use24HourFormat);
                int firstRaised = 0;
                int secondRaised = 0;
                first.Use24HourFormatChanged += (_, _) => firstRaised++;
                second.Use24HourFormatChanged += (_, _) => secondRaised++;

                Send();
                (int, int) same = (firstRaised, secondRaised);
                use24HourForm = true;
                Send();
                (int, int) changed = (firstRaised, secondRaised);
                Send();

                Assert.Equal((false, false), start);
                Assert.Equal((0, 0), same);
                Assert.Equal((1, 1), changed);
                Assert.Equal((1, 1), (firstRaised, secondRaised));
                Assert.Equal((true, true), (first.Use24HourFormat, second.Use24HourFormat));

                void Send()
                {
                    _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                    _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                }
            }
            finally
            {
                Win32Window.Use24HourFormatReader = previous;
            }
        });
}
