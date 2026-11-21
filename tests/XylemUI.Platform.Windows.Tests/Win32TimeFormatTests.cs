using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// Windows' 24-hour form (row LC2, design §4.11, "The locale of the formats" and "The key of the 24-hour form"): whether the user's
// short time format holds H outside single quotes, read up to the length GetLocaleInfoEx returns, when the app tier's settings are
// made and on each WM_SETTINGCHANGE a window of the host receives.
[Collection(UiThreadGroup.Name)]
public sealed partial class Win32TimeFormatTests(UiThreadFixture ui)
{
    // LOCALE_SSHORTTIME (winnls.h), written here apart from the host's constant.
    private const uint LocaleShortTime = 0x79;

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
    ///   format with "HH" past its null; a 24-hour format with "h" past its null. Each row gives the opposite of its expected value
    ///   as the value before. Two more rows stand for a call that failed (0 written), with the value before on and off.
    ///
    /// Steps:
    /// - Read each buffer.
    ///
    /// Expected:
    /// - The row's value: an H between quotes or past the null never counts, and the value before counts only when the call
    ///   failed, which keeps it.
    /// </remarks>
    /// <param name="buffer">What the call left in the buffer.</param>
    /// <param name="written">What the call returned.</param>
    /// <param name="before">The value before the read.</param>
    /// <param name="expected">Whether the format is in 24-hour form.</param>
    [Theory]
    [InlineData("h:mm:ss tt\0", 11, true, false)]
    [InlineData("HH:mm:ss\0", 9, false, true)]
    [InlineData("H:mm\0", 5, false, true)]
    [InlineData("h:mm tt\0", 8, true, false)]
    [InlineData("h'H'mm\0", 7, true, false)]
    [InlineData("'H'H:mm\0", 8, false, true)]
    [InlineData("h'o''H'mm\0", 10, true, false)]
    [InlineData("h''H:mm\0", 8, false, true)]
    [InlineData("h:mm 'H\0", 8, true, false)]
    [InlineData("\0", 1, true, false)]
    [InlineData("h:mm:ss tt\0HH:mm:ss\0", 11, true, false)]
    [InlineData("HH:mm\0h:mm tt\0", 6, false, true)]
    [InlineData("", 0, true, true)]
    [InlineData("", 0, false, false)]
    public void Uses24Hour_ReadsOnlyTheCharactersWritten(string buffer, int written, bool before, bool expected)
    {
        _ = ui;
        Assert.Equal(expected, Win32TimeFormat.Uses24Hour(buffer, written, before));
    }

