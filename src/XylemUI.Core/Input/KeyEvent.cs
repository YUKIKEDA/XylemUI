using System.Text;

namespace XylemUI;

/// <summary>
/// An event of the keyboard, as a handler receives it (design §7, "Keys and the focus"). It is routed from the focused node, or
/// from the root when no node has the focus, in the capture and the bubble phase. Each handler gets its own copy, whose
/// <see cref="CurrentTarget"/> is its node's.
/// </summary>
public readonly struct KeyEvent
{
    private readonly WindowInput _input;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyEvent"/> struct.
    /// </summary>
    /// <param name="input">The input of the window the event is routed in.</param>
    /// <param name="target">The node the route starts from.</param>
    /// <param name="currentTarget">The node of the handler called now.</param>
    /// <param name="source">The input the platform gave.</param>
    internal KeyEvent(WindowInput input, NodeHandle target, NodeHandle currentTarget, in KeyInput source)
    {
        _input = input;
        TargetNode = target;
        CurrentNode = currentTarget;
        Kind = source.Kind;
        Key = source.Key;
        PhysicalKey = source.PhysicalKey;
        KeyChar = source.KeyChar;
        Modifiers = source.Modifiers;
        IsRepeat = source.IsRepeat;
        Timestamp = source.Timestamp;
        PrefersCharacter = source.PrefersCharacter;
    }

    /// <summary>
    /// Gets whether the key went down or up.
    /// </summary>
    public KeyEventKind Kind { get; }

    /// <summary>
    /// Gets the node the route starts from: the focused node, or the root when the window is not active or no node has the focus.
    /// </summary>
    public Element Target => new(_input.Tree, TargetNode);

    /// <summary>
    /// Gets the node of the handler called now.
    /// </summary>
    public Element CurrentTarget => new(_input.Tree, CurrentNode);

    /// <summary>
    /// Gets the key as the keyboard's layout names it.
    /// </summary>
    public Key Key { get; }

    /// <summary>
    /// Gets where the key is on the keyboard, whatever the layout.
    /// </summary>
    public PhysicalKey PhysicalKey { get; }

    /// <summary>
    /// Gets the character the key types with the modifiers held; <see langword="default"/> for a key that types none, a dead key,
    /// and a control character (Control+A). It is not text input: the text comes as a <see cref="TextInputEvent"/> (design §4.9).
    /// </summary>
    public Rune KeyChar { get; }

    /// <summary>
    /// Gets a value indicating whether the key types another character with its modifiers than without them, or is a dead key: AltGr
    /// on Windows comes as Control and Alt (design §4.9, "Text input on Windows"). A text field does not take such a key as a
    /// command, so that AltGr+A types its character and does not select all.
    /// </summary>
    public bool PrefersCharacter { get; }

    /// <summary>
    /// Gets the modifier keys held down. A modifier key's own down already has it.
    /// </summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>
    /// Gets a value indicating whether a down is the key's automatic repeat; <see langword="false"/> for an up.
    /// </summary>
    public bool IsRepeat { get; }

    /// <summary>
    /// Gets when the platform received the input.
    /// </summary>
    public TimeSpan Timestamp { get; }

    /// <summary>
    /// Gets the node of <see cref="Target"/>.
    /// </summary>
    internal NodeHandle TargetNode { get; }

    /// <summary>
    /// Gets the node of <see cref="CurrentTarget"/>.
    /// </summary>
    internal NodeHandle CurrentNode { get; }

    /// <summary>
    /// Gets the window's input that delivers the event, which the button routes its keys' clicks through.
    /// </summary>
    internal WindowInput Input => _input;

    /// <summary>
    /// Stops the event: no handler after this one is called, in this phase or the next, and the default action (Tab) does not run.
    /// A stopped key is handled, so the platform does not pass it to the OS.
    /// </summary>
    public void StopPropagation() => _input.StopPropagation();
}

/// <summary>
/// A change of the focus, as a handler receives it (design §7, "Keys and the focus"): the node that lost the focus gets a
/// <see cref="FocusEventKind.FocusOut"/>, then the node that took it a <see cref="FocusEventKind.FocusIn"/>, each routed from that
/// node in the capture and the bubble phase.
/// </summary>
public readonly struct FocusEvent
{
    private readonly FocusManager _focus;

    /// <summary>
    /// Initializes a new instance of the <see cref="FocusEvent"/> struct.
    /// </summary>
    /// <param name="focus">The focus of the window the event is routed in.</param>
    /// <param name="kind">Whether the focus came or went.</param>
    /// <param name="target">The node that took or lost the focus.</param>
    /// <param name="currentTarget">The node of the handler called now.</param>
    internal FocusEvent(FocusManager focus, FocusEventKind kind, NodeHandle target, NodeHandle currentTarget)
    {
        _focus = focus;
        Kind = kind;
        TargetNode = target;
        CurrentNode = currentTarget;
    }

    /// <summary>
    /// Gets whether the focus came or went.
    /// </summary>
    public FocusEventKind Kind { get; }

    /// <summary>
    /// Gets the node that took the focus, for a <see cref="FocusEventKind.FocusIn"/>, or lost it, for a
    /// <see cref="FocusEventKind.FocusOut"/>. A handler of an ancestor tells its own node's change from a descendant's with it.
    /// </summary>
    public Element Target => new(_focus.Tree, TargetNode);

    /// <summary>
    /// Gets the node of the handler called now.
    /// </summary>
    public Element CurrentTarget => new(_focus.Tree, CurrentNode);

    /// <summary>
    /// Gets the node of <see cref="Target"/>.
    /// </summary>
    internal NodeHandle TargetNode { get; }

    /// <summary>
    /// Gets the node of <see cref="CurrentTarget"/>.
    /// </summary>
    internal NodeHandle CurrentNode { get; }

    /// <summary>
    /// Stops the event: no handler after this one is called, in this phase or the next. The focus has changed already.
    /// </summary>
    public void StopPropagation() => _focus.StopPropagation();
}

/// <summary>
/// The kinds of <see cref="KeyEvent"/>.
/// </summary>
public enum KeyEventKind : byte
{
    /// <summary>
    /// A key went down, or repeats while it is held (<see cref="KeyEvent.IsRepeat"/>).
    /// </summary>
    Down,

    /// <summary>
    /// A key went up.
    /// </summary>
    Up,
}

/// <summary>
/// The kinds of <see cref="FocusEvent"/>.
/// </summary>
public enum FocusEventKind : byte
{
    /// <summary>
    /// The node took the focus, after the previous node's <see cref="FocusOut"/>.
    /// </summary>
    FocusIn,

    /// <summary>
    /// The node lost the focus: another node took it, the node or an ancestor was removed or hidden, the node stopped being
    /// focusable, or the window stopped being active.
    /// </summary>
    FocusOut,
}

/// <summary>
/// Handles an event of the keyboard (design §8.9).
/// </summary>
/// <param name="e">The event.</param>
public delegate void KeyHandler(in KeyEvent e);

/// <summary>
/// Handles a change of the focus (design §8.9).
/// </summary>
/// <param name="e">The event.</param>
public delegate void FocusHandler(in FocusEvent e);
