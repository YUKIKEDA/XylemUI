using System.Runtime.InteropServices;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The AccessKit C API calls the UIA provider makes (design §4.4.1, "The shape of P02g"), as <c>include/accesskit.h</c> of the
/// release 0.23.2 declares them. The library is linked into a NativeAOT executable and loaded as <c>accesskit.dll</c> under the
/// JIT, from these same declarations (design §9). A C <c>bool</c> is one byte, passed as <see cref="byte"/>.
/// </summary>
internal static unsafe partial class AccessKit
{
    private const string Library = "accesskit";

    // The values of accesskit_role (accesskit.h lines 278 to 485, in order from 0) that the tree's roles map to (design §4.4.1).

    /// <summary><c>ACCESSKIT_ROLE_UNKNOWN</c>.</summary>
    public const byte RoleUnknown = 0;

    /// <summary><c>ACCESSKIT_ROLE_LABEL</c>.</summary>
    public const byte RoleLabel = 3;

    /// <summary><c>ACCESSKIT_ROLE_IMAGE</c>.</summary>
    public const byte RoleImage = 4;

    /// <summary><c>ACCESSKIT_ROLE_LINK</c>.</summary>
    public const byte RoleLink = 5;

    /// <summary><c>ACCESSKIT_ROLE_LIST_ITEM</c>.</summary>
    public const byte RoleListItem = 7;

    /// <summary><c>ACCESSKIT_ROLE_LIST_BOX_OPTION</c>.</summary>
    public const byte RoleListBoxOption = 10;

    /// <summary><c>ACCESSKIT_ROLE_MENU_ITEM</c>.</summary>
    public const byte RoleMenuItem = 11;

    /// <summary><c>ACCESSKIT_ROLE_CHECK_BOX</c>.</summary>
    public const byte RoleCheckBox = 15;

    /// <summary><c>ACCESSKIT_ROLE_RADIO_BUTTON</c>.</summary>
    public const byte RoleRadioButton = 16;

    /// <summary><c>ACCESSKIT_ROLE_TEXT_INPUT</c>.</summary>
    public const byte RoleTextInput = 17;

    /// <summary><c>ACCESSKIT_ROLE_BUTTON</c>.</summary>
    public const byte RoleButton = 18;

    /// <summary><c>ACCESSKIT_ROLE_LIST</c>.</summary>
    public const byte RoleList = 24;

    /// <summary><c>ACCESSKIT_ROLE_MENU</c>.</summary>
    public const byte RoleMenu = 30;

    /// <summary><c>ACCESSKIT_ROLE_MULTILINE_TEXT_INPUT</c>.</summary>
    public const byte RoleMultilineTextInput = 31;

    /// <summary><c>ACCESSKIT_ROLE_PASSWORD_INPUT</c>.</summary>
    public const byte RolePasswordInput = 40;

    /// <summary><c>ACCESSKIT_ROLE_COMBO_BOX</c>.</summary>
    public const byte RoleComboBox = 56;

    /// <summary><c>ACCESSKIT_ROLE_DIALOG</c>.</summary>
    public const byte RoleDialog = 66;

    /// <summary><c>ACCESSKIT_ROLE_GROUP</c>.</summary>
    public const byte RoleGroup = 78;

    /// <summary><c>ACCESSKIT_ROLE_LIST_BOX</c>.</summary>
    public const byte RoleListBox = 87;

    /// <summary><c>ACCESSKIT_ROLE_MENU_BAR</c>.</summary>
    public const byte RoleMenuBar = 93;

    /// <summary><c>ACCESSKIT_ROLE_PROGRESS_INDICATOR</c>.</summary>
    public const byte RoleProgressIndicator = 101;

    /// <summary><c>ACCESSKIT_ROLE_SCROLL_VIEW</c>.</summary>
    public const byte RoleScrollView = 108;

    /// <summary><c>ACCESSKIT_ROLE_SLIDER</c>.</summary>
    public const byte RoleSlider = 113;

    /// <summary><c>ACCESSKIT_ROLE_SPLITTER</c>.</summary>
    public const byte RoleSplitter = 115;

    /// <summary><c>ACCESSKIT_ROLE_TAB</c>.</summary>
    public const byte RoleTab = 120;

    /// <summary><c>ACCESSKIT_ROLE_TAB_LIST</c>.</summary>
    public const byte RoleTabList = 121;

    /// <summary><c>ACCESSKIT_ROLE_TOOLTIP</c>.</summary>
    public const byte RoleTooltip = 128;

    /// <summary><c>ACCESSKIT_ROLE_WINDOW</c>.</summary>
    public const byte RoleWindow = 133;

    // The roles row AX1 adds (design §4.4.1, the table of roles), counted in the same enum.

    /// <summary><c>ACCESSKIT_ROLE_CELL</c>.</summary>
    public const byte RoleCell = 2;

