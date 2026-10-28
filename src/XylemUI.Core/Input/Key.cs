namespace XylemUI;

/// <summary>
/// A key as the keyboard's layout names it (design §7, "Keys and the focus"): the names and the values of Avalonia's <c>Key</c>
/// (<c>Avalonia.Base/Input/Key.cs</c>), one name for each value, and the left and right modifier keys apart. Windows gives it
/// from the virtual-key code; <see cref="PhysicalKey"/> names where the key is.
/// </summary>
public enum Key : ushort
{
    /// <summary>
    /// No key, or one that has no name here.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Cancel key (Control+Break).
    /// </summary>
    Cancel = 1,

    /// <summary>
    /// The Backspace key.
    /// </summary>
    Back = 2,

    /// <summary>
    /// The Tab key.
    /// </summary>
    Tab = 3,

    /// <summary>
    /// The Clear key (5 on the number pad without Num Lock).
    /// </summary>
    Clear = 5,

    /// <summary>
    /// The Enter key, on the main keys and on the number pad.
    /// </summary>
    Enter = 6,

    /// <summary>
    /// The Pause key.
    /// </summary>
    Pause = 7,

    /// <summary>
    /// The Caps Lock key.
    /// </summary>
    CapsLock = 8,

    /// <summary>
    /// The IME's Kana mode key (the Hangul mode key on a Korean keyboard).
    /// </summary>
    KanaMode = 9,

    /// <summary>
    /// The IME's Junja mode key.
    /// </summary>
    JunjaMode = 10,

    /// <summary>
    /// The IME's final mode key.
    /// </summary>
    FinalMode = 11,

    /// <summary>
    /// The IME's Kanji mode key (the Hanja mode key on a Korean keyboard).
    /// </summary>
    KanjiMode = 12,

    /// <summary>
    /// The Esc key.
    /// </summary>
    Escape = 13,

    /// <summary>
    /// The IME's convert key.
    /// </summary>
    ImeConvert = 14,

    /// <summary>
    /// The IME's non-convert key.
    /// </summary>
    ImeNonConvert = 15,

    /// <summary>
    /// The IME's accept key.
    /// </summary>
    ImeAccept = 16,

    /// <summary>
    /// The IME's mode change key.
    /// </summary>
    ImeModeChange = 17,

    /// <summary>
    /// The space bar.
    /// </summary>
    Space = 18,

    /// <summary>
    /// The Page Up key.
    /// </summary>
    PageUp = 19,

    /// <summary>
    /// The Page Down key.
    /// </summary>
    PageDown = 20,

    /// <summary>
    /// The End key.
    /// </summary>
    End = 21,

    /// <summary>
    /// The Home key.
    /// </summary>
    Home = 22,

    /// <summary>
    /// The left arrow.
    /// </summary>
    Left = 23,

    /// <summary>
    /// The up arrow.
    /// </summary>
    Up = 24,

    /// <summary>
    /// The right arrow.
    /// </summary>
    Right = 25,

    /// <summary>
    /// The down arrow.
    /// </summary>
    Down = 26,

    /// <summary>
    /// The Select key.
    /// </summary>
    Select = 27,

    /// <summary>
    /// The Print key.
    /// </summary>
    Print = 28,

    /// <summary>
    /// The Execute key.
    /// </summary>
    Execute = 29,

    /// <summary>
    /// The Print Screen key.
    /// </summary>
    PrintScreen = 30,

    /// <summary>
    /// The Insert key.
    /// </summary>
    Insert = 31,

    /// <summary>
    /// The Delete key.
    /// </summary>
    Delete = 32,

    /// <summary>
    /// The Help key.
    /// </summary>
    Help = 33,

    /// <summary>
    /// The 0 key on the main keys.
    /// </summary>
    D0 = 34,

    /// <summary>
    /// The 1 key on the main keys.
    /// </summary>
    D1 = 35,

    /// <summary>
    /// The 2 key on the main keys.
    /// </summary>
    D2 = 36,

    /// <summary>
    /// The 3 key on the main keys.
    /// </summary>
    D3 = 37,

    /// <summary>
    /// The 4 key on the main keys.
    /// </summary>
    D4 = 38,

    /// <summary>
    /// The 5 key on the main keys.
    /// </summary>
    D5 = 39,

    /// <summary>
    /// The 6 key on the main keys.
    /// </summary>
    D6 = 40,

    /// <summary>
    /// The 7 key on the main keys.
    /// </summary>
    D7 = 41,

