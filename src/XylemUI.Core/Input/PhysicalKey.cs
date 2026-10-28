namespace XylemUI;

/// <summary>
/// Where a key is on the keyboard, whatever the layout (design §7, "Keys and the focus"): the names of the W3C UI Events
/// <c>code</c> values, which Avalonia's <c>PhysicalKey</c> follows (<c>Avalonia.Base/Input/PhysicalKey.cs</c>, without the
/// <c>Key</c> of the letters). Windows gives it from the scan code; the places are named after a US keyboard.
/// </summary>
public enum PhysicalKey : ushort
{
    /// <summary>
    /// No key, or a scan code that has no name here.
    /// </summary>
    None = 0,

    /// <summary>
    /// The `~ key on a US keyboard; 半角/全角 on a Japanese one.
    /// </summary>
    Backquote = 1,

    /// <summary>
    /// The \| key on a US keyboard.
    /// </summary>
    Backslash = 2,

    /// <summary>
    /// The [{ key on a US keyboard.
    /// </summary>
    BracketLeft = 3,

    /// <summary>
    /// The ]} key on a US keyboard.
    /// </summary>
    BracketRight = 4,

    /// <summary>
    /// The ,&lt; key on a US keyboard.
    /// </summary>
    Comma = 5,

    /// <summary>
    /// The 0 key on the main keys.
    /// </summary>
    Digit0 = 6,

    /// <summary>
    /// The 1 key on the main keys.
    /// </summary>
    Digit1 = 7,

    /// <summary>
    /// The 2 key on the main keys.
    /// </summary>
    Digit2 = 8,

    /// <summary>
    /// The 3 key on the main keys.
    /// </summary>
    Digit3 = 9,

    /// <summary>
    /// The 4 key on the main keys.
    /// </summary>
    Digit4 = 10,

    /// <summary>
    /// The 5 key on the main keys.
    /// </summary>
    Digit5 = 11,

    /// <summary>
    /// The 6 key on the main keys.
    /// </summary>
    Digit6 = 12,

    /// <summary>
    /// The 7 key on the main keys.
    /// </summary>
    Digit7 = 13,

    /// <summary>
    /// The 8 key on the main keys.
    /// </summary>
    Digit8 = 14,

    /// <summary>
    /// The 9 key on the main keys.
    /// </summary>
    Digit9 = 15,

    /// <summary>
    /// The =+ key on a US keyboard.
    /// </summary>
    Equal = 16,

    /// <summary>
    /// The key beside the left Shift on a 102-key keyboard.
    /// </summary>
    IntlBackslash = 17,

    /// <summary>
    /// The ろ key on a Japanese keyboard.
    /// </summary>
    IntlRo = 18,

    /// <summary>
    /// The ¥ key on a Japanese keyboard.
    /// </summary>
    IntlYen = 19,

    /// <summary>
    /// The key at the place of A on a US keyboard.
    /// </summary>
    KeyA = 20,

    /// <summary>
    /// The key at the place of B on a US keyboard.
    /// </summary>
    KeyB = 21,

    /// <summary>
    /// The key at the place of C on a US keyboard.
    /// </summary>
    KeyC = 22,

    /// <summary>
    /// The key at the place of D on a US keyboard.
    /// </summary>
    KeyD = 23,

    /// <summary>
    /// The key at the place of E on a US keyboard.
    /// </summary>
    KeyE = 24,

    /// <summary>
    /// The key at the place of F on a US keyboard.
    /// </summary>
    KeyF = 25,

    /// <summary>
    /// The key at the place of G on a US keyboard.
    /// </summary>
    KeyG = 26,

    /// <summary>
    /// The key at the place of H on a US keyboard.
    /// </summary>
    KeyH = 27,

    /// <summary>
    /// The key at the place of I on a US keyboard.
    /// </summary>
    KeyI = 28,

    /// <summary>
    /// The key at the place of J on a US keyboard.
    /// </summary>
    KeyJ = 29,

    /// <summary>
    /// The key at the place of K on a US keyboard.
    /// </summary>
    KeyK = 30,

    /// <summary>
    /// The key at the place of L on a US keyboard.
    /// </summary>
    KeyL = 31,