    /// <summary><c>ACCESSKIT_ROLE_ROW</c>.</summary>
    public const byte RoleRow = 6;

    /// <summary><c>ACCESSKIT_ROLE_TREE_ITEM</c>.</summary>
    public const byte RoleTreeItem = 9;

    /// <summary><c>ACCESSKIT_ROLE_PARAGRAPH</c>.</summary>
    public const byte RoleParagraph = 13;

    /// <summary><c>ACCESSKIT_ROLE_PANE</c>.</summary>
    public const byte RolePane = 20;

    /// <summary><c>ACCESSKIT_ROLE_ROW_HEADER</c>.</summary>
    public const byte RoleRowHeader = 21;

    /// <summary><c>ACCESSKIT_ROLE_COLUMN_HEADER</c>.</summary>
    public const byte RoleColumnHeader = 22;

    /// <summary><c>ACCESSKIT_ROLE_TABLE</c>.</summary>
    public const byte RoleTable = 25;

    /// <summary><c>ACCESSKIT_ROLE_SWITCH</c>.</summary>
    public const byte RoleSwitch = 29;

    /// <summary><c>ACCESSKIT_ROLE_SEARCH_INPUT</c>.</summary>
    public const byte RoleSearchInput = 32;

    /// <summary><c>ACCESSKIT_ROLE_DATE_INPUT</c>.</summary>
    public const byte RoleDateInput = 33;

    /// <summary><c>ACCESSKIT_ROLE_DATE_TIME_INPUT</c>.</summary>
    public const byte RoleDateTimeInput = 34;

    /// <summary><c>ACCESSKIT_ROLE_TIME_INPUT</c>.</summary>
    public const byte RoleTimeInput = 37;

    /// <summary><c>ACCESSKIT_ROLE_ALERT</c>.</summary>
    public const byte RoleAlert = 44;

    /// <summary><c>ACCESSKIT_ROLE_ALERT_DIALOG</c>.</summary>
    public const byte RoleAlertDialog = 45;

    /// <summary><c>ACCESSKIT_ROLE_ARTICLE</c>.</summary>
    public const byte RoleArticle = 47;

    /// <summary><c>ACCESSKIT_ROLE_CODE</c>.</summary>
    public const byte RoleCode = 54;

    /// <summary><c>ACCESSKIT_ROLE_DISCLOSURE_TRIANGLE</c>.</summary>
    public const byte RoleDisclosureTriangle = 67;

    /// <summary><c>ACCESSKIT_ROLE_DOCUMENT</c>.</summary>
    public const byte RoleDocument = 68;

    /// <summary><c>ACCESSKIT_ROLE_FEED</c>.</summary>
    public const byte RoleFeed = 71;

    /// <summary><c>ACCESSKIT_ROLE_FIGURE</c>.</summary>
    public const byte RoleFigure = 73;

    /// <summary><c>ACCESSKIT_ROLE_GRID</c>.</summary>
    public const byte RoleGrid = 76;

    /// <summary><c>ACCESSKIT_ROLE_GRID_CELL</c>.</summary>
    public const byte RoleGridCell = 77;

    /// <summary><c>ACCESSKIT_ROLE_HEADING</c>.</summary>
    public const byte RoleHeading = 80;

    /// <summary><c>ACCESSKIT_ROLE_LOG</c>.</summary>
    public const byte RoleLog = 88;

    /// <summary><c>ACCESSKIT_ROLE_MENU_ITEM_CHECK_BOX</c>.</summary>
    public const byte RoleMenuItemCheckBox = 94;

    /// <summary><c>ACCESSKIT_ROLE_MENU_ITEM_RADIO</c>.</summary>
    public const byte RoleMenuItemRadio = 95;

    /// <summary><c>ACCESSKIT_ROLE_METER</c>.</summary>
    public const byte RoleMeter = 97;

    /// <summary><c>ACCESSKIT_ROLE_NAVIGATION</c>.</summary>
    public const byte RoleNavigation = 98;

    /// <summary><c>ACCESSKIT_ROLE_RADIO_GROUP</c>.</summary>
    public const byte RoleRadioGroup = 102;

    /// <summary><c>ACCESSKIT_ROLE_REGION</c>.</summary>
    public const byte RoleRegion = 103;

    /// <summary><c>ACCESSKIT_ROLE_SCROLL_BAR</c>.</summary>
    public const byte RoleScrollBar = 107;

    /// <summary><c>ACCESSKIT_ROLE_SPIN_BUTTON</c>.</summary>
    public const byte RoleSpinButton = 114;

    /// <summary><c>ACCESSKIT_ROLE_STATUS</c>.</summary>
    public const byte RoleStatus = 116;

    /// <summary><c>ACCESSKIT_ROLE_TAB_PANEL</c>.</summary>
    public const byte RoleTabPanel = 122;

