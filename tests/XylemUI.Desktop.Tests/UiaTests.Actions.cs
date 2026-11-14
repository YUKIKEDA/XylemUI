using System.Numerics;
using System.Windows.Automation;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The actions of assistive technology through UIA (design §4.4.1, "States and actions", row P02g's third implementation PR): the
// client calls a pattern on its own thread, AccessKit posts the request to the window, and the UI thread carries it out.
public sealed unsafe partial class UiaTests
{
    // UIA's ids of PositionInSet and SizeOfSet (UIAutomationClient.h), which UIAutomationClient's managed classes do not name.
    private const int PositionInSetId = 30152;
    private const int SizeOfSetId = 30153;

    /// <summary>
    /// The request for an action is read with the layout of <c>accesskit_action_request</c> on x64.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Read the size of the request's struct.
    ///
    /// Expected:
    /// - 80 bytes: the action at 0, the tree's id at 1, the node's id at 24, and the optional data at 32 (accesskit.h lines 1065 to
    ///   1070).
    /// </remarks>
    [Fact]
    public void ActionRequest_HasTheLayoutOfAccessKit() => Assert.Equal(80, sizeof(AccessKit.ActionRequest));

    /// <summary>
    /// UIA's Invoke runs the button's click, on the UI thread, once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA invoke" with a button "Go" that counts its clicks and records the thread they run on.
    ///
    /// Steps:
    /// - On the client's thread, find "Go" and call its InvokePattern's Invoke; run the loop until the click ran.
    ///
    /// Expected:
    /// - One click, on the UI thread.
    /// </remarks>
    [Fact]
    public void Invoke_ClicksTheButton_OnTheUiThread() =>
        ui.Invoke(() =>
        {
            int clicks = 0;
            int thread = 0;
            using var window = new DesktopWindow(
                () =>
                    Button(
                        "Go",
                        () =>
                        {
                            clicks++;
                            thread = Environment.CurrentManagedThreadId;
                        }
                    ),
                new WindowOptions("UIA invoke", 200, 200),
                useWarp: true
            );

            ActByUia(window, root => ((InvokePattern)Named(root, "Go").GetCurrentPattern(InvokePattern.Pattern)).Invoke(), () => clicks > 0);

            Assert.Equal((1, Environment.CurrentManagedThreadId), (clicks, thread));
        });

    /// <summary>
    /// UIA's Invoke delivers a click at the time of the window's clock, which the pointer's and the keys' input carries.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA click time" whose clock is a test clock advanced by 5 seconds, with a node of the role Button and the
    ///   name "Stamp" that records the time of its click.
    ///
    /// Steps:
    /// - On the client's thread, find "Stamp" and call its InvokePattern's Invoke; run the loop until the click ran.
    ///
    /// Expected:
    /// - The click's time is 5 seconds.
    /// </remarks>
    [Fact]
    public void Invoke_ClicksAtTheTimeOfTheWindowsClock() =>
        ui.Invoke(() =>
        {
            TimeSpan? stamp = null;
            using var window = new DesktopWindow(
                () =>
                    Box()
                        .Height(20)
                        .On(PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent e) => stamp = e.Timestamp)
                        .Accessible(new AccessibilityInfo { Role = SemanticsRole.Button, Name = "Stamp" }),
                new WindowOptions("UIA click time", 200, 200),
                useWarp: true
            );
            var time = new TestTime();
            time.Advance(TimeSpan.FromSeconds(5));
            window.Window.Time = time;

            ActByUia(
                window,
                root => ((InvokePattern)Named(root, "Stamp").GetCurrentPattern(InvokePattern.Pattern)).Invoke(),
                () => stamp is not null
            );

            Assert.Equal(TimeSpan.FromSeconds(5), stamp);
        });

    /// <summary>
    /// UIA's SetFocus moves the tree's focus to the node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA focus action" with two buttons "First" and "Second"; nothing focused.
    ///
    /// Steps:
    /// - On the client's thread, find "Second" and call SetFocus; run the loop until the tree's focus moved.
    ///
    /// Expected:
    /// - "Second" has the focus.
    /// </remarks>
    [Fact]
    public void SetFocus_MovesTheFocus() =>
        ui.Invoke(() =>
        {
            Element second = default;
            using var window = new DesktopWindow(
                () => Column(Button("First", () => { }), second = Button("Second", () => { })),
                new WindowOptions("UIA focus action", 200, 200),
                useWarp: true
            );

            ActByUia(window, root => Named(root, "Second").SetFocus(), () => window.Tree.Focus.Focused == second.Node);

            Assert.Equal(second.Node, window.Tree.Focus.Focused);
        });

    /// <summary>
    /// UIA's Toggle on a check box runs its click, as AccessKit sends Toggle as a click.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA toggle" with a node of the role CheckBox named "Agree" whose click counts.
    ///
    /// Steps:
    /// - On the client's thread, find "Agree" and call its TogglePattern's Toggle; run the loop until the click ran.
    ///
    /// Expected:
    /// - One click.
    /// </remarks>
    [Fact]
    public void Toggle_ClicksTheCheckBox() =>
        ui.Invoke(() =>
        {
            int clicks = 0;
            using var window = new DesktopWindow(
                () => Box().Height(20).OnClick(() => clicks++).Accessible(new AccessibilityInfo { Role = SemanticsRole.CheckBox, Name = "Agree" }),
                new WindowOptions("UIA toggle", 200, 200),
                useWarp: true
            );

            ActByUia(window, root => ((TogglePattern)Named(root, "Agree").GetCurrentPattern(TogglePattern.Pattern)).Toggle(), () => clicks > 0);

            Assert.Equal(1, clicks);
        });

    /// <summary>
    /// UIA's SetValue writes a text field's text, cut to the field's first line and its most clusters; a read-only field keeps its
    /// text.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA set value" with a field "free" of at most 5 clusters, a field "line" of one line, and a read-only
    ///   field "fixed", all empty.
    ///
    /// Steps:
    /// - On the client's thread, set "free" to "abcdefgh", "line" to "one\ntwo", and try to set "fixed" to "x"; run the loop
    ///   until "line" changed.
    ///
    /// Expected:
    /// - "free" is "abcde", "line" is "one", "fixed" is empty.
    /// </remarks>
    [Fact]
    public void SetValue_WritesTheField_NotAReadOnlyOne() =>
        ui.Invoke(() =>
        {
            Signal<string> free = Signal.Create(string.Empty);
            Signal<string> line = Signal.Create(string.Empty);
            Signal<string> fixedText = Signal.Create(string.Empty);
            using var window = new DesktopWindow(
                () =>
                    Column(
                        TextField(free, new TextFieldOptions { MaxLength = 5 }).TestId("free"),
                        TextField(line).TestId("line"),
                        TextField(fixedText, new TextFieldOptions { ReadOnly = true }).TestId("fixed")
                    ),
                new WindowOptions("UIA set value", 200, 200),
                useWarp: true
            );

            ActByUia(
                window,
                root =>
                {
                    ((ValuePattern)ById(root, "free").GetCurrentPattern(ValuePattern.Pattern)).SetValue("abcdefgh");
                    ((ValuePattern)ById(root, "line").GetCurrentPattern(ValuePattern.Pattern)).SetValue("one\ntwo");
                    try
                    {
                        ((ValuePattern)ById(root, "fixed").GetCurrentPattern(ValuePattern.Pattern)).SetValue("x");
                    }
                    catch (Exception exception) when (exception is ElementNotEnabledException or InvalidOperationException)
                    {
                        // The client may refuse a read-only element itself; either way the text stays.
                    }
                },
                () => line.Peek().Length > 0
            );
            RunAccessibilityStage(window);

            Assert.Equal(("abcde", "one", string.Empty), (free.Peek(), line.Peek(), fixedText.Peek()));
        });

    /// <summary>
    /// Four calls of UIA's SetValue in a row from one client thread all return, and each writes its field (Issue #895).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA set value in a row" with two text fields, their test IDs "free" and "line", both empty.
    ///
    /// Steps:
    /// - In one action of the client, find the field and SetValue each time: "one" to "free", "two" to "line", "three" to
    ///   "line", "four" to "free", counting the calls that returned; run the loop until the client is done and "free" is "four".
    ///
    /// Expected:
    /// - Four calls returned before the watchdog; "free" is "four" and "line" is "three".
    /// </remarks>
    [Fact]
    public void SetValue_FourTimesInARow_EachReturnsAndWrites() =>
        ui.Invoke(() =>
        {
            Signal<string> free = Signal.Create(string.Empty);
            Signal<string> line = Signal.Create(string.Empty);
            using var window = new DesktopWindow(
                () => Column(TextField(free).TestId("free"), TextField(line).TestId("line")),
                new WindowOptions("UIA set value in a row", 200, 200),
                useWarp: true
            );
            int returned = 0;

            ActByUia(
                window,
                root =>
                {
                    (string Id, string Text)[] writes = [("free", "one"), ("line", "two"), ("line", "three"), ("free", "four")];
                    foreach ((string id, string text) in writes)
                    {
                        ((ValuePattern)ById(root, id).GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
                        _ = Interlocked.Increment(ref returned);
                    }
                },
                () => free.Peek() == "four"
            );
            RunAccessibilityStage(window);

            Assert.Equal((4, "four", "three"), (Volatile.Read(ref returned), free.Peek(), line.Peek()));
        });

    /// <summary>
    /// UIA's Expand and Collapse open and close a select's list, which then has UIA's Selection, and SelectionItem's Select on an
    /// option chooses it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA select" with a select of "apple" and "pear", "apple" chosen, its test ID "fruit".
    ///
    /// Steps:
    /// - Expand the select through UIA; run the loop until it is open. Collapse it; run the loop until it is closed. Expand it
    ///   again. Read whether its list has the Selection pattern, and Select "pear"; run the loop until the value changed.
    ///
    /// Expected:
    /// - The select closed on Collapse; the list has the Selection pattern; the value is "pear".
    /// </remarks>
    [Fact]
    public void ExpandSelect_AndChooseAnOption() =>
        ui.Invoke(() =>
        {
            Signal<string> fruit = Signal.Create("apple");
            Element select = default;
            using var window = new DesktopWindow(
                () => select = Select(fruit, ["apple", "pear"], static s => Elements.Text(s)).TestId("fruit"),
                new WindowOptions("UIA select", 200, 200),
                useWarp: true
            );
            bool Open() => (window.Tree.Nodes.GetRef(select.Node).Pseudo & PseudoState.Open) != 0;

            ActByUia(window, root => ((ExpandCollapsePattern)ById(root, "fruit").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand(), Open);
            RunAccessibilityStage(window);
            ActByUia(
                window,
                root => ((ExpandCollapsePattern)ById(root, "fruit").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse(),
                () => !Open()
            );
            RunAccessibilityStage(window);
            bool collapsed = !Open();
            ActByUia(window, root => ((ExpandCollapsePattern)ById(root, "fruit").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand(), Open);
            RunAccessibilityStage(window);
            bool selection = false;
            ActByUia(
                window,
                root =>
                {
                    AutomationElement list = root.FindFirst(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List)
                    );
                    selection = list.TryGetCurrentPattern(SelectionPattern.Pattern, out _);
                    ((SelectionItemPattern)Named(root, "pear").GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                },
                () => fruit.Peek() == "pear"
            );

            Assert.Equal((true, true, "pear"), (collapsed, selection, fruit.Peek()));
        });

    /// <summary>
    /// A scroll view clips its nodes: UIA finds the nodes in view and the one just past it, not those farther; ScrollIntoView on the
    /// one past it scrolls the view by the least that shows it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA scroll into view" with a scroll view 60 high holding ten buttons "b1" to "b10", 30 high each.
    ///
    /// Steps:
    /// - On the client's thread, read whether "b3" and "b9" are found, and call "b3"'s ScrollItemPattern's ScrollIntoView; run the
    ///   loop until the view scrolled.
    ///
    /// Expected:
    /// - "b3" is found and "b9" is not; the view is at 30.
    /// </remarks>
    [Fact]
    public void ScrollIntoView_ShowsTheNodePastTheView() =>
        ui.Invoke(() =>
        {
            var state = new ScrollState();
            using var window = new DesktopWindow(
                () => ScrollView(Column([.. Enumerable.Range(1, 10).Select(i => Button($"b{i}", () => { }).Height(30))]), state).Height(60),
                new WindowOptions("UIA scroll into view", 200, 200),
                useWarp: true
            );
            (bool Third, bool Ninth) found = default;

            ActByUia(
                window,
                root =>
                {
                    found = (Find(root, "b3") is not null, Find(root, "b9") is not null);
                    ((ScrollItemPattern)Named(root, "b3").GetCurrentPattern(ScrollItemPattern.Pattern)).ScrollIntoView();
                },
                () => state.OffsetValue.Y > 0
            );

            Assert.Equal((true, false, 30f), (found.Third, found.Ninth, state.OffsetValue.Y));
        });

    /// <summary>
    /// A virtualized list's rows give UIA their place, from 1, and the list's number of items.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA list" with a box 200 high that scrolls, a virtualized list of 1,000 rows 20 high with test IDs "row0"
    ///   on.
    ///
    /// Steps:
    /// - On the client's thread, find "row2" and read its PositionInSet and SizeOfSet.
    ///
    /// Expected:
    /// - 3 and 1,000.
    /// </remarks>
    [Fact]
    public void VirtualListRows_GiveTheirPlaceAndCount() =>
        ui.Invoke(() =>
        {
            Signal<IReadOnlyList<int>> items = Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 1_000)]);
            using var window = new DesktopWindow(
                () =>
                    Box()
                        .Height(200)
                        .Overflow(Overflow.Visible, Overflow.Scroll)
                        .VirtualFor(items, static i => i, static item => Box().Height(20).TestId($"row{item.Get()}")),
                new WindowOptions("UIA list", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            (object Place, object Size) read = ReadByUia(() =>
            {
                AutomationElement row = ById(AutomationElement.FromHandle((nint)hwnd), "row2");
                return (
                    row.GetCurrentPropertyValue(AutomationProperty.LookupById(PositionInSetId)),
                    row.GetCurrentPropertyValue(AutomationProperty.LookupById(SizeOfSetId))
                );
            });

            Assert.Equal((3, 1_000), (read.Place, read.Size));
        });

    /// <summary>
    /// A link range is a Hyperlink to UIA, and its Invoke runs the link's click.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA link" with a text "Read the docs" whose "the docs" is a link that counts its clicks.
    ///
    /// Steps:
    /// - On the client's thread, find the Hyperlink "the docs" and call its InvokePattern's Invoke; run the loop until it clicked.
    ///
    /// Expected:
    /// - One click.
    /// </remarks>
    [Fact]
    public void LinkInvoke_RunsTheLinksClick() =>
        ui.Invoke(() =>
        {
            int clicks = 0;
            PointerHandler click = (in PointerEvent e) => clicks++;
            using var window = new DesktopWindow(
                () =>
                    Elements.Text(
                        (ref w) =>
                        {
                            w.Write("Read ".AsSpan());
                            using (w.Link(click))
                            {
                                w.Write("the docs".AsSpan());
                            }
                        }
                    ),
                new WindowOptions("UIA link", 300, 100),
                useWarp: true
            );

            ActByUia(
                window,
                root =>
                {
                    AutomationElement link = root.FindFirst(
                        TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                            new PropertyCondition(AutomationElement.NameProperty, "the docs")
                        )
                    );
                    ((InvokePattern)link.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                },
                () => clicks > 0
            );

            Assert.Equal(1, clicks);
        });

    /// <summary>
    /// The provider refuses SetValue on a read-only field, also when the request reaches it: UIAutomationClient refuses a read-only
    /// value itself, and AccessKit sends SetValue whatever the field is (accesskit_windows 0.35.1's `adapters/windows/src/node.rs`,
    /// lines 1349 to 1353).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA read only" with a read-only field "fixed" and a field "free", both empty; UIA has read the tree.
    ///
    /// Steps:
    /// - Hand the provider a request of SetValue "x" for each field's node, as AccessKit hands it.
    ///
    /// Expected:
    /// - "free" is "x"; "fixed" is empty.
    /// </remarks>
    [Fact]
    public void SetValueRequest_OnAReadOnlyField_IsRefused() =>
        ui.Invoke(() =>
        {
            Signal<string> free = Signal.Create(string.Empty);
            Signal<string> fixedText = Signal.Create(string.Empty);
            Element freeField = default;
            Element fixedField = default;
            using var window = new DesktopWindow(
                () => Column(freeField = TextField(free), fixedField = TextField(fixedText, new TextFieldOptions { ReadOnly = true })),
                new WindowOptions("UIA read only", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            _ = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);
            Win32Accessibility provider = window.Window.Accessibility!;

            SetValue(provider, window.Tree.Accessibility.Find(freeField.Node)!.Id);
            SetValue(provider, window.Tree.Accessibility.Find(fixedField.Node)!.Id);

            Assert.Equal(("x", string.Empty), (free.Peek(), fixedText.Peek()));
        });

    // Hands the provider a request of SetValue "x", laid out as AccessKit's, from memory the test owns.
    private static void SetValue(Win32Accessibility provider, AccessibilityNodeId node)
    {
        byte* text = stackalloc byte[] { (byte)'x', 0 };
        var request = new AccessKit.ActionRequest { Action = AccessKit.ActionSetValue, TargetNode = node.Value };
        request.Data.HasValue = 1;
        request.Data.Value.Tag = AccessKit.ActionDataValue;
        request.Data.Value.Value.Text = text;
        provider.Perform(&request);
    }

    // Runs an action of the client on a thread of its own, while the UI thread runs the loop until the client is done and the action
    // took effect.
    private static void ActByUia(DesktopWindow window, Action<AutomationElement> act, Func<bool> done)
    {
        HWND hwnd = window.Window.Hwnd;
        Exception? error = null;
        using var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                act(AutomationElement.FromHandle((nint)hwnd));
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                finished.Set();
                Win32MessageLoop.Wake();
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        using (var watchdog = new LoopWatchdog())
        {
            LoopTestHelpers.RunLoop(watchdog, () => finished.IsSet && (error is not null || done()), () => { });
            Assert.False(watchdog.Expired);
        }

        thread.Join();
        Assert.Null(error);
    }

    private static AutomationElement? Find(AutomationElement root, string name) =>
        root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));

    private static AutomationElement Named(AutomationElement root, string name) =>
        Find(root, name) ?? throw new InvalidOperationException($"UIA found no element named \"{name}\".");

    private static AutomationElement ById(AutomationElement root, string id) =>
        root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id))
        ?? throw new InvalidOperationException($"UIA found no element with the automation ID \"{id}\".");
}
