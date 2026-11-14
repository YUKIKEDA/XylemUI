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

    /// <summary>A set of links that move around the app or a document.</summary>
    Navigation,

    /// <summary>A title bar that the app draws.</summary>
    TitleBar,

    /// <summary>The content a tab shows.</summary>
    TabPanel,

    /// <summary>A bar of buttons and other controls.</summary>
    Toolbar,

    /// <summary>A part of the window with a name, which a person may want to go to.</summary>
    Region,

    /// <summary>A status line, whose changes are read out when the person is idle; it takes its name from its content.</summary>
    Status,

    /// <summary>A message that needs attention, read out at once; it takes its name from its content.</summary>
    Alert,

    /// <summary>A dialog that holds an alert.</summary>
    AlertDialog,

    /// <summary>A value in a known range that the person cannot change, such as the space a disk has left.</summary>
    Meter,

    /// <summary>A switch, on or off; it takes its name from its content.</summary>
    Switch,

    /// <summary>A group of radio buttons.</summary>
    RadioGroup,

    /// <summary>A number that steps up and down.</summary>
    SpinButton,

    /// <summary>A field of text to search with.</summary>
    SearchBox,

    /// <summary>A scroll bar.</summary>
    ScrollBar,

    /// <summary>An item of a menu that is checked or not; it takes its name from its content.</summary>
    MenuItemCheckBox,

    /// <summary>An item of a menu that is one of a set of choices; it takes its name from its content.</summary>
    MenuItemRadio,

    /// <summary>A list whose items are chosen.</summary>
    ListBox,

    /// <summary>An item of a list whose items are chosen; it takes its name from its content.</summary>
    ListBoxItem,

    /// <summary>A tree of items that open and close.</summary>
    Tree,

    /// <summary>An item of a tree; it takes its name from its content.</summary>
    TreeItem,

    /// <summary>A grid whose rows open and close as the items of a tree do.</summary>
    TreeGrid,

    /// <summary>A table of data.</summary>
    Table,

    /// <summary>A grid of cells that the person moves among.</summary>
    Grid,

    /// <summary>A row of a table or a grid.</summary>
    Row,

    /// <summary>A cell of a table; it takes its name from its content.</summary>
    Cell,

    /// <summary>A cell of a grid; it takes its name from its content.</summary>
    GridCell,

    /// <summary>The header of a column; it takes its name from its content.</summary>
    ColumnHeader,

    /// <summary>The header of a row; it takes its name from its content.</summary>
    RowHeader,

    /// <summary>A heading, whose level is <see cref="AccessibilityInfo.Level"/>; it takes its name from its content.</summary>
    Heading,

    /// <summary>A button that shows and hides a part; it takes its name from its content.</summary>
    Disclosure,

    /// <summary>A field of a date.</summary>
    DateInput,

    /// <summary>A field of a time.</summary>
    TimeInput,

    /// <summary>A field of a date and a time.</summary>
    DateTimeInput,

    /// <summary>A log, whose added lines are read out when the person is idle.</summary>
    Log,

    /// <summary>A list of articles that grows as the person scrolls.</summary>
    Feed,

    /// <summary>A part that makes sense on its own, such as a post.</summary>
    Article,

    /// <summary>A document.</summary>
    Document,

    /// <summary>A paragraph.</summary>
    Paragraph,

    /// <summary>A piece of code.</summary>
    Code,

    /// <summary>A figure, such as a chart or a picture with its caption.</summary>
    Figure,

    /// <summary>A pane of the window.</summary>
    Pane,
}

/// <summary>
/// The states of a node that only the app knows (design §4.4.1). Whether the node is disabled, focused, or open is read from its
/// pseudo-states, not from these, and whether a popup's anchor is open from its popup.
/// </summary>
[Flags]
public enum SemanticsStates : ushort
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

    /// <summary>
    /// The node's value is not valid. On Windows only the text of the node's error message tells it, at the start of its description
    /// (design §4.4.1, "Folding on Windows").
    /// </summary>
    Invalid = 32,

    /// <summary>More than one of the node's items can be selected (a list, a tree).</summary>
    Multiselectable = 64,

    /// <summary>The node is changing, and assistive technology may wait before it reads it.</summary>
    Busy = 128,

    /// <summary>The node, a dialog, keeps the rest of the window from being used while it is open.</summary>
    Modal = 256,

    /// <summary>
    /// The node opens and closes (a tree's item, a disclosure), and <see cref="Expanded"/> says whether it is open. Without it, the node
    /// has no open state and <see cref="Expanded"/> is not read.
    /// </summary>
    Expandable = 512,

    /// <summary>The node is open; read only with <see cref="Expandable"/>.</summary>
    Expanded = 1024,
}

/// <summary>
/// How assistive technology reads out a change of a node's name (design §4.4.1, "Live regions").
/// </summary>
public enum SemanticsLive : byte
{
    /// <summary>The role's own (assertive for an alert, polite for a status and a log); else that of the nearest node above.</summary>
    Inherit,

    /// <summary>A change is not read out.</summary>
    Off,

    /// <summary>A change is read out when the person is idle.</summary>
    Polite,

    /// <summary>A change is read out at once.</summary>
    Assertive,
}

/// <summary>
/// Which way a node's items run (design §4.4.1, "Orientation").
/// </summary>
public enum SemanticsOrientation : byte
{
    /// <summary>None given: assistive technology takes the role's own.</summary>
    None,

    /// <summary>The items run from side to side.</summary>
    Horizontal,

    /// <summary>The items run from top to bottom.</summary>
    Vertical,
}

