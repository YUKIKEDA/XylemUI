using System.Windows.Automation;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// What row AX1 adds, as AccessKit's nodes hold it and as a real UIA client reads it (design §4.4.1, "How row AX1 is checked").
public sealed unsafe partial class UiaTests
{
    // UIA's ids that UIAutomationClient's managed classes do not name (UIAutomationClient.h).
    private const int LiveSettingId = 30135;
    private const int LevelId = 30154;
    private const int FullDescriptionId = 30159;
    private const int LiveRegionChangedEventId = 20024;
    private const int AriaPropertiesId = 30102;
    private const int ControllerForId = 30104;

    // The sliders and ranges whose IsReadOnly RangeValue_FollowsTheSteps_AndSetValueReachesTheHandler reads.
    private static readonly string[] ReadOnlyChecked = ["Both", "Fixed", "Disk", "Bar"];

    // The table of roles (design §4.4.1): AccessKit's role and UIA's control type of each role row AX1 adds, and of the earlier ones.
    private static readonly Dictionary<SemanticsRole, byte> AccessKitRoles = new()
    {
        [SemanticsRole.Window] = AccessKit.RoleWindow,
        [SemanticsRole.Dialog] = AccessKit.RoleDialog,
        [SemanticsRole.Group] = AccessKit.RoleGroup,
        [SemanticsRole.Text] = AccessKit.RoleLabel,
        [SemanticsRole.Hyperlink] = AccessKit.RoleLink,
        [SemanticsRole.Image] = AccessKit.RoleImage,
        [SemanticsRole.Button] = AccessKit.RoleButton,
        [SemanticsRole.CheckBox] = AccessKit.RoleCheckBox,
        [SemanticsRole.RadioButton] = AccessKit.RoleRadioButton,
        [SemanticsRole.ComboBox] = AccessKit.RoleComboBox,
        [SemanticsRole.Edit] = AccessKit.RoleTextInput,
        [SemanticsRole.List] = AccessKit.RoleList,
        [SemanticsRole.ListItem] = AccessKit.RoleListItem,
        [SemanticsRole.Menu] = AccessKit.RoleMenu,
        [SemanticsRole.MenuBar] = AccessKit.RoleMenuBar,
        [SemanticsRole.MenuItem] = AccessKit.RoleMenuItem,
        [SemanticsRole.Slider] = AccessKit.RoleSlider,
        [SemanticsRole.ProgressBar] = AccessKit.RoleProgressIndicator,
        [SemanticsRole.ScrollViewer] = AccessKit.RoleScrollView,
        [SemanticsRole.Tab] = AccessKit.RoleTabList,
        [SemanticsRole.TabItem] = AccessKit.RoleTab,
        [SemanticsRole.ToolTip] = AccessKit.RoleTooltip,
        [SemanticsRole.Separator] = AccessKit.RoleSplitter,
        [SemanticsRole.Navigation] = 98,
        [SemanticsRole.TitleBar] = 126,
        [SemanticsRole.TabPanel] = 122,
        [SemanticsRole.Toolbar] = 127,
        [SemanticsRole.Region] = 103,
        [SemanticsRole.Status] = 116,
        [SemanticsRole.Alert] = 44,
        [SemanticsRole.AlertDialog] = 45,
        [SemanticsRole.Meter] = 97,
        [SemanticsRole.Switch] = 29,
        [SemanticsRole.RadioGroup] = 102,
        [SemanticsRole.SpinButton] = 114,
        [SemanticsRole.SearchBox] = 32,
        [SemanticsRole.ScrollBar] = 107,
        [SemanticsRole.MenuItemCheckBox] = 94,
        [SemanticsRole.MenuItemRadio] = 95,
        [SemanticsRole.ListBox] = 87,
        [SemanticsRole.ListBoxItem] = 10,
        [SemanticsRole.Tree] = 129,
        [SemanticsRole.TreeItem] = 9,
        [SemanticsRole.TreeGrid] = 130,
        [SemanticsRole.Table] = 25,
        [SemanticsRole.Grid] = 76,
        [SemanticsRole.Row] = 6,
        [SemanticsRole.Cell] = 2,
        [SemanticsRole.GridCell] = 77,
        [SemanticsRole.ColumnHeader] = 22,
        [SemanticsRole.RowHeader] = 21,
        [SemanticsRole.Heading] = 80,
        [SemanticsRole.Disclosure] = 67,
        [SemanticsRole.DateInput] = 33,
        [SemanticsRole.TimeInput] = 37,
        [SemanticsRole.DateTimeInput] = 34,
        [SemanticsRole.Log] = 88,
        [SemanticsRole.Feed] = 71,
        [SemanticsRole.Article] = 47,
        [SemanticsRole.Document] = 68,
        [SemanticsRole.Paragraph] = 13,
        [SemanticsRole.Code] = 54,
        [SemanticsRole.Figure] = 73,
        [SemanticsRole.Pane] = 20,
    };

    /// <summary>
    /// Every role but None is AccessKit's role of the table of roles, by the values of <c>accesskit.h</c>; a role added with no
    /// mapping fails here.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The table of roles, with AccessKit's values written as numbers from accesskit-c 0.23.2's header for the roles row AX1 adds.
    ///
    /// Steps:
    /// - For each value of <see cref="SemanticsRole"/> but None, map a node of that role.
    ///
    /// Expected:
    /// - The table's role for each, never <c>ACCESSKIT_ROLE_UNKNOWN</c>; the table has a row for each role.
    /// </remarks>
    [Fact]
    public void RoleOf_EveryRole_IsTheTablesAccessKitRole()
    {
        SemanticsRole[] roles = [.. Enum.GetValues<SemanticsRole>().Where(static r => r != SemanticsRole.None)];
        foreach (SemanticsRole role in roles)
        {
            byte made = Win32Accessibility.RoleOf(new AccessibilityNode(new AccessibilityNodeId(1), default) { Role = role });
            Assert.Equal((role, AccessKitRoles[role]), (role, made));
            Assert.NotEqual(AccessKit.RoleUnknown, made);
        }

        Assert.Equal(roles.Length, AccessKitRoles.Count);
    }

