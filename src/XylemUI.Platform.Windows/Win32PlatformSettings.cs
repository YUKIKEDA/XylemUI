using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The OS's settings of the Windows host (design §6.0, "OS settings"): one object of the app tier. It reads whether Windows asks for
/// less motion, the settings a text field draws with, the user's preferred UI languages, whether the short time format is in 24-hour
/// form, and the OS's appearance when it is made, and again on each <c>WM_SETTINGCHANGE</c>, <c>WM_SYSCOLORCHANGE</c>, or
/// <c>WM_DWMCOLORIZATIONCOLORCHANGED</c> a window of the host receives (<see cref="ReadAgain"/>).
/// </summary>
/// <remarks>
/// The loop's message-only windows cannot receive these messages: a message-only window receives no broadcast message (Microsoft's
/// "Window Features", "Message-Only Windows"). Windows sends them to every top-level window, so the first window to receive one reads
/// the change, the others read the same values, and the change is told once. One change of Windows' appearance sent up to 10 of them
/// in spike #888; only the reading that finds another value tells it (design §13.2, "同じ値はまとめる").
/// </remarks>
internal sealed unsafe class Win32PlatformSettings : IPlatformSettings
{
    // GetCaretBlinkTime's value for a caret that does not blink (its documentation).
    private const uint NoBlink = uint.MaxValue;

    private string[] _uiLanguages;
    private PlatformSettingsChanges _pending;
    private Action? _raise;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32PlatformSettings"/> class: reads Windows' settings now.
    /// </summary>
    public Win32PlatformSettings()
    {
        ReducesMotion = ReducesMotionReader(false);
        TextEditing = TextEditingReader(TextEditingSettings.Default);
        _uiLanguages = UiLanguagesReader();
        Use24HourFormat = Use24HourFormatReader(false);
        Appearance = AppearanceReader(default);
    }

    /// <inheritdoc/>
    public event Action<PlatformSettingsChanges>? Changed;