    /// <summary>
    /// The 8 key on the main keys.
    /// </summary>
    D8 = 42,

    /// <summary>
    /// The 9 key on the main keys.
    /// </summary>
    D9 = 43,

    /// <summary>
    /// The A key.
    /// </summary>
    A = 44,

    /// <summary>
    /// The B key.
    /// </summary>
    B = 45,

    /// <summary>
    /// The C key.
    /// </summary>
    C = 46,

    /// <summary>
    /// The D key.
    /// </summary>
    D = 47,

    /// <summary>
    /// The E key.
    /// </summary>
    E = 48,

    /// <summary>
    /// The F key.
    /// </summary>
    F = 49,

    /// <summary>
    /// The G key.
    /// </summary>
    G = 50,

    /// <summary>
    /// The H key.
    /// </summary>
    H = 51,

    /// <summary>
    /// The I key.
    /// </summary>
    I = 52,

    /// <summary>
    /// The J key.
    /// </summary>
    J = 53,

    /// <summary>
    /// The K key.
    /// </summary>
    K = 54,

    /// <summary>
    /// The L key.
    /// </summary>
    L = 55,

    /// <summary>
    /// The M key.
    /// </summary>
    M = 56,

    /// <summary>
    /// The N key.
    /// </summary>
    N = 57,

    /// <summary>
    /// The O key.
    /// </summary>
    O = 58,

    /// <summary>
    /// The P key.
    /// </summary>
    P = 59,

    /// <summary>
    /// The Q key.
    /// </summary>
    Q = 60,

    /// <summary>
    /// The R key.
    /// </summary>
    R = 61,

    /// <summary>
    /// The S key.
    /// </summary>
    S = 62,

    /// <summary>
    /// The T key.
    /// </summary>
    T = 63,

    /// <summary>
    /// The U key.
    /// </summary>
    U = 64,

    /// <summary>
    /// The V key.
    /// </summary>
    V = 65,

    /// <summary>
    /// The W key.
    /// </summary>
    W = 66,

    /// <summary>
    /// The X key.
    /// </summary>
    X = 67,

    /// <summary>
    /// The Y key.
    /// </summary>
    Y = 68,

    /// <summary>
    /// The Z key.
    /// </summary>
    Z = 69,

    /// <summary>
    /// The left Windows key.
    /// </summary>
    LWin = 70,

    /// <summary>
    /// The right Windows key.
    /// </summary>
    RWin = 71,

    /// <summary>
    /// The Application (context menu) key.
    /// </summary>
    Apps = 72,

    /// <summary>
    /// The Sleep key.
    /// </summary>
    Sleep = 73,

    /// <summary>
    /// The 0 key on the number pad.
    /// </summary>
    NumPad0 = 74,

    /// <summary>
    /// The 1 key on the number pad.
    /// </summary>
    NumPad1 = 75,

    /// <summary>
    /// The 2 key on the number pad.
    /// </summary>
    NumPad2 = 76,

    /// <summary>
    /// The 3 key on the number pad.
    /// </summary>
    NumPad3 = 77,

    /// <summary>
    /// The 4 key on the number pad.
    /// </summary>
    NumPad4 = 78,

    /// <summary>
    /// The 5 key on the number pad.
    /// </summary>
    NumPad5 = 79,

    /// <summary>
    /// The 6 key on the number pad.
    /// </summary>
    NumPad6 = 80,

    /// <summary>
    /// The 7 key on the number pad.
    /// </summary>
    NumPad7 = 81,

    /// <summary>
    /// The 8 key on the number pad.
    /// </summary>
    NumPad8 = 82,

    /// <summary>
    /// The 9 key on the number pad.
    /// </summary>
    NumPad9 = 83,

    /// <summary>
    /// The * key on the number pad.
    /// </summary>
    Multiply = 84,

    /// <summary>
    /// The + key on the number pad.
    /// </summary>
    Add = 85,

    /// <summary>
    /// The separator key on the number pad.
    /// </summary>
    Separator = 86,

    /// <summary>
    /// The - key on the number pad.
    /// </summary>
    Subtract = 87,

    /// <summary>
    /// The decimal key on the number pad.
    /// </summary>
    Decimal = 88,

    /// <summary>
    /// The / key on the number pad.
    /// </summary>
    Divide = 89,

    /// <summary>
    /// The F1 key.
    /// </summary>
    F1 = 90,

    /// <summary>
    /// The F2 key.
    /// </summary>
    F2 = 91,

