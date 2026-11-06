namespace XylemUI;

/// <summary>
/// What a node is to assistive technology (design §4.4.1, "The shape of P02g"): a part gives its own by default, and
/// <see cref="ElementExtensions.Accessible(Element, AccessibilityInfo)"/> sets another. A node with none is left out of the
/// accessibility tree, and its children go to the nearest ancestor that has one.
/// </summary>
public enum SemanticsRole : byte
{
    /// <summary>
    /// No role: the node is not in the accessibility tree. As the role of an <see cref="AccessibilityInfo"/>, it takes the role a
    /// part gave away.
    /// </summary>
    None,

    /// <summary>A window, the root of the tree.</summary>
    Window,

    /// <summary>A dialog.</summary>
    Dialog,

    /// <summary>A group of other nodes.</summary>
    Group,

    /// <summary>Text that no other node takes as its name.</summary>
    Text,

    /// <summary>A link.</summary>
    Hyperlink,

    /// <summary>An image.</summary>
    Image,

    /// <summary>A button.</summary>
    Button,

    /// <summary>A check box.</summary>
    CheckBox,

    /// <summary>A radio button.</summary>
    RadioButton,

    /// <summary>A box that shows one choice and opens a list of the others.</summary>
    ComboBox,

    /// <summary>A field of text.</summary>
    Edit,

    /// <summary>A list.</summary>
    List,

    /// <summary>An item of a list.</summary>
    ListItem,

    /// <summary>A menu.</summary>
    Menu,

    /// <summary>A menu bar.</summary>
    MenuBar,

    /// <summary>An item of a menu.</summary>
    MenuItem,

    /// <summary>A slider.</summary>
    Slider,

    /// <summary>A progress bar.</summary>
    ProgressBar,

    /// <summary>A box that scrolls its content.</summary>
    ScrollViewer,

    /// <summary>A set of tabs.</summary>
    Tab,

    /// <summary>A tab.</summary>
    TabItem,

    /// <summary>A tooltip.</summary>
    ToolTip,

    /// <summary>A separator.</summary>
    Separator,
}

/// <summary>
/// The states of a node that only the app knows (design §4.4.1). Whether the node is disabled, focused, or open is read from its
/// pseudo-states, not from these.
/// </summary>
[Flags]
public enum SemanticsStates : byte
{
    /// <summary>None of the states.</summary>
    None = 0,

    /// <summary>The node is checked (a check box).</summary>
    Checked = 1,

    /// <summary>The node is checked in part (a check box of several items).</summary>
    Mixed = 2,

    /// <summary>The node is selected (an item of a list).</summary>
    Selected = 4,

    /// <summary>The node's value cannot be changed.</summary>
    ReadOnly = 8,

    /// <summary>The node needs a value before its form can be sent.</summary>
    Required = 16,
}

/// <summary>
/// The value of a node that takes one in a range (a slider, a progress bar).
/// </summary>
/// <param name="Minimum">The smallest value.</param>
/// <param name="Maximum">The largest value.</param>
/// <param name="Value">The value.</param>
public readonly record struct SemanticsRange(double Minimum, double Maximum, double Value);

/// <summary>
/// The information a node gives assistive technology (design §4.4.1, "The shape of P02g"). Each value left
/// <see langword="null"/> keeps the one the node's part gives; a node of no part with no role is not in the accessibility tree.
/// </summary>
public sealed record AccessibilityInfo
{
    /// <summary>
    /// Gets the node's role; with <see langword="null"/>, the one its part gives.
    /// </summary>
    public SemanticsRole? Role { get; init; }

    /// <summary>
    /// Gets the node's name; with <see langword="null"/>, the one its part gives, or, for a role that takes its name from its
    /// content (a button, an item), the text of its descendants that have no role, joined by spaces.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets a longer description of the node.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the node's value as text; with <see langword="null"/>, the one its part gives (a field's text).
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Gets the states only the app knows; with none, the ones its part gives.
    /// </summary>
    public SemanticsStates States { get; init; }

    /// <summary>
    /// Gets the node's value in a range; with <see langword="null"/>, the one its part gives.
    /// </summary>
    public SemanticsRange? Range { get; init; }

    /// <summary>
    /// Returns this information over another one: each value this one leaves unset is the other's.
    /// </summary>
    /// <param name="under">The information below, a part's.</param>
    /// <returns>The information that results.</returns>
    internal AccessibilityInfo Over(AccessibilityInfo? under) =>
        under is null
            ? this
            : new AccessibilityInfo
            {
                Role = Role ?? under.Role,
                Name = Name ?? under.Name,
                Description = Description ?? under.Description,
                Value = Value ?? under.Value,
                States = States != SemanticsStates.None ? States : under.States,
                Range = Range ?? under.Range,
            };
}