    /// <summary>
    /// The format read is the user's short time format (<c>LOCALE_SSHORTTIME</c>, 0x79; design §4.11, "The locale of the formats",
    /// Issue #906), not another value of <c>GetLocaleInfoEx</c> such as the long time format (<c>LOCALE_STIMEFORMAT</c>, 0x1003,
    /// which Flutter reads) or the short date format (<c>LOCALE_SSHORTDATE</c>, 0x1F).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - This machine's settings. The test calls <c>GetLocaleInfoEx</c> itself, through a declaration of its own, with the
    ///   constant written here.
    ///
    /// Steps:
    /// - Read the format through <see cref="Win32TimeFormat.ReadShortTimeFormat"/>; read <c>LOCALE_SSHORTTIME</c> directly.
    ///
    /// Expected:
    /// - The two strings are equal and not empty. Another constant in the host shows here only when this machine's format of that
    ///   value differs from its short time format; Windows' defaults do (the long time format has seconds and the short one not).
    /// </remarks>
    [Fact]
    public void ReadShortTimeFormat_ReadsTheShortTimeFormat()
    {
        _ = ui;
        Span<char> buffer = stackalloc char[100];
        int written = Win32TimeFormat.ReadShortTimeFormat(buffer);

        Assert.True(written > 1);
        Assert.Equal(ReadDirectly(LocaleShortTime), new string(buffer[..(written - 1)]));
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
    /// On <c>WM_SETTINGCHANGE</c> to any window of the host, the app tier's settings read the 24-hour form again and tell a change
    /// once, when the value changed, and only then.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A reader set by the test that returns the 12-hour form; settings made with it; two hidden windows whose setting messages
    ///   read the settings again, and a handler that records the settings' notices. The reader is put back after the test.
    ///
    /// Steps:
    /// - Send <c>WM_SETTINGCHANGE</c> to both windows; make the reader return the 24-hour form and send it to both; send it to
    ///   both once more.
    ///
    /// Expected:
    /// - The settings start in the 12-hour form. No notice after the first round; one notice after the second, marked as the
    ///   24-hour form only, and the settings hold the 24-hour form; no new notice after the third.
    /// </remarks>
    [Fact]
    public void SettingChange_TwoWindows_TellsAChangeOnce() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previous = Win32PlatformSettings.Use24HourFormatReader;
            try
            {
                bool use24HourForm = false;
                Win32PlatformSettings.Use24HourFormatReader = _ => use24HourForm;
                var settings = new Win32PlatformSettings();
                using var first = new Win32Window("time format 1", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                using var second = new Win32Window("time format 2", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                bool start = settings.Use24HourFormat;
                var notices = new List<PlatformSettingsChanges>();
                settings.Changed += notices.Add;

                Send();
                int same = notices.Count;
                use24HourForm = true;
                Send();
                Send();

                Assert.False(start);
                Assert.Equal(0, same);
                Assert.Equal([PlatformSettingsChanges.Use24HourFormat], notices);
                Assert.True(settings.Use24HourFormat);

                void Send()
                {
                    _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                    _ = SendMessageW(second.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                }
            }
            finally
            {
                Win32PlatformSettings.Use24HourFormatReader = previous;
            }
        });

    /// <summary>
    /// A read that fails on <c>WM_SETTINGCHANGE</c> keeps the settings' value before: the settings give the reader their own value,
    /// which a failed read returns.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A reader set by the test that returns the 24-hour form; settings made with it; a hidden window whose setting messages read
    ///   them again, and a handler that counts the settings' notices. The reader is put back after the test.
    ///
    /// Steps:
    /// - Make the reader return the value it is given, as <see cref="Win32TimeFormat.ReadUse24HourFormat"/> does when
    ///   <c>GetLocaleInfoEx</c> fails; send <c>WM_SETTINGCHANGE</c>.
    ///
    /// Expected:
    /// - The settings are still in the 24-hour form, and no notice was raised.
    /// </remarks>
    [Fact]
    public void SettingChange_AReadThatFails_KeepsTheValueBefore() =>
        ui.Invoke(() =>
        {
            Func<bool, bool> previous = Win32PlatformSettings.Use24HourFormatReader;
            try
            {
                Win32PlatformSettings.Use24HourFormatReader = static _ => true;
                var settings = new Win32PlatformSettings();
                using var window = new Win32Window("time format kept", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                int raised = 0;
                settings.Changed += _ => raised++;

                Win32PlatformSettings.Use24HourFormatReader = static before => before;
                _ = SendMessageW(window.Hwnd, WM.WM_SETTINGCHANGE, default, default);

                Assert.True(settings.Use24HourFormat);
                Assert.Equal(0, raised);
            }
            finally
            {
                Win32PlatformSettings.Use24HourFormatReader = previous;
            }
        });

    // The format of one LCTYPE as GetLocaleInfoEx gives it for the user, without its terminating null; empty when the call fails.
    private static unsafe string ReadDirectly(uint type)
    {
        Span<char> buffer = stackalloc char[100];
        fixed (char* data = buffer)
        {
            int written = GetLocaleInfoEx(null, type, data, buffer.Length);
            return written <= 0 ? string.Empty : new string(buffer[..(written - 1)]);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetLocaleInfoEx(char* localeName, uint type, char* data, int length);
}
