using System.Text;

namespace XylemUI;

/// <summary>
/// One step of a key binding (design §7, "Shortcuts and actions"): a key with its modifiers, as <c>ctrl-shift-p</c>. A step matches
/// a key that went down by its logical key, or by the character it types when the step was written as a character
/// (<c>ctrl-?</c>), since a symbol sits on different keys in different layouts.
/// </summary>
/// <remarks>
/// The text form is GPUI's: the modifiers <c>ctrl</c>, <c>alt</c>, <c>shift</c>, <c>win</c> (also <c>cmd</c>, <c>super</c>,
/// <c>meta</c>), and <c>secondary</c>, each followed by <c>-</c>, then the key. <c>secondary</c> is Control on Windows and Linux
/// and the command key on macOS. The key is a name (<c>enter</c>, <c>escape</c>, <c>f5</c>, <c>pageup</c>, …), a letter or a digit,
/// or any other single character, which is matched as a character.
/// </remarks>
public readonly struct Keystroke : IEquatable<Keystroke>
{
    private static readonly (string Name, Key Key)[] Names =
    [
        ("enter", Key.Enter),
        ("escape", Key.Escape),
        ("tab", Key.Tab),
        ("space", Key.Space),
        ("backspace", Key.Back),
        ("delete", Key.Delete),
        ("insert", Key.Insert),
        ("home", Key.Home),
        ("end", Key.End),
        ("pageup", Key.PageUp),
        ("pagedown", Key.PageDown),
        ("left", Key.Left),
        ("right", Key.Right),
        ("up", Key.Up),
        ("down", Key.Down),
        ("menu", Key.Apps),
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="Keystroke"/> struct that matches a logical key.
    /// </summary>
    /// <param name="key">The logical key.</param>
    /// <param name="modifiers">The modifiers that are down with it, and no others.</param>
    public Keystroke(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Keystroke"/> struct that matches the character a key types.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <param name="modifiers">
    /// The modifiers that are down with it, apart from Shift: the character already says whether Shift made it.
    /// </param>
    public Keystroke(Rune character, KeyModifiers modifiers = KeyModifiers.None)
    {
        Character = character;
        Modifiers = modifiers & ~KeyModifiers.Shift;
    }

    /// <summary>
    /// Gets the logical key the step matches; <see cref="Key.None"/> when it matches a character.
    /// </summary>
    public Key Key { get; }

    /// <summary>
    /// Gets the character the step matches; the default rune when it matches a logical key.
    /// </summary>
    public Rune Character { get; }

    /// <summary>
    /// Gets the modifiers that are down with the key.
    /// </summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>
    /// Gets a value indicating whether the step matches a character, not a logical key.
    /// </summary>
    public bool IsCharacter => Key == Key.None;

    /// <summary>
    /// Compares two steps.
    /// </summary>
    /// <param name="left">A step.</param>
    /// <param name="right">Another.</param>
    /// <returns>Whether they match the same keys.</returns>
    public static bool operator ==(Keystroke left, Keystroke right) => left.Equals(right);

    /// <summary>
    /// Compares two steps.
    /// </summary>
    /// <param name="left">A step.</param>
    /// <param name="right">Another.</param>
    /// <returns>Whether they match different keys.</returns>
    public static bool operator !=(Keystroke left, Keystroke right) => !left.Equals(right);

    /// <summary>
    /// Reads a step from its text form.
    /// </summary>
    /// <param name="text">The text, as <c>ctrl-shift-p</c>.</param>
    /// <returns>The step.</returns>
    /// <exception cref="FormatException">The text is not a step.</exception>
    public static Keystroke Parse(ReadOnlySpan<char> text) =>
        TryParse(text, out Keystroke keystroke) ? keystroke : throw new FormatException($"'{text}' is not a keystroke; write it as 'ctrl-shift-p'.");

    /// <summary>
    /// Reads a step from its text form.
    /// </summary>
    /// <param name="text">The text, as <c>ctrl-shift-p</c>.</param>
    /// <param name="keystroke">The step.</param>
    /// <returns>Whether the text is a step.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out Keystroke keystroke)
    {
        keystroke = default;
        KeyModifiers modifiers = KeyModifiers.None;
        while (true)
        {
            // The key itself can be "-": a hyphen after the last modifier, or alone.
            int dash = text.IndexOf('-');
            if (dash <= 0 || dash == text.Length - 1)
            {
                break;
            }

            if (!TryModifier(text[..dash], out KeyModifiers modifier) || (modifiers & modifier) != 0)
            {
                return false;
            }

            modifiers |= modifier;
            text = text[(dash + 1)..];
        }

        if (text.IsEmpty)
        {
            return false;
        }

        foreach ((string name, Key key) in Names)
        {
            if (text.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                keystroke = new Keystroke(key, modifiers);
                return true;
            }
        }

        if (
            text.Length is 2 or 3
            && (text[0] is 'f' or 'F')
            && int.TryParse(text[1..], System.Globalization.NumberStyles.None, null, out int function)
        )
        {
            if (function is < 1 or > 24)
            {
                return false;
            }

            keystroke = new Keystroke(Key.F1 + (ushort)(function - 1), modifiers);
            return true;
        }

        if (Rune.DecodeFromUtf16(text, out Rune rune, out int consumed) != System.Buffers.OperationStatus.Done || consumed != text.Length)
        {
            return false;
        }

        if (rune.IsAscii && char.IsAsciiLetter((char)rune.Value))
        {
            keystroke = new Keystroke(Key.A + (ushort)(char.ToLowerInvariant((char)rune.Value) - 'a'), modifiers);
            return true;
        }

        if (rune.IsAscii && char.IsAsciiDigit((char)rune.Value))
        {
            keystroke = new Keystroke(Key.D0 + (ushort)((char)rune.Value - '0'), modifiers);
            return true;
        }

        if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune))
        {
            return false;
        }

        keystroke = new Keystroke(rune, modifiers);
        return true;
    }

    /// <summary>
    /// Returns whether a key that went down is this step.
    /// </summary>
    /// <param name="key">The logical key.</param>
    /// <param name="character">The character the key types; the default rune when it types none.</param>
    /// <param name="modifiers">The modifiers that are down.</param>
    /// <returns>Whether the key is this step.</returns>
    public bool Matches(Key key, Rune character, KeyModifiers modifiers) =>
        IsCharacter
            ? character.Value != 0 && character == Character && (modifiers & ~KeyModifiers.Shift) == Modifiers
            : key == Key && modifiers == Modifiers;

    /// <inheritdoc/>
    public bool Equals(Keystroke other) => Key == other.Key && Character == other.Character && Modifiers == other.Modifiers;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Keystroke other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Key, Character, Modifiers);

    /// <summary>
    /// Returns the text form <see cref="Parse"/> reads.
    /// </summary>
    /// <returns>The text, as <c>ctrl-shift-p</c>.</returns>
    public override string ToString()
    {
        var text = new StringBuilder();
        Append(text, KeyModifiers.Control, "ctrl-");
        Append(text, KeyModifiers.Alt, "alt-");
        Append(text, KeyModifiers.Shift, "shift-");
        Append(text, KeyModifiers.Meta, "win-");
        return text.Append(KeyName(lower: true)).ToString();
    }

    /// <summary>
    /// Returns the text a menu or a tooltip shows for the step on Windows: the modifiers in the order Ctrl, Alt, Shift, Win, joined
    /// with <c>+</c>, then the key (design §7, "Shortcuts and actions"; WinUI's order of <c>VirtualKeyModifiers</c>).
    /// </summary>
    /// <returns>The text, as <c>Ctrl+Shift+P</c>.</returns>
    public string ToDisplayString()
    {
        var text = new StringBuilder();
        Append(text, KeyModifiers.Control, "Ctrl+");
        Append(text, KeyModifiers.Alt, "Alt+");
        Append(text, KeyModifiers.Shift, "Shift+");
        Append(text, KeyModifiers.Meta, "Win+");
        return text.Append(KeyName(lower: false)).ToString();
    }

    private static bool TryModifier(ReadOnlySpan<char> text, out KeyModifiers modifier)
    {
        if (text.Equals("ctrl", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyModifiers.Control;
        }
        else if (text.Equals("alt", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyModifiers.Alt;
        }
        else if (text.Equals("shift", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyModifiers.Shift;
        }
        else if (
            text.Equals("win", StringComparison.OrdinalIgnoreCase)
            || text.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || text.Equals("super", StringComparison.OrdinalIgnoreCase)
            || text.Equals("meta", StringComparison.OrdinalIgnoreCase)
        )
        {
            modifier = KeyModifiers.Meta;
        }
        else if (text.Equals("secondary", StringComparison.OrdinalIgnoreCase))
        {
            // Control everywhere but macOS, where it is the command key (GPUI's `secondary`).
            modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        }
        else
        {
            modifier = KeyModifiers.None;
        }

        return modifier != KeyModifiers.None;
    }

    private void Append(StringBuilder text, KeyModifiers modifier, string name)
    {
        if ((Modifiers & modifier) != 0)
        {
            _ = text.Append(name);
        }
    }

    private string KeyName(bool lower)
    {
        if (IsCharacter)
        {
            return Character.ToString();
        }

        foreach ((string name, Key key) in Names)
        {
            if (key == Key)
            {
                return lower ? name : DisplayName(name);
            }
        }

        if (Key is >= Key.A and <= Key.Z)
        {
            char letter = (char)('a' + (Key - Key.A));
            return (lower ? letter : char.ToUpperInvariant(letter)).ToString();
        }

        if (Key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (Key - Key.D0))).ToString();
        }

        if (Key is >= Key.F1 and <= Key.F24)
        {
            return (lower ? "f" : "F") + (Key - Key.F1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Key.ToString();
    }

    // The names Microsoft's style guide gives the keys ("Keys and keyboard shortcuts": Esc, Page up, Page down, Spacebar); the others
    // are the text form's name with a capital. Row LC3 reads them from the dictionary of the parts' strings (design §4.11).
    private static string DisplayName(string name) =>
        name switch
        {
            "escape" => "Esc",
            "pageup" => "Page up",
            "pagedown" => "Page down",
            "space" => "Spacebar",
            _ => char.ToUpperInvariant(name[0]) + name[1..],
        };
}
