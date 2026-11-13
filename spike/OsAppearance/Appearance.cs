using static OsAppearance.Native;
using static OsAppearance.Report;

namespace OsAppearance;

/// <summary>
/// One reading of what design §13.2 reads (UISettings colours, the light/dark decision, high contrast and its 8 system
/// colours), plus the registry values beside them for comparison. A reading is a list of named text values, so two readings
/// compare by name.
/// </summary>
internal static unsafe class Appearance
{
    // UIColorType (windows.ui.viewmanagement.h): Background 0, Foreground 1, AccentDark3 2, AccentDark2 3, AccentDark1 4,
    // Accent 5, AccentLight1 6, AccentLight2 7, AccentLight3 8.
    private static readonly (string Name, int Type)[] UiColors =
    [
        ("Foreground", 1),
        ("Background", 0),
        ("Accent", 5),
        ("AccentLight1", 6),
        ("AccentLight2", 7),
        ("AccentLight3", 8),
        ("AccentDark1", 4),
        ("AccentDark2", 3),
        ("AccentDark3", 2),
    ];

    // The 8 colours of design §13.2 ("Windows 10/11 system colors" in GetSysColor's documentation), winuser.h indices.
    private static readonly (string Name, int Index)[] SysColors =
    [
        ("COLOR_WINDOW", 5),
        ("COLOR_WINDOWTEXT", 8),
        ("COLOR_HOTLIGHT", 26),
        ("COLOR_GRAYTEXT", 17),
        ("COLOR_HIGHLIGHT", 13),
        ("COLOR_HIGHLIGHTTEXT", 14),
        ("COLOR_3DFACE", 15),
        ("COLOR_BTNTEXT", 18),
    ];

    public static List<(string Name, string Value)> Read()
    {
        var values = new List<(string Name, string Value)>();
        foreach ((string name, int type) in UiColors)
        {
            values.Add(($"uisettings.{name}", UISettingsProbe.ColorText(type)));
        }

        values.Add(("scheme.from_foreground", SchemeFromForeground()));

        HighContrastW contrast = default;
        contrast.Size = (uint)sizeof(HighContrastW);
        int ok = SystemParametersInfoW(SpiGetHighContrast, contrast.Size, &contrast, 0);
        if (ok == 0)
        {
            values.Add(("highcontrast.on", "FAILED SystemParametersInfoW returned 0"));
        }
        else
        {
            values.Add(("highcontrast.on", ((contrast.Flags & HcfHighContrastOn) != 0) ? "true" : "false"));
            values.Add(("highcontrast.flags", Hex(contrast.Flags)));
            values.Add(("highcontrast.scheme", contrast.DefaultScheme is null ? "(null)" : new string(contrast.DefaultScheme)));
        }

        foreach ((string name, int index) in SysColors)
        {
            values.Add(($"syscolor.{name}", ColorRef(GetSysColor(index))));
        }

        values.Add(("scheme.from_windowtext", SchemeFromColorRef(GetSysColor(8))));

        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        values.Add(("registry.AppsUseLightTheme", RegistryValues.ReadDword(HkeyCurrentUser, Personalize, "AppsUseLightTheme")));
        values.Add(("registry.SystemUsesLightTheme", RegistryValues.ReadDword(HkeyCurrentUser, Personalize, "SystemUsesLightTheme")));
        values.Add(("registry.ColorPrevalence", RegistryValues.ReadDword(HkeyCurrentUser, Personalize, "ColorPrevalence")));
        return values;
    }

    public static void Print(List<(string Name, string Value)> values)
    {
        foreach ((string name, string value) in values)
        {
            Probe(name, value);
        }
    }

    /// <summary>"name: old -> new; ..." for every value that differs, or "(nothing)".</summary>
    public static string Diff(List<(string Name, string Value)> before, List<(string Name, string Value)> after)
    {
        var parts = new List<string>();
        var old = new Dictionary<string, string>();
        foreach ((string name, string value) in before)
        {
            old[name] = value;
        }

        foreach ((string name, string value) in after)
        {
            if (!old.TryGetValue(name, out string? previous))
            {
                parts.Add($"{name}: (absent) -> {value}");
            }
            else if (previous != value)
            {
                parts.Add($"{name}: {previous} -> {value}");
            }
        }

        return parts.Count == 0 ? "(nothing)" : string.Join("; ", parts);
    }

    /// <summary>
    /// Microsoft's test in "Support Dark and Light themes in Win32 apps": a colour is light when ((5 * G) + (2 * R) + B) &gt;
    /// (8 * 128). A light Foreground means the dark scheme.
    /// </summary>
    private static string SchemeFromForeground()
    {
        if (!UISettingsProbe.TryGetColor(UISettingsProbe.Foreground, out WinRTColor c))
        {
            return "unknown (Foreground not read)";
        }

        int brightness = (5 * c.G) + (2 * c.R) + c.B;
        bool light = brightness > 8 * 128;
        return $"{(light ? "Dark" : "Light")} (Foreground {UISettingsProbe.Format(c)}, 5G+2R+B={Text(brightness)} {(light ? ">" : "<=")} 1024)";
    }

    /// <summary>The same test on COLOR_WINDOWTEXT, which design §13.2 uses while high contrast is on.</summary>
    private static string SchemeFromColorRef(uint colorRef)
    {
        int r = (int)(colorRef & 0xFF);
        int g = (int)((colorRef >> 8) & 0xFF);
        int b = (int)((colorRef >> 16) & 0xFF);
        int brightness = (5 * g) + (2 * r) + b;
        bool light = brightness > 8 * 128;
        return $"{(light ? "Dark" : "Light")} (COLOR_WINDOWTEXT {ColorRef(colorRef)}, 5G+2R+B={Text(brightness)} {(light ? ">" : "<=")} 1024)";
    }

    /// <summary>A COLORREF (0x00BBGGRR) as #RRGGBB.</summary>
    private static string ColorRef(uint colorRef) => $"#{colorRef & 0xFF:X2}{(colorRef >> 8) & 0xFF:X2}{(colorRef >> 16) & 0xFF:X2}";
}

/// <summary>RegGetValueW for a DWORD and a string.</summary>
internal static unsafe class RegistryValues
{
    public static string ReadDword(nint root, string subKey, string value)
    {
        uint data = 0;
        uint size = sizeof(uint);
        int status;
        fixed (char* key = subKey)
        fixed (char* name = value)
        {
            status = RegGetValueW(root, key, name, RrfRtRegDword, null, &data, &size);
        }

        return status == 0 ? Text(data) : $"(absent, status {Text(status)})";
    }

    public static string ReadString(nint root, string subKey, string value)
    {
        char* buffer = stackalloc char[256];
        uint size = 256 * sizeof(char);
        int status;
        fixed (char* key = subKey)
        fixed (char* name = value)
        {
            status = RegGetValueW(root, key, name, RrfRtRegSz, null, buffer, &size);
        }

        return status == 0 ? new string(buffer) : $"(absent, status {Text(status)})";
    }
}