    /// <summary><c>ACCESSKIT_ROLE_TITLE_BAR</c>.</summary>
    public const byte RoleTitleBar = 126;

    /// <summary><c>ACCESSKIT_ROLE_TOOLBAR</c>.</summary>
    public const byte RoleToolbar = 127;

    /// <summary><c>ACCESSKIT_ROLE_TREE</c>.</summary>
    public const byte RoleTree = 129;

    /// <summary><c>ACCESSKIT_ROLE_TREE_GRID</c>.</summary>
    public const byte RoleTreeGrid = 130;

    // The values of accesskit_action (accesskit.h lines 64 to 149, in order from 0) that the nodes declare (design §4.4.1).

    /// <summary><c>ACCESSKIT_ACTION_CLICK</c>.</summary>
    public const byte ActionClick = 0;

    /// <summary><c>ACCESSKIT_ACTION_FOCUS</c>.</summary>
    public const byte ActionFocus = 1;

    /// <summary><c>ACCESSKIT_ACTION_COLLAPSE</c>.</summary>
    public const byte ActionCollapse = 3;

    /// <summary><c>ACCESSKIT_ACTION_EXPAND</c>.</summary>
    public const byte ActionExpand = 4;

    /// <summary><c>ACCESSKIT_ACTION_DECREMENT</c>.</summary>
    public const byte ActionDecrement = 6;

    /// <summary><c>ACCESSKIT_ACTION_INCREMENT</c>.</summary>
    public const byte ActionIncrement = 7;

    /// <summary><c>ACCESSKIT_ACTION_SCROLL_INTO_VIEW</c>.</summary>
    public const byte ActionScrollIntoView = 15;

    /// <summary><c>ACCESSKIT_ACTION_SET_VALUE</c>.</summary>
    public const byte ActionSetValue = 20;

    /// <summary><c>ACCESSKIT_ACTION_DATA_VALUE</c> (accesskit.h line 1011): the data is a text.</summary>
    public const int ActionDataValue = 1;

    /// <summary><c>ACCESSKIT_ACTION_DATA_NUMERIC_VALUE</c> (accesskit.h line 1012): the data is a number, UIA's RangeValue.SetValue.</summary>
    public const int ActionDataNumericValue = 2;

    /// <summary><c>ACCESSKIT_INVALID_TRUE</c> (accesskit.h line 208).</summary>
    public const byte InvalidTrue = 0;

    /// <summary><c>ACCESSKIT_LIVE_OFF</c> (accesskit.h line 240); <c>POLITE</c> and <c>ASSERTIVE</c> follow.</summary>
    public const byte LiveOff = 0;

    /// <summary><c>ACCESSKIT_ORIENTATION_HORIZONTAL</c> (accesskit.h line 256); <c>VERTICAL</c> follows.</summary>
    public const byte OrientationHorizontal = 0;

    /// <summary><c>ACCESSKIT_TOGGLED_FALSE</c> (accesskit.h line 593).</summary>
    public const byte ToggledFalse = 0;

    /// <summary><c>ACCESSKIT_TOGGLED_TRUE</c>.</summary>
    public const byte ToggledTrue = 1;

    /// <summary><c>ACCESSKIT_TOGGLED_MIXED</c>.</summary>
    public const byte ToggledMixed = 2;

    /// <summary>Makes a node of a role (<c>accesskit_node_new</c>), which a tree update then owns, or the caller frees.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The node.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_new")]
    public static partial nint NodeNew(byte role);

    /// <summary>Frees a node that no tree update took (<c>accesskit_node_free</c>).</summary>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_free")]
    public static partial void NodeFree(nint node);

    /// <summary>Sets a node's children, in order (<c>accesskit_node_set_children</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="length">The number of children.</param>
    /// <param name="values">The children's ids.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_children")]
    public static partial void NodeSetChildren(nint node, nuint length, ulong* values);

    /// <summary>Sets a node's label, its name (<c>accesskit_node_set_label_with_length</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The UTF-8 text, not terminated.</param>
    /// <param name="length">Its length in bytes.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_label_with_length")]
    public static partial void NodeSetLabel(nint node, byte* value, nuint length);

    /// <summary>Sets a node's description (<c>accesskit_node_set_description_with_length</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The UTF-8 text, not terminated.</param>
    /// <param name="length">Its length in bytes.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_description_with_length")]
    public static partial void NodeSetDescription(nint node, byte* value, nuint length);

    /// <summary>Sets a node's value as text (<c>accesskit_node_set_value_with_length</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The UTF-8 text, not terminated.</param>
    /// <param name="length">Its length in bytes.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_value_with_length")]
    public static partial void NodeSetValue(nint node, byte* value, nuint length);