    /// <summary>
    /// The key at the place of M on a US keyboard.
    /// </summary>
    KeyM = 32,

    /// <summary>
    /// The key at the place of N on a US keyboard.
    /// </summary>
    KeyN = 33,

    /// <summary>
    /// The key at the place of O on a US keyboard.
    /// </summary>
    KeyO = 34,

    /// <summary>
    /// The key at the place of P on a US keyboard.
    /// </summary>
    KeyP = 35,

    /// <summary>
    /// The key at the place of Q on a US keyboard.
    /// </summary>
    KeyQ = 36,

    /// <summary>
    /// The key at the place of R on a US keyboard.
    /// </summary>
    KeyR = 37,

    /// <summary>
    /// The key at the place of S on a US keyboard.
    /// </summary>
    KeyS = 38,

    /// <summary>
    /// The key at the place of T on a US keyboard.
    /// </summary>
    KeyT = 39,

    /// <summary>
    /// The key at the place of U on a US keyboard.
    /// </summary>
    KeyU = 40,

    /// <summary>
    /// The key at the place of V on a US keyboard.
    /// </summary>
    KeyV = 41,

    /// <summary>
    /// The key at the place of W on a US keyboard.
    /// </summary>
    KeyW = 42,

    /// <summary>
    /// The key at the place of X on a US keyboard.
    /// </summary>
    KeyX = 43,

    /// <summary>
    /// The key at the place of Y on a US keyboard.
    /// </summary>
    KeyY = 44,

    /// <summary>
    /// The key at the place of Z on a US keyboard.
    /// </summary>
    KeyZ = 45,

    /// <summary>
    /// The -_ key on a US keyboard.
    /// </summary>
    Minus = 46,

    /// <summary>
    /// The .&gt; key on a US keyboard.
    /// </summary>
    Period = 47,

    /// <summary>
    /// The '" key on a US keyboard.
    /// </summary>
    Quote = 48,

    /// <summary>
    /// The ;: key on a US keyboard.
    /// </summary>
    Semicolon = 49,

    /// <summary>
    /// The /? key on a US keyboard.
    /// </summary>
    Slash = 50,

    /// <summary>
    /// The left Alt key.
    /// </summary>
    AltLeft = 51,

    /// <summary>
    /// The right Alt key (AltGr on some layouts).
    /// </summary>
    AltRight = 52,

    /// <summary>
    /// The Backspace key.
    /// </summary>
    Backspace = 53,

    /// <summary>
    /// The Caps Lock key.
    /// </summary>
    CapsLock = 54,

    /// <summary>
    /// The Application (context menu) key.
    /// </summary>
    ContextMenu = 55,

    /// <summary>
    /// The left Control key.
    /// </summary>
    ControlLeft = 56,

    /// <summary>
    /// The right Control key.
    /// </summary>
    ControlRight = 57,

    /// <summary>
    /// The Enter key on the main keys.
    /// </summary>
    Enter = 58,

    /// <summary>
    /// The left Windows key.
    /// </summary>
    MetaLeft = 59,

    /// <summary>
    /// The right Windows key.
    /// </summary>
    MetaRight = 60,

    /// <summary>
    /// The left Shift key.
    /// </summary>
    ShiftLeft = 61,

    /// <summary>
    /// The right Shift key.
    /// </summary>
    ShiftRight = 62,

    /// <summary>
    /// The space bar.
    /// </summary>
    Space = 63,

    /// <summary>
    /// The Tab key.
    /// </summary>
    Tab = 64,

    /// <summary>
    /// The 変換 key on a Japanese keyboard.
    /// </summary>
    Convert = 65,

    /// <summary>
    /// The カタカナ/ひらがな key on a Japanese keyboard.
    /// </summary>
    KanaMode = 66,

    /// <summary>
    /// The 한/영 key on a Korean keyboard.
    /// </summary>
    Lang1 = 67,

    /// <summary>
    /// The 한자 key on a Korean keyboard.
    /// </summary>
    Lang2 = 68,

    /// <summary>
    /// The カタカナ key on a Japanese keyboard.
    /// </summary>
    Lang3 = 69,

    /// <summary>
    /// The ひらがな key on a Japanese keyboard.
    /// </summary>
    Lang4 = 70,

