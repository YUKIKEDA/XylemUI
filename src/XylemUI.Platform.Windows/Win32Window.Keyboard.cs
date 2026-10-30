using System.Text;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Receives the keyboard's input of a window (design §7, "Keys and the focus").
/// </summary>
/// <param name="input">The input.</param>
/// <returns>Whether the key was handled; the window returns 0 for it and does not pass it to the OS.</returns>
internal delegate bool KeyInputHandler(in KeyInput input);

/// <summary>
/// Receives the text typed into a window (design §4.9, "Text input on Windows").
/// </summary>
/// <param name="input">The input.</param>
/// <returns>Whether a handler stopped the text.</returns>
internal delegate bool TextInputReceiver(in TextInput input);

/// <content>
/// The keyboard part of the window (design §7, "Keys and the focus"): turns the key messages into <see cref="KeyInput"/>, keeps the
/// window menu from opening on Alt, and reports the window's activation.
/// </content>
public sealed unsafe partial class Win32Window
{
    private const uint WmKeyDown = WM.WM_KEYDOWN;
    private const uint WmKeyUp = WM.WM_KEYUP;
    private const uint WmSysKeyDown = WM.WM_SYSKEYDOWN;
    private const uint WmSysKeyUp = WM.WM_SYSKEYUP;
    private const uint WmSysCommand = WM.WM_SYSCOMMAND;
    private const uint WmMenuChar = WM.WM_MENUCHAR;
    private const uint WmActivate = WM.WM_ACTIVATE;
    private const uint WmChar = WM.WM_CHAR;

    // The scan code of the right Shift, which Windows does not mark as extended (Avalonia's KeyInterop reads it with MapVirtualKey).
    private const uint RightShiftScanCode = 0x36;

    // ToUnicodeEx's flag that leaves the keyboard's state as it is (Windows 10 1607 and later), so that a dead key still combines.
    private const uint KeepKeyboardState = 1 << 2;

    /// <summary>
    /// Gets or sets what receives the window's keyboard input; <see langword="null"/> for nothing, and every key goes to the OS.
    /// </summary>
    internal KeyInputHandler? KeyInputReceived { get; set; }

    // Set when the message loop delivered a key down before TranslateMessage and no handler stopped it, so that the dispatch of the
    // same message passes it to DefWindowProc and does not deliver it again.
    private bool _keyDeliveredBeforeTranslate;

    // The high surrogate of a WM_CHAR whose low half has not come yet.
    private TypedText _characters;

    /// <summary>
    /// Gets or sets what receives the text typed into the window (<c>WM_CHAR</c>); <see langword="null"/> for nothing.
    /// </summary>
    internal TextInputReceiver? TextInputReceived { get; set; }

    /// <summary>
    /// Gets or sets what receives whether the window is active (<c>WM_ACTIVATE</c>); <see langword="null"/> for nothing.
    /// </summary>
    internal Action<bool>? ActivationChanged { get; set; }

    /// <summary>
    /// Gets a value indicating whether the window is the active window of its thread (<c>GetActiveWindow</c>): for a host that
    /// starts to listen to <see cref="ActivationChanged"/> after the window was shown, whose first <c>WM_ACTIVATE</c> it missed.
    /// </summary>
    internal bool IsActive => _hwnd != HWND.NULL && GetActiveWindow() == _hwnd;