    /// <inheritdoc/>
    /// <remarks>
    /// "Animation effects" is off in Windows' settings: <c>SPI_GETCLIENTAREAANIMATION</c> is false. A read that fails keeps the value
    /// before.
    /// </remarks>
    public bool ReducesMotion { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Read again on <c>WM_SYSCOLORCHANGE</c> too, for the selection's colors.
    /// </remarks>
    public TextEditingSettings TextEditing { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<string> UiLanguages => _uiLanguages;

    /// <inheritdoc/>
    /// <remarks>
    /// The user's short time format (<c>LOCALE_SSHORTTIME</c>) holds the hour pattern <c>H</c> outside single quotes (design §4.11,
    /// "The locale of the formats"); a read that fails keeps the value before, <see langword="false"/> at the start.
    /// </remarks>
    public bool Use24HourFormat { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Read by <see cref="Win32Appearance.Read"/> (design §13.2, "Windows で読むもの").
    /// </remarks>
    public SystemAppearance Appearance { get; private set; }

    /// <summary>
    /// Gets or sets what reads <see cref="ReducesMotion"/> from the value before it, for the tests of the change without changing a
    /// setting of Windows.
    /// </summary>
    internal static Func<bool, bool> ReducesMotionReader { get; set; } = ReadReducesMotion;

    /// <summary>
    /// Gets or sets what reads <see cref="TextEditing"/> from the value before it, for the tests of the change without changing a
    /// setting of Windows.
    /// </summary>
    internal static Func<TextEditingSettings, TextEditingSettings> TextEditingReader { get; set; } = ReadTextEditing;

    /// <summary>
    /// Gets or sets what reads <see cref="UiLanguages"/>, for the tests of the change without changing a setting of Windows.
    /// </summary>
    internal static Func<string[]> UiLanguagesReader { get; set; } = Win32UiLanguage.ReadAll;

    /// <summary>
    /// Gets or sets what reads <see cref="Use24HourFormat"/> from the value before it, for the tests of the change without changing
    /// a setting of Windows.
    /// </summary>
    internal static Func<bool, bool> Use24HourFormatReader { get; set; } = Win32TimeFormat.ReadUse24HourFormat;

    /// <summary>
    /// Gets or sets what reads <see cref="Appearance"/> from the value before it, for the tests of the change without changing a
    /// setting of Windows.
    /// </summary>
    internal static Func<SystemAppearance, SystemAppearance> AppearanceReader { get; set; } = Win32Appearance.Read;

    /// <summary>
    /// Reads every setting again, and raises <see cref="IPlatformSettings.Changed"/> once, marked with the values that changed,
    /// when one did; through <see cref="SynchronousRenderGuard.RunOrDefer"/>, so that during a synchronous render it runs after the
    /// render (design §2.11.1). A window of the host calls it on <c>WM_SETTINGCHANGE</c>, <c>WM_SYSCOLORCHANGE</c>, and
    /// <c>WM_DWMCOLORIZATIONCOLORCHANGED</c>.
    /// </summary>
    /// <remarks>
    /// The setting the message names is not matched: the values come from several settings, the documentation does not say which
    /// name a change of the list of languages sends (design §4.11, "A change of the OS"), and the message is rare. Every value is
    /// read before any is kept, so a read that throws leaves every value as it was and raises nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    public void ReadAgain()
    {
        UiThread.VerifyAccess();
        bool reducesMotion = ReducesMotionReader(ReducesMotion);
        TextEditingSettings textEditing = TextEditingReader(TextEditing);
        string[] uiLanguages = UiLanguagesReader();
        bool use24HourFormat = Use24HourFormatReader(Use24HourFormat);
        SystemAppearance appearance = AppearanceReader(Appearance);

        PlatformSettingsChanges changes = PlatformSettingsChanges.None;
        if (reducesMotion != ReducesMotion)
        {
            ReducesMotion = reducesMotion;
            changes |= PlatformSettingsChanges.ReducesMotion;
        }

        if (textEditing != TextEditing)
        {
            TextEditing = textEditing;
            changes |= PlatformSettingsChanges.TextEditing;
        }

        if (!uiLanguages.AsSpan().SequenceEqual(_uiLanguages))
        {
            _uiLanguages = uiLanguages;
            changes |= PlatformSettingsChanges.UiLanguages;
        }

        if (use24HourFormat != Use24HourFormat)
        {
            Use24HourFormat = use24HourFormat;
            changes |= PlatformSettingsChanges.Use24HourFormat;
        }

        if (appearance != Appearance)
        {
            Appearance = appearance;
            changes |= PlatformSettingsChanges.Appearance;
        }

        if (changes != PlatformSettingsChanges.None)
        {
            // Two readings deferred by one synchronous render are told once, with the marks of both.
            _pending |= changes;
            SynchronousRenderGuard.RunOrDefer(_raise ??= Raise);
        }
    }

    // Whether the client area's animations are off (SPI_GETCLIENTAREAANIMATION); a call that fails keeps the value before.
    private static bool ReadReducesMotion(bool before)
    {
        BOOL animates;
        return SystemParametersInfoW(SPI.SPI_GETCLIENTAREAANIMATION, 0, &animates, 0) == BOOL.FALSE ? before : animates == BOOL.FALSE;
    }

    // The settings from Windows: the caret's blink (GetCaretBlinkTime, INFINITE for none) and width (SPI_GETCARETWIDTH), the
    // selection's colors (GetSysColor), whether access keys are always underlined (SPI_GETKEYBOARDCUES), and how far a press moves
    // before a drag starts (SM_CXDRAG, SM_CYDRAG, design §7, "Drag and drop"). A call that fails keeps the value before.
    private static TextEditingSettings ReadTextEditing(TextEditingSettings before)
    {
        uint blink = GetCaretBlinkTime();
        TimeSpan? caretBlink =
            blink == 0 ? before.CaretBlink
            : blink == NoBlink ? null
            : TimeSpan.FromMilliseconds(blink);
        uint width;
        float caretWidth = SystemParametersInfoW(SPI.SPI_GETCARETWIDTH, 0, &width, 0) == BOOL.FALSE ? before.CaretWidth : width;
        BOOL cues;
        bool keyboardCues = SystemParametersInfoW(SPI.SPI_GETKEYBOARDCUES, 0, &cues, 0) == BOOL.FALSE ? before.KeyboardCues : cues != BOOL.FALSE;
        int dragWidth = GetSystemMetrics(SM.SM_CXDRAG);
        int dragHeight = GetSystemMetrics(SM.SM_CYDRAG);
        return new TextEditingSettings(
            caretBlink,
            caretWidth,
            FromColorRef(GetSysColor(COLOR.COLOR_HIGHLIGHT)),
            FromColorRef(GetSysColor(COLOR.COLOR_HIGHLIGHTTEXT)),
            keyboardCues,
            dragWidth > 0 ? dragWidth : before.DragWidth,
            dragHeight > 0 ? dragHeight : before.DragHeight
        );
    }

    /// <summary>
    /// Returns the opaque color of a <c>COLORREF</c>, which is <c>0x00BBGGRR</c>.
    /// </summary>
    /// <param name="colorRef">The <c>COLORREF</c>.</param>
    /// <returns>The color.</returns>
    internal static Color FromColorRef(uint colorRef) => new((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16), 0xFF);

    private void Raise()
    {
        PlatformSettingsChanges changes = _pending;
        _pending = PlatformSettingsChanges.None;
        if (changes != PlatformSettingsChanges.None)
        {
            Changed?.Invoke(changes);
        }
    }
}