/// <summary>
/// The value of a node that takes one in a range (a slider, a progress bar, a meter, a spin button, a scroll bar).
/// </summary>
/// <param name="Minimum">The smallest value.</param>
/// <param name="Maximum">The largest value.</param>
/// <param name="Value">The value.</param>
/// <param name="Step">The change of a small step (UIA's SmallChange); 0 for none.</param>
/// <param name="LargeStep">The change of a large step (UIA's LargeChange); 0 for none, where a large step is a small one.</param>
/// <exception cref="ArgumentOutOfRangeException">A step is negative or not a finite number.</exception>
public readonly record struct SemanticsRange(double Minimum, double Maximum, double Value, double Step = 0, double LargeStep = 0)
{
    /// <summary>
    /// Gets the change of a small step; 0 for none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public double Step
    {
        get;
        init => field = CheckStep(value, nameof(Step));
    } = CheckStep(Step, nameof(Step));

    /// <summary>
    /// Gets the change of a large step; 0 for none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public double LargeStep
    {
        get;
        init => field = CheckStep(value, nameof(LargeStep));
    } = CheckStep(LargeStep, nameof(LargeStep));

    // A with expression sets the members through their init accessors and does not call the constructor: both check.
    private static double CheckStep(double step, string name) =>
        double.IsFinite(step) && step >= 0
            ? step
            : throw new ArgumentOutOfRangeException(name, step, $"The {name} of a range is 0 or a finite number above 0.");
}

/// <summary>
/// The information a node gives assistive technology (design §4.4.1, "The shape of P02g"). Each value left
/// <see langword="null"/> keeps the one the node's part gives; a node of no part with no role is not in the accessibility tree,
/// unless it takes the focus, which makes it a group.
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
    /// Gets the states only the app knows, which join the ones its part gives: the app cannot take a part's state away.
    /// </summary>
    public SemanticsStates States { get; init; }

    /// <summary>
    /// Gets the node's value in a range; with <see langword="null"/>, the one its part gives.
    /// </summary>
    public SemanticsRange? Range { get; init; }

    /// <summary>
    /// Gets a value indicating whether the node and its subtree are left out of the accessibility tree and out of the name an
    /// ancestor takes from its content: a decorative image, icon, or divider (design §4.4.1, "Excluded nodes"; Flutter's
    /// <c>ExcludeSemantics</c>, Avalonia's <c>AccessibilityView.Raw</c>). A node that takes the focus does not belong inside one.
    /// </summary>
    public bool Excluded { get; init; }

    /// <summary>
    /// Gets how assistive technology reads out a change of the node's name (design §4.4.1, "Live regions"); with
    /// <see cref="SemanticsLive.Inherit"/>, the one its part gives, else its role's or its parent's.
    /// </summary>
    public SemanticsLive Live { get; init; }

    /// <summary>
    /// Gets which way the node's items run; with <see cref="SemanticsOrientation.None"/>, the one its part gives, else none.
    /// </summary>
    public SemanticsOrientation Orientation { get; init; }

    /// <summary>
    /// Gets the node's level, from 1: a heading's, or a tree item's depth; 0 for none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int Level
    {
        get;
        init => field = CheckCount(value, nameof(Level));
    }

    /// <summary>
    /// Gets the item's place in its set, from 1; 0 for none. Given, it wins over the place a virtualized list gives its row.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int PositionInSet
    {
        get;
        init => field = CheckCount(value, nameof(PositionInSet));
    }

    /// <summary>
    /// Gets the number of items in the set, given on the container of the items, not on an item (design §4.4.1, "Level, place, and
    /// count"); 0 for none. Given, it wins over the number of a virtualized list's items.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int SizeOfSet
    {
        get;
        init => field = CheckCount(value, nameof(SizeOfSet));
    }

    /// <summary>
    /// Returns this information over another one: each value this one leaves unset is the other's, and the states of both join.
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
                States = States | under.States,
                Range = Range ?? under.Range,
                Excluded = Excluded || under.Excluded,
                Live = Live != SemanticsLive.Inherit ? Live : under.Live,
                Orientation = Orientation != SemanticsOrientation.None ? Orientation : under.Orientation,
                Level = Level != 0 ? Level : under.Level,
                PositionInSet = PositionInSet != 0 ? PositionInSet : under.PositionInSet,
                SizeOfSet = SizeOfSet != 0 ? SizeOfSet : under.SizeOfSet,
                PartStates = PartStates | under.PartStates,
                Placeholder = Placeholder ?? under.Placeholder,
            };

    /// <summary>
    /// Gets the states that only a part gives, which <see cref="SemanticsStates"/> does not name: a text field of many lines, or a
    /// password field (design §4.4.1, the table of roles).
    /// </summary>
    internal AccessibilityStates PartStates { get; init; }

    /// <summary>
    /// Gets the text a text field shows while it is empty, which only a part gives (design §4.4.1, "States and actions").
    /// </summary>
    internal string? Placeholder { get; init; }

    /// <summary>
    /// Gets a value indicating whether the information gives a value the accessibility tree keeps apart from its nodes
    /// (<see cref="AccessibilityNode.More"/>): a live setting, an orientation, a level, or a range with a step.
    /// </summary>
    internal bool GivesMore =>
        Live != SemanticsLive.Inherit || Orientation != SemanticsOrientation.None || Level != 0 || Range is { Step: > 0 } or { LargeStep: > 0 };

    // A with expression sets the members through their init accessors and does not call a constructor: the check is there.
    private static int CheckCount(int value, string name) =>
        value >= 0 ? value : throw new ArgumentOutOfRangeException(name, value, $"The {name} of a node is 0 (none) or a number from 1.");
}