    /// <summary>
    /// The 無変換 key on a Japanese keyboard.
    /// </summary>
    NonConvert = 71,

    /// <summary>
    /// The Delete key.
    /// </summary>
    Delete = 72,

    /// <summary>
    /// The End key.
    /// </summary>
    End = 73,

    /// <summary>
    /// The Help key.
    /// </summary>
    Help = 74,

    /// <summary>
    /// The Home key.
    /// </summary>
    Home = 75,

    /// <summary>
    /// The Insert key.
    /// </summary>
    Insert = 76,

    /// <summary>
    /// The Page Down key.
    /// </summary>
    PageDown = 77,

    /// <summary>
    /// The Page Up key.
    /// </summary>
    PageUp = 78,

    /// <summary>
    /// The down arrow.
    /// </summary>
    ArrowDown = 79,

    /// <summary>
    /// The left arrow.
    /// </summary>
    ArrowLeft = 80,

    /// <summary>
    /// The right arrow.
    /// </summary>
    ArrowRight = 81,

    /// <summary>
    /// The up arrow.
    /// </summary>
    ArrowUp = 82,

    /// <summary>
    /// The Num Lock key.
    /// </summary>
    NumLock = 83,

    /// <summary>
    /// The 0 key on the number pad.
    /// </summary>
    Numpad0 = 84,

    /// <summary>
    /// The 1 key on the number pad.
    /// </summary>
    Numpad1 = 85,

    /// <summary>
    /// The 2 key on the number pad.
    /// </summary>
    Numpad2 = 86,

    /// <summary>
    /// The 3 key on the number pad.
    /// </summary>
    Numpad3 = 87,

    /// <summary>
    /// The 4 key on the number pad.
    /// </summary>
    Numpad4 = 88,

    /// <summary>
    /// The 5 key on the number pad.
    /// </summary>
    Numpad5 = 89,

    /// <summary>
    /// The 6 key on the number pad.
    /// </summary>
    Numpad6 = 90,

    /// <summary>
    /// The 7 key on the number pad.
    /// </summary>
    Numpad7 = 91,

    /// <summary>
    /// The 8 key on the number pad.
    /// </summary>
    Numpad8 = 92,

    /// <summary>
    /// The 9 key on the number pad.
    /// </summary>
    Numpad9 = 93,

    /// <summary>
    /// The + key on the number pad.
    /// </summary>
    NumpadAdd = 94,

    /// <summary>
    /// The , key on a Brazilian keyboard's number pad.
    /// </summary>
    NumpadComma = 95,

    /// <summary>
    /// The decimal key on the number pad.
    /// </summary>
    NumpadDecimal = 96,

    /// <summary>
    /// The / key on the number pad.
    /// </summary>
    NumpadDivide = 97,

    /// <summary>
    /// The Enter key on the number pad.
    /// </summary>
    NumpadEnter = 98,

    /// <summary>
    /// The = key on the number pad.
    /// </summary>
    NumpadEqual = 99,

    /// <summary>
    /// The * key on the number pad.
    /// </summary>
    NumpadMultiply = 100,

    /// <summary>
    /// The - key on the number pad.
    /// </summary>
    NumpadSubtract = 101,

    /// <summary>
    /// The Esc key.
    /// </summary>
    Escape = 102,

    /// <summary>
    /// The F1 key.
    /// </summary>
    F1 = 103,

    /// <summary>
    /// The F2 key.
    /// </summary>
    F2 = 104,

    /// <summary>
    /// The F3 key.
    /// </summary>
    F3 = 105,

    /// <summary>
    /// The F4 key.
    /// </summary>
    F4 = 106,

    /// <summary>
    /// The F5 key.
    /// </summary>
    F5 = 107,

    /// <summary>
    /// The F6 key.
    /// </summary>
    F6 = 108,

    /// <summary>
    /// The F7 key.
    /// </summary>
    F7 = 109,

    /// <summary>
    /// The F8 key.
    /// </summary>
    F8 = 110,

    /// <summary>
    /// The F9 key.
    /// </summary>
    F9 = 111,

    /// <summary>
    /// The F10 key.
    /// </summary>
    F10 = 112,

