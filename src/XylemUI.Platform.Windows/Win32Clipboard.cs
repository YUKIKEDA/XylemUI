using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The clipboard of Windows as plain text, <c>CF_UNICODETEXT</c> (design §4.9, "The clipboard"). It opens the clipboard once for
/// each call and does not try again: a clipboard another program holds open fails the call (GPUI's <c>clipboard.rs</c> lines 346 to
/// 355), where waiting to try again would stop the UI thread.
/// </summary>
/// <param name="window">The window that owns what it puts on the clipboard: a clipboard opened with no window has no owner after
/// <c>EmptyClipboard</c>, and <c>SetClipboardData</c> then fails (<c>OpenClipboard</c>'s documentation).</param>
internal sealed unsafe class Win32Clipboard(Win32Window window) : IClipboardBackend
{
    private const uint UnicodeText = CF.CF_UNICODETEXT;

    /// <inheritdoc/>
    public string? GetText()
    {
        if (OpenClipboard(window.Hwnd) == BOOL.FALSE)
        {
            return null;
        }

        try
        {
            HANDLE data = GetClipboardData(UnicodeText);
            if (data == HANDLE.NULL)
            {
                return null;
            }

            var memory = (HGLOBAL)data.Value;
            var text = (char*)GlobalLock(memory);
            if (text == null)
            {
                return null;
            }

            try
            {
                // The text ends at its NUL, inside the memory's size.
                var units = new ReadOnlySpan<char>(text, (int)(GlobalSize(memory) / sizeof(char)));
                int end = units.IndexOf('\0');
                return new string(end < 0 ? units : units[..end]);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <inheritdoc/>
    public bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (OpenClipboard(window.Hwnd) == BOOL.FALSE)
        {
            return false;
        }

        try
        {
            if (EmptyClipboard() == BOOL.FALSE)
            {
                return false;
            }

            HGLOBAL memory = GlobalAlloc(GMEM.GMEM_MOVEABLE, (nuint)((text.Length + 1) * sizeof(char)));
            if (memory == HGLOBAL.NULL)
            {
                return false;
            }

            var target = (char*)GlobalLock(memory);
            if (target == null)
            {
                _ = GlobalFree(memory);
                return false;
            }

            // A NUL inside the text would end it for every reader: it becomes a space, as GPUI writes it (clipboard.rs lines 181
            // to 188).
            var span = new Span<char>(target, text.Length + 1);
            text.AsSpan().CopyTo(span);
            span[..text.Length].Replace('\0', ' ');
            span[text.Length] = '\0';
            _ = GlobalUnlock(memory);

            // The system owns the memory once SetClipboardData succeeds (its documentation); before, it is ours to free.
            if (SetClipboardData(UnicodeText, (HANDLE)memory.Value) == HANDLE.NULL)
            {
                _ = GlobalFree(memory);
                return false;
            }

            return true;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }
}
