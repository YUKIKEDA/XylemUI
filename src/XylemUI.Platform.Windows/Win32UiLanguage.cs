using System.Runtime.InteropServices;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Reads the user's UI language from Windows (design §2.13, "The defaults of Text"). The libraries run with invariant globalization,
/// where <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> is the invariant culture whatever the OS's language is, so
/// the host asks Windows.
/// </summary>
internal static unsafe partial class Win32UiLanguage
{
    // MUI_LANGUAGE_NAME (winnls.h): the languages as tags such as "ja-JP". TerraFX does not declare the winnls functions.
    private const uint MuiLanguageName = 0x8;

    /// <summary>
    /// Returns the tag of the first of the user's preferred UI languages (<c>GetUserPreferredUILanguages</c>), or "und"
    /// (undetermined) when Windows gives none.
    /// </summary>
    /// <returns>The tag, such as "ja-JP".</returns>
    public static string Read()
    {
        uint count;
        uint length = 0;
        if (GetUserPreferredUILanguages(MuiLanguageName, &count, null, &length) == 0 || length == 0 || length > 4096)
        {
            return "und";
        }

        // The buffer is the length Windows asked for, in characters, and lives until the string is copied out of it. The list is
        // null-separated and ends with two nulls; the first entry is the language in use.
        char* buffer = stackalloc char[(int)length];
        if (GetUserPreferredUILanguages(MuiLanguageName, &count, buffer, &length) == 0 || count == 0)
        {
            return "und";
        }

        string first = new(buffer);
        return first.Length > 0 ? first : "und";
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetUserPreferredUILanguages(uint flags, uint* count, char* buffer, uint* length);
}