    /// <summary>
    /// The F11 key.
    /// </summary>
    F11 = 113,

    /// <summary>
    /// The F12 key.
    /// </summary>
    F12 = 114,

    /// <summary>
    /// The F13 key.
    /// </summary>
    F13 = 115,

    /// <summary>
    /// The F14 key.
    /// </summary>
    F14 = 116,

    /// <summary>
    /// The F15 key.
    /// </summary>
    F15 = 117,

    /// <summary>
    /// The F16 key.
    /// </summary>
    F16 = 118,

    /// <summary>
    /// The F17 key.
    /// </summary>
    F17 = 119,

    /// <summary>
    /// The F18 key.
    /// </summary>
    F18 = 120,

    /// <summary>
    /// The F19 key.
    /// </summary>
    F19 = 121,

    /// <summary>
    /// The F20 key.
    /// </summary>
    F20 = 122,

    /// <summary>
    /// The F21 key.
    /// </summary>
    F21 = 123,

    /// <summary>
    /// The F22 key.
    /// </summary>
    F22 = 124,

    /// <summary>
    /// The F23 key.
    /// </summary>
    F23 = 125,

    /// <summary>
    /// The F24 key.
    /// </summary>
    F24 = 126,

    /// <summary>
    /// The Print Screen key.
    /// </summary>
    PrintScreen = 127,

    /// <summary>
    /// The Scroll Lock key.
    /// </summary>
    ScrollLock = 128,

    /// <summary>
    /// The Pause key.
    /// </summary>
    Pause = 129,

    /// <summary>
    /// The browser's Back key.
    /// </summary>
    BrowserBack = 130,

    /// <summary>
    /// The browser's Favorites key.
    /// </summary>
    BrowserFavorites = 131,

    /// <summary>
    /// The browser's Forward key.
    /// </summary>
    BrowserForward = 132,

    /// <summary>
    /// The browser's Home key.
    /// </summary>
    BrowserHome = 133,

    /// <summary>
    /// The browser's Refresh key.
    /// </summary>
    BrowserRefresh = 134,

    /// <summary>
    /// The browser's Search key.
    /// </summary>
    BrowserSearch = 135,

    /// <summary>
    /// The browser's Stop key.
    /// </summary>
    BrowserStop = 136,

    /// <summary>
    /// The Eject key.
    /// </summary>
    Eject = 137,

    /// <summary>
    /// The Start Application 1 key.
    /// </summary>
    LaunchApp1 = 138,

    /// <summary>
    /// The Start Application 2 key.
    /// </summary>
    LaunchApp2 = 139,

    /// <summary>
    /// The Start Mail key.
    /// </summary>
    LaunchMail = 140,

    /// <summary>
    /// The Play/Pause Media key.
    /// </summary>
    MediaPlayPause = 141,

    /// <summary>
    /// The Select Media key.
    /// </summary>
    MediaSelect = 142,

    /// <summary>
    /// The Stop Media key.
    /// </summary>
    MediaStop = 143,

    /// <summary>
    /// The Next Track key.
    /// </summary>
    MediaTrackNext = 144,

    /// <summary>
    /// The Previous Track key.
    /// </summary>
    MediaTrackPrevious = 145,

    /// <summary>
    /// The Power key.
    /// </summary>
    Power = 146,

    /// <summary>
    /// The Sleep key.
    /// </summary>
    Sleep = 147,

    /// <summary>
    /// The Volume Down key.
    /// </summary>
    AudioVolumeDown = 148,

    /// <summary>
    /// The Volume Mute key.
    /// </summary>
    AudioVolumeMute = 149,

    /// <summary>
    /// The Volume Up key.
    /// </summary>
    AudioVolumeUp = 150,

    /// <summary>
    /// The Wake Up key.
    /// </summary>
    WakeUp = 151,

    /// <summary>
    /// The Copy key.
    /// </summary>
    Copy = 152,

    /// <summary>
    /// The Cut key.
    /// </summary>
    Cut = 153,

    /// <summary>
    /// The Paste key.
    /// </summary>
    Paste = 154,

    /// <summary>
    /// The Undo key.
    /// </summary>
    Undo = 155,
}
