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
/// The window is the host's, not an app window's: the clipboard is the OS's one and belongs to the app tier, and it reads and
/// writes with no app window open (design §6.0). The loop's wake window is not used, so that <c>Win32MessageLoop.Wake</c> still
/// does nothing before the loop starts (Issue #928).
/// </remarks>
internal sealed unsafe class Win32Clipboard : IClipboardBackend
{
    private const uint UnicodeText = CF.CF_UNICODETEXT;
    private const string OwnerClassName = "XylemUI.Clipboard";

    private static nint _owner;

    /// <summary>
    /// Gets the message-only window that owns what the clipboard puts on it, or <see cref="HWND.NULL"/> when there is none: the
    /// window is made on the UI thread only, at the first call that opens the clipboard there, and kept for the process, as the
    /// loop's wake window is (design §6.0; Issue #928). A call off the UI thread before then, or a call whose window could not be
    /// made, gets none, and a later call on the UI thread tries again.
    /// </summary>
    internal static HWND Owner
    {
        get
        {
            // Made on the UI thread only: a window belongs to the thread that made it and goes when that thread ends, which would
            // leave the clipboard with an owner that no longer exists.
            nint owner = Volatile.Read(ref _owner);
            if (owner == 0 && UiThread.CheckAccess())
            {
                owner = (nint)MessageWindow.Create(OwnerClassName, out _);
                Volatile.Write(ref _owner, owner);
            }

            return (HWND)owner;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// With no owner window (<see cref="Owner"/>), it opens the clipboard with none: reading needs no owner.
    /// </remarks>
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
    /// <remarks>
    /// With no owner window (<see cref="Owner"/>), it returns <see langword="false"/>: <c>SetClipboardData</c> fails on a clipboard
    /// that has no owner.
    /// </remarks>
    public bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        HWND owner = Owner;
        if (owner == HWND.NULL || OpenClipboard(owner) == BOOL.FALSE)
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