    /// <summary>
    /// Sets the text a text input shows while it is empty (<c>accesskit_node_set_placeholder_with_length</c>); the Windows adapter
    /// gives it as the UIA HelpText while the input is empty.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The UTF-8 text, not terminated.</param>
    /// <param name="length">Its length in bytes.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_placeholder_with_length")]
    public static partial void NodeSetPlaceholder(nint node, byte* value, nuint length);

    /// <summary>Sets the id an app's author gives a node, UIA's AutomationId (<c>accesskit_node_set_author_id_with_length</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The UTF-8 text, not terminated.</param>
    /// <param name="length">Its length in bytes.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_author_id_with_length")]
    public static partial void NodeSetAuthorId(nint node, byte* value, nuint length);

    /// <summary>Declares an action a node takes (<c>accesskit_node_add_action</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="action">The action, such as <see cref="ActionClick"/>.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_add_action")]
    public static partial void NodeAddAction(nint node, byte action);

    /// <summary>Marks a node that clips its children to its bounds (<c>accesskit_node_set_clips_children</c>).</summary>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_clips_children")]
    public static partial void NodeSetClipsChildren(nint node);

    /// <summary>Sets a node's place in its set, from 0 (<c>accesskit_node_set_position_in_set</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The place.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_position_in_set")]
    public static partial void NodeSetPositionInSet(nint node, nuint value);

    /// <summary>Sets the size of the set a node's items are in (<c>accesskit_node_set_size_of_set</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The size.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_size_of_set")]
    public static partial void NodeSetSizeOfSet(nint node, nuint value);

    /// <summary>Marks a node disabled (<c>accesskit_node_set_disabled</c>).</summary>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_disabled")]
    public static partial void NodeSetDisabled(nint node);

    /// <summary>Marks a node read-only (<c>accesskit_node_set_read_only</c>).</summary>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_read_only")]
    public static partial void NodeSetReadOnly(nint node);

    /// <summary>Marks a node required (<c>accesskit_node_set_required</c>).</summary>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_required")]
    public static partial void NodeSetRequired(nint node);

    /// <summary>Sets whether a node is checked (<c>accesskit_node_set_toggled</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value"><see cref="ToggledFalse"/>, <see cref="ToggledTrue"/>, or <see cref="ToggledMixed"/>.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_toggled")]
    public static partial void NodeSetToggled(nint node, byte value);

    /// <summary>Sets whether a node that opens something is open (<c>accesskit_node_set_expanded</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">1 when open.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_expanded")]
    public static partial void NodeSetExpanded(nint node, byte value);

    /// <summary>Sets whether a node is selected (<c>accesskit_node_set_selected</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">1 when selected.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_selected")]
    public static partial void NodeSetSelected(nint node, byte value);

    /// <summary>Marks a node not valid (<c>accesskit_node_set_invalid</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value"><see cref="InvalidTrue"/>.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_invalid")]
    public static partial void NodeSetInvalid(nint node, byte value);

    /// <summary>Marks a node whose items can be selected more than one at once (<c>accesskit_node_set_multiselectable</c>).</summary>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_multiselectable")]
    public static partial void NodeSetMultiselectable(nint node);

    /// <summary>Marks a node that is changing (<c>accesskit_node_set_busy</c>).</summary>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_busy")]
    public static partial void NodeSetBusy(nint node);

    /// <summary>Marks a dialog modal (<c>accesskit_node_set_modal</c>).</summary>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_modal")]
    public static partial void NodeSetModal(nint node);

    /// <summary>Sets how a change of a node's name is read out (<c>accesskit_node_set_live</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value"><see cref="LiveOff"/>, polite, or assertive.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_live")]
    public static partial void NodeSetLive(nint node, byte value);

    /// <summary>Sets which way a node's items run (<c>accesskit_node_set_orientation</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value"><see cref="OrientationHorizontal"/>, or vertical.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_orientation")]
    public static partial void NodeSetOrientation(nint node, byte value);

    /// <summary>Sets a node's level, from 0 (<c>accesskit_node_set_level</c>); the Windows adapter gives UIA one more.</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The level.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_level")]
    public static partial void NodeSetLevel(nint node, nuint value);

    /// <summary>Adds a node that names a node (<c>accesskit_node_push_labelled_by</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="item">The id of the node that names it.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_push_labelled_by")]
    public static partial void NodePushLabelledBy(nint node, ulong item);

    /// <summary>Adds a node that describes a node (<c>accesskit_node_push_described_by</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="item">The id of the node that describes it.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_push_described_by")]
    public static partial void NodePushDescribedBy(nint node, ulong item);

    /// <summary>Adds a node a node controls (<c>accesskit_node_push_controlled</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="item">The id of the node it controls.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_push_controlled")]
    public static partial void NodePushControlled(nint node, ulong item);

    /// <summary>Sets the node that holds a node's error message (<c>accesskit_node_set_error_message</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The id of the node of the message.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_error_message")]
    public static partial void NodeSetErrorMessage(nint node, ulong value);

