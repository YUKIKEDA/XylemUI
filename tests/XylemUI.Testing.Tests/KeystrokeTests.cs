using System.Text;

namespace XylemUI.Testing.Tests;

// The text form of a key binding's step (design §7, "Shortcuts and actions", row KM1b): what is read, what a step matches, and
// what a menu shows for it.
public sealed class KeystrokeTests
{
    /// <summary>
    /// A step's text gives the logical key and the modifiers, whatever the case and the order of the modifiers.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse each text.
    ///
    /// Expected:
    /// - The key and the modifiers of the row; the step is not a character step.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <param name="key">The logical key.</param>
    /// <param name="modifiers">The modifiers.</param>
    [Theory]
    [InlineData("p", Key.P, KeyModifiers.None)]
    [InlineData("ctrl-shift-p", Key.P, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Shift-Ctrl-P", Key.P, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("alt-7", Key.D7, KeyModifiers.Alt)]
    [InlineData("win-enter", Key.Enter, KeyModifiers.Meta)]
    [InlineData("cmd-enter", Key.Enter, KeyModifiers.Meta)]
    [InlineData("super-enter", Key.Enter, KeyModifiers.Meta)]
    [InlineData("meta-enter", Key.Enter, KeyModifiers.Meta)]
    [InlineData("escape", Key.Escape, KeyModifiers.None)]
    [InlineData("shift-tab", Key.Tab, KeyModifiers.Shift)]
    [InlineData("space", Key.Space, KeyModifiers.None)]
    [InlineData("backspace", Key.Back, KeyModifiers.None)]
    [InlineData("delete", Key.Delete, KeyModifiers.None)]
    [InlineData("insert", Key.Insert, KeyModifiers.None)]
    [InlineData("home", Key.Home, KeyModifiers.None)]
    [InlineData("end", Key.End, KeyModifiers.None)]
    [InlineData("pageup", Key.PageUp, KeyModifiers.None)]
    [InlineData("pagedown", Key.PageDown, KeyModifiers.None)]
    [InlineData("left", Key.Left, KeyModifiers.None)]
    [InlineData("right", Key.Right, KeyModifiers.None)]
    [InlineData("up", Key.Up, KeyModifiers.None)]
    [InlineData("down", Key.Down, KeyModifiers.None)]
    [InlineData("menu", Key.Apps, KeyModifiers.None)]
    [InlineData("f1", Key.F1, KeyModifiers.None)]
    [InlineData("ctrl-F12", Key.F12, KeyModifiers.Control)]
    [InlineData("f24", Key.F24, KeyModifiers.None)]
    public void Parse_AKeyWithModifiers_GivesTheLogicalKey(string text, Key key, KeyModifiers modifiers)
    {
        Keystroke keystroke = Keystroke.Parse(text);

        Assert.Equal(key, keystroke.Key);
        Assert.Equal(modifiers, keystroke.Modifiers);
        Assert.False(keystroke.IsCharacter);
    }

    /// <summary>
    /// <c>secondary</c> is Control on Windows and Linux and the command key on macOS.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The OS the test runs on.
    ///
    /// Steps:
    /// - Parse <c>secondary-s</c>.
    ///
    /// Expected:
    /// - The key S, with <c>Meta</c> on macOS and <c>Control</c> elsewhere.
    /// </remarks>
    [Fact]
    public void Parse_Secondary_IsTheCommandModifierOfTheOs()
    {
        Keystroke keystroke = Keystroke.Parse("secondary-s");

        Assert.Equal(Key.S, keystroke.Key);
        Assert.Equal(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control, keystroke.Modifiers);
    }

    /// <summary>
    /// A key written as a symbol is a character step, and Shift is not part of it: the character already says whether Shift made
    /// it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse <c>ctrl-?</c>, <c>-</c>, <c>ctrl--</c>, <c>shift-+</c>, and <c>é</c>.
    ///
    /// Expected:
    /// - Character steps of <c>?</c> with Control, <c>-</c> with none, <c>-</c> with Control, <c>+</c> with none (Shift dropped),
    ///   and <c>é</c> with none.
    /// </remarks>
    [Fact]
    public void Parse_ASymbol_IsACharacterStepWithoutShift()
    {
        Assert.Equal(new Keystroke(new Rune('?'), KeyModifiers.Control), Keystroke.Parse("ctrl-?"));
        Assert.Equal(new Keystroke(new Rune('-')), Keystroke.Parse("-"));
        Assert.Equal(new Keystroke(new Rune('-'), KeyModifiers.Control), Keystroke.Parse("ctrl--"));
        Assert.Equal(new Keystroke(new Rune('+')), Keystroke.Parse("shift-+"));
        Assert.Equal(new Keystroke(new Rune('é')), Keystroke.Parse("é"));
        Assert.True(Keystroke.Parse("ctrl-?").IsCharacter);
        Assert.Equal(KeyModifiers.None, Keystroke.Parse("shift-+").Modifiers);
    }

    /// <summary>
    /// Text that is not a step is refused: <c>TryParse</c> returns false and <c>Parse</c> throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse each text: empty; an unknown modifier; a modifier written twice; a modifier with no key; a key of two characters
    ///   that is no name; <c>f0</c> and <c>f25</c>; a space; a control character.
    ///
    /// Expected:
    /// - <c>TryParse</c> is false and <c>Parse</c> throws <see cref="FormatException"/>.
    /// </remarks>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("hyper-a")]
    [InlineData("ctrl-ctrl-a")]
    [InlineData("ctrl-")]
    [InlineData("ab")]
    [InlineData("f0")]
    [InlineData("f25")]
    [InlineData(" ")]
    [InlineData("\u0007")]
    public void Parse_NotAStep_IsRefused(string text)
    {
        Assert.False(Keystroke.TryParse(text, out _));
        _ = Assert.Throws<FormatException>(() => Keystroke.Parse(text));
    }

    /// <summary>
    /// A key step matches its logical key with exactly its modifiers; a character step matches the character the key types with
    /// its modifiers, whether or not Shift is down.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The steps <c>ctrl-s</c> and <c>ctrl-?</c>.
    ///
    /// Steps:
    /// - Match keys that went down against them.
    ///
    /// Expected:
    /// - <c>ctrl-s</c>: S with Control matches; S with Control and Shift, S with none, and D with Control do not; a key that types
    ///   "s" on another logical key does not. <c>ctrl-?</c>: a key that types "?" with Control matches with and without Shift, on
    ///   any logical key; with Control and Alt it does not; a key that types "/" does not; a key that types nothing does not.
    /// </remarks>
    [Fact]
    public void Matches_KeyStepsByKey_CharacterStepsByCharacter()
    {
        Keystroke save = Keystroke.Parse("ctrl-s");
        Assert.True(save.Matches(Key.S, new Rune('s'), KeyModifiers.Control));
        Assert.False(save.Matches(Key.S, new Rune('S'), KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(save.Matches(Key.S, new Rune('s'), KeyModifiers.None));
        Assert.False(save.Matches(Key.D, new Rune('d'), KeyModifiers.Control));
        Assert.False(save.Matches(Key.OemSemicolon, new Rune('s'), KeyModifiers.Control));

        Keystroke help = Keystroke.Parse("ctrl-?");
        Assert.True(help.Matches(Key.OemQuestion, new Rune('?'), KeyModifiers.Control | KeyModifiers.Shift));
        Assert.True(help.Matches(Key.OemComma, new Rune('?'), KeyModifiers.Control));
        Assert.False(help.Matches(Key.OemQuestion, new Rune('?'), KeyModifiers.Control | KeyModifiers.Alt));
        Assert.False(help.Matches(Key.OemQuestion, new Rune('/'), KeyModifiers.Control));
        Assert.False(help.Matches(Key.OemQuestion, default, KeyModifiers.Control));
    }

    /// <summary>
    /// A step's text form reads back as the same step, and what a menu shows on Windows has the modifiers in the order Ctrl, Alt,
    /// Shift, Win, joined with <c>+</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse each text; write it back; write what a menu shows.
    ///
    /// Expected:
    /// - The text form of the row, which parses to an equal step, and the display text of the row.
    /// </remarks>
    /// <param name="text">The text parsed.</param>
    /// <param name="roundTrip">The text form written back.</param>
    /// <param name="display">What a menu shows.</param>
    [Theory]
    [InlineData("win-shift-alt-ctrl-p", "ctrl-alt-shift-win-p", "Ctrl+Alt+Shift+Win+P")]
    [InlineData("ctrl-s", "ctrl-s", "Ctrl+S")]
    [InlineData("shift-f5", "shift-f5", "Shift+F5")]
    [InlineData("escape", "escape", "Esc")]
    [InlineData("ctrl-pageup", "ctrl-pageup", "Ctrl+Page up")]
    [InlineData("pagedown", "pagedown", "Page down")]
    [InlineData("space", "space", "Spacebar")]
    [InlineData("alt-enter", "alt-enter", "Alt+Enter")]
    [InlineData("ctrl-0", "ctrl-0", "Ctrl+0")]
    [InlineData("ctrl-?", "ctrl-?", "Ctrl+?")]
    [InlineData("ctrl--", "ctrl--", "Ctrl+-")]
    public void ToString_AndToDisplayString_WriteTheStep(string text, string roundTrip, string display)
    {
        Keystroke keystroke = Keystroke.Parse(text);

        Assert.Equal(roundTrip, keystroke.ToString());
        Assert.Equal(keystroke, Keystroke.Parse(keystroke.ToString()));
        Assert.Equal(display, keystroke.ToDisplayString());
    }
}
