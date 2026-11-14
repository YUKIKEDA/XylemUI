using System.Numerics;
using System.Windows.Automation;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The window's tree as a real UIA client reads it (design §4.4.1, "The shape of P02g"): AccessKit's Windows adapter answers the
// window's WM_GETOBJECT. The client is UIAutomationClient (System.Windows.Automation), as spike S10 read it; it runs on a thread of
// its own, as a screen reader runs in a process of its own, while the UI thread runs the message loop and answers.
[Collection(UiThreadGroup.Name)]
public sealed unsafe partial class UiaTests(UiThreadFixture ui)
{
    /// <summary>
    /// UIA reads the window's tree: the window, and each node's control type, name, automation ID, state, value, and box on the
    /// screen, in physical pixels at the window's scale.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA tree" of 300 x 300: a column of a button "Save" 30 high with the test ID "save", a disabled button
    ///   "Off" 30 high, a text field holding "hello", an empty text field with the placeholder "Hint", a text "Note", a focusable
    ///   box holding a text "Area", a node 20 high with the role CheckBox, the name "Agree", and the state Checked, and a scroll view
    ///   60 high holding a button "Inner" 30 high and a box 200 high.
    ///
    /// Steps:
    /// - On a thread of its own, the UIA client takes the window's element and every element below it; the UI thread runs the loop.
    ///
    /// Expected:
    /// - The window: ControlType.Window, the name "UIA tree", the framework "XylemUI". "Save": ControlType.Button, the automation
    ///   ID "save", enabled, its box the node's box in client coordinates times the scale, moved to the screen. "Off": disabled.
    ///   An edit whose value is "hello", and an edit whose help text is "Hint"; a text named "Note"; a group, keyboard focusable,
    ///   holding the text "Area". "Agree": ControlType.CheckBox, toggled on. A pane for the scroll view, and "Inner" in it.
    /// </remarks>
    [Fact]
    public void Tree_IsReadByUia_WithRolesNamesStatesAndBoxes() =>
        ui.Invoke(() =>
        {
            Element save = default;
            using var window = new TestWindow(
                () =>
                    Column(
                        save = Button("Save", () => { }).TestId("save").Height(30),
                        Button("Off", () => { }).Disabled().Height(30),
                        TextField(Signal.Create("hello")),
                        TextField(Signal.Create(string.Empty), new TextFieldOptions { Placeholder = "Hint" }),
                        Elements.Text("Note"),
                        Box(Elements.Text("Area")).Focusable(),
                        Box()
                            .Height(20)
                            .Accessible(
                                new AccessibilityInfo
                                {
                                    Role = SemanticsRole.CheckBox,
                                    Name = "Agree",
                                    States = SemanticsStates.Checked,
                                }
                            ),
                        ScrollView(Column(Button("Inner", () => { }).Height(30), Box().Height(200))).Height(60)
                    ),
                new WindowOptions("UIA tree", 300, 300),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            Read read = ReadByUia(() =>
            {
                AutomationElement root = AutomationElement.FromHandle((nint)hwnd);
                var elements = new List<Seen>();
                foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                {
                    elements.Add(Seen.Of(element));
                }

                return new Read(Seen.Of(root), root.Current.FrameworkId, elements);
            });

            Seen saved = read.Elements.Single(e => e.Name == "Save");
            Assert.Equal((ControlType.Window, "UIA tree", "XylemUI"), (read.Root.Type, read.Root.Name, read.Framework));
            Assert.Equal((ControlType.Button, "save", true), (saved.Type, saved.AutomationId, saved.Enabled));
            AssertBox(ScreenBox(window, save.Node), saved.Box);
            Assert.False(read.Elements.Single(e => e.Name == "Off").Enabled);
            Assert.Equal(["hello", string.Empty], read.Elements.Where(e => e.Type == ControlType.Edit).Select(e => e.Value));
            Assert.Equal("Hint", read.Elements.Single(e => e.Type == ControlType.Edit && e.Value == string.Empty).HelpText);
            Seen group = read.Elements.Single(e => e.Type == ControlType.Group);
            Assert.True(group.Focusable);
            Assert.Equal("Area", read.Elements.Single(e => e.Parent == group.Id && e.Type == ControlType.Text).Name);
            Assert.Single(read.Elements, e => e.Type == ControlType.Text && e.Name == "Note");
            Seen agree = read.Elements.Single(e => e.Name == "Agree");
            Assert.Equal((ControlType.CheckBox, (ToggleState?)ToggleState.On), (agree.Type, agree.Toggle));
            Assert.Single(read.Elements, e => e.Type == ControlType.Pane);
            Assert.Single(read.Elements, e => e.Name == "Inner");
        });

    /// <summary>
    /// A scroll moves the boxes UIA reads of the nodes in the scroll view; the view's own box stays.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA scroll" of 300 x 300: a scroll view 60 high holding a button "Inner" 30 high and a box 200 high; UIA
    ///   has read the tree once.
    ///
    /// Steps:
    /// - Scroll the view to 20; run the loop until a frame presented; read the boxes of "Inner" and of the pane again.
    ///
    /// Expected:
    /// - "Inner" is 20 × the scale higher on the screen, and its box is the node's box after the scroll; the pane's box is the same.
    /// </remarks>
    [Fact]
    public void Scroll_MovesTheBoxesInTheView() =>
        ui.Invoke(() =>
        {
            var state = new ScrollState();
            Element inner = default;
            using var window = new TestWindow(
                () => ScrollView(Column(inner = Button("Inner", () => { }).Height(30), Box().Height(200)), state).Height(60),
                new WindowOptions("UIA scroll", 300, 300),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            (System.Windows.Rect Inner, System.Windows.Rect Pane) Boxes() =>
                ReadByUia(() =>
                {
                    AutomationElement root = AutomationElement.FromHandle((nint)hwnd);
                    AutomationElement button = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Inner"));
                    AutomationElement pane = root.FindFirst(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane)
                    );
                    return (button.Current.BoundingRectangle, pane.Current.BoundingRectangle);
                });

            (System.Windows.Rect before, System.Windows.Rect paneBefore) = Boxes();
            int presents = window.Frames.PresentCount;
            state.ScrollTo(new Vector2(0, 20));
            using (var watchdog = new LoopWatchdog())
            {
                LoopTestHelpers.RunLoop(watchdog, () => window.Frames.PresentCount > presents, () => { });
                Assert.False(watchdog.Expired);
            }

            (System.Windows.Rect after, System.Windows.Rect paneAfter) = Boxes();
            float scale = window.Window.ScaleFactor;
            Assert.Equal(before.Y - (20 * scale), after.Y, tolerance: 1);
            AssertBox(ScreenBox(window, inner.Node), after);
            Assert.Equal(paneBefore, paneAfter);
        });

    /// <summary>
    /// Nothing of the accessibility tree is made before assistive technology asks for it: the window's frames run without it, and
    /// the first question of UIA makes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA first" of 200 x 200 with a button "One".
    ///
    /// Steps:
    /// - After the window's first frame, which its size runs, read whether the tree has its accessibility tree. Read the window's
    ///   name through UIA; read it again.
    ///
    /// Expected:
    /// - No accessibility tree before; after, a tree that is active, and the name "UIA first".
    /// </remarks>
    [Fact]
    public void Tree_IsMadeAtTheFirstQuestion() =>
        ui.Invoke(() =>
        {
            using var window = new TestWindow(() => Button("One", () => { }), new WindowOptions("UIA first", 200, 200), useWarp: true);
            HWND hwnd = window.Window.Hwnd;
            bool before = window.Tree.HasAccessibilityTree;

            string name = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);

            Assert.Equal((false, true, "UIA first"), (before, window.Tree.AccessibilityActive, name));
        });