    /// <summary>Sets the node that is active inside a node (<c>accesskit_node_set_active_descendant</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The id of the active node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_active_descendant")]
    public static partial void NodeSetActiveDescendant(nint node, ulong value);

    /// <summary>Sets the change of a small step of a node's number (<c>accesskit_node_set_numeric_value_step</c>), UIA's SmallChange.</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The step.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_numeric_value_step")]
    public static partial void NodeSetNumericValueStep(nint node, double value);

    /// <summary>Sets the change of a large step of a node's number (<c>accesskit_node_set_numeric_value_jump</c>), UIA's LargeChange.</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The step.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_numeric_value_jump")]
    public static partial void NodeSetNumericValueJump(nint node, double value);

    /// <summary>Sets a node's number (<c>accesskit_node_set_numeric_value</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The number.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_numeric_value")]
    public static partial void NodeSetNumericValue(nint node, double value);

    /// <summary>Sets the least number a node takes (<c>accesskit_node_set_min_numeric_value</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The number.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_min_numeric_value")]
    public static partial void NodeSetMinNumericValue(nint node, double value);

    /// <summary>Sets the greatest number a node takes (<c>accesskit_node_set_max_numeric_value</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The number.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_max_numeric_value")]
    public static partial void NodeSetMaxNumericValue(nint node, double value);

    /// <summary>Sets a node's horizontal scroll position (<c>accesskit_node_set_scroll_x</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The position.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_scroll_x")]
    public static partial void NodeSetScrollX(nint node, double value);

    /// <summary>Sets a node's least horizontal scroll position (<c>accesskit_node_set_scroll_x_min</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The position.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_scroll_x_min")]
    public static partial void NodeSetScrollXMin(nint node, double value);

    /// <summary>Sets a node's greatest horizontal scroll position (<c>accesskit_node_set_scroll_x_max</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The position.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_scroll_x_max")]
    public static partial void NodeSetScrollXMax(nint node, double value);

    /// <summary>Sets a node's vertical scroll position (<c>accesskit_node_set_scroll_y</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The position.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_scroll_y")]
    public static partial void NodeSetScrollY(nint node, double value);

    /// <summary>Sets a node's least vertical scroll position (<c>accesskit_node_set_scroll_y_min</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The position.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_scroll_y_min")]
    public static partial void NodeSetScrollYMin(nint node, double value);

    /// <summary>Sets a node's greatest vertical scroll position (<c>accesskit_node_set_scroll_y_max</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The position.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_scroll_y_max")]
    public static partial void NodeSetScrollYMax(nint node, double value);

    /// <summary>
    /// Sets the transform of a node's coordinates, which also applies to its own bounds and to its descendants
    /// (<c>accesskit_node_set_transform</c>; accesskit 0.25.1's <c>common/src/lib.rs</c>, lines 2509 to 2521).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The transform.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_transform")]
    public static partial void NodeSetTransform(nint node, Affine value);

    /// <summary>Sets a node's bounds in its own coordinates (<c>accesskit_node_set_bounds</c>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The bounds.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_node_set_bounds")]
    public static partial void NodeSetBounds(nint node, Rect value);

    /// <summary>Makes the information of a tree whose root is a node (<c>accesskit_tree_info_new</c>).</summary>
    /// <param name="root">The root's id.</param>
    /// <returns>The information, which a tree update then owns.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_tree_info_new")]
    public static partial nint TreeInfoNew(ulong root);

    /// <summary>Sets the name of the toolkit a tree comes from (<c>accesskit_tree_info_set_toolkit_name_with_length</c>).</summary>
    /// <param name="tree">The information.</param>
    /// <param name="value">The UTF-8 text, not terminated.</param>
    /// <param name="length">Its length in bytes.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_tree_info_set_toolkit_name_with_length")]
    public static partial void TreeInfoSetToolkitName(nint tree, byte* value, nuint length);

    /// <summary>Makes a tree update with room for some nodes and the node that has the focus (<c>accesskit_tree_update_with_capacity_and_focus</c>).</summary>
    /// <param name="capacity">The number of nodes it takes without growing.</param>
    /// <param name="focus">The id of the node that has the focus, or of the root when none has.</param>
    /// <returns>The update, which the adapter then owns.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_tree_update_with_capacity_and_focus")]
    public static partial nint TreeUpdateWithCapacityAndFocus(nuint capacity, ulong focus);

    /// <summary>Puts a node into a tree update, which then owns it (<c>accesskit_tree_update_push_node</c>).</summary>
    /// <param name="update">The update.</param>
    /// <param name="id">The node's id.</param>
    /// <param name="node">The node.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_tree_update_push_node")]
    public static partial void TreeUpdatePushNode(nint update, ulong id, nint node);

