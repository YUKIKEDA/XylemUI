using System.Diagnostics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The clipboard of Windows and the settings a text field draws with (row P02b, design §4.9, "The clipboard", "The caret", and
// "Drawing the selection"), which the app tier's settings read (design §6.0, "OS settings"). No test changes a setting of Windows: the
// reading again uses a reader the test sets. The clipboard test
// puts the text the clipboard held back after it. Another process may hold the clipboard open for a moment (the clipboard history
// reads what a set wrote); the product tries once (design §4.9), so the tests try each step again for up to two seconds (Issue #585).
[Collection(UiThreadGroup.Name)]
public sealed unsafe class TextEditingPlatformTests(UiThreadFixture ui)
{
    /// <summary>
    /// Text put on the clipboard comes back as it was, with a NUL inside it as a space, with no window of the app open: the
    /// clipboard's owner is a message-only window of its own (design §6.0, "The clipboard").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The clipboard of the app tier, with no window made by the test; the text the clipboard holds is kept and put back after
    ///   the test.
    ///
    /// Steps:
    /// - Set "日本語 and text"; get it. Set "a\0b"; get it.
    ///
    /// Expected:
    /// - Both sets succeed; the gets give "日本語 and text" and "a b".
    /// </remarks>
    [Fact]
    public void Clipboard_SetThenGet_GivesTheText() =>
        ui.Invoke(() =>
        {
            var clipboard = new Win32Clipboard();
            string? before = Before(clipboard);
            try
            {
                Assert.True(Eventually(() => clipboard.SetText("日本語 and text")));
                Assert.Equal("日本語 and text", Read(clipboard));
                Assert.True(Eventually(() => clipboard.SetText("a\0b")));
                Assert.Equal("a b", Read(clipboard));
            }
            finally
            {
                if (before is not null)
                {
                    _ = Eventually(() => clipboard.SetText(before));
                }
            }
        });

    /// <summary>
    /// The clipboard holds text after text is put on it, and none after it is emptied (row P02d, design §4.9, "The clipboard").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The clipboard of the app tier; the text the clipboard holds is kept and put back after the test.
    ///
    /// Steps:
    /// - Set "text"; ask whether it holds text. Empty the clipboard with <c>EmptyClipboard</c>; ask again.
    ///
    /// Expected:
    /// - The set succeeds; true, then false.
    /// </remarks>
    [Fact]
    public void Clipboard_HasText_AfterASetAndNotAfterEmptying() =>
        ui.Invoke(() =>
        {
            var clipboard = new Win32Clipboard();
            string? before = Before(clipboard);
            try
            {
                Assert.True(Eventually(() => clipboard.SetText("text")));
                bool afterSet = clipboard.HasText();
                Assert.True(Eventually(() => OpenClipboard(Win32Clipboard.Owner) != BOOL.FALSE));
                try
                {
                    Assert.True(EmptyClipboard() != BOOL.FALSE);
                }
                finally
                {
                    _ = CloseClipboard();
                }

                Assert.True(afterSet);
                Assert.False(clipboard.HasText());
            }
            finally
            {
                if (before is not null)
                {
                    _ = Eventually(() => clipboard.SetText(before));
                }
            }
        });

    /// <summary>
    /// What the clipboard puts on Windows' clipboard is owned by a message-only window of its own (design §6.0, "The clipboard";
    /// Issue #928): not the loop's wake window, and no window that shows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The clipboard of the app tier; the text the clipboard holds is kept and put back after the test.
    ///
    /// Steps:
    /// - Set "owner"; read the clipboard's owner (<c>GetClipboardOwner</c>); look for this process's message-only window of the
    ///   class "XylemUI.Clipboard" (<c>FindWindowExW</c> under <c>HWND_MESSAGE</c>, whose matches are walked until one belongs to
    ///   this process by <c>GetWindowThreadProcessId</c>).
    ///
    /// Expected:
    /// - The set succeeds; the owner and the window found are both the clipboard's owner window.
    /// </remarks>
    [Fact]
    public void Clipboard_Set_IsOwnedByItsMessageOnlyWindow() =>
        ui.Invoke(() =>
        {
            var clipboard = new Win32Clipboard();
            string? before = Before(clipboard);
            try
            {
                Assert.True(Eventually(() => clipboard.SetText("owner")));
                HWND owner = GetClipboardOwner();
                HWND found = MessageWindowOfThisProcess("XylemUI.Clipboard");

                Assert.Equal(Win32Clipboard.Owner, owner);
                Assert.Equal(Win32Clipboard.Owner, found);
            }
            finally
            {
                if (before is not null)
                {
                    _ = Eventually(() => clipboard.SetText(before));
                }
            }
        });

    /// <summary>
    /// New settings of the app tier read the caret's blink and width, the selection's colors, whether access keys are always
    /// underlined, and how far a press moves before a drag starts from Windows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Make the settings. Read their text fields' settings, and <c>GetCaretBlinkTime</c>, <c>SPI_GETCARETWIDTH</c>,
    ///   <c>GetSysColor</c> of <c>COLOR_HIGHLIGHT</c> and <c>COLOR_HIGHLIGHTTEXT</c>, <c>SPI_GETKEYBOARDCUES</c>, and
    ///   <c>SM_CXDRAG</c> and <c>SM_CYDRAG</c> straight from Windows.
    ///
    /// Expected:
    /// - The blink is the blink time (none for <c>INFINITE</c>), the width is the caret width, the colors are the system colors,
    ///   read as 0x00BBGGRR, the keyboard cues are the setting, and the drag size is the two metrics.
    /// </remarks>
    [Fact]
    public void TextEditing_NewSettings_IsWindowsSettings() =>
        ui.Invoke(() =>
        {
            var settings = new Win32PlatformSettings();
            uint blink = GetCaretBlinkTime();
            uint width;
            Assert.NotEqual(BOOL.FALSE, SystemParametersInfoW(SPI.SPI_GETCARETWIDTH, 0, &width, 0));
            uint highlight = GetSysColor(COLOR.COLOR_HIGHLIGHT);
            uint highlightText = GetSysColor(COLOR.COLOR_HIGHLIGHTTEXT);
            BOOL cues;
            Assert.NotEqual(BOOL.FALSE, SystemParametersInfoW(SPI.SPI_GETKEYBOARDCUES, 0, &cues, 0));
            int dragWidth = GetSystemMetrics(SM.SM_CXDRAG);
            int dragHeight = GetSystemMetrics(SM.SM_CYDRAG);

            TextEditingSettings read = settings.TextEditing;

            Assert.Equal(blink == uint.MaxValue ? null : TimeSpan.FromMilliseconds(blink), read.CaretBlink);
            Assert.Equal(width, read.CaretWidth);
            Assert.Equal(new Color((byte)highlight, (byte)(highlight >> 8), (byte)(highlight >> 16)), read.SelectionBackground);
            Assert.Equal(new Color((byte)highlightText, (byte)(highlightText >> 8), (byte)(highlightText >> 16)), read.SelectionForeground);
            Assert.Equal(cues != BOOL.FALSE, read.KeyboardCues);
            Assert.Equal((dragWidth, dragHeight), (read.DragWidth, read.DragHeight));
        });

    /// <summary>
    /// Both <c>WM_SETTINGCHANGE</c> and <c>WM_SYSCOLORCHANGE</c> to a window of the host make the app tier's settings read the text
    /// fields' settings again; a change is told once, marked as the text fields' settings, whichever window received the message.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A reader set by the test, put back after it, that returns the value it is given; settings made with it; two hidden windows
    ///   whose setting messages read the settings again, and a handler that records the settings' notices.
    ///
    /// Steps:
    /// - Make the reader return the settings' value with a blink of none and a width of 3; send <c>WM_SYSCOLORCHANGE</c> to the
    ///   second window. Make it return a width of 4; send <c>WM_SETTINGCHANGE</c> to the first window. Send both messages to both
    ///   windows again.
    ///
    /// Expected:
    /// - Two notices, each marked as the text fields' settings only, and the settings hold the width of 4.
    /// </remarks>
    [Fact]
    public void SettingOrColorChange_ReadsAgain_AndTellsEachChangeOnce() =>
        ui.Invoke(() =>
        {
            Func<TextEditingSettings, TextEditingSettings> previous = Win32PlatformSettings.TextEditingReader;
            try
            {
                Win32PlatformSettings.TextEditingReader = static before => before;
                var settings = new Win32PlatformSettings();
                using var first = new Win32Window("settings 1", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                using var second = new Win32Window("settings 2", 200, 100) { SettingsMessageReceived = settings.ReadAgain };
                var notices = new List<PlatformSettingsChanges>();
                settings.Changed += notices.Add;
                TextEditingSettings next = settings.TextEditing with { CaretBlink = null, CaretWidth = 3 };
                Win32PlatformSettings.TextEditingReader = _ => next;

                _ = SendMessageW(second.Hwnd, WM.WM_SYSCOLORCHANGE, default, default);
                next = next with { CaretWidth = 4 };
                _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                foreach (Win32Window window in new[] { first, second })
                {
                    _ = SendMessageW(window.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                    _ = SendMessageW(window.Hwnd, WM.WM_SYSCOLORCHANGE, default, default);
                }

                Assert.Equal([PlatformSettingsChanges.TextEditing, PlatformSettingsChanges.TextEditing], notices);
                Assert.Equal(4f, settings.TextEditing.CaretWidth);
            }
            finally
            {
                Win32PlatformSettings.TextEditingReader = previous;
            }
        });

    // Tries a step of the clipboard again, every 10 ms for up to two seconds, while another process holds the clipboard open.
    private static bool Eventually(Func<bool> attempt)
    {
        long until = Stopwatch.GetTimestamp() + (2 * Stopwatch.Frequency);
        while (!attempt())
        {
            if (Stopwatch.GetTimestamp() > until)
            {
                return false;
            }

            Thread.Sleep(10);
        }

        return true;
    }

    // This process's message-only window of a class: FindWindowExW under HWND_MESSAGE looks through every process of the session,
    // where another test process or a running sample has a window of the same class (PR #937's review).
    private static HWND MessageWindowOfThisProcess(string className)
    {
        uint process = (uint)Environment.ProcessId;
        fixed (char* name = className)
        {
            HWND window = FindWindowExW(HWND.HWND_MESSAGE, HWND.NULL, name, null);
            while (window != HWND.NULL)
            {
                uint owner;
                _ = GetWindowThreadProcessId(window, &owner);
                if (owner == process)
                {
                    return window;
                }

                window = FindWindowExW(HWND.HWND_MESSAGE, window, name, null);
            }
        }

        return HWND.NULL;
    }

    // The text on the clipboard, read again while it cannot be opened (the read then gives none).
    private static string? Read(Win32Clipboard clipboard)
    {
        string? text = null;
        _ = Eventually(() => (text = clipboard.GetText()) is not null);
        return text;
    }

    // The text the clipboard holds before a test, read once the clipboard can be opened: none when it holds no text.
    private static string? Before(Win32Clipboard clipboard)
    {
        _ = Eventually(() =>
        {
            if (OpenClipboard(Win32Clipboard.Owner) == BOOL.FALSE)
            {
                return false;
            }

            _ = CloseClipboard();
            return true;
        });
        return clipboard.HasText() ? Read(clipboard) : null;
    }
}
