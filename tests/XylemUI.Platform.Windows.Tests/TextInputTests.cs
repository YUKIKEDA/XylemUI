using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The text typed into a window (row P02b, design §4.9, "Text input on Windows"): a key down goes to the window before the message loop
// translates it, so a stopped key makes no WM_CHAR; WM_CHAR becomes text, a surrogate pair joined and a control character dropped;
// and a key that prefers its character, as AltGr's does. The keyboard's state and layout cannot be made with SendMessage, so the
// comparison of PrefersCharacter takes ToUnicodeEx's results as arguments and is tested apart.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class TextInputTests(UiThreadFixture ui)
{
    /// <summary>
    /// The units of <c>WM_CHAR</c> become text: a surrogate pair is one text when its low half comes; a lone low half, a control
    /// character, a Tab, and DEL are none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None: the decoder is a function of the units it is given.
    ///
    /// Steps:
    /// - Give 'a'; the high and the low half of U+1F600; a lone low half; U+0009 (Tab); U+0001; U+007F; 'é'.
    ///
    /// Expected:
    /// - "a"; nothing, then "😀"; nothing for the next four; "é".
    /// </remarks>
    [Fact]
    public void TypedText_Units_JoinSurrogatePairsAndDropControlCharacters()
    {
        var typed = default(TypedText);
        string emoji = char.ConvertFromUtf32(0x1F600);

        string?[] texts =
        [
            typed.Next('a'),
            typed.Next(emoji[0]),
            typed.Next(emoji[1]),
            typed.Next(emoji[1]),
            typed.Next('\t'),
            typed.Next((char)0x01),
            typed.Next((char)0x7F),
            typed.Next('é'),
        ];

        Assert.Equal(["a", null, emoji, null, null, null, null, "é"], texts);
    }

    /// <summary>
    /// A key prefers its character when it is a dead key, or when it types a character with Control, Alt, or Windows held that
    /// differs from the one without them (AltGr); not with no such modifier, not when the character is the same, and not when it
    /// types none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None: the comparison is a function of <c>ToUnicodeEx</c>'s results.
    ///
    /// Steps:
    /// - Compare: a dead key (-1); 'ą' with Control and Alt against 'a' without (Polish AltGr+A); 'a' with Shift only; 'a' with
    ///   Control against 'a' without; Control+A's U+0001 against 'a'.
    ///
    /// Expected:
    /// - True, true, false, false, false.
    /// </remarks>
    [Fact]
    public void PrefersCharacter_ComparesTheCharacterWithoutTheModifiers()
    {
        static bool Prefers(int written, string with, int writtenWithout, string without, bool held) =>
            Win32Window.PrefersCharacter(written, with.AsSpan(), writtenWithout, without.AsSpan(), held);

        bool[] results =
        [
            Prefers(-1, "´", 0, string.Empty, held: false),
            Prefers(1, "ą", 1, "a", held: true),
            Prefers(1, "a", 0, string.Empty, held: false),
            Prefers(1, "a", 1, "a", held: true),
            Prefers(1, "\u0001", 1, "a", held: true),
        ];

        Assert.Equal([true, true, false, false, false], results);
    }

    /// <summary>
    /// The message loop delivers a key down before it translates it: a stopped down makes no <c>WM_CHAR</c>, and a down no handler
    /// stopped is translated once and types its character, delivered once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose key handler logs each down and stops it while a flag is set, and whose text handler logs each text.
    ///
    /// Steps:
    /// - With the flag set, post a <c>WM_KEYDOWN</c> of A (scan code 0x1E) and run the message loop for a few iterations. Clear the
    ///   flag, post it again, and run the loop again.
    ///
    /// Expected:
    /// - The first run logs one down and no text. The second logs one down and one text, the character the down reported: what A
    ///   types depends on the machine's layout and Caps Lock, so the test compares with the key's own character.
    /// </remarks>
    [Fact]
    public void KeyDown_DeliveredBeforeTranslate_StoppedMakesNoCharacter() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("text input", 200, 100);
            bool stop = true;
            var downs = 0;
            var texts = new List<string>();
            string typed = string.Empty;
            window.KeyInputReceived = (in KeyInput input) =>
            {
                if (input.Kind == KeyEventKind.Down)
                {
                    downs++;
                    typed = input.KeyChar.ToString();
                }

                return stop;
            };
            window.TextInputReceived = (in TextInput input) =>
            {
                texts.Add(input.Text);
                return true;
            };

            Run(window);
            Assert.Equal((1, 0), (downs, texts.Count));
            stop = false;
            downs = 0;
            Run(window);
            Assert.Equal(1, downs);
            Assert.Equal([typed], texts);
        });

    /// <summary>
    /// <c>WM_CHAR</c> messages become text for the window's handler: the two halves of a surrogate pair as one text, and a control
    /// character as none; a text the handler stops returns 0.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose text handler logs each text and stops it.
    ///
    /// Steps:
    /// - Send <c>WM_CHAR</c> of the high half of U+1F600, of its low half, and of U+0009.
    ///
    /// Expected:
    /// - One text, the emoji; the low half's message returns 0.
    /// </remarks>
    [Fact]
    public void WmChar_SurrogatePairAndTab_GiveOneTextAndNone() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("characters", 200, 100);
            var texts = new List<string>();
            window.TextInputReceived = (in TextInput input) =>
            {
                texts.Add(input.Text);
                return true;
            };
            string emoji = char.ConvertFromUtf32(0x1F600);

            _ = SendMessageW(window.Hwnd, WM.WM_CHAR, emoji[0], 1);
            nint low = SendMessageW(window.Hwnd, WM.WM_CHAR, emoji[1], 1);
            _ = SendMessageW(window.Hwnd, WM.WM_CHAR, '\t', 1);

            Assert.Equal([emoji], texts);
            Assert.Equal(0, low);
        });

    // Posts a down of A and runs the message loop for a few iterations, so that the down and any WM_CHAR it makes are dispatched.
    private static void Run(Win32Window window)
    {
        using var watchdog = new LoopWatchdog();
        using var ticker = new WakeTicker(TimeSpan.FromMilliseconds(5));
        int iterations = 0;
        LoopTestHelpers.RunLoop(watchdog, () => ++iterations > 3, () => _ = PostMessageW(window.Hwnd, WM.WM_KEYDOWN, 'A', (nint)((0x1Eu << 16) | 1)));
        Assert.False(watchdog.Expired);
    }
}