    /// <summary>Gives a tree update the tree's information, which it then owns (<c>accesskit_tree_update_set_tree_info</c>).</summary>
    /// <param name="update">The update.</param>
    /// <param name="tree">The information.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_tree_update_set_tree_info")]
    public static partial void TreeUpdateSetTreeInfo(nint update, nint tree);

    /// <summary>Frees a request for an action, which the action handler owns (<c>accesskit_action_request_free</c>).</summary>
    /// <param name="request">The request.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_action_request_free")]
    public static partial void ActionRequestFree(nint request);

    /// <summary>
    /// Makes the adapter of a window that is not subclassed (<c>accesskit_windows_adapter_new</c>): the window passes it its
    /// <c>WM_GETOBJECT</c> and its focus.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="isWindowFocused">1 when the window has the keyboard focus now.</param>
    /// <param name="actionHandler">The callback that takes a request for an action, on any thread.</param>
    /// <param name="actionHandlerUserData">The value passed to <paramref name="actionHandler"/>.</param>
    /// <returns>The adapter.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_windows_adapter_new")]
    public static partial nint WindowsAdapterNew(
        nint hwnd,
        byte isWindowFocused,
        delegate* unmanaged<nint, nint, void> actionHandler,
        nint actionHandlerUserData
    );

    /// <summary>Frees an adapter (<c>accesskit_windows_adapter_free</c>).</summary>
    /// <param name="adapter">The adapter.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_windows_adapter_free")]
    public static partial void WindowsAdapterFree(nint adapter);

    /// <summary>
    /// Hands the adapter a tree update when assistive technology has asked for the tree, and does nothing otherwise
    /// (<c>accesskit_windows_adapter_update_if_active</c>).
    /// </summary>
    /// <param name="adapter">The adapter.</param>
    /// <param name="factory">The callback that makes the update; called only when the adapter is active.</param>
    /// <param name="userData">The value passed to <paramref name="factory"/>.</param>
    /// <returns>The events to raise (<see cref="WindowsQueuedEventsRaise"/>), or 0 when the adapter is not active.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_windows_adapter_update_if_active")]
    public static partial nint WindowsAdapterUpdateIfActive(nint adapter, delegate* unmanaged<nint, nint> factory, nint userData);

    /// <summary>Tells the adapter whether the window has the keyboard focus (<c>accesskit_windows_adapter_update_window_focus_state</c>).</summary>
    /// <param name="adapter">The adapter.</param>
    /// <param name="isFocused">1 when it has.</param>
    /// <returns>The events to raise (<see cref="WindowsQueuedEventsRaise"/>), or 0 for none.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_windows_adapter_update_window_focus_state")]
    public static partial nint WindowsAdapterUpdateWindowFocusState(nint adapter, byte isFocused);

    /// <summary>
    /// Answers a window's <c>WM_GETOBJECT</c> (<c>accesskit_windows_adapter_handle_wm_getobject</c>). The first one calls the
    /// activation handler on the window's thread, which returns the whole tree.
    /// </summary>
    /// <param name="adapter">The adapter.</param>
    /// <param name="wParam">The message's <c>wParam</c>.</param>
    /// <param name="lParam">The message's <c>lParam</c>.</param>
    /// <param name="activationHandler">The callback that makes the first tree update.</param>
    /// <param name="activationHandlerUserData">The value passed to <paramref name="activationHandler"/>.</param>
    /// <returns>The answer to return from the window procedure, when it has one.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_windows_adapter_handle_wm_getobject")]
    public static partial OptionalLresult WindowsAdapterHandleWmGetObject(
        nint adapter,
        nuint wParam,
        nint lParam,
        delegate* unmanaged<nint, nint> activationHandler,
        nint activationHandlerUserData
    );

    /// <summary>Raises the events an adapter queued, and frees them (<c>accesskit_windows_queued_events_raise</c>).</summary>
    /// <param name="events">The events; never 0.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_windows_queued_events_raise")]
    public static partial void WindowsQueuedEventsRaise(nint events);

    // What a node holds, read back: declared for the tests only (design §4.4.1, "How row AX1 is checked"), which read a node the
    // provider makes before an update takes it.

    /// <summary>Reads a node's role (<c>accesskit_node_role</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The role.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_role")]
    public static partial byte NodeRole(nint node);

    /// <summary>Reads a node's label (<c>accesskit_node_label</c>), which <see cref="StringFree"/> frees.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The UTF-8 text, terminated, or null.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_label")]
    public static partial byte* NodeLabel(nint node);

    /// <summary>Reads a node's description (<c>accesskit_node_description</c>), which <see cref="StringFree"/> frees.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The UTF-8 text, terminated, or null.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_description")]
    public static partial byte* NodeDescription(nint node);

    /// <summary>Frees a text AccessKit returned (<c>accesskit_string_free</c>).</summary>
    /// <param name="text">The text.</param>
    [LibraryImport(Library, EntryPoint = "accesskit_string_free")]
    public static partial void StringFree(byte* text);

