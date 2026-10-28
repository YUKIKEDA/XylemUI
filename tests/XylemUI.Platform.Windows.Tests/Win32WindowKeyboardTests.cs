using System.Text;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The keyboard part of the window (row K1a, design §7, "Keys and the focus"): the key messages as KeyInput, the result a handled key
// returns, the window menu that Alt does not open, and the activation. The keyboard's state and layout cannot be made with
// SendMessage, so the decoding takes them as arguments and is tested apart.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class Win32WindowKeyboardTests(UiThreadFixture ui)
{
    /// <summary>
    /// A key message decodes to its kind, the key of its virtual-key code with the side of Shift, Control, and Alt, the physical key
    /// of its scan code and extended bit, the repeat of bit 30 for a down only, and the modifiers and character given; the IME's and
    /// SendInput's keys and the messages that are no key message give none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None: the decoding is a function of the message's values and the state given.
    ///
    /// Steps:
    /// - Decode: a repeated down of A (scan 0x1E) with Shift and the character 'A'; an up of A with bit 30 and a character given;
    ///   a system down of Alt with the extended bit; downs of Shift with the scan codes 0x2A and 0x36; a down of Control with the
    ///   extended bit; a down of Enter with the extended bit; downs of VK_PROCESSKEY and VK_PACKET; a WM_CHAR.
    ///
    /// Expected:
    /// - A / KeyA / Shift / repeat / 'A'; the up with no repeat and no character; RightAlt / AltRight; LeftShift / ShiftLeft and
    ///   RightShift / ShiftRight; RightCtrl / ControlRight; Enter / NumpadEnter; none for the last three.
    /// </remarks>
    [Fact]
    public void DecodeKey_KeyMessages_GiveTheirInput()
    {
        TimeSpan at = TimeSpan.FromSeconds(2);
        const nint Repeat = 1 << 30;
        const nint Extended = 1 << 24;
        static nint Scan(uint code) => (nint)(code << 16);

        KeyInput? down = Win32Window.DecodeKey(WM.WM_KEYDOWN, 'A', Scan(0x1E) | Repeat, KeyModifiers.Shift, new Rune('A'), at);
        KeyInput? up = Win32Window.DecodeKey(WM.WM_KEYUP, 'A', Scan(0x1E) | Repeat, KeyModifiers.None, new Rune('a'), at);

        Assert.Equal(new KeyInput(KeyEventKind.Down, Key.A, PhysicalKey.KeyA, new Rune('A'), KeyModifiers.Shift, true, at), down);
        Assert.Equal(new KeyInput(KeyEventKind.Up, Key.A, PhysicalKey.KeyA, default, KeyModifiers.None, false, at), up);
        Assert.Equal((Key.RightAlt, PhysicalKey.AltRight), Keys(WM.WM_SYSKEYDOWN, VK.VK_MENU, Scan(0x38) | Extended));
        Assert.Equal((Key.LeftShift, PhysicalKey.ShiftLeft), Keys(WM.WM_KEYDOWN, VK.VK_SHIFT, Scan(0x2A)));
        Assert.Equal((Key.RightShift, PhysicalKey.ShiftRight), Keys(WM.WM_KEYDOWN, VK.VK_SHIFT, Scan(0x36)));
        Assert.Equal((Key.RightCtrl, PhysicalKey.ControlRight), Keys(WM.WM_KEYDOWN, VK.VK_CONTROL, Scan(0x1D) | Extended));
        Assert.Equal((Key.Enter, PhysicalKey.NumpadEnter), Keys(WM.WM_KEYDOWN, VK.VK_RETURN, Scan(0x1C) | Extended));
        Assert.Null(Win32Window.DecodeKey(WM.WM_KEYDOWN, VK.VK_PROCESSKEY, Scan(0x1E), KeyModifiers.None, default, at));
        Assert.Null(Win32Window.DecodeKey(WM.WM_KEYDOWN, VK.VK_PACKET, 0, KeyModifiers.None, default, at));
        Assert.Null(Win32Window.DecodeKey(WM.WM_CHAR, 'a', Scan(0x1E), KeyModifiers.None, default, at));

        static (Key, PhysicalKey) Keys(uint message, int virtualKey, nint lParam) =>
            Win32Window.DecodeKey(message, (nuint)virtualKey, lParam, KeyModifiers.None, default, TimeSpan.Zero) is { } input
                ? (input.Key, input.PhysicalKey)
                : default;
    }

    /// <summary>
    /// The character of what <c>ToUnicodeEx</c> wrote is one character or one surrogate pair that is no control character; a dead
    /// key, nothing, two characters, and a control character give none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Read: 1 of "a"; 2 of the surrogate pair of U+1F600; −1 of "^" (a dead key); 0; 2 of "ab"; 1 of U+0001 (Control+A); 1 of U+007F.
    ///
    /// Expected:
    /// - 'a'; U+1F600; then none for each of the others.
    /// </remarks>
    [Fact]
    public void TypedCharacter_OneCharacterThatIsNoControl_IsTheCharacter()
    {
        Assert.Equal(new Rune('a'), Win32Window.TypedCharacter(1, "a"));
        Assert.Equal(new Rune(0x1F600), Win32Window.TypedCharacter(2, "😀"));
        Assert.Equal(default, Win32Window.TypedCharacter(-1, "^"));
        Assert.Equal(default, Win32Window.TypedCharacter(0, "a"));
        Assert.Equal(default, Win32Window.TypedCharacter(2, "ab"));
        Assert.Equal(default, Win32Window.TypedCharacter(1, "\u0001"));
        Assert.Equal(default, Win32Window.TypedCharacter(1, "\u007F"));
    }

    /// <summary>
    /// The window delivers its four key messages as inputs, and a key that the input handles returns 0, "if an application processes
    /// this message" (WM_KEYDOWN's documentation). What <c>DefWindowProc</c> does with a key the input does not handle (Alt+F4 closes
    /// the window) reads the keyboard's state, which <c>SendMessage</c> cannot make, so it is not tested here.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose input records each key and handles every key.
    ///
    /// Steps:
    /// - Send WM_KEYDOWN of A, WM_KEYUP of A, WM_SYSKEYDOWN of F4 with the context bit (Alt), and WM_SYSKEYUP of F4; dispatch the
    ///   messages they posted to the window.
    ///
    /// Expected:
    /// - Each returns 0; the inputs are a down and an up of A and of F4; the window is alive.
    /// </remarks>
    [Fact]
    public void KeyMessage_Handled_ReturnsZero() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("keys", 200, 100);
            var keys = new List<(KeyEventKind, Key)>();
            window.KeyInputReceived = (in KeyInput input) =>
            {
                keys.Add((input.Kind, input.Key));
                return true;
            };
            nint a = (nint)((0x1Eu << 16) | 1);
            nint altF4 = (nint)((1u << 29) | (0x3Eu << 16) | 1);

            var results = new List<nint>
            {
                SendMessageW(window.Hwnd, WM.WM_KEYDOWN, 'A', a),
                SendMessageW(window.Hwnd, WM.WM_KEYUP, 'A', a | unchecked((nint)0xC0000000)),
                SendMessageW(window.Hwnd, WM.WM_SYSKEYDOWN, VK.VK_F4, altF4),
                SendMessageW(window.Hwnd, WM.WM_SYSKEYUP, VK.VK_F4, altF4 | unchecked((nint)0xC0000000)),
            };
            Pump(window);

            Assert.Equal([0, 0, 0, 0], results);
            Assert.Equal([(KeyEventKind.Down, Key.A), (KeyEventKind.Up, Key.A), (KeyEventKind.Down, Key.F4), (KeyEventKind.Up, Key.F4)], keys);
            Assert.True(window.IsAlive);
        });

    /// <summary>
    /// The window menu does not open from the keyboard: <c>SC_KEYMENU</c> returns 0 and enters no menu loop, and <c>WM_MENUCHAR</c>
    /// closes with no beep.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose message hook records the messages it receives.
    ///
    /// Steps:
    /// - Send WM_SYSCOMMAND of SC_KEYMENU with a system bit in the low four bits and the character of Space; send WM_MENUCHAR.
    ///
    /// Expected:
    /// - 0 for the first and no WM_ENTERMENULOOP or WM_INITMENU recorded; MNC_CLOSE in the high word for the second.
    /// </remarks>
    [Fact]
    public void KeyMenu_DoesNotOpen_AndMenuCharCloses() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("menu", 200, 100);
            var messages = new List<uint>();
            window.MessageHook = (_, message) =>
            {
                messages.Add(message);
                return false;
            };

            LRESULT keyMenu = SendMessageW(window.Hwnd, WM.WM_SYSCOMMAND, SC.SC_KEYMENU | 0x2, ' ');
            LRESULT menuChar = SendMessageW(window.Hwnd, WM.WM_MENUCHAR, 'q', 0);

            Assert.Equal(0, (nint)keyMenu);
            Assert.DoesNotContain((uint)WM.WM_ENTERMENULOOP, messages);
            Assert.DoesNotContain((uint)WM.WM_INITMENU, messages);
            Assert.Equal(MNC_CLOSE << 16, (nint)menuChar);
        });

    /// <summary>
    /// WM_ACTIVATE reports whether the window is active: WA_ACTIVE and WA_CLICKACTIVE are active, WA_INACTIVE is not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window that records its activation.
    ///
    /// Steps:
    /// - Send WM_ACTIVATE with WA_ACTIVE, WA_INACTIVE, and WA_CLICKACTIVE; read the last value reported after each. DefWindowProc
    ///   gives the window the keyboard's focus on WA_ACTIVE, and Windows then sends a WM_ACTIVATE of its own.
    ///
    /// Expected:
    /// - True, false, true.
    /// </remarks>
    [Fact]
    public void Activate_ReportsWhetherTheWindowIsActive() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("activate", 200, 100);
            var active = new List<bool>();
            window.ActivationChanged = active.Add;

            var after = new List<bool>();
            foreach (nuint state in (nuint[])[WA_ACTIVE, WA_INACTIVE, WA_CLICKACTIVE])
            {
                _ = SendMessageW(window.Hwnd, WM.WM_ACTIVATE, state, 0);
                after.Add(active[^1]);
            }

            Assert.Equal([true, false, true], after);
        });

    // Dispatches the messages posted to the window, such as the SC_CLOSE that DefWindowProc posts for Alt+F4.
    private static void Pump(Win32Window window)
    {
        MSG message;
        while (window.IsAlive && PeekMessageW(&message, window.Hwnd, 0, 0, PM.PM_REMOVE) != BOOL.FALSE)
        {
            _ = DispatchMessageW(&message);
        }
    }
}
