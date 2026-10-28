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

    // The scan code of the right Shift, which Windows does not mark as extended (Avalonia's KeyInterop reads it with MapVirtualKey).
    private const uint RightShiftScanCode = 0x36;

    // ToUnicodeEx's flag that leaves the keyboard's state as it is (Windows 10 1607 and later), so that a dead key still combines.
    private const uint KeepKeyboardState = 1 << 2;

    /// <summary>
    /// Gets or sets what receives the window's keyboard input; <see langword="null"/> for nothing, and every key goes to the OS.
    /// </summary>
    internal KeyInputHandler? KeyInputReceived { get; set; }

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

    // The character a key types with the keyboard's state, without changing the state (GPUI's events.rs calls ToUnicode with the same
    // flag).
    private static Rune ReadTypedCharacter(uint virtualKey, uint scanCode)
    {
        byte* state = stackalloc byte[256];
        if (GetKeyboardState(state) == BOOL.FALSE)
        {
            return default;
        }

        char* buffer = stackalloc char[8];
        int written = ToUnicodeEx(virtualKey, scanCode, state, buffer, 8, KeepKeyboardState, GetKeyboardLayout(0));
        return TypedCharacter(written, new ReadOnlySpan<char>(buffer, 8));
    }

    // The keyboard's messages and the window's activation. Returns whether the message was handled, with the result it returns.
    private bool HandleKeyMessage(uint message, WPARAM wParam, LPARAM lParam, ref LRESULT result)
    {
        switch (message)
        {
            case WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp:
                if (DecodeKey(message, wParam, lParam, ReadModifiers(), default, Now()) is not { } input)
                {
                    return false;
                }

                if (input.Kind == KeyEventKind.Down)
                {
                    input = input with { KeyChar = ReadTypedCharacter((uint)(nuint)wParam & 0xFFFF, ((uint)(nint)lParam >> 16) & 0xFF) };
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
