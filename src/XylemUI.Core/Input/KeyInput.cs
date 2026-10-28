using System.Text;

namespace XylemUI;

/// <summary>
/// One input of the keyboard as the platform hands it to Core (design §7, "Keys and the focus"): the entry the tests inject keys
/// through (design §12.4).
/// </summary>
/// <param name="Kind">Whether the key went down or up.</param>
/// <param name="Key">The key as the layout names it.</param>
/// <param name="PhysicalKey">Where the key is.</param>
/// <param name="KeyChar">The character the key types with the modifiers held; <see langword="default"/> for none, a dead key, or a
/// control character.</param>
/// <param name="Modifiers">The modifier keys held down.</param>
/// <param name="IsRepeat">Whether a down is the key's automatic repeat; <see langword="false"/> for an up.</param>
/// <param name="Timestamp">When the platform received the input.</param>
internal readonly record struct KeyInput(
    KeyEventKind Kind,
    Key Key,
    PhysicalKey PhysicalKey,
    Rune KeyChar,
    KeyModifiers Modifiers,
    bool IsRepeat,
    TimeSpan Timestamp
);
