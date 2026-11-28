using TerraFX.Interop.Windows;
using TerraFX.Interop.WinRT;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.WinRT.WinRT;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Reads the OS's appearance on Windows (design §13.2, "Windows で読むもの"): light or dark and the accent color from WinRT's
/// <c>UISettings</c>, high contrast from <c>SPI_GETHIGHCONTRAST</c>, and, while it is on, its eight colors from <c>GetSysColor</c>.
/// </summary>
/// <remarks>
/// <c>UISettings</c> is activated with <c>RoActivateInstance</c> and called through its vtable (<c>IUISettings3</c>), with no WinRT
/// projection (design §13.2, "WinRT の呼び方"; spike #888, <c>docs/benchmarks/th1-os-appearance.md</c>). It is activated for each
/// reading and released in it, so that no reference outlives the reading on the UI thread.
/// </remarks>
internal static unsafe class Win32Appearance
{
    private const string UISettingsClass = "Windows.UI.ViewManagement.UISettings";

    /// <summary>
    /// Reads the OS's appearance now. A <c>UISettings</c> that cannot be made or read is read as light with no accent color (design
    /// §13.2, "WinRT の呼び方"); a <c>SPI_GETHIGHCONTRAST</c> that fails keeps whether high contrast was on before.
    /// </summary>
    /// <param name="before">The appearance read before.</param>
    /// <returns>The appearance.</returns>
    public static SystemAppearance Read(SystemAppearance before)
    {
        ReadUISettings(out Color? foreground, out Color? accent);

        // The call writes the local structure, whose size it is given; the scheme's name it points to is not read.
        var contrast = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        bool highContrast =
            SystemParametersInfoW(SPI.SPI_GETHIGHCONTRAST, contrast.cbSize, &contrast, 0) == BOOL.FALSE
                ? before.HighContrast
                : (contrast.dwFlags & HCF.HCF_HIGHCONTRASTON) != 0;
        return FromValues(foreground, accent, highContrast ? ReadContrastColors(static index => GetSysColor(index)) : null);
    }

    /// <summary>
    /// Makes the OS's appearance from what was read (design §13.2, "Windows で読むもの"). While high contrast is on, it is dark when
    /// the theme's <see cref="HighContrastColors.WindowText"/> is a light color; otherwise it is dark when <c>UISettings</c>'
    /// foreground is one, and light when there is none. A color is light by Microsoft's <c>IsColorLight</c>,
    /// <c>((5 * G) + (2 * R) + B) &gt; (8 * 128)</c> ("Support Dark and Light themes in Win32 apps").
    /// </summary>
    /// <param name="foreground"><c>UISettings</c>' <c>UIColorType.Foreground</c>; <see langword="null"/> when it was not read.</param>
    /// <param name="accent"><c>UISettings</c>' <c>UIColorType.Accent</c>; <see langword="null"/> when it was not read.</param>
    /// <param name="contrast">The high-contrast theme's colors while high contrast is on; <see langword="null"/> while it is
    /// off.</param>
    /// <returns>The appearance, with the accent color as it was read.</returns>
    internal static SystemAppearance FromValues(Color? foreground, Color? accent, HighContrastColors? contrast)
    {
        if (contrast is { } colors)
        {
            return new SystemAppearance(IsLight(colors.WindowText) ? ColorScheme.Dark : ColorScheme.Light, true, accent, colors);
        }

        ColorScheme scheme = foreground is { } text && IsLight(text) ? ColorScheme.Dark : ColorScheme.Light;
        return new SystemAppearance(scheme, false, accent, null);
    }

    /// <summary>
    /// Reads the high-contrast colors from the system colors (design §13.2, "Windows で読むもの"): <c>COLOR_WINDOW</c>,
    /// <c>COLOR_WINDOWTEXT</c>, <c>COLOR_HOTLIGHT</c>, <c>COLOR_GRAYTEXT</c>, <c>COLOR_HIGHLIGHT</c>, <c>COLOR_HIGHLIGHTTEXT</c>,
    /// <c>COLOR_3DFACE</c>, and <c>COLOR_BTNTEXT</c>, in the order of <see cref="HighContrastColors"/>'s members (the "Windows
    /// 10/11 system colors" of Microsoft's "GetSysColor").
    /// </summary>
    /// <param name="getSysColor">What returns the <c>COLORREF</c> (<c>0x00BBGGRR</c>) of a system color's index: Windows'
    /// <c>GetSysColor</c>, or the tests' table.</param>
    /// <returns>The colors, each opaque.</returns>
    internal static HighContrastColors ReadContrastColors(Func<int, uint> getSysColor) =>
        new(
            Win32PlatformSettings.FromColorRef(getSysColor(COLOR.COLOR_WINDOW)),
            Win32PlatformSettings.FromColorRef(getSysColor(COLOR.COLOR_WINDOWTEXT)),
            Win32PlatformSettings.FromColorRef(getSysColor(COLOR.COLOR_HOTLIGHT)),
            Win32PlatformSettings.FromColorRef(getSysColor(COLOR.COLOR_GRAYTEXT)),
            Win32PlatformSettings.FromColorRef(getSysColor(COLOR.COLOR_HIGHLIGHT)),
            Win32PlatformSettings.FromColorRef(getSysColor(COLOR.COLOR_HIGHLIGHTTEXT)),
            Win32PlatformSettings.FromColorRef(getSysColor(COLOR.COLOR_3DFACE)),
            Win32PlatformSettings.FromColorRef(getSysColor(COLOR.COLOR_BTNTEXT))
        );

    private static bool IsLight(Color color) => (5 * color.G) + (2 * color.R) + color.B > 8 * 128;

    // UISettings' foreground and accent colors; none of a call that fails. The activation and the QueryInterface each return a
    // reference that this method releases before it returns.
    private static void ReadUISettings(out Color? foreground, out Color? accent)
    {
        foreground = null;
        accent = null;
        IInspectable* instance = null;

        // The string is a reference to the pinned constant, which outlives the activation: nothing to delete.
        fixed (char* name = UISettingsClass)
        {
            HSTRING_HEADER header;
            HSTRING id;
            if (
                WindowsCreateStringReference(name, (uint)UISettingsClass.Length, &header, &id) < 0
                || RoActivateInstance(id, &instance) < 0
                || instance is null
            )
            {
                return;
            }
        }

        IUISettings3* settings = null;
        Guid iid = __uuidof<IUISettings3>();
        int result = instance->QueryInterface(&iid, (void**)&settings);
        _ = instance->Release();
        if (result < 0 || settings is null)
        {
            return;
        }

        foreground = ColorValue(settings, UIColorType.UIColorType_Foreground);
        accent = ColorValue(settings, UIColorType.UIColorType_Accent);
        _ = settings->Release();
    }

    // The settings' color of a type; none when the call fails.
    private static Color? ColorValue(IUISettings3* settings, UIColorType type)
    {
        TerraFX.Interop.WinRT.Color value;
        return settings->GetColorValue(type, &value) < 0 ? null : new Color(value.R, value.G, value.B, value.A);
    }
}