    /// <summary>Reads whether a node is not valid (<c>accesskit_node_invalid</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_invalid")]
    public static partial OptionalByte NodeInvalid(nint node);

    /// <summary>Reads whether a node's items can be selected more than one at once (<c>accesskit_node_is_multiselectable</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>1 when they can.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_is_multiselectable")]
    public static partial byte NodeIsMultiselectable(nint node);

    /// <summary>Reads whether a node is changing (<c>accesskit_node_is_busy</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>1 when it is.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_is_busy")]
    public static partial byte NodeIsBusy(nint node);

    /// <summary>Reads whether a node is modal (<c>accesskit_node_is_modal</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>1 when it is.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_is_modal")]
    public static partial byte NodeIsModal(nint node);

    /// <summary>Reads whether a node is read-only (<c>accesskit_node_is_read_only</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>1 when it is.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_is_read_only")]
    public static partial byte NodeIsReadOnly(nint node);

    /// <summary>Reads whether a node is open (<c>accesskit_node_is_expanded</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_is_expanded")]
    public static partial OptionalByte NodeIsExpanded(nint node);

    /// <summary>Reads whether a node is checked (<c>accesskit_node_toggled</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_toggled")]
    public static partial OptionalByte NodeToggled(nint node);

    /// <summary>Reads whether a node is selected (<c>accesskit_node_is_selected</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_is_selected")]
    public static partial OptionalByte NodeIsSelected(nint node);

    /// <summary>Reads how a change of a node's name is read out (<c>accesskit_node_live</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_live")]
    public static partial OptionalByte NodeLive(nint node);

    /// <summary>Reads which way a node's items run (<c>accesskit_node_orientation</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_orientation")]
    public static partial OptionalByte NodeOrientation(nint node);

    /// <summary>Reads a node's small step (<c>accesskit_node_numeric_value_step</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_numeric_value_step")]
    public static partial OptionalDouble NodeNumericValueStep(nint node);

    /// <summary>Reads a node's large step (<c>accesskit_node_numeric_value_jump</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_numeric_value_jump")]
    public static partial OptionalDouble NodeNumericValueJump(nint node);

    /// <summary>Reads a node's level (<c>accesskit_node_level</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_level")]
    public static partial OptionalIndex NodeLevel(nint node);

    /// <summary>Reads a node's place in its set (<c>accesskit_node_position_in_set</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_position_in_set")]
    public static partial OptionalIndex NodePositionInSet(nint node);

    /// <summary>Reads the size of the set a node holds (<c>accesskit_node_size_of_set</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_size_of_set")]
    public static partial OptionalIndex NodeSizeOfSet(nint node);

    /// <summary>Reads the nodes that name a node (<c>accesskit_node_labelled_by</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The ids, which the node owns.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_labelled_by")]
    public static partial NodeIds NodeLabelledBy(nint node);

    /// <summary>Reads the nodes that describe a node (<c>accesskit_node_described_by</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The ids, which the node owns.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_described_by")]
    public static partial NodeIds NodeDescribedBy(nint node);

    /// <summary>Reads the nodes a node controls (<c>accesskit_node_controls</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The ids, which the node owns.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_controls")]
    public static partial NodeIds NodeControls(nint node);

    /// <summary>Reads a node's children (<c>accesskit_node_children</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The ids, which the node owns.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_children")]
    public static partial NodeIds NodeChildren(nint node);

    /// <summary>Reads the node of a node's error message (<c>accesskit_node_error_message</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The id, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_error_message")]
    public static partial OptionalNodeId NodeErrorMessage(nint node);

    /// <summary>Reads the node active inside a node (<c>accesskit_node_active_descendant</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The id, if set.</returns>
    [LibraryImport(Library, EntryPoint = "accesskit_node_active_descendant")]
    public static partial OptionalNodeId NodeActiveDescendant(nint node);

    /// <summary>A one-byte value that may be absent (<c>accesskit_opt_bool</c>, <c>_opt_live</c>, and the others of one byte).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct OptionalByte
    {
        private readonly byte _hasValue;
        private readonly byte _value;

        /// <summary>Gets a value indicating whether there is a value.</summary>
        public bool HasValue => _hasValue != 0;

        /// <summary>Gets the value.</summary>
        public byte Value => _value;
    }

    /// <summary>A number that may be absent (<c>accesskit_opt_double</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct OptionalDouble
    {
        private readonly byte _hasValue;
        private readonly double _value;

        /// <summary>Gets a value indicating whether there is a value.</summary>
        public bool HasValue => _hasValue != 0;

        /// <summary>Gets the value.</summary>
        public double Value => _value;
    }

    /// <summary>An index that may be absent (<c>accesskit_opt_index</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct OptionalIndex
    {
        private readonly byte _hasValue;
        private readonly nuint _value;

        /// <summary>Gets a value indicating whether there is a value.</summary>
        public bool HasValue => _hasValue != 0;

        /// <summary>Gets the value.</summary>
        public nuint Value => _value;
    }