    /// <summary>
    /// The provider writes the states, the range's steps, the relations, the live setting, the level, the place, the count, and the
    /// orientation into AccessKit's node, read back with AccessKit's own functions; what a node does not have is not written.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window "AccessKit node" and its provider; a node of the role Slider named "Volume", described "Loudness", holding a child
    ///   of id 15, with the states ReadOnly, Invalid, Multiselectable, Busy, Modal, Expandable, and Expanded, the range 0 to 10 at 4 with the steps 1 and 5, the live setting Assertive, the orientation Vertical,
    ///   the level 1 and the place 2 (from 0), the count 7, labelled by 3 and 4, described by 5, controlling 6 and 7, the error message
    ///   9, and the active descendant 10; a node of the role Switch, off, with none of them; a ListBoxItem and a TreeItem not selected;
    ///   a node of the role Status with no live setting of its own.
    ///
    /// Steps:
    /// - Make each node's AccessKit node; read it back; free it.
    ///
    /// Expected:
    /// - The first node's role, name, description, read-only state, children, and values as given, its live setting 2 and orientation
    ///   1; the second's role Switch, toggled false, not read-only, with no name and no children, and nothing else set; the
    ///   items selected false; the status's live setting 1 (polite).
    /// </remarks>
    [Fact]
    public void MakeNode_WritesStatesStepsRelationsLiveLevelPlaceCountAndOrientation() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("AccessKit node", 200, 200);
            using var provider = new Win32Accessibility(window, new UiTree());
            var parent = new AccessibilityNode(new AccessibilityNodeId(1), default) { Role = SemanticsRole.Group };
            var full = new AccessibilityNode(new AccessibilityNodeId(2), default)
            {
                Parent = parent,
                Role = SemanticsRole.Slider,
                Name = "Volume",
                Description = "Loudness",
                States =
                    AccessibilityStates.ReadOnly
                    | AccessibilityStates.Invalid
                    | AccessibilityStates.Multiselectable
                    | AccessibilityStates.Busy
                    | AccessibilityStates.Modal
                    | AccessibilityStates.Expandable
                    | AccessibilityStates.Expanded,
                Range = new SemanticsRange(0, 10, 4, Step: 1, LargeStep: 5),
                Live = SemanticsLive.Assertive,
                Orientation = SemanticsOrientation.Vertical,
                Level = 1,
                PositionInSet = 2,
                SizeOfSet = 7,
                ErrorMessage = new AccessibilityNodeId(9),
                ActiveDescendant = new AccessibilityNodeId(10),
            };
            _ = full.SetRelation(AccessibilityRelation.LabelledBy, [new AccessibilityNodeId(3), new AccessibilityNodeId(4)]);
            _ = full.SetRelation(AccessibilityRelation.DescribedBy, [new AccessibilityNodeId(5)]);
            _ = full.SetRelation(AccessibilityRelation.Controls, [new AccessibilityNodeId(6), new AccessibilityNodeId(7)]);
            full.Children.Add(new AccessibilityNode(new AccessibilityNodeId(15), default) { Parent = full, Role = SemanticsRole.Text });
            var bare = new AccessibilityNode(new AccessibilityNodeId(11), default) { Parent = parent, Role = SemanticsRole.Switch };
            var option = new AccessibilityNode(new AccessibilityNodeId(12), default) { Parent = parent, Role = SemanticsRole.ListBoxItem };
            var item = new AccessibilityNode(new AccessibilityNodeId(13), default) { Parent = parent, Role = SemanticsRole.TreeItem };
            var status = new AccessibilityNode(new AccessibilityNodeId(14), default)
            {
                Parent = parent,
                Role = SemanticsRole.Status,
                Live = SemanticsLive.Polite,
            };

            nint made = provider.MakeNode(full);
            try
            {
                Assert.Equal(AccessKit.RoleSlider, AccessKit.NodeRole(made));
                Assert.Equal(("Volume", "Loudness"), (TakeText(AccessKit.NodeLabel(made)), TakeText(AccessKit.NodeDescription(made))));
                Assert.Equal((byte)1, AccessKit.NodeIsReadOnly(made));
                Assert.Equal([15UL], AccessKit.NodeChildren(made).Values.ToArray());
                Assert.Equal((true, (byte)0), (AccessKit.NodeInvalid(made).HasValue, AccessKit.NodeInvalid(made).Value));
                Assert.Equal(
                    ((byte)1, (byte)1, (byte)1),
                    (AccessKit.NodeIsMultiselectable(made), AccessKit.NodeIsBusy(made), AccessKit.NodeIsModal(made))
                );
                Assert.Equal((true, (byte)1), (AccessKit.NodeIsExpanded(made).HasValue, AccessKit.NodeIsExpanded(made).Value));
                Assert.Equal((1.0, 5.0), (AccessKit.NodeNumericValueStep(made).Value, AccessKit.NodeNumericValueJump(made).Value));
                Assert.Equal(((byte)2, (byte)1), (AccessKit.NodeLive(made).Value, AccessKit.NodeOrientation(made).Value));
                Assert.Equal(
                    ((nuint)1, (nuint)2, (nuint)7),
                    (AccessKit.NodeLevel(made).Value, AccessKit.NodePositionInSet(made).Value, AccessKit.NodeSizeOfSet(made).Value)
                );
                Assert.Equal([3UL, 4UL], AccessKit.NodeLabelledBy(made).Values.ToArray());
                Assert.Equal([5UL], AccessKit.NodeDescribedBy(made).Values.ToArray());
                Assert.Equal([6UL, 7UL], AccessKit.NodeControls(made).Values.ToArray());
                Assert.Equal((9UL, 10UL), (AccessKit.NodeErrorMessage(made).Value, AccessKit.NodeActiveDescendant(made).Value));
            }
            finally
            {
                AccessKit.NodeFree(made);
            }

