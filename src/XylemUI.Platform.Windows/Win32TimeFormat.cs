using System.Runtime.InteropServices;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Reads whether the user's time format shows the hours in 24-hour form (design §4.11, "The locale of the formats"): the only value
/// the formats take from Windows' regional settings. The libraries run with invariant globalization, where the cultures do not
/// carry the user's settings, so the host asks Windows.
/// </summary>
internal static unsafe partial class Win32TimeFormat
{
    // LOCALE_STIMEFORMAT (winnls.h): the user's time format, such as "h:mm:ss tt". TerraFX does not declare the winnls functions.
    private const uint LocaleTimeFormat = 0x1003;

    // The buffer of Flutter 3.47.8's GetUserTimeFormat (engine/src/flutter/shell/platform/windows/system_utils.cc, line 104). A
    // longer format fails the call, and the value before is kept.
    private const int BufferLength = 100;

    /// <summary>
    /// Returns whether the user's time format (<c>GetLocaleInfoEx</c> with <c>LOCALE_STIMEFORMAT</c>) holds the hour pattern
    /// <c>H</c> outside single quotes (<see cref="Uses24Hour"/>).
    /// </summary>
    /// <param name="before">The value returned when Windows gives no format.</param>
    /// <returns>Whether the hours are in 24-hour form; <paramref name="before"/> when the call fails.</returns>
    public static bool ReadUse24HourFormat(bool before)
    {
        char* buffer = stackalloc char[BufferLength];
        int written = GetLocaleInfoEx(null, LocaleTimeFormat, buffer, BufferLength);
        return written <= 0 ? before : Uses24Hour(new ReadOnlySpan<char>(buffer, BufferLength), written);
    }

    /// <summary>
    /// Returns whether a time format that <c>GetLocaleInfoEx</c> wrote holds the hour pattern <c>H</c> outside single quotes (design
    /// §4.11, "The locale of the formats"). Only the characters the call says it wrote are read: what lies past the format's
    /// terminating null is not the format (PR #854's review).
    /// </summary>
    /// <remarks>
    /// Microsoft's "Hour, Minute, and Second Format Pictures" shows the characters between single quotes as they are and writes a
    /// quote itself as two in a row (<c>'abc''bar'</c> is <c>abc'bar</c>), so <c>h'H'mm</c> is a 12-hour format. Flutter 3.47.8
    /// (<c>engine/src/flutter/shell/platform/windows/system_utils.cc</c>, lines 113 to 115, <c>time_format.find(L"H")</c>) and
    /// Avalonia 12.1.4 (<c>src/Avalonia.Controls/DateTimePickers/TimePicker.cs</c>, lines 101 to 102, <c>IndexOf("H")</c>) count
    /// an <c>H</c> between quotes too. A quote that is not closed quotes the rest of the format, which Microsoft's pages do not
    /// describe.
    /// </remarks>
    /// <param name="buffer">The buffer the call wrote into.</param>
    /// <param name="written">What the call returned: the characters it wrote, the terminating null included; at least 1.</param>
    /// <returns>Whether the format holds <c>H</c> outside single quotes.</returns>
    public static bool Uses24Hour(ReadOnlySpan<char> buffer, int written)
    {
        // Two quotes in a row close and open again: inside quotes they stay inside, and outside they quote nothing, so a toggle
        // reads both as the pages say.
        bool quoted = false;
        foreach (char c in buffer[..(written - 1)])
        {
            if (c == '\'')
            {
                quoted = !quoted;
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
