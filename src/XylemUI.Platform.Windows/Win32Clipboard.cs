using System.ComponentModel;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The clipboard of Windows as plain text, <c>CF_UNICODETEXT</c> (design §4.9, "The clipboard"). It opens the clipboard once for
/// each call and does not try again: a clipboard another program holds open fails the call (GPUI's <c>clipboard.rs</c> lines 346 to
/// 355), where waiting to try again would stop the UI thread.
/// </summary>
/// <remarks>
/// What it puts on the clipboard is owned by a message-only window of its own (<see cref="Owner"/>): a clipboard opened with no
/// window has no owner after <c>EmptyClipboard</c>, and <c>SetClipboardData</c> then fails (<c>OpenClipboard</c>'s documentation).
/// The clipboard is the OS's one, so it belongs to the app tier and no app window owns it (design §6.0): closing a window does not
/// take its text away. The loop's wake window is not used, so that <c>Win32MessageLoop.Wake</c> still does nothing before the loop
/// starts (Issue #928).
/// </remarks>
internal sealed unsafe class Win32Clipboard : IClipboardBackend
{
    private const uint UnicodeText = CF.CF_UNICODETEXT;
    private const string OwnerClassName = "XylemUI.Clipboard";

    private static nint _owner;

    /// <summary>
    /// Gets the message-only window that owns what the clipboard puts on it. It is made on the UI thread at the first call that
    /// opens the clipboard, and kept for the process, as the loop's wake window is.
    /// </summary>
    /// <exception cref="Win32Exception">The window could not be made.</exception>
    internal static HWND Owner
    {
        get
        {
            if (_owner == 0)
            {
                _owner = CreateOwner();
            }

            return (HWND)_owner;
        }
    }

    /// <inheritdoc/>
    public string? GetText()
    {
        if (OpenClipboard(Owner) == BOOL.FALSE)
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
    /// <remarks>
    /// It asks with <c>IsClipboardFormatAvailable</c>, which needs no open clipboard; Microsoft's documentation gives it for a menu
    /// that enables its paste.
    /// </remarks>
    public bool HasText() => IsClipboardFormatAvailable(UnicodeText) != BOOL.FALSE;

    /// <inheritdoc/>
    public bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (OpenClipboard(Owner) == BOOL.FALSE)
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

    // A message-only window (HWND_MESSAGE): it is never shown, takes no broadcast, and gets the clipboard's notices to its owner,
    // which the default procedure handles.
    private static nint CreateOwner()
    {
        HINSTANCE instance = GetModuleHandleW(null);
        fixed (char* className = OwnerClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &OwnerProcedure,
                hInstance = instance,
                lpszClassName = className,
            };

            if (RegisterClassExW(&windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }

            HWND window = CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, HMENU.NULL, instance, null);
            if (window == HWND.NULL)
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }

            return (nint)window;
        }
    }

    [UnmanagedCallersOnly]
    private static LRESULT OwnerProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        DefWindowProcW(hwnd, message, wParam, lParam);
}