            made = provider.MakeNode(bare);
            try
            {
                Assert.Equal(
                    (AccessKit.RoleSwitch, (string?)null, (byte)0),
                    (AccessKit.NodeRole(made), TakeText(AccessKit.NodeLabel(made)), AccessKit.NodeIsReadOnly(made))
                );
                Assert.Empty(AccessKit.NodeChildren(made).Values.ToArray());
                Assert.Equal((true, AccessKit.ToggledFalse), (AccessKit.NodeToggled(made).HasValue, AccessKit.NodeToggled(made).Value));
                Assert.False(AccessKit.NodeInvalid(made).HasValue);
                Assert.Equal(
                    ((byte)0, (byte)0, (byte)0),
                    (AccessKit.NodeIsMultiselectable(made), AccessKit.NodeIsBusy(made), AccessKit.NodeIsModal(made))
                );
                Assert.False(AccessKit.NodeIsExpanded(made).HasValue);
                Assert.False(AccessKit.NodeLive(made).HasValue || AccessKit.NodeOrientation(made).HasValue);
                Assert.False(
                    AccessKit.NodeLevel(made).HasValue || AccessKit.NodePositionInSet(made).HasValue || AccessKit.NodeSizeOfSet(made).HasValue
                );
                Assert.False(AccessKit.NodeErrorMessage(made).HasValue || AccessKit.NodeActiveDescendant(made).HasValue);
                Assert.Equal(0, AccessKit.NodeLabelledBy(made).Values.Length + AccessKit.NodeControls(made).Values.Length);
            }
            finally
            {
                AccessKit.NodeFree(made);
            }

            foreach (AccessibilityNode unselected in new[] { option, item })
            {
                made = provider.MakeNode(unselected);
                try
                {
                    Assert.Equal((true, (byte)0), (AccessKit.NodeIsSelected(made).HasValue, AccessKit.NodeIsSelected(made).Value));
                }
                finally
                {
                    AccessKit.NodeFree(made);
                }
            }

