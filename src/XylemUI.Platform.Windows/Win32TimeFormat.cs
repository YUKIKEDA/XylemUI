using System.Runtime.InteropServices;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Reads whether the user's short time format shows the hours in 24-hour form (design §4.11, "The locale of the formats"): the only
/// value the formats take from Windows' regional settings. The libraries run with invariant globalization, where the cultures do not
/// carry the user's settings, so the host asks Windows.
/// </summary>
/// <remarks>
/// The short time format (<c>LOCALE_SSHORTTIME</c>) is read, not the long one (<c>LOCALE_STIMEFORMAT</c>): the key decides the
/// short time (<c>"jm"</c>), and a user can set the short time to <c>HH:mm</c> and the long time to <c>h:mm:ss tt</c> (Issue #906).
/// Flutter 3.47.8 reads the long one (<c>engine/src/flutter/shell/platform/windows/system_utils.cc</c>, lines 101 to 115).
/// </remarks>
internal static unsafe partial class Win32TimeFormat
{
    // LOCALE_SSHORTTIME (winnls.h, Windows 7 and later): the user's short time format, such as "h:mm tt". TerraFX does not declare
    // the winnls functions.
    private const uint LocaleShortTime = 0x79;

    // The buffer of Flutter 3.47.8's GetUserTimeFormat (engine/src/flutter/shell/platform/windows/system_utils.cc, line 104). A
    // longer format fails the call, and the value before is kept.
    private const int BufferLength = 100;

    /// <summary>
    /// Returns whether the user's short time format (<c>GetLocaleInfoEx</c> with <c>LOCALE_SSHORTTIME</c>) holds the hour pattern
    /// <c>H</c> outside single quotes (<see cref="Uses24Hour"/>).
    /// </summary>
    /// <param name="before">The value returned when Windows gives no format.</param>
    /// <returns>Whether the hours are in 24-hour form; <paramref name="before"/> when the call fails.</returns>
    public static bool ReadUse24HourFormat(bool before)
    {
        Span<char> buffer = stackalloc char[BufferLength];
        int written = ReadShortTimeFormat(buffer);
        return Uses24Hour(buffer, written, before);
    }

    /// <summary>
    /// Writes the user's short time format (<c>GetLocaleInfoEx</c> with <c>LOCALE_NAME_USER_DEFAULT</c> and
    /// <c>LOCALE_SSHORTTIME</c>, the user's override included) into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer; a format longer than it fails the call.</param>
    /// <returns>What the call returned: the characters written, the terminating null included; 0 when the call failed.</returns>
    public static int ReadShortTimeFormat(Span<char> buffer)
    {
        fixed (char* data = buffer)
        {
            return GetLocaleInfoEx(null, LocaleShortTime, data, buffer.Length);
        }
    }

    /// <summary>
    /// Returns whether a time format that <c>GetLocaleInfoEx</c> wrote holds the hour pattern <c>H</c> outside single quotes (design
    /// §4.11, "The locale of the formats"). Only the characters the call says it wrote are read: what lies past the format's
    /// terminating null is not the format (PR #854's review). Of a list, only the first format is read.
    /// </summary>
    /// <remarks>
    /// Microsoft's <c>LOCALE_SSHORTTIME</c> page says: "This constant can specify multiple formats in a semicolon-delimited list.
    /// However, the preferred short time format should be the first value listed." So the read stops at the first <c>;</c>
    /// outside single quotes; a <c>;</c> between quotes is text, as any character between quotes is.
    ///
    /// Microsoft's "Hour, Minute, and Second Format Pictures" shows the characters between single quotes as they are and writes a
    /// quote itself as two in a row (<c>'abc''bar'</c> is <c>abc'bar</c>), so <c>h'H'mm</c> is a 12-hour format. Flutter 3.47.8
    /// (<c>engine/src/flutter/shell/platform/windows/system_utils.cc</c>, lines 113 to 115, <c>time_format.find(L"H")</c>) and
    /// Avalonia 12.1.4 (<c>src/Avalonia.Controls/DateTimePickers/TimePicker.cs</c>, lines 101 to 102, <c>IndexOf("H")</c>) count
    /// an <c>H</c> between quotes too. A quote that is not closed quotes the rest of the format, which Microsoft's pages do not
    /// describe.
    /// </remarks>
    /// <param name="buffer">The buffer the call wrote into.</param>
    /// <param name="written">What the call returned: the characters it wrote, the terminating null included; 0 when it failed.</param>
    /// <param name="before">The value returned when the call failed.</param>
    /// <returns>
    /// Whether the format, or the first format of a list, holds <c>H</c> outside single quotes; <paramref name="before"/> when the
    /// call failed.
    /// </returns>
    public static bool Uses24Hour(ReadOnlySpan<char> buffer, int written, bool before)
    {
        if (written <= 0)
        {
            return before;
        }

        // Two quotes in a row close and open again: inside quotes they stay inside, and outside they quote nothing, so a toggle
        // reads both as the pages say.
        bool quoted = false;
        foreach (char c in buffer[..(written - 1)])
        {
            if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (c == ';' && !quoted)
            {
                break;
            }
            else if (c == 'H' && !quoted)
            {
                return true;
            }
        }

        return false;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetLocaleInfoEx(char* localeName, uint type, char* data, int length);
}
