using System.Diagnostics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The clipboard of Windows and the settings a text field draws with (row P02b, design §4.9, "The clipboard", "The caret", and
// "Drawing the selection"). No test changes a setting of Windows: the reading again uses a reader the test sets. The clipboard test
// puts the text the clipboard held back after it. Another process may hold the clipboard open for a moment (the clipboard history
// reads what a set wrote); the product tries once (design §4.9), so the tests try each step again for up to two seconds (Issue #585).
[Collection(UiThreadGroup.Name)]
public sealed unsafe class TextEditingPlatformTests(UiThreadFixture ui)
{
    /// <summary>
    /// Text put on the clipboard comes back as it was, with a NUL inside it as a space.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window's clipboard; the text the clipboard holds is kept and put back after the test.
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
            using var window = new Win32Window("clipboard", 200, 100);
            var clipboard = new Win32Clipboard(window);
            string? before = Before(window, clipboard);
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
    /// - A hidden window's clipboard; the text the clipboard holds is kept and put back after the test.
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
            using var window = new Win32Window("clipboard", 200, 100);
            var clipboard = new Win32Clipboard(window);
            string? before = Before(window, clipboard);
            try
            {
                Assert.True(Eventually(() => clipboard.SetText("text")));
                bool afterSet = clipboard.HasText();
                Assert.True(Eventually(() => OpenClipboard(window.Hwnd) != BOOL.FALSE));
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
    /// A new window reads the caret's blink and width, the selection's colors, whether access keys are always underlined, and how far
    /// a press moves before a drag starts from Windows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window.
    ///
    /// Steps:
    /// - Read the window's settings, and <c>GetCaretBlinkTime</c>, <c>SPI_GETCARETWIDTH</c>, <c>GetSysColor</c> of
    ///   <c>COLOR_HIGHLIGHT</c> and <c>COLOR_HIGHLIGHTTEXT</c>, <c>SPI_GETKEYBOARDCUES</c>, and <c>SM_CXDRAG</c> and <c>SM_CYDRAG</c>
    ///   straight from Windows.
    ///
    /// Expected:
    /// - The blink is the blink time (none for <c>INFINITE</c>), the width is the caret width, the colors are the system colors,
    ///   read as 0x00BBGGRR, the keyboard cues are the setting, and the drag size is the two metrics.
    /// </remarks>
    [Fact]
    public void TextEditing_NewWindow_IsWindowsSettings() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("settings", 200, 100);
            uint blink = GetCaretBlinkTime();
            uint width;
            Assert.NotEqual(BOOL.FALSE, SystemParametersInfoW(SPI.SPI_GETCARETWIDTH, 0, &width, 0));
            uint highlight = GetSysColor(COLOR.COLOR_HIGHLIGHT);
            uint highlightText = GetSysColor(COLOR.COLOR_HIGHLIGHTTEXT);
            BOOL cues;
            Assert.NotEqual(BOOL.FALSE, SystemParametersInfoW(SPI.SPI_GETKEYBOARDCUES, 0, &cues, 0));
            int dragWidth = GetSystemMetrics(SM.SM_CXDRAG);
            int dragHeight = GetSystemMetrics(SM.SM_CYDRAG);

            TextEditingSettings read = window.TextEditing;

            Assert.Equal(blink == uint.MaxValue ? null : TimeSpan.FromMilliseconds(blink), read.CaretBlink);
            Assert.Equal(width, read.CaretWidth);
            Assert.Equal(new Color((byte)highlight, (byte)(highlight >> 8), (byte)(highlight >> 16)), read.SelectionBackground);
            Assert.Equal(new Color((byte)highlightText, (byte)(highlightText >> 8), (byte)(highlightText >> 16)), read.SelectionForeground);
            Assert.Equal(cues != BOOL.FALSE, read.KeyboardCues);
            Assert.Equal((dragWidth, dragHeight), (read.DragWidth, read.DragHeight));
        });

    /// <summary>
    /// Each window that receives <c>WM_SETTINGCHANGE</c> or <c>WM_SYSCOLORCHANGE</c> reads the settings again and raises the change
    /// when its own value changed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two hidden windows, a handler on each that counts the change, and a reader set by the test that returns a blink of none and
    ///   a width of 3. The reader is put back after the test.
    ///
    /// Steps:
    /// - Send <c>WM_SETTINGCHANGE</c> to the first window and <c>WM_SYSCOLORCHANGE</c> to the second; then send both messages to
    ///   both windows again.
    ///
    /// Expected:
    /// - After the first two messages, both windows hold the reader's value and each raised the change once; after the others, no
    ///   new change.
    /// </remarks>
    [Fact]
    public void SettingChange_EachWindowReadsAgainAndRaisesWhenChanged() =>
        ui.Invoke(() =>
        {
            Func<TextEditingSettings, TextEditingSettings> previous = Win32Window.TextEditingReader;
            try
            {
                using var first = new Win32Window("settings 1", 200, 100);
                using var second = new Win32Window("settings 2", 200, 100);
                TextEditingSettings next = first.TextEditing with { CaretBlink = null, CaretWidth = 3 };
                int firstRaised = 0;
                int secondRaised = 0;
                first.TextEditingChanged += (_, _) => firstRaised++;
                second.TextEditingChanged += (_, _) => secondRaised++;
                Win32Window.TextEditingReader = _ => next;

                _ = SendMessageW(first.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                _ = SendMessageW(second.Hwnd, WM.WM_SYSCOLORCHANGE, default, default);
                (int, int) once = (firstRaised, secondRaised);
                foreach (Win32Window window in new[] { first, second })
                {
                    _ = SendMessageW(window.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                    _ = SendMessageW(window.Hwnd, WM.WM_SYSCOLORCHANGE, default, default);
                }

                Assert.Equal((1, 1), once);
                Assert.Equal((1, 1), (firstRaised, secondRaised));
                Assert.Equal(next, first.TextEditing);
                Assert.Equal(next, second.TextEditing);
            }
            finally
            {
                Win32Window.TextEditingReader = previous;
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

    // The text on the clipboard, read again while it cannot be opened (the read then gives none).
    private static string? Read(Win32Clipboard clipboard)
    {
        string? text = null;
        _ = Eventually(() => (text = clipboard.GetText()) is not null);
        return text;
    }

    // The text the clipboard holds before a test, read once the clipboard can be opened: none when it holds no text.
    private static string? Before(Win32Window window, Win32Clipboard clipboard)
    {
        _ = Eventually(() =>
        {
            if (OpenClipboard(window.Hwnd) == BOOL.FALSE)
            {
                return false;
            }

            _ = CloseClipboard();
            return true;
        });
        return clipboard.HasText() ? Read(clipboard) : null;
    }
}