    /// <summary>
    /// The F3 key.
    /// </summary>
    F3 = 92,

    /// <summary>
    /// The F4 key.
    /// </summary>
    F4 = 93,

    /// <summary>
    /// The F5 key.
    /// </summary>
    F5 = 94,

    /// <summary>
    /// The F6 key.
    /// </summary>
    F6 = 95,

    /// <summary>
    /// The F7 key.
    /// </summary>
    F7 = 96,

    /// <summary>
    /// The F8 key.
    /// </summary>
    F8 = 97,

    /// <summary>
    /// The F9 key.
    /// </summary>
    F9 = 98,

    /// <summary>
    /// The F10 key.
    /// </summary>
    F10 = 99,

    /// <summary>
    /// The F11 key.
    /// </summary>
    F11 = 100,

    /// <summary>
    /// The F12 key.
    /// </summary>
    F12 = 101,

    /// <summary>
    /// The F13 key.
    /// </summary>
    F13 = 102,

    /// <summary>
    /// The F14 key.
    /// </summary>
    F14 = 103,

    /// <summary>
    /// The F15 key.
    /// </summary>
    F15 = 104,

    /// <summary>
    /// The F16 key.
    /// </summary>
    F16 = 105,

    /// <summary>
    /// The F17 key.
    /// </summary>
    F17 = 106,

    /// <summary>
    /// The F18 key.
    /// </summary>
    F18 = 107,

    /// <summary>
    /// The F19 key.
    /// </summary>
    F19 = 108,

    /// <summary>
    /// The F20 key.
    /// </summary>
    F20 = 109,

    /// <summary>
    /// The F21 key.
    /// </summary>
    F21 = 110,

    /// <summary>
    /// The F22 key.
    /// </summary>
    F22 = 111,

    /// <summary>
    /// The F23 key.
    /// </summary>
    F23 = 112,

    /// <summary>
    /// The F24 key.
    /// </summary>
    F24 = 113,

    /// <summary>
    /// The Num Lock key.
    /// </summary>
    NumLock = 114,

    /// <summary>
    /// The Scroll Lock key.
    /// </summary>
    Scroll = 115,

    /// <summary>
    /// The left Shift key.
    /// </summary>
    LeftShift = 116,

    /// <summary>
    /// The right Shift key.
    /// </summary>
    RightShift = 117,

    /// <summary>
    /// The left Control key.
    /// </summary>
    LeftCtrl = 118,

    /// <summary>
    /// The right Control key.
    /// </summary>
    RightCtrl = 119,

    /// <summary>
    /// The left Alt key.
    /// </summary>
    LeftAlt = 120,

    /// <summary>
    /// The right Alt key (AltGr on some layouts).
    /// </summary>
    RightAlt = 121,

    /// <summary>
    /// The browser's Back key.
    /// </summary>
    BrowserBack = 122,

    /// <summary>
    /// The browser's Forward key.
    /// </summary>
    BrowserForward = 123,

    /// <summary>
    /// The browser's Refresh key.
    /// </summary>
    BrowserRefresh = 124,

    /// <summary>
    /// The browser's Stop key.
    /// </summary>
    BrowserStop = 125,

    /// <summary>
    /// The browser's Search key.
    /// </summary>
    BrowserSearch = 126,

    /// <summary>
    /// The browser's Favorites key.
    /// </summary>
    BrowserFavorites = 127,

    /// <summary>
    /// The browser's Home key.
    /// </summary>
    BrowserHome = 128,

    /// <summary>
    /// The Volume Mute key.
    /// </summary>
    VolumeMute = 129,

    /// <summary>
    /// The Volume Down key.
    /// </summary>
    VolumeDown = 130,

    /// <summary>
    /// The Volume Up key.
    /// </summary>
    VolumeUp = 131,

    /// <summary>
    /// The Next Track key.
    /// </summary>
    MediaNextTrack = 132,

    /// <summary>
    /// The Previous Track key.
    /// </summary>
    MediaPreviousTrack = 133,

    /// <summary>
    /// The Stop Media key.
    /// </summary>
    MediaStop = 134,

    /// <summary>
    /// The Play/Pause Media key.
    /// </summary>
    MediaPlayPause = 135,

    /// <summary>
    /// The Start Mail key.
    /// </summary>
    LaunchMail = 136,

    /// <summary>
    /// The Select Media key.
    /// </summary>
    SelectMedia = 137,

