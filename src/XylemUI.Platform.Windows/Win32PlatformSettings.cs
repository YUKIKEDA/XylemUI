using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The OS's settings of the Windows host (design §6.0, "OS settings"): one object of the app tier. It reads whether Windows asks for
/// less motion, the settings a text field draws with, the user's preferred UI languages, and whether the short time format is in
/// 24-hour form when it is made, and again on each <c>WM_SETTINGCHANGE</c> or <c>WM_SYSCOLORCHANGE</c> a window of the host receives
/// (<see cref="ReadAgain"/>).
/// </summary>
/// <remarks>
/// The loop's message-only windows cannot receive these messages: a message-only window receives no broadcast message (Microsoft's
/// "Window Features", "Message-Only Windows"). Windows sends them to every top-level window, so the first window to receive one reads
/// the change, the others read the same values, and the change is told once.
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
    /// Reads every setting again, and raises <see cref="IPlatformSettings.Changed"/> once, marked with the values that changed,
    /// when one did; through <see cref="SynchronousRenderGuard.RunOrDefer"/>, so that during a synchronous render it runs after the
    /// render (design §2.11.1). A window of the host calls it on <c>WM_SETTINGCHANGE</c> and <c>WM_SYSCOLORCHANGE</c>.
    /// </summary>
    /// <remarks>
    /// The setting the message names is not matched: the values come from several settings, the documentation does not say which
    /// name a change of the list of languages sends (design §4.11, "A change of the OS"), and the message is rare.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A value changed, and the calling thread is not the UI thread.</exception>
    public void ReadAgain()
    {
        PlatformSettingsChanges changes = PlatformSettingsChanges.None;
        bool reducesMotion = ReducesMotionReader(ReducesMotion);
        if (reducesMotion != ReducesMotion)
        {
            ReducesMotion = reducesMotion;
            changes |= PlatformSettingsChanges.ReducesMotion;
        }

        TextEditingSettings textEditing = TextEditingReader(TextEditing);
        if (textEditing != TextEditing)
        {
            TextEditing = textEditing;
            changes |= PlatformSettingsChanges.TextEditing;
        }

        string[] uiLanguages = UiLanguagesReader();
        if (!uiLanguages.AsSpan().SequenceEqual(_uiLanguages))
        {
            _uiLanguages = uiLanguages;
            changes |= PlatformSettingsChanges.UiLanguages;
        }

        bool use24HourFormat = Use24HourFormatReader(Use24HourFormat);
        if (use24HourFormat != Use24HourFormat)
        {
            Use24HourFormat = use24HourFormat;
            changes |= PlatformSettingsChanges.Use24HourFormat;
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

    // A COLORREF is 0x00BBGGRR.
    private static Color FromColorRef(uint colorRef) => new((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16), 0xFF);

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