    /// <summary>A node's id that may be absent (<c>accesskit_opt_node_id</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct OptionalNodeId
    {
        private readonly byte _hasValue;
        private readonly ulong _value;

        /// <summary>Gets a value indicating whether there is a value.</summary>
        public bool HasValue => _hasValue != 0;

        /// <summary>Gets the value.</summary>
        public ulong Value => _value;
    }

    /// <summary>The ids of nodes (<c>accesskit_node_ids</c>), which the node read owns.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct NodeIds
    {
        private readonly nuint _length;
        private readonly ulong* _values;

        /// <summary>Gets the ids.</summary>
        public ReadOnlySpan<ulong> Values => new(_values, (int)_length);
    }

    /// <summary>A rectangle by its edges (<c>accesskit_rect</c>, from kurbo).</summary>
    /// <param name="x0">The left edge.</param>
    /// <param name="y0">The top edge.</param>
    /// <param name="x1">The right edge.</param>
    /// <param name="y1">The bottom edge.</param>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Rect(double x0, double y0, double x1, double y1)
    {
        /// <summary>Gets the left edge.</summary>
        public double X0 { get; } = x0;

        /// <summary>Gets the top edge.</summary>
        public double Y0 { get; } = y0;

        /// <summary>Gets the right edge.</summary>
        public double X1 { get; } = x1;

        /// <summary>Gets the bottom edge.</summary>
        public double Y1 { get; } = y1;
    }

    /// <summary>
    /// A 2D affine transform (<c>accesskit_affine</c>, from kurbo): a point (x, y) goes to (A·x + C·y + E, B·x + D·y + F).
    /// </summary>
    /// <param name="a">The x scale.</param>
    /// <param name="b">The y shear.</param>
    /// <param name="c">The x shear.</param>
    /// <param name="d">The y scale.</param>
    /// <param name="e">The x translation.</param>
    /// <param name="f">The y translation.</param>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Affine(double a, double b, double c, double d, double e, double f)
    {
        /// <summary>Gets the x scale.</summary>
        public double A { get; } = a;

        /// <summary>Gets the y shear.</summary>
        public double B { get; } = b;

        /// <summary>Gets the x shear.</summary>
        public double C { get; } = c;

        /// <summary>Gets the y scale.</summary>
        public double D { get; } = d;

        /// <summary>Gets the x translation.</summary>
        public double E { get; } = e;

        /// <summary>Gets the y translation.</summary>
        public double F { get; } = f;
    }

    /// <summary>
    /// A request for an action (<c>accesskit_action_request</c>, accesskit.h lines 1065 to 1070): the action at byte 0, the tree's
    /// id at 1 to 16, the node's id at 24, and the optional data at 32; 80 bytes on x64.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ActionRequest
    {
        /// <summary>The action, such as <see cref="ActionClick"/>.</summary>
        public byte Action;

        /// <summary>The id of the tree the node is in.</summary>
        public TreeId TargetTree;

        /// <summary>The id of the node the action is for.</summary>
        public ulong TargetNode;

        /// <summary>The action's data, if any.</summary>
        public OptionalActionData Data;
    }

    /// <summary>The id of a tree (<c>accesskit_tree_id</c>: 16 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TreeId
    {
        /// <summary>The id's bytes.</summary>
        public fixed byte Bytes[16];
    }

    /// <summary>The data of an action that may be absent (<c>accesskit_opt_action_data</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OptionalActionData
    {
        /// <summary>1 when there is data.</summary>
        public byte HasValue;

        /// <summary>The data.</summary>
        public ActionData Value;
    }

    /// <summary>The data of an action (<c>accesskit_action_data</c>: a C enum's tag, then a union of 32 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ActionData
    {
        /// <summary>Which member of the union holds the data, such as <see cref="ActionDataValue"/>.</summary>
        public int Tag;

        /// <summary>The data.</summary>
        public ActionDataUnion Value;
    }

    /// <summary>The union of an action's data: a text (UTF-8, terminated) or a number, among others not read.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct ActionDataUnion
    {
        /// <summary>The text, for <see cref="ActionDataValue"/>.</summary>
        [FieldOffset(0)]
        public byte* Text;

        /// <summary>The number, for a numeric value.</summary>
        [FieldOffset(0)]
        public double Number;
    }

    /// <summary>An answer that may be absent (<c>accesskit_opt_lresult</c>: a one-byte bool, then the <c>LRESULT</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct OptionalLresult
    {
        private readonly byte _hasValue;
        private readonly nint _value;

        /// <summary>Gets a value indicating whether the adapter answered.</summary>
        public bool HasValue => _hasValue != 0;

        /// <summary>Gets the answer.</summary>
        public nint Value => _value;
    }
}