    /// <summary>
    /// The Start Application 1 key.
    /// </summary>
    LaunchApplication1 = 138,

    /// <summary>
    /// The Start Application 2 key.
    /// </summary>
    LaunchApplication2 = 139,

    /// <summary>
    /// The ;: key on a US keyboard (<c>VK_OEM_1</c>; it differs by layout).
    /// </summary>
    OemSemicolon = 140,

    /// <summary>
    /// The =+ key on the main keys (<c>VK_OEM_PLUS</c>).
    /// </summary>
    OemPlus = 141,

    /// <summary>
    /// The ,&lt; key (<c>VK_OEM_COMMA</c>).
    /// </summary>
    OemComma = 142,

    /// <summary>
    /// The -_ key on the main keys (<c>VK_OEM_MINUS</c>).
    /// </summary>
    OemMinus = 143,

    /// <summary>
    /// The .&gt; key (<c>VK_OEM_PERIOD</c>).
    /// </summary>
    OemPeriod = 144,

    /// <summary>
    /// The /? key on a US keyboard (<c>VK_OEM_2</c>; it differs by layout).
    /// </summary>
    OemQuestion = 145,

    /// <summary>
    /// The `~ key on a US keyboard (<c>VK_OEM_3</c>; it differs by layout).
    /// </summary>
    OemTilde = 146,

    /// <summary>
    /// The /? key of a Brazilian keyboard (<c>VK_ABNT_C1</c>, 0xC1, which the Windows SDK does not name).
    /// </summary>
    AbntC1 = 147,

    /// <summary>
    /// The . key of a Brazilian keyboard's number pad (<c>VK_ABNT_C2</c>, 0xC2).
    /// </summary>
    AbntC2 = 148,

    /// <summary>
    /// The [{ key on a US keyboard (<c>VK_OEM_4</c>; it differs by layout).
    /// </summary>
    OemOpenBrackets = 149,

    /// <summary>
    /// The \| key on a US keyboard (<c>VK_OEM_5</c>; it differs by layout).
    /// </summary>
    OemPipe = 150,

    /// <summary>
    /// The ]} key on a US keyboard (<c>VK_OEM_6</c>; it differs by layout).
    /// </summary>
    OemCloseBrackets = 151,

    /// <summary>
    /// The '" key on a US keyboard (<c>VK_OEM_7</c>; it differs by layout).
    /// </summary>
    OemQuotes = 152,

    /// <summary>
    /// A key that differs by layout (<c>VK_OEM_8</c>).
    /// </summary>
    Oem8 = 153,

    /// <summary>
    /// The key beside the left Shift on a 102-key keyboard (<c>VK_OEM_102</c>).
    /// </summary>
    OemBackslash = 154,

    /// <summary>
    /// An OEM key (<c>VK_OEM_ATTN</c>).
    /// </summary>
    OemAttn = 157,

    /// <summary>
    /// An OEM key (<c>VK_OEM_FINISH</c>).
    /// </summary>
    OemFinish = 158,

    /// <summary>
    /// An OEM key (<c>VK_OEM_COPY</c>).
    /// </summary>
    OemCopy = 159,

    /// <summary>
    /// An OEM key (<c>VK_OEM_AUTO</c>).
    /// </summary>
    OemAuto = 160,

    /// <summary>
    /// An OEM key (<c>VK_OEM_ENLW</c>).
    /// </summary>
    OemEnlw = 161,

    /// <summary>
    /// An OEM key (<c>VK_OEM_BACKTAB</c>).
    /// </summary>
    OemBackTab = 162,

    /// <summary>
    /// The Attn key.
    /// </summary>
    Attn = 163,

    /// <summary>
    /// The CrSel key.
    /// </summary>
    CrSel = 164,

    /// <summary>
    /// The ExSel key.
    /// </summary>
    ExSel = 165,

    /// <summary>
    /// The Erase EOF key.
    /// </summary>
    EraseEof = 166,

    /// <summary>
    /// The Play key.
    /// </summary>
    Play = 167,

    /// <summary>
    /// The Zoom key.
    /// </summary>
    Zoom = 168,

    /// <summary>
    /// A key reserved for the future (<c>VK_NONAME</c>).
    /// </summary>
    NoName = 169,

    /// <summary>
    /// The PA1 key.
    /// </summary>
    Pa1 = 170,

    /// <summary>
    /// The Clear key (<c>VK_OEM_CLEAR</c>).
    /// </summary>
    OemClear = 171,
}
