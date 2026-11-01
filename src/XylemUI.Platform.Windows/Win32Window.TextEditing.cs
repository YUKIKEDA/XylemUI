using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <content>
/// The OS's settings a text field draws with (design §4.9, "The caret" and "Drawing the selection"): the caret's blink and width,
/// and the selection's colors, read for each window as <see cref="ReducesMotion"/> is.
/// </content>
public sealed unsafe partial class Win32Window
{
    private const uint WmSysColorChange = WM.WM_SYSCOLORCHANGE;

    // GetCaretBlinkTime's value for a caret that does not blink (its documentation).
    private const uint NoBlink = uint.MaxValue;

    private Action? _raiseTextEditingChanged;

    /// <summary>
    /// Occurs on the UI thread when <see cref="TextEditing"/> changed, on a <c>WM_SETTINGCHANGE</c> or a <c>WM_SYSCOLORCHANGE</c>.
    /// </summary>
    internal event EventHandler? TextEditingChanged;

    /// <summary>
    /// Gets or sets what reads <see cref="TextEditing"/> from the value before it, for the tests of the change without changing a
    /// setting of Windows.
    /// </summary>
    internal static Func<TextEditingSettings, TextEditingSettings> TextEditingReader { get; set; } = ReadTextEditing;

    /// <summary>
    /// Gets the OS's settings a text field draws with: read when the window is created and on each <c>WM_SETTINGCHANGE</c> and
    /// <c>WM_SYSCOLORCHANGE</c> it receives.
    /// </summary>
    internal TextEditingSettings TextEditing { get; private set; }

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

    // Each window compares with its own value, as ReadReducesMotionAgain does.
    private void ReadTextEditingAgain()
    {
        TextEditingSettings read = TextEditingReader(TextEditing);
        if (read != TextEditing)
        {
            TextEditing = read;
            SynchronousRenderGuard.RunOrDefer(_raiseTextEditingChanged ??= () => TextEditingChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