    /// <summary>
    /// Delivers a key down of a XylemUI window before the message loop translates it (design §4.9, "Text input on Windows"; GPUI's
    /// <c>translate_accelerator</c>, <c>platform.rs</c> lines 501 to 515 and 604 to 610). A stopped key is neither translated nor
    /// dispatched, so no <c>WM_CHAR</c> comes from it; the others are, and their dispatch passes them to <c>DefWindowProc</c>.
    /// </summary>
    /// <param name="message">The message the loop took from the queue.</param>
    /// <returns>Whether a handler stopped the key: the loop then neither translates nor dispatches it.</returns>
    internal static bool DeliverKeyBeforeTranslate(MSG* message)
    {
        if (message->message is not (WmKeyDown or WmSysKeyDown))
        {
            return false;
        }

        try
        {
            nint id = GetWindowLongPtrW(message->hwnd, GWLP.GWLP_USERDATA);
            if (id == 0 || !NativeHandleTable.TryResolve(id, out Win32Window? window))
            {
                return false;
            }

            LRESULT result = 0;
            if (window.HandleKeyMessage(message->message, message->wParam, message->lParam, ref result))
            {
                return true;
            }

            window._keyDeliveredBeforeTranslate = true;
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return false;
    }

    /// <summary>
    /// Returns the input a key message reports, or <see langword="null"/> for a message that is not a key message and for the keys
    /// that go to the OS alone: the IME's <c>VK_PROCESSKEY</c> and the characters of <c>SendInput</c>'s <c>VK_PACKET</c>. The state the
    /// message does not carry comes in the arguments, so that a test gives it (design §7, "Keys and the focus").
    /// </summary>
    /// <param name="message">The message: <c>WM_KEYDOWN</c>, <c>WM_SYSKEYDOWN</c>, <c>WM_KEYUP</c>, or <c>WM_SYSKEYUP</c>.</param>
    /// <param name="wParam">The message's <c>wParam</c>: the virtual-key code.</param>
    /// <param name="lParam">The message's <c>lParam</c>: the scan code in bits 16 to 23, the extended key in bit 24, and whether the
    /// key was down before in bit 30.</param>
    /// <param name="modifiers">The modifier keys held down (<c>GetKeyState</c>).</param>
    /// <param name="keyChar">For a down, the character the key types (<see cref="TypedCharacter"/>); <see langword="default"/> for
    /// none.</param>
    /// <param name="timestamp">When the message was received.</param>
    /// <returns>The input.</returns>
    internal static KeyInput? DecodeKey(uint message, nuint wParam, nint lParam, KeyModifiers modifiers, Rune keyChar, TimeSpan timestamp)
    {
        KeyEventKind kind;
        switch (message)
        {
            case WmKeyDown or WmSysKeyDown:
                kind = KeyEventKind.Down;
                break;
            case WmKeyUp or WmSysKeyUp:
                kind = KeyEventKind.Up;
                break;
            default:
                return null;
        }

        uint virtualKey = (uint)wParam & 0xFFFF;
        if (virtualKey is VK.VK_PROCESSKEY or VK.VK_PACKET)
        {
            return null;
        }

        uint data = (uint)lParam;
        uint scanCode = (data >> 16) & 0xFF;
        bool extended = (data & (1u << 24)) != 0;
        bool repeat = kind == KeyEventKind.Down && (data & (1u << 30)) != 0;
        Key key = Win32Keys.FromVirtualKey(Side(virtualKey, scanCode, extended));
        PhysicalKey physical = Win32Keys.FromScanCode(extended ? scanCode | 0xE000 : scanCode);
        return new KeyInput(kind, key, physical, kind == KeyEventKind.Down ? keyChar : default, modifiers, repeat, timestamp);
    }

    /// <summary>
    /// Returns the character of what <c>ToUnicodeEx</c> wrote: <see langword="default"/> for a dead key (a negative count), for
    /// nothing written, for more than one character, and for a control character (U+0000 to U+001F and U+007F, Control+A's U+0001).
    /// </summary>
    /// <param name="written">What <c>ToUnicodeEx</c> returned.</param>
    /// <param name="buffer">The characters it wrote.</param>
    /// <returns>The character.</returns>
    internal static Rune TypedCharacter(int written, ReadOnlySpan<char> buffer)
    {
        if (written <= 0 || written > buffer.Length)
        {
            return default;
        }

        if (Rune.DecodeFromUtf16(buffer[..written], out Rune rune, out int used) != System.Buffers.OperationStatus.Done || used != written)
        {
            return default;
        }

        return rune.Value is < 0x20 or 0x7F ? default : rune;
    }

    /// <summary>
    /// Returns whether a key prefers to type its character over being a command (design §4.9, "Text input on Windows"; GPUI's
    /// <c>process_key</c>, <c>events.rs</c> lines 1641 to 1688): a dead key does, and a key that types a character with Control, Alt,
    /// or Windows held does when the character without them differs, as AltGr's does.
    /// </summary>
    /// <param name="written">What <c>ToUnicodeEx</c> returned with the keyboard's state.</param>
    /// <param name="buffer">The characters it wrote.</param>
    /// <param name="writtenWithout">What it returned with Control, Alt, and Windows taken off the state.</param>
    /// <param name="bufferWithout">The characters it wrote then.</param>
    /// <param name="modifiersHeld">Whether Control, Alt, or Windows is held.</param>
    /// <returns>Whether the key prefers its character.</returns>
    internal static bool PrefersCharacter(
        int written,
        ReadOnlySpan<char> buffer,
        int writtenWithout,
        ReadOnlySpan<char> bufferWithout,
        bool modifiersHeld
    )
    {
        if (written < 0)
        {
            return true;
        }

        if (TypedCharacter(written, buffer) == default || !modifiersHeld)
        {
            return false;
        }

        int count = Math.Min(written, buffer.Length);
        int countWithout = Math.Min(Math.Abs(writtenWithout), bufferWithout.Length);
        return written != writtenWithout || !buffer[..count].SequenceEqual(bufferWithout[..countWithout]);
    }

    // The virtual-key code that names the side of Shift, Control, and Alt: Shift by its scan code, the others by the extended bit,
    // which the right Control and Alt have (WM_KEYDOWN's documentation).
    private static uint Side(uint virtualKey, uint scanCode, bool extended) =>
        (uint)(
            virtualKey switch
            {
                VK.VK_SHIFT => scanCode == RightShiftScanCode ? VK.VK_RSHIFT : VK.VK_LSHIFT,
                VK.VK_CONTROL => extended ? VK.VK_RCONTROL : VK.VK_LCONTROL,
                VK.VK_MENU => extended ? VK.VK_RMENU : VK.VK_LMENU,
                _ => (int)virtualKey,
            }
        );

    private static bool IsDown(int virtualKey) => GetKeyState(virtualKey) < 0;

    private static bool IsMetaDown() => IsDown(VK.VK_LWIN) || IsDown(VK.VK_RWIN);

    // The modifiers held down: no key message carries them.
    private static KeyModifiers ReadModifiers() =>
        (IsDown(VK.VK_CONTROL) ? KeyModifiers.Control : 0)
        | (IsDown(VK.VK_SHIFT) ? KeyModifiers.Shift : 0)
        | (IsDown(VK.VK_MENU) ? KeyModifiers.Alt : 0)
        | (IsMetaDown() ? KeyModifiers.Meta : 0);

    // The keys PrefersCharacter takes off the keyboard's state.
    private static ReadOnlySpan<int> ModifierKeys =>
        [VK.VK_CONTROL, VK.VK_LCONTROL, VK.VK_RCONTROL, VK.VK_MENU, VK.VK_LMENU, VK.VK_RMENU, VK.VK_LWIN, VK.VK_RWIN];

    // The character a key types with the keyboard's state, and whether it prefers to type it (PrefersCharacter). Both calls of
    // ToUnicodeEx keep the keyboard's state: one that changed it would break a dead key's composition (GPUI's events.rs calls
    // ToUnicode with the same flag both times, lines 1613 to 1688).
    private static (Rune KeyChar, bool PrefersCharacter) ReadTypedCharacter(uint virtualKey, uint scanCode)
    {
        byte* state = stackalloc byte[256];
        if (GetKeyboardState(state) == BOOL.FALSE)
        {
            return (default, false);
        }

        HKL layout = GetKeyboardLayout(0);
        char* buffer = stackalloc char[8];
        int written = ToUnicodeEx(virtualKey, scanCode, state, buffer, 8, KeepKeyboardState, layout);
        bool held =
            (state[VK.VK_CONTROL] & 0x80) != 0
            || (state[VK.VK_MENU] & 0x80) != 0
            || (state[VK.VK_LWIN] & 0x80) != 0
            || (state[VK.VK_RWIN] & 0x80) != 0;
        int writtenWithout = 0;
        char* bufferWithout = stackalloc char[8];
        if (held && written > 0)
        {
            foreach (int key in ModifierKeys)
            {
                state[key] = 0;
            }

            writtenWithout = ToUnicodeEx(virtualKey, scanCode, state, bufferWithout, 8, KeepKeyboardState, layout);
        }

        var typed = new ReadOnlySpan<char>(buffer, 8);
        return (TypedCharacter(written, typed), PrefersCharacter(written, typed, writtenWithout, new ReadOnlySpan<char>(bufferWithout, 8), held));
    }

    // A key message's lParam with a scan code: one that has none (a key SendInput gave by its virtual-key code alone) takes the scan
    // code the layout maps the key to, as Avalonia's KeyInterop does (lines 475 to 491), and its extended bit, which such a message
    // does not set either (PR #356's review). MAPVK_VK_TO_VSC_EX gave VK_LEFT 0x4B with no 0xE0 on Windows 11 26300, so the bit also
    // comes from the keys that exist only as extended keys: the list of "About Keyboard Input" ("Extended-Key Flag") less the number
    // pad's Enter, whose VK_RETURN the main Enter shares, and Break (Control+Pause); and the Windows and Application keys, whose scan
    // codes are 0xE05B to 0xE05D.
    private static LPARAM WithScanCode(uint virtualKey, LPARAM lParam)
    {
        uint data = (uint)(nint)lParam;
        if (((data >> 16) & 0xFF) != 0)
        {
            return lParam;
        }

        uint mapped = MapVirtualKeyW(virtualKey, MAPVK_VK_TO_VSC_EX);
        if ((mapped & 0xFF) == 0)
        {
            return lParam;
        }

        data |= (mapped & 0xFF) << 16;
        if ((mapped & 0xFF00) == 0xE000 || IsExtendedOnly(virtualKey))
        {
            data |= 1u << 24;
        }

        return (nint)(int)data;
    }

    private static bool IsExtendedOnly(uint virtualKey) =>
        (int)virtualKey
            is VK.VK_INSERT
                or VK.VK_DELETE
                or VK.VK_HOME
                or VK.VK_END
                or VK.VK_PRIOR
                or VK.VK_NEXT
                or VK.VK_LEFT
                or VK.VK_UP
                or VK.VK_RIGHT
                or VK.VK_DOWN
                or VK.VK_NUMLOCK
                or VK.VK_SNAPSHOT
                or VK.VK_DIVIDE
                or VK.VK_RCONTROL
                or VK.VK_RMENU
                or VK.VK_LWIN
                or VK.VK_RWIN
                or VK.VK_APPS;

    // The keyboard's messages and the window's activation. Returns whether the message was handled, with the result it returns.
    private bool HandleKeyMessage(uint message, WPARAM wParam, LPARAM lParam, ref LRESULT result)
    {
        switch (message)
        {
            case WmKeyDown or WmSysKeyDown when _keyDeliveredBeforeTranslate:
                // The message loop delivered this down before TranslateMessage, and no handler stopped it (DeliverKeyBeforeTranslate).
                _keyDeliveredBeforeTranslate = false;
                return false;
            case WmChar:
                if (_characters.Next((char)(nuint)wParam) is { } text && TextInputReceived?.Invoke(new TextInput(text, Now())) == true)
                {
                    result = 0;
                    return true;
                }

                return false;
            case WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp:
                lParam = WithScanCode((uint)(nuint)wParam & 0xFFFF, lParam);
                if (DecodeKey(message, wParam, lParam, ReadModifiers(), default, Now()) is not { } input)
                {
                    return false;
                }

                if (input.Kind == KeyEventKind.Down)
                {
                    (Rune keyChar, bool prefers) = ReadTypedCharacter((uint)(nuint)wParam & 0xFFFF, ((uint)(nint)lParam >> 16) & 0xFF);
                    input = input with { KeyChar = keyChar, PrefersCharacter = prefers };
                }

                // A key a handler stopped returns 0, "if an application processes this message" (WM_KEYDOWN's documentation); the
                // others go to DefWindowProc, which closes the window on Alt+F4 (Avalonia's AppWndProc.cs lines 987 to 993).
                if (KeyInputReceived?.Invoke(in input) == true)
                {
                    result = 0;
                    return true;
                }

                return false;
            case WmSysCommand:
                // The window has no menu: Alt, F10, Alt and a character, and Alt+Space open none (design §7, "Alt and the menu key").
                // The low four bits are the system's. The menu a mouse opens (SC_MOUSEMENU) still opens.
                if (((uint)(nuint)wParam & 0xFFF0) == SC.SC_KEYMENU)
                {
                    result = 0;
                    return true;
                }

                return false;
            case WmMenuChar:
                // Alt and a character that no menu has: close, with no beep (Avalonia's AppWndProc.cs lines 245 to 249).
                result = MNC_CLOSE << 16;
                return true;
            case WmActivate:
                // The low word is WA_INACTIVE, WA_ACTIVE, or WA_CLICKACTIVE. DefWindowProc still runs: it gives the window the
                // keyboard's focus when it is not minimized.
                ActivationChanged?.Invoke(((uint)(nuint)wParam & 0xFFFF) != WA_INACTIVE);
                return false;
            default:
                return false;
        }
    }
}

/// <summary>
/// Turns the UTF-16 units of <c>WM_CHAR</c> into text (design §4.9, "Text input on Windows"): a surrogate pair comes as two messages
/// and is one text; a control character, a Tab included, is none. GPUI joins the pairs the same way (<c>events.rs</c> lines 1375 to
/// 1404).
/// </summary>
internal struct TypedText
{
    private char _high;

    /// <summary>
    /// Returns the text a unit completes.
    /// </summary>
    /// <param name="unit">The unit of one <c>WM_CHAR</c>.</param>
    /// <returns>The text; <see langword="null"/> for a high surrogate, which waits for its low half, a lone low surrogate, and a
    /// control character (below U+0020, and U+007F).</returns>
    public string? Next(char unit)
    {
        if (char.IsHighSurrogate(unit))
        {
            _high = unit;
            return null;
        }

        char high = _high;
        _high = default;
        if (char.IsLowSurrogate(unit))
        {
            return high == default ? null : new string([high, unit]);
        }

        return unit is < (char)0x20 or (char)0x7F ? null : unit.ToString();
    }
}
