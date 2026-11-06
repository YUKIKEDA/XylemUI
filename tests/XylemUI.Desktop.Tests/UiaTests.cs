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
public sealed unsafe class UiaTests(UiThreadFixture ui)
{
    /// <summary>
    /// UIA reads the window's tree: the window, and each node's control type, name, automation ID, state, value, and box on the
    /// screen, in physical pixels at the window's scale.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window "UIA tree" of 300 x 300: a column of a button "Save" 30 high with the test ID "save", a disabled button
    ///   "Off" 30 high, a text field holding "hello", a node 20 high with the role CheckBox, the name "Agree", and the state Checked,
    ///   and a scroll view 60 high holding a button "Inner" 30 high and a box 200 high.
    ///
    /// Steps:
    /// - On a thread of its own, the UIA client takes the window's element and every element below it; the UI thread runs the loop.
    ///
    /// Expected:
    /// - The window: ControlType.Window, the name "UIA tree", the framework "XylemUI". "Save": ControlType.Button, the automation
    ///   ID "save", enabled, its box the node's box in client coordinates times the scale, moved to the screen. "Off": disabled.
    ///   An edit whose value is "hello". "Agree": ControlType.CheckBox, toggled on. A pane for the scroll view, and "Inner" in it.
    /// </remarks>
    [Fact]
    public void Tree_IsReadByUia_WithRolesNamesStatesAndBoxes() =>
        ui.Invoke(() =>
        {
            Element save = default;
            using var window = new DesktopWindow(
                () =>
                    Column(
                        save = Button("Save", () => { }).TestId("save").Height(30),
                        Button("Off", () => { }).Disabled().Height(30),
                        TextField(Signal.Create("hello")),
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
                new WindowOptions("UIA tree", 300, 300)
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
            Assert.Equal("hello", read.Elements.Single(e => e.Type == ControlType.Edit).Value);
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
            using var window = new DesktopWindow(
                () => ScrollView(Column(inner = Button("Inner", () => { }).Height(30), Box().Height(200)), state).Height(60),
                new WindowOptions("UIA scroll", 300, 300)
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
    private static System.Windows.Rect ScreenBox(DesktopWindow window, NodeHandle node)
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
        ToggleState? Toggle
    )
    {
        public static Seen Of(AutomationElement element)
        {
            AutomationElement.AutomationElementInformation current = element.Current;
            string? value = element.TryGetCurrentPattern(ValuePattern.Pattern, out object? v) ? ((ValuePattern)v).Current.Value : null;
            ToggleState? toggle = element.TryGetCurrentPattern(TogglePattern.Pattern, out object? t) ? ((TogglePattern)t).Current.ToggleState : null;
            return new Seen(current.ControlType, current.Name, current.AutomationId, current.IsEnabled, current.BoundingRectangle, value, toggle);
        }
    }
}