            made = provider.MakeNode(status);
            try
            {
                Assert.Equal((true, (byte)1), (AccessKit.NodeLive(made).HasValue, AccessKit.NodeLive(made).Value));
            }
            finally
            {
                AccessKit.NodeFree(made);
            }
        });

    /// <summary>
    /// The provider writes none of the values row AX1 added for a node whose information stopped giving them: no level, live setting,
    /// orientation, or step.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "AccessKit defaults" with a slider whose information is a signal: the level 3, the live setting Assertive,
    ///   the orientation Vertical, and the range 0 to 10 at 5 with the steps 1 and 2; UIA has read the tree.
    ///
    /// Steps:
    /// - Make the slider's AccessKit node and read it back. Set the signal to the same slider with the range alone; run the
    ///   accessibility stage; make and read the node again.
    ///
    /// Expected:
    /// - First the level 2, the live setting 2, the orientation 1, and the steps 1 and 2. Then none of them is set.
    /// </remarks>
    [Fact]
    public void MakeNode_AfterTheAddedValuesGo_WritesNoneOfThem() =>
        ui.Invoke(() =>
        {
            var plainInfo = new AccessibilityInfo
            {
                Role = SemanticsRole.Slider,
                Name = "s",
                Range = new SemanticsRange(0, 10, 5),
            };
            var info = Signal.Create(
                plainInfo with
                {
                    Level = 3,
                    Live = SemanticsLive.Assertive,
                    Orientation = SemanticsOrientation.Vertical,
                    Range = new SemanticsRange(0, 10, 5, Step: 1, LargeStep: 2),
                }
            );
            Element slider = default;
            using var window = new DesktopWindow(
                () => slider = Box().Width(50).Height(10).Accessible(info),
                new WindowOptions("AccessKit defaults", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            _ = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);
            Win32Accessibility provider = window.Window.Accessibility!;
            (
                AccessKit.OptionalIndex Level,
                AccessKit.OptionalByte Live,
                AccessKit.OptionalByte Orientation,
                AccessKit.OptionalDouble Step,
                AccessKit.OptionalDouble Jump
            ) given = Read();

            info.Set(plainInfo);
            RunAccessibilityStage(window);
            (
                AccessKit.OptionalIndex Level,
                AccessKit.OptionalByte Live,
                AccessKit.OptionalByte Orientation,
                AccessKit.OptionalDouble Step,
                AccessKit.OptionalDouble Jump
            ) gone = Read();

            Assert.Equal(
                ((nuint)2, (byte)2, (byte)1, 1.0, 2.0),
                (given.Level.Value, given.Live.Value, given.Orientation.Value, given.Step.Value, given.Jump.Value)
            );
            Assert.Equal(
                (false, false, false, false, false),
                (gone.Level.HasValue, gone.Live.HasValue, gone.Orientation.HasValue, gone.Step.HasValue, gone.Jump.HasValue)
            );

            (AccessKit.OptionalIndex, AccessKit.OptionalByte, AccessKit.OptionalByte, AccessKit.OptionalDouble, AccessKit.OptionalDouble) Read()
            {
                nint made = provider.MakeNode(window.Tree.Accessibility.Find(slider.Node)!);
                try
                {
                    return (
                        AccessKit.NodeLevel(made),
                        AccessKit.NodeLive(made),
                        AccessKit.NodeOrientation(made),
                        AccessKit.NodeNumericValueStep(made),
                        AccessKit.NodeNumericValueJump(made)
                    );
                }
                finally
                {
                    AccessKit.NodeFree(made);
                }
            }
        });

    /// <summary>
    /// UIA reads the control type of every role row AX1 adds, in one read of the tree.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA roles" of 300 x 400 with a box 6 high for each role after Separator, named by the role.
    ///
    /// Steps:
    /// - On the client's thread, read every element's name and control type.
    ///
    /// Expected:
    /// - The control type of the table of roles for each name.
    /// </remarks>
    [Fact]
    public void Tree_IsReadByUia_WithTheControlTypeOfEveryAddedRole() =>
        ui.Invoke(() =>
        {
            var expected = new Dictionary<SemanticsRole, ControlType>
            {
                [SemanticsRole.Navigation] = ControlType.Group,
                [SemanticsRole.TitleBar] = ControlType.Pane,
                [SemanticsRole.TabPanel] = ControlType.Pane,
                [SemanticsRole.Toolbar] = ControlType.ToolBar,
                [SemanticsRole.Region] = ControlType.Group,
                [SemanticsRole.Status] = ControlType.StatusBar,
                [SemanticsRole.Alert] = ControlType.Text,
                [SemanticsRole.AlertDialog] = ControlType.Window,
                [SemanticsRole.Meter] = ControlType.ProgressBar,
                [SemanticsRole.Switch] = ControlType.Button,
                [SemanticsRole.RadioGroup] = ControlType.Group,
                [SemanticsRole.SpinButton] = ControlType.Spinner,
                [SemanticsRole.SearchBox] = ControlType.Edit,
                [SemanticsRole.ScrollBar] = ControlType.ScrollBar,
                [SemanticsRole.MenuItemCheckBox] = ControlType.CheckBox,
                [SemanticsRole.MenuItemRadio] = ControlType.RadioButton,
                [SemanticsRole.ListBox] = ControlType.List,
                [SemanticsRole.ListBoxItem] = ControlType.ListItem,
                [SemanticsRole.Tree] = ControlType.Tree,
                [SemanticsRole.TreeItem] = ControlType.TreeItem,
                [SemanticsRole.TreeGrid] = ControlType.DataGrid,
                [SemanticsRole.Table] = ControlType.Table,
                [SemanticsRole.Grid] = ControlType.DataGrid,
                [SemanticsRole.Row] = ControlType.DataItem,
                [SemanticsRole.Cell] = ControlType.DataItem,
                [SemanticsRole.GridCell] = ControlType.DataItem,
                [SemanticsRole.ColumnHeader] = ControlType.DataItem,
                [SemanticsRole.RowHeader] = ControlType.DataItem,
                [SemanticsRole.Heading] = ControlType.Text,
                [SemanticsRole.Disclosure] = ControlType.Button,
                [SemanticsRole.DateInput] = ControlType.Edit,
                [SemanticsRole.TimeInput] = ControlType.Edit,
                [SemanticsRole.DateTimeInput] = ControlType.Edit,
                [SemanticsRole.Log] = ControlType.Group,
                [SemanticsRole.Feed] = ControlType.Group,
                [SemanticsRole.Article] = ControlType.Group,
                [SemanticsRole.Document] = ControlType.Document,
                [SemanticsRole.Paragraph] = ControlType.Group,
                [SemanticsRole.Code] = ControlType.Text,
                [SemanticsRole.Figure] = ControlType.Group,
                [SemanticsRole.Pane] = ControlType.Pane,
            };
            using var window = new DesktopWindow(
                () => Column([.. expected.Keys.Select(r => Box().Height(6).Accessible(new AccessibilityInfo { Role = r, Name = r.ToString() }))]),
                new WindowOptions("UIA roles", 300, 400),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            Dictionary<string, ControlType> read = ReadByUia(() =>
            {
                var types = new Dictionary<string, ControlType>();
                foreach (
                    AutomationElement element in AutomationElement.FromHandle((nint)hwnd).FindAll(TreeScope.Descendants, Condition.TrueCondition)
                )
                {
                    _ = types.TryAdd(element.Current.Name, element.Current.ControlType);
                }

                return types;
            });

            Assert.Equal(41, expected.Count);
            foreach ((SemanticsRole role, ControlType type) in expected)
            {
                Assert.Equal((role, type.ProgrammaticName), (role, read[role.ToString()].ProgrammaticName));
            }
        });

    /// <summary>
    /// A switch and a check box of a menu have UIA's Toggle, Off while off; a radio button in a radio group and a radio item of a
    /// menu have SelectionItem.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA toggle roles" with a Switch "Wifi" and a MenuItemCheckBox "Bold", both off, a RadioGroup holding a
    ///   RadioButton "Small", and a MenuItemRadio "Large".
    ///
    /// Steps:
    /// - On the client's thread, read the toggle states and whether the radio nodes have SelectionItem.
    ///
    /// Expected:
    /// - Off and Off; true and true.
    /// </remarks>
    [Fact]
    public void ToggleRoles_HaveToggle_AndRadioRolesHaveSelectionItem() =>
        ui.Invoke(() =>
        {
            using var window = new DesktopWindow(
                () =>
                    Column(
                        Named(SemanticsRole.Switch, "Wifi"),
                        Named(SemanticsRole.MenuItemCheckBox, "Bold"),
                        Box(Named(SemanticsRole.RadioButton, "Small")).Accessible(new AccessibilityInfo { Role = SemanticsRole.RadioGroup }),
                        Named(SemanticsRole.MenuItemRadio, "Large")
                    ),
                new WindowOptions("UIA toggle roles", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            (ToggleState, ToggleState, bool, bool) read = ReadByUia(() =>
            {
                AutomationElement root = AutomationElement.FromHandle((nint)hwnd);
                return (
                    ((TogglePattern)Named(root, "Wifi").GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState,
                    ((TogglePattern)Named(root, "Bold").GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState,
                    Named(root, "Small").TryGetCurrentPattern(SelectionItemPattern.Pattern, out _),
                    Named(root, "Large").TryGetCurrentPattern(SelectionItemPattern.Pattern, out _)
                );
            });

            Assert.Equal((ToggleState.Off, ToggleState.Off, true, true), read);
        });

    /// <summary>
    /// A list box, a tree, and a radio group have UIA's Selection, which can select many with Multiselectable and needs a selection
    /// with Required; a container that selects many raises AddedToSelection when an item is selected.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA selection" with a ListBox "Fruit", Multiselectable, of the items "Apple" (selected) and "Pear" (whose
    ///   information is a signal); a Tree "Files", Required, of a TreeItem "Doc"; a RadioGroup "Size" of a RadioButton "Small".
    ///
    /// Steps:
    /// - Read each container's Selection. Listen for AddedToSelection on the list; select "Pear".
    ///
    /// Expected:
    /// - Selection on all three; CanSelectMultiple true for "Fruit", false for "Size"; IsSelectionRequired true for "Files"; an
    ///   event of "Pear".
    /// </remarks>
    [Fact]
    public void SelectionContainers_HaveSelection_AndAddedToSelectionIsRaised() =>
        ui.Invoke(() =>
        {
            Signal<AccessibilityInfo> pear = Signal.Create(new AccessibilityInfo { Role = SemanticsRole.ListBoxItem, Name = "Pear" });
            using var window = new DesktopWindow(
                () =>
                    Column(
                        Column(
                                Box()
                                    .Height(10)
                                    .Accessible(
                                        new AccessibilityInfo
                                        {
                                            Role = SemanticsRole.ListBoxItem,
                                            Name = "Apple",
                                            States = SemanticsStates.Selected,
                                        }
                                    ),
                                Box().Height(10).Accessible(pear)
                            )
                            .Accessible(Container(SemanticsRole.ListBox, "Fruit", SemanticsStates.Multiselectable)),
                        Box(Named(SemanticsRole.TreeItem, "Doc")).Accessible(Container(SemanticsRole.Tree, "Files", SemanticsStates.Required)),
                        Box(Named(SemanticsRole.RadioButton, "Small")).Accessible(Container(SemanticsRole.RadioGroup, "Size", SemanticsStates.None))
                    ),
                new WindowOptions("UIA selection", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            (bool, bool, bool) multiple = ReadByUia(() =>
            {
                AutomationElement root = AutomationElement.FromHandle((nint)hwnd);
                SelectionPattern fruit = (SelectionPattern)Named(root, "Fruit").GetCurrentPattern(SelectionPattern.Pattern);
                SelectionPattern files = (SelectionPattern)Named(root, "Files").GetCurrentPattern(SelectionPattern.Pattern);
                SelectionPattern size = (SelectionPattern)Named(root, "Size").GetCurrentPattern(SelectionPattern.Pattern);
                return (fruit.Current.CanSelectMultiple, files.Current.IsSelectionRequired, size.Current.CanSelectMultiple);
            });
            object? added = ListenByUia(
                done =>
                {
                    string? name = null;
                    var handler = new AutomationEventHandler(
                        (sender, _) =>
                        {
                            name = (sender as AutomationElement)?.Current.Name;
                            done.Set();
                        }
                    );
                    Automation.AddAutomationEventHandler(
                        SelectionItemPattern.ElementAddedToSelectionEvent,
                        Named(AutomationElement.FromHandle((nint)hwnd), "Fruit"),
                        TreeScope.Subtree,
                        handler
                    );
                    return (handler, () => name);
                },
                () =>
                {
                    pear.Set(pear.Peek() with { States = SemanticsStates.Selected });
                    RunAccessibilityStage(window);
                }
            );

            Assert.Equal((true, true, false), multiple);
            Assert.Equal("Pear", added);
        });

    /// <summary>
    /// UIA's Expand on a tree item calls its handler of Expand; a tree item that is not Expandable is a leaf.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA tree item" with a Tree of a TreeItem "Folder", Expandable, whose handler of Expand counts, and a
    ///   TreeItem "Leaf".
    ///
    /// Steps:
    /// - On the client's thread, read the state of "Leaf"; Expand "Folder"; run the loop until it counted.
    ///
    /// Expected:
    /// - "Leaf" is LeafNode; one Expand.
    /// </remarks>
    [Fact]
    public void TreeItemExpand_CallsItsHandler_AndAnItemNotExpandableIsALeaf() =>
        ui.Invoke(() =>
        {
            int expands = 0;
            using var window = new DesktopWindow(
                () =>
                    Column(
                            Box()
                                .Height(10)
                                .Accessible(
                                    new AccessibilityInfo
                                    {
                                        Role = SemanticsRole.TreeItem,
                                        Name = "Folder",
                                        States = SemanticsStates.Expandable,
                                    }
                                )
                                .OnExpand(() => expands++),
                            Named(SemanticsRole.TreeItem, "Leaf")
                        )
                        .Accessible(new AccessibilityInfo { Role = SemanticsRole.Tree }),
                new WindowOptions("UIA tree item", 200, 200),
                useWarp: true
            );
            ExpandCollapseState leaf = default;

            ActByUia(
                window,
                root =>
                {
                    leaf = ((ExpandCollapsePattern)Named(root, "Leaf").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Current.ExpandCollapseState;
                    ((ExpandCollapsePattern)Named(root, "Folder").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                },
                () => expands > 0
            );

            Assert.Equal((ExpandCollapseState.LeafNode, 1), (leaf, expands));
        });

    /// <summary>
    /// UIA's RangeValue has the range's steps, a large step of 0 being the small one; SetValue(42) reaches the handler of a number
    /// with 42 and a disabled node refuses it; a node with no handler, a meter, and a scroll bar are read-only.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA range" with sliders 0 to 100 at 10 that record the number they take: "Both" with the steps 2 and 10,
    ///   "Small" with the step 3 alone, "Off" disabled; a slider "Fixed" with no handler; a Meter "Disk" and a ScrollBar "Bar", both
    ///   with a handler of a number.
    ///
    /// Steps:
    /// - On the client's thread, read the steps and IsReadOnly; set "Both" to 42 and try to set "Off" to 42; run the loop until
    ///   "Both" took a number.
    ///
    /// Expected:
    /// - SmallChange 2 and LargeChange 10 for "Both", LargeChange 3 for "Small"; IsReadOnly false for "Both", true for "Fixed",
    ///   "Disk", and "Bar"; "Both" took 42; "Off" took nothing.
    /// </remarks>
    [Fact]
    public void RangeValue_FollowsTheSteps_AndSetValueReachesTheHandler() =>
        ui.Invoke(() =>
        {
            var taken = new List<double>();
            var refused = new List<double>();
            using var window = new DesktopWindow(
                () =>
                    Column(
                        Ranged(SemanticsRole.Slider, "Both", 2, 10).OnSetRangeValue(taken.Add),
                        Ranged(SemanticsRole.Slider, "Small", 3, 0).OnSetRangeValue(_ => { }),
                        Ranged(SemanticsRole.Slider, "Off", 1, 0).OnSetRangeValue(refused.Add).Disabled(),
                        Ranged(SemanticsRole.Slider, "Fixed", 1, 0),
                        Ranged(SemanticsRole.Meter, "Disk", 1, 0).OnSetRangeValue(_ => { }),
                        Ranged(SemanticsRole.ScrollBar, "Bar", 1, 0).OnSetRangeValue(_ => { })
                    ),
                new WindowOptions("UIA range", 200, 200),
                useWarp: true
            );
            (double, double, double) steps = default;
            bool[] readOnly = [];

            ActByUia(
                window,
                root =>
                {
                    RangeValuePattern Range(string name) => (RangeValuePattern)Named(root, name).GetCurrentPattern(RangeValuePattern.Pattern);
                    steps = (Range("Both").Current.SmallChange, Range("Both").Current.LargeChange, Range("Small").Current.LargeChange);
                    readOnly = [.. ReadOnlyChecked.Select(n => Range(n).Current.IsReadOnly)];
                    Range("Both").SetValue(42);
                    try
                    {
                        Range("Off").SetValue(42);
                    }
                    catch (Exception exception) when (exception is ElementNotEnabledException or InvalidOperationException)
                    {
                        // The client or the adapter refuses a disabled element; either way the handler takes nothing.
                    }
                },
                () => taken.Count > 0
            );
            RunAccessibilityStage(window);

            Assert.Equal((2.0, 10.0, 3.0), steps);
            Assert.Equal([false, true, true, true], readOnly);
            Assert.Equal([42.0], taken);
            Assert.Empty(refused);
        });

    /// <summary>
    /// UIA reads the live setting of a node by its id: a status's Polite by default, an alert's Assertive, and Off where the app gives it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA live" with a Status holding a text from a signal "Saving", an Alert "Error", and a Group "Quiet" with
    ///   the live setting Off.
    ///
    /// Steps:
    /// - On the client's thread, read the LiveSetting of the three through the COM client.
    ///
    /// Expected:
    /// - Polite (1), Assertive (2), Off (0).
    /// </remarks>
    [Fact]
    public void LiveSetting_IsReadById() =>
        ui.Invoke(() =>
        {
            Signal<string> text = Signal.Create("Saving");
            using var window = new DesktopWindow(
                () =>
                    Column(
                        Box(Elements.Text((ref w) => w.Write(text.Get())))
                            .Accessible(new AccessibilityInfo { Role = SemanticsRole.Status })
                            .TestId("status"),
                        Named(SemanticsRole.Alert, "Error"),
                        Box()
                            .Height(10)
                            .Accessible(
                                new AccessibilityInfo
                                {
                                    Role = SemanticsRole.Group,
                                    Name = "Quiet",
                                    Live = SemanticsLive.Off,
                                }
                            )
                    ),
                new WindowOptions("UIA live", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            // The managed client does not support LiveSetting ("Unsupported Property"): the COM client reads it by its id.
            (object?, object?, object?) settings = ReadByUia(() =>
            {
                using var client = new UiaComClient((nint)hwnd);
                return (
                    client.ReadById("status", LiveSettingId),
                    client.ReadByName("Error", LiveSettingId),
                    client.ReadByName("Quiet", LiveSettingId)
                );
            });

            Assert.Equal(((object?)1, (object?)2, (object?)0), settings);
        });

    /// <summary>
    /// A change of a status's text, which it folds into its name, raises UIA's LiveRegionChanged.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA live event" with a Status holding a text from a signal "Saving"; a client listening for
    ///   LiveRegionChanged (20024), which the managed client must know by its id.
    ///
    /// Steps:
    /// - Set the signal to "Saved" and run the accessibility stage; run the loop until the client got an event.
    ///
    /// Expected:
    /// - An event. Skipped, naming the reason, when the managed client does not know the event's id: then a screen reader checks it.
    /// </remarks>
    [Fact]
    public void StatusTextChange_RaisesLiveRegionChanged()
    {
        if (AutomationEvent.LookupById(LiveRegionChangedEventId) is null)
        {
            Assert.Skip("UIAutomationClient's managed classes do not know LiveRegionChanged (20024); a screen reader checks the event.");
        }

        ui.Invoke(() =>
        {
            Signal<string> text = Signal.Create("Saving");
            using var window = new DesktopWindow(
                () => Box(Elements.Text((ref w) => w.Write(text.Get()))).Accessible(new AccessibilityInfo { Role = SemanticsRole.Status }),
                new WindowOptions("UIA live event", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            _ = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);

            object? changed = ListenByUia(
                done =>
                {
                    bool got = false;
                    var handler = new AutomationEventHandler(
                        (_, _) =>
                        {
                            got = true;
                            done.Set();
                        }
                    );
                    Automation.AddAutomationEventHandler(
                        AutomationEvent.LookupById(LiveRegionChangedEventId),
                        AutomationElement.FromHandle((nint)hwnd),
                        TreeScope.Subtree,
                        handler
                    );
                    return (handler, () => got);
                },
                () =>
                {
                    text.Set("Saved");
                    RunAccessibilityStage(window);
                }
            );

            Assert.True(changed is true);
        });
    }

    /// <summary>
    /// UIA reads the level, the place, the count, and the orientation; a heading of level 2 has UIA's Level 2, and its
    /// AriaProperties' level is 1, the upstream mismatch row AX2 corrects.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA level" with a Heading "Title" of level 2, a List with the count 7 holding a ListItem "Third" at the
    ///   place 3, and a ListBox "Row" running horizontally.
    ///
    /// Steps:
    /// - On the client's thread, read the Level and AriaProperties of "Title", the PositionInSet and SizeOfSet of "Third", and the
    ///   Orientation of "Row".
    ///
    /// Expected:
    /// - 2 and "level=1" among the properties; 3 and 7; Horizontal.
    /// </remarks>
    [Fact]
    public void LevelPlaceCountAndOrientation_AreReadByUia() =>
        ui.Invoke(() =>
        {
            using var window = new DesktopWindow(
                () =>
                    Column(
                        Box()
                            .Height(10)
                            .Accessible(
                                new AccessibilityInfo
                                {
                                    Role = SemanticsRole.Heading,
                                    Name = "Title",
                                    Level = 2,
                                }
                            ),
                        Box(
                                Box()
                                    .Height(10)
                                    .Accessible(
                                        new AccessibilityInfo
                                        {
                                            Role = SemanticsRole.ListItem,
                                            Name = "Third",
                                            PositionInSet = 3,
                                        }
                                    )
                            )
                            .Accessible(new AccessibilityInfo { Role = SemanticsRole.List, SizeOfSet = 7 }),
                        Box()
                            .Height(10)
                            .Accessible(
                                new AccessibilityInfo
                                {
                                    Role = SemanticsRole.ListBox,
                                    Name = "Row",
                                    Orientation = SemanticsOrientation.Horizontal,
                                }
                            )
                    ),
                new WindowOptions("UIA level", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            (object? Level, string Aria, object Place, object Size, object Orientation) read = ReadByUia(() =>
            {
                AutomationElement root = AutomationElement.FromHandle((nint)hwnd);
                AutomationElement third = Named(root, "Third");

                // The managed client knows neither Level nor AriaProperties by its id: the COM client reads them.
                using var client = new UiaComClient((nint)hwnd);
                return (
                    client.ReadByName("Title", LevelId),
                    client.ReadByName("Title", AriaPropertiesId) as string ?? string.Empty,
                    third.GetCurrentPropertyValue(AutomationProperty.LookupById(PositionInSetId)),
                    third.GetCurrentPropertyValue(AutomationProperty.LookupById(SizeOfSetId)),
                    Named(root, "Row").GetCurrentPropertyValue(AutomationElement.OrientationProperty)
                );
            });

            Assert.Equal((object?)2, read.Level);
            Assert.Contains("level=1", read.Aria, StringComparison.Ordinal);
            Assert.Equal(((object)3, (object)7), (read.Place, read.Size));
            Assert.Equal(OrientationType.Horizontal, read.Orientation);
        });

    /// <summary>
    /// UIA's SetValue writes the text of a search box and of the date and time inputs, as of an edit, and not of a read-only or a
    /// disabled one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA text roles" with empty text fields of the roles SearchBox ("search"), DateInput ("date"), TimeInput
    ///   ("time"), and DateTimeInput ("when"), a read-only SearchBox ("fixed"), and a disabled DateInput ("off").
    ///
    /// Steps:
    /// - On the client's thread, set each field to "x" in an action of its own, and run the loop until its text changed or the client
    ///   refused it (the read-only and the disabled field: until the action ended). The actions are split because a third UIA SetValue
    ///   in a row from one client thread does not return (Issue #895).
    ///
    /// Expected:
    /// - "x" in the four; "fixed" and "off" empty.
    /// </remarks>
    [Fact]
    public void SetValue_WritesTheTextRoles_NotAReadOnlyOrDisabledOne() =>
        ui.Invoke(() =>
        {
            string[] ids = ["search", "date", "time", "when", "fixed", "off"];
            SemanticsRole[] roles =
            [
                SemanticsRole.SearchBox,
                SemanticsRole.DateInput,
                SemanticsRole.TimeInput,
                SemanticsRole.DateTimeInput,
                SemanticsRole.SearchBox,
                SemanticsRole.DateInput,
            ];
            Signal<string>[] texts = [.. ids.Select(_ => Signal.Create(string.Empty))];
            using var window = new DesktopWindow(
                () =>
                    Column([
                        .. ids.Select(
                            (id, i) =>
                            {
                                Element field = TextField(texts[i], new TextFieldOptions { ReadOnly = id == "fixed" })
                                    .Accessible(new AccessibilityInfo { Role = roles[i] })
                                    .TestId(id);
                                return id == "off" ? field.Disabled() : field;
                            }
                        ),
                    ]),
                new WindowOptions("UIA text roles", 200, 300),
                useWarp: true
            );

            // One SetValue for each action of the client: a third in a row from one client thread does not return (Issue #895).
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                Signal<string> text = texts[i];
                bool writable = i < 4;
                bool refused = false;
                ActByUia(
                    window,
                    root =>
                    {
                        try
                        {
                            ((ValuePattern)ById(root, id).GetCurrentPattern(ValuePattern.Pattern)).SetValue("x");
                        }
                        catch (Exception exception) when (exception is ElementNotEnabledException or InvalidOperationException)
                        {
                            // The client may refuse a read-only or disabled element itself; either way the text stays.
                            refused = true;
                        }
                    },
                    () => !writable || refused || text.Peek().Length > 0
                );
            }

            RunAccessibilityStage(window);

            Assert.Equal(["x", "x", "x", "x", string.Empty, string.Empty], texts.Select(t => t.Peek()));
        });

    /// <summary>
    /// UIA reads a node's controlled nodes as ControllerFor, the text of its error message as its FullDescription while it is invalid,
    /// and the text of its label as its Name.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA relations" with a TabItem "One" that controls a TabPanel "Panel one", and an Edit ("field"), Invalid,
    ///   labelled by a text "Email", with a text "Required" as its error message.
    ///
    /// Steps:
    /// - On the client's thread, read the ControllerFor of "One", and the Name and FullDescription of "field".
    ///
    /// Expected:
    /// - ["Panel one"]; "Email"; "Required".
    /// </remarks>
    [Fact]
    public void Relations_AreReadByUia_AsControllerForFullDescriptionAndName() =>
        ui.Invoke(() =>
        {
            using var window = new DesktopWindow(
                () =>
                {
                    Element panel = Named(SemanticsRole.TabPanel, "Panel one");
                    Element tab = Named(SemanticsRole.TabItem, "One").Controls(panel);
                    Element label = Elements.Text("Email");
                    Element error = Elements.Text("Required");
                    Element field = Box()
                        .Width(50)
                        .Height(10)
                        .Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit, States = SemanticsStates.Invalid })
                        .TestId("field")
                        .LabelledBy(label)
                        .ErrorMessage(error);
                    return Column(tab, panel, label, field, error);
                },
                new WindowOptions("UIA relations", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            (object? Controlled, string Name, object? Description) read = ReadByUia(() =>
            {
                AutomationElement root = AutomationElement.FromHandle((nint)hwnd);

                // The managed client knows neither ControllerFor nor FullDescription by its id: the COM client reads them.
                using var client = new UiaComClient((nint)hwnd);
                return (client.ReadByName("One", ControllerForId), ById(root, "field").Current.Name, client.ReadById("field", FullDescriptionId));
            });

            Assert.Equal(["Panel one"], Assert.IsType<string[]>(read.Controlled));
            Assert.Equal(("Email", (object?)"Required"), (read.Name, read.Description));
        });

    /// <summary>
    /// Moving the active descendant of the focused node raises UIA's focus event on the node it moves to.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA active descendant" with a focusable ListBox of the ListBoxItems "One" and "Two", whose active
    ///   descendant is a signal at "One"; the list focused, the window told it has the keyboard focus; a client listening for the
    ///   focus.
    ///
    /// Steps:
    /// - Set the signal to "Two" and run the accessibility stage; run the loop until the client got an event of "Two".
    ///
    /// Expected:
    /// - An event whose element is "Two".
    /// </remarks>
    [Fact]
    public void ActiveDescendantMove_RaisesAFocusEventOnTheNewNode() =>
        ui.Invoke(() =>
        {
            var active = Signal.Create<Element>(default);
            Element list = default;
            Element two = default;
            using var window = new DesktopWindow(
                () =>
                {
                    Element one = Named(SemanticsRole.ListBoxItem, "One");
                    two = Named(SemanticsRole.ListBoxItem, "Two");
                    active.Set(one);
                    return list = Column(one, two)
                        .Focusable()
                        .Accessible(new AccessibilityInfo { Role = SemanticsRole.ListBox, Name = "Numbers" })
                        .ActiveDescendant(active);
                },
                new WindowOptions("UIA active descendant", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            window.Input.SetActive(true);
            Assert.True(window.Tree.Focus.Focus(list.Node));
            RunAccessibilityStage(window);
            _ = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);
            _ = SendMessageW(hwnd, WM.WM_SETFOCUS, 0, 0);

            object? focused = ListenByUia(
                done =>
                {
                    string? name = null;
                    var handler = new AutomationFocusChangedEventHandler(
                        (sender, _) =>
                        {
                            if (sender is AutomationElement element && element.Current.Name == "Two")
                            {
                                name = element.Current.Name;
                                done.Set();
                            }
                        }
                    );
                    Automation.AddAutomationFocusChangedEventHandler(handler);
                    return (handler, () => name);
                },
                () =>
                {
                    active.Set(two);
                    RunAccessibilityStage(window);
                }
            );
            _ = SendMessageW(hwnd, WM.WM_KILLFOCUS, 0, 0);

            Assert.Equal("Two", focused);
        });

    /// <summary>
    /// A modal alert dialog has UIA's Window pattern with IsModal true.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA modal" with an AlertDialog "Delete?" that is Modal.
    ///
    /// Steps:
    /// - On the client's thread, read its WindowPattern's IsModal.
    ///
    /// Expected:
    /// - True.
    /// </remarks>
    [Fact]
    public void ModalAlertDialog_IsModal() =>
        ui.Invoke(() =>
        {
            using var window = new DesktopWindow(
                () =>
                    Box()
                        .Height(40)
                        .Accessible(
                            new AccessibilityInfo
                            {
                                Role = SemanticsRole.AlertDialog,
                                Name = "Delete?",
                                States = SemanticsStates.Modal,
                            }
                        ),
                new WindowOptions("UIA modal", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            bool modal = ReadByUia(() =>
                ((WindowPattern)Named(AutomationElement.FromHandle((nint)hwnd), "Delete?").GetCurrentPattern(WindowPattern.Pattern)).Current.IsModal
            );

            Assert.True(modal);
        });

    /// <summary>
    /// Pins the holes of the bundled AccessKit that row AX2 fills: a grid has no Grid pattern, its Selection gives no selected item,
    /// and a labelled field has no LabeledBy. This test fails when AccessKit is updated, and row AX2 rewrites it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA holes" with a Grid "Cells" holding a Row of a GridCell "A1", Selected, and an Edit "field" labelled by a
    ///   text "Email".
    ///
    /// Steps:
    /// - On the client's thread, read whether "Cells" has the Grid pattern and its Selection's selected items, and the LabeledBy of
    ///   "field".
    ///
    /// Expected:
    /// - No Grid pattern; no selected item; no LabeledBy.
    /// </remarks>
    [Fact]
    public void UpstreamHoles_GridPatternSelectionAndLabeledBy_AreMissing() =>
        ui.Invoke(() =>
        {
            using var window = new DesktopWindow(
                () =>
                {
                    Element label = Elements.Text("Email");
                    return Column(
                        Box(
                                Box(
                                        Box()
                                            .Height(10)
                                            .Accessible(
                                                new AccessibilityInfo
                                                {
                                                    Role = SemanticsRole.GridCell,
                                                    Name = "A1",
                                                    States = SemanticsStates.Selected,
                                                }
                                            )
                                    )
                                    .Accessible(new AccessibilityInfo { Role = SemanticsRole.Row })
                            )
                            .Accessible(new AccessibilityInfo { Role = SemanticsRole.Grid, Name = "Cells" }),
                        label,
                        Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = SemanticsRole.Edit }).TestId("field").LabelledBy(label)
                    );
                },
                new WindowOptions("UIA holes", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            (bool Grid, int Selected, object LabeledBy) read = ReadByUia(() =>
            {
                AutomationElement root = AutomationElement.FromHandle((nint)hwnd);
                AutomationElement cells = Named(root, "Cells");
                int selected = cells.TryGetCurrentPattern(SelectionPattern.Pattern, out object? selection)
                    ? ((SelectionPattern)selection).Current.GetSelection().Length
                    : -1;
                return (
                    cells.TryGetCurrentPattern(GridPattern.Pattern, out _),
                    selected,
                    ById(root, "field").GetCurrentPropertyValue(AutomationElement.LabeledByProperty)
                );
            });

            Assert.False(read.Grid);
            Assert.Equal(0, read.Selected);
            Assert.Null(read.LabeledBy as AutomationElement);
        });

    // A text AccessKit returned, which this frees with accesskit_string_free; null for none.
    private static string? TakeText(byte* text)
    {
        if (text == null)
        {
            return null;
        }

        try
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)text);
        }
        finally
        {
            AccessKit.StringFree(text);
        }
    }

    // A box 10 high of a role and a name.
    private static Element Named(SemanticsRole role, string name) =>
        Box().Width(50).Height(10).Accessible(new AccessibilityInfo { Role = role, Name = name });

    private static AccessibilityInfo Container(SemanticsRole role, string name, SemanticsStates states) =>
        new()
        {
            Role = role,
            Name = name,
            States = states,
        };

    // A box of a role with the range 0 to 100 at 10 and steps.
    private static Element Ranged(SemanticsRole role, string name, double step, double largeStep) =>
        Box()
            .Width(50)
            .Height(10)
            .Accessible(
                new AccessibilityInfo
                {
                    Role = role,
                    Name = name,
                    Range = new SemanticsRange(0, 100, 10, step, largeStep),
                }
            );
}