    /// <summary>
    /// A change of a node's name reaches the UIA client as an event of the name's change, from the frame that changed it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA name" of 200 x 200 with a button whose text is a signal "Before"; a UIA client listening for changes
    ///   of the name on the button's element.
    ///
    /// Steps:
    /// - Set the signal to "After"; run the loop until the client got an event or the watchdog ends it.
    ///
    /// Expected:
    /// - One event, for the name, whose new value is "After".
    /// </remarks>
    [Fact]
    public void NameChange_RaisesAPropertyEvent() =>
        ui.Invoke(() =>
        {
            Signal<string> label = Signal.Create("Before");
            using var window = new TestWindow(
                () => Button(Elements.Text((ref text) => text.Write(label.Get())), () => { }),
                new WindowOptions("UIA name", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;

            object? changed = ListenByUia(
                done =>
                {
                    AutomationElement button = AutomationElement
                        .FromHandle((nint)hwnd)
                        .FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Before"));
                    object? value = null;
                    var handler = new AutomationPropertyChangedEventHandler(
                        (_, e) =>
                        {
                            value = e.NewValue;
                            done.Set();
                        }
                    );
                    Automation.AddAutomationPropertyChangedEventHandler(button, TreeScope.Element, handler, AutomationElement.NameProperty);
                    return (handler, () => value);
                },
                () => label.Set("After")
            );

            Assert.Equal("After", changed);
        });

    /// <summary>
    /// The window's keyboard focus reaches the UIA client as an event of the focus, on the node that has the tree's focus.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA focus" of 200 x 200 with two buttons, "First" and "Second", the second focused in the tree; a UIA
    ///   client listening for changes of the focus.
    ///
    /// Steps:
    /// - Send the window WM_SETFOCUS, as Windows does when it takes the keyboard focus; run the loop until the client got an event.
    ///
    /// Expected:
    /// - An event whose element is "Second".
    /// </remarks>
    [Fact]
    public void WindowFocus_RaisesAFocusEventOnTheFocusedNode() =>
        ui.Invoke(() =>
        {
            Element second = default;
            using var window = new TestWindow(
                () => Column(Button("First", () => { }), second = Button("Second", () => { })),
                new WindowOptions("UIA focus", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            Assert.True(window.Tree.Focus.Focus(second.Node));
            RunAccessibilityStage(window);
            _ = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);

            object? focused = ListenByUia(
                done =>
                {
                    string? name = null;
                    var handler = new AutomationFocusChangedEventHandler(
                        (sender, _) =>
                        {
                            if (sender is AutomationElement element && element.Current.ProcessId == Environment.ProcessId)
                            {
                                name = element.Current.Name;
                                done.Set();
                            }
                        }
                    );
                    Automation.AddAutomationFocusChangedEventHandler(handler);
                    return (handler, () => name);
                },
                () => _ = SendMessageW(hwnd, WM.WM_SETFOCUS, 0, 0)
            );
            _ = SendMessageW(hwnd, WM.WM_KILLFOCUS, 0, 0);

            Assert.Equal("Second", focused);
        });

    /// <summary>
    /// A move of the tree's focus while the window has the keyboard focus, as Tab moves it, reaches the UIA client as an event of
    /// the focus on the node that takes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA focus move" with two buttons, "First" focused in the tree and "Second"; the window told it has the
    ///   keyboard focus (WM_SETFOCUS); a UIA client listening for changes of the focus.
    ///
    /// Steps:
    /// - Focus "Second" in the tree and run the accessibility stage; run the loop until the client got an event of "Second".
    ///
    /// Expected:
    /// - An event whose element is "Second".
    /// </remarks>
    [Fact]
    public void FocusMove_RaisesAFocusEventOnTheNewNode() =>
        ui.Invoke(() =>
        {
            Element first = default;
            Element second = default;
            using var window = new TestWindow(
                () => Column(first = Button("First", () => { }), second = Button("Second", () => { })),
                new WindowOptions("UIA focus move", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            window.Input.SetActive(true);
            Assert.True(window.Tree.Focus.Focus(first.Node));
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
                            if (sender is AutomationElement element && element.Current.Name == "Second")
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
                    _ = window.Tree.Focus.Focus(second.Node);
                    RunAccessibilityStage(window);
                }
            );
            _ = SendMessageW(hwnd, WM.WM_KILLFOCUS, 0, 0);

            Assert.Equal("Second", focused);
        });

    /// <summary>
    /// The accessibility stage of a window whose tree AccessKit has, which hands AccessKit the nodes that changed, allocates nothing
    /// when no name changes: the focus moves between two buttons, which makes their nodes again with the same names. The headless
    /// test of the whole frame is AccessibilityTests.SteadyState_FocusAndHoverWithTheTree_AllocatesNothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA steady" of 200 x 200 with two buttons; UIA has read the tree, so AccessKit is active.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: focus the first button or the second by the
    ///   iteration's number, and run the layout pass and the accessibility stage.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    [Fact]
    public void SteadyState_AccessibilityStageWithAccessKit_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            Element first = default;
            Element second = default;
            using var window = new TestWindow(
                () => Column(first = Button("First", () => { }), second = Button("Second", () => { })),
                new WindowOptions("UIA steady", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            _ = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);
            Assert.True(window.Tree.AccessibilityActive);

            _ = Testing.PerformanceAssert.DoesNotAllocate(
                1_000,
                i =>
                {
                    _ = window.Tree.Focus.Focus(i % 2 == 0 ? first.Node : second.Node);
                    RunAccessibilityStage(window);
                }
            );
        });

    /// <summary>
    /// A change of the window's scale scales the boxes UIA reads, also when the window keeps its size in logical pixels, which
    /// changes no node of the tree.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA scale" with a button "One" 100 x 30; UIA has read its box.
    ///
    /// Steps:
    /// - Send WM_DPICHANGED for twice the window's DPI, with a suggested rectangle whose client area is twice as large (the frame
    ///   kept), so that the window keeps its size in logical pixels; run a frame; read the box again.
    ///
    /// Expected:
    /// - The box is 100 × the new scale wide, twice as wide as before.
    /// </remarks>
    [Fact]
    public void ScaleChange_ScalesTheBoxes() =>
        ui.Invoke(() =>
        {
            using var window = new TestWindow(
                () => Column(Button("One", () => { }).Width(100).Height(30)),
                new WindowOptions("UIA scale", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            System.Windows.Rect Box() =>
                ReadByUia(() =>
                    AutomationElement
                        .FromHandle((nint)hwnd)
                        .FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "One"))
                        .Current.BoundingRectangle
                );
            System.Windows.Rect before = Box();
            float scale = window.Window.ScaleFactor;
            RECT outer;
            RECT client;
            _ = GetWindowRect(hwnd, &outer);
            _ = GetClientRect(hwnd, &client);
            var suggested = new RECT
            {
                left = outer.left,
                top = outer.top,
                right = outer.right + (client.right - client.left),
                bottom = outer.bottom + (client.bottom - client.top),
            };
            uint dpi = (uint)MathF.Round(scale * 96 * 2);
            _ = SendMessageW(hwnd, WM.WM_DPICHANGED, (nuint)((dpi << 16) | dpi), (nint)(&suggested));
            RunAccessibilityStage(window);

            System.Windows.Rect after = Box();

            Assert.Equal((100 * scale, 100 * scale * 2), (before.Width, after.Width));
        });

    /// <summary>
    /// Each update hands AccessKit the nodes that changed alone, not again those of the updates before it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA update" with three buttons 100 x 30, the second's text a signal "Zero"; UIA has read the tree.
    ///
    /// Steps:
    /// - Set the signal to "One"; run a frame; read how many nodes the update handed AccessKit. Set it to "Two"; run a frame; read
    ///   it again.
    ///
    /// Expected:
    /// - 1, then 1: the second button's node each time.
    /// </remarks>
    [Fact]
    public void Update_HandsAccessKitTheChangedNodesAlone() =>
        ui.Invoke(() =>
        {
            Signal<string> label = Signal.Create("Zero");
            using var window = new TestWindow(
                () =>
                    Column(
                        Button("First", () => { }).Width(100).Height(30),
                        Button(Elements.Text((ref text) => text.Write(label.Get())), () => { }).Width(100).Height(30),
                        Button("Third", () => { }).Width(100).Height(30)
                    ),
                new WindowOptions("UIA update", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            _ = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);
            Win32Accessibility provider = window.Window.Accessibility!;

            label.Set("One");
            RunAccessibilityStage(window);
            int first = provider.LastUpdateNodes;
            label.Set("Two");
            RunAccessibilityStage(window);

            Assert.Equal((1, 1), (first, provider.LastUpdateNodes));
        });

    /// <summary>
    /// A change of a scroll view's range alone hands AccessKit the view's node again, whose largest scroll position changed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA range" with a scroll view 60 high holding a box 100 high with no role; UIA has read the tree.
    ///
    /// Steps:
    /// - Make the box 300 high; run a frame; read how many nodes the update handed AccessKit.
    ///
    /// Expected:
    /// - 1: the scroll view's node, the only node whose values changed.
    /// </remarks>
    [Fact]
    public void ScrollRange_Change_HandsTheViewAgain() =>
        ui.Invoke(() =>
        {
            Element content = default;
            using var window = new TestWindow(
                () => ScrollView(content = Box().Height(100)).Height(60),
                new WindowOptions("UIA range", 200, 200),
                useWarp: true
            );
            HWND hwnd = window.Window.Hwnd;
            _ = ReadByUia(() => AutomationElement.FromHandle((nint)hwnd).Current.Name);
            Win32Accessibility provider = window.Window.Accessibility!;

            window.Tree.SetStyle(content.Node, window.Tree.Nodes.GetRef(content.Node).Layout.Style with { Height = Length.Px(300) });
            RunAccessibilityStage(window);

            Assert.Equal(1, provider.LastUpdateNodes);
        });

    /// <summary>
    /// A question before the tree has a root, as Windows' own clients ask a window that has just shown, gets AccessKit's
    /// placeholder; once the tree has a root, the next question hands AccessKit the whole tree first, which UIA then reads.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA early" and a tree with no root, whose provider is the window's.
    ///
    /// Steps:
    /// - Send the window the WM_GETOBJECT that UIA sends first; read whether the tree is made. Give the tree a root holding a button
    ///   "Late" and size it; read the button through UIA, whose question comes as the next WM_GETOBJECT.
    ///
    /// Expected:
    /// - No tree after the question, and no failure; then an active tree, and UIA reads "Late" as a button of the framework "XylemUI".
    /// </remarks>
    [Fact]
    public void QuestionBeforeTheRoot_GetsThePlaceholder_ThenTheTree() =>
        ui.Invoke(() =>
        {
            const int UiaRootObjectId = -25;
            using var window = new Win32Window("UIA early", 200, 200);
            var tree = new UiTree { Title = "UIA early" };
            using var provider = new Win32Accessibility(window, tree);
            window.Accessibility = provider;
            HWND hwnd = window.Hwnd;

            _ = SendMessageW(hwnd, WM.WM_GETOBJECT, 0, UiaRootObjectId);
            bool early = tree.HasAccessibilityTree;
            Handle<UiNode> root = tree.CreateNode(new LayoutStyle());
            Handle<UiNode> button = tree.CreateNode(new LayoutStyle { Width = Length.Px(50), Height = Length.Px(20) });
            tree.SetAccessibility(button, new AccessibilityInfo { Role = SemanticsRole.Button, Name = "Late" });
            tree.AppendChild(root, button);
            Invalidation.NotifyWindowResized(tree, root, new Size(200, 200));
            LayoutEngine.RunLayoutPass(tree);

            (string Type, string Framework) read = ReadByUia(() =>
            {
                AutomationElement element = AutomationElement
                    .FromHandle((nint)hwnd)
                    .FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Late"));
                return (element.Current.ControlType.ProgrammaticName, element.Current.FrameworkId);
            });

            Assert.Equal((false, true, "ControlType.Button", "XylemUI"), (early, tree.AccessibilityActive, read.Type, read.Framework));
        });

    // The frame's layout pass and accessibility stage, without drawing: a present outside the loop's frame clock can block on a
    // hidden window of CI's runner (Update_HandsAccessKitTheChangedNodesAlone hung there for 5 minutes on its second present).
    private static void RunAccessibilityStage(TestWindow window)
    {
        LayoutEngine.RunLayoutPass(window.Tree);
        window.Tree.UpdateAccessibility();
    }

    // Starts a UIA client on a thread of its own that listens (listen returns its handler and how to read what it got), then runs
    // the change on the UI thread, and runs the loop until the client got its event or the watchdog ends the wait. Returns what the
    // client got.
    private static object? ListenByUia(Func<ManualResetEventSlim, (Delegate Handler, Func<object?> Got)> listen, Action change)
    {
        Func<object?>? got = null;
        Exception? error = null;
        using var ready = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            Delegate? handler = null;
            try
            {
                (handler, got) = listen(done);
                ready.Set();
                Win32MessageLoop.Wake();
                _ = done.Wait(TimeSpan.FromSeconds(4));
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                if (handler is not null)
                {
                    Automation.RemoveAllEventHandlers();
                }

                ready.Set();
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
            LoopTestHelpers.RunLoop(watchdog, () => ready.IsSet, () => { });
            Assert.False(watchdog.Expired);
        }

        using (var watchdog = new LoopWatchdog())
        {
            LoopTestHelpers.RunLoop(watchdog, () => finished.IsSet, change);
            Assert.False(watchdog.Expired);
        }

        thread.Join();
        Assert.Null(error);
        return got?.Invoke();
    }

    // Runs the read on a thread of its own while the UI thread runs the loop, which answers the client's messages.
    private static T ReadByUia<T>(Func<T> read)
    {
        T? result = default;
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                result = read();
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                done.Set();
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
            LoopTestHelpers.RunLoop(watchdog, () => done.IsSet, () => { });
            Assert.False(watchdog.Expired);
        }

        thread.Join();
        Assert.Null(error);
        return result!;
    }

    // A node's box on the screen in physical pixels: its box in the window's logical coordinates (the boxes of its ancestors, less
    // the scroll positions), times the scale, moved by the client area's origin.
    private static System.Windows.Rect ScreenBox(TestWindow window, NodeHandle node)
    {
        UiTree tree = window.Tree;
        Rect rect = tree.Nodes.GetRef(node).Layout.Rect;
        float x = rect.X;
        float y = rect.Y;
        for (NodeHandle parent = tree.Nodes.GetRef(node).Parent; !parent.IsNone; parent = tree.Nodes.GetRef(parent).Parent)
        {
            Rect r = tree.Nodes.GetRef(parent).Layout.Rect;
            Vector2 scroll = tree.ScrollOffsetOf(parent);
            x += r.X - scroll.X;
            y += r.Y - scroll.Y;
        }

        float scale = window.Window.ScaleFactor;
        POINT origin = default;
        _ = ClientToScreen(window.Window.Hwnd, &origin);
        return new System.Windows.Rect(origin.x + (x * scale), origin.y + (y * scale), rect.Width * scale, rect.Height * scale);
    }

    // UIA's boxes are whole pixels; a box in logical pixels at a scale is not.
    private static void AssertBox(System.Windows.Rect expected, System.Windows.Rect actual)
    {
        Assert.Equal(expected.X, actual.X, tolerance: 1);
        Assert.Equal(expected.Y, actual.Y, tolerance: 1);
        Assert.Equal(expected.Width, actual.Width, tolerance: 1);
        Assert.Equal(expected.Height, actual.Height, tolerance: 1);
    }

    // What the client read of the tree.
    private sealed record Read(Seen Root, string Framework, List<Seen> Elements);

    // What the client read of one element.
    private sealed record Seen(
        ControlType Type,
        string Name,
        string AutomationId,
        bool Enabled,
        System.Windows.Rect Box,
        string? Value,
        ToggleState? Toggle,
        string HelpText,
        bool Focusable,
        string Id,
        string? Parent
    )
    {
        public static Seen Of(AutomationElement element)
        {
            AutomationElement.AutomationElementInformation current = element.Current;
            string? value = element.TryGetCurrentPattern(ValuePattern.Pattern, out object? v) ? ((ValuePattern)v).Current.Value : null;
            ToggleState? toggle = element.TryGetCurrentPattern(TogglePattern.Pattern, out object? t) ? ((TogglePattern)t).Current.ToggleState : null;
            AutomationElement? parent = System.Windows.Automation.TreeWalker.ControlViewWalker.GetParent(element);
            return new Seen(
                current.ControlType,
                current.Name,
                current.AutomationId,
                current.IsEnabled,
                current.BoundingRectangle,
                value,
                toggle,
                current.HelpText,
                current.IsKeyboardFocusable,
                string.Join('.', element.GetRuntimeId()),
                parent is null ? null : string.Join('.', parent.GetRuntimeId())
            );
        }
    }
}
