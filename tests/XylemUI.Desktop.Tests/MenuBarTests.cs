using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The menu bar (row P02d3, design §13.1, "The window's access keys, the label, and the menu bar") in a headless app: what selects it
// and leaves it, its keys, its access keys, the pointer, and Tab.
public sealed class MenuBarTests
{
    /// <summary>
    /// Alt alone and F10 select the bar with its first item focused and no menu open; Alt, F10, and Esc leave it and give the focus
    /// back. Alt with another key or a press while it is down does not select it, and neither Alt alone nor F10 selects it while
    /// another popup is open.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bar of <see cref="Start"/>, a focusable box focused.
    ///
    /// Steps:
    /// - Alt alone, then Alt; F10, then F10; Alt alone, then Esc; reading the focus and the popups after each.
    /// - Alt down, F5 with Alt, Alt up; Alt down, a press on the box, Alt up; reading the focus after each.
    /// - Open the second box's menu; Alt alone; F10; reading the focus and the popups after each.
    ///
    /// Expected:
    /// - Each first key focuses "File" with no popup; each second key gives the focus back to the box.
    /// - After both Alt with a key or a press, the box keeps the focus.
    /// - The menu's "Cut" keeps the focus, in the one popup, after each.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBar_AltAloneAndF10_SelectWithoutOpening_AndLeaveBackToTheFocus()
    {
        await using Bar bar = await Start();
        HeadlessApp app = bar.App;
        var steps = new List<(NodeHandle Focused, int Popups)>();
        void Read() => steps.Add((app.Tree.Focus.Focused, app.Popups.Count));

        PressAlt(app);
        Read();
        PressAlt(app);
        Read();
        _ = app.Keyboard.Press(Key.F10);
        Read();
        _ = app.Keyboard.Press(Key.F10);
        Read();
        PressAlt(app);
        Read();
        _ = app.Keyboard.Press(Key.Escape);
        Read();

        _ = app.Keyboard.Down(Key.LeftAlt, KeyModifiers.Alt);
        _ = app.Keyboard.Press(Key.F5, KeyModifiers.Alt);
        _ = app.Keyboard.Up(Key.LeftAlt);
        _ = app.PumpAndSettle();
        NodeHandle afterKey = app.Tree.Focus.Focused;
        _ = app.Keyboard.Down(Key.LeftAlt, KeyModifiers.Alt);
        app.Pointer.Click(bar.Content);
        _ = app.Keyboard.Up(Key.LeftAlt);
        _ = app.PumpAndSettle();
        NodeHandle afterPress = app.Tree.Focus.Focused;
        bar.SecondMenu.Set(true);
        _ = app.PumpAndSettle();
        var withMenu = new List<(string Focused, int Popups)>();
        PressAlt(app);
        withMenu.Add((bar.NameOf(app.Tree.Focus.Focused), app.Popups.Count));
        _ = app.Keyboard.Press(Key.F10);
        _ = app.PumpAndSettle();
        withMenu.Add((bar.NameOf(app.Tree.Focus.Focused), app.Popups.Count));

        NodeHandle file = bar.File.Node;
        NodeHandle content = bar.Content.Node;
        Assert.Equal([(file, 0), (content, 0), (file, 0), (content, 0), (file, 0), (content, 0)], steps);
        Assert.Equal(content, afterKey);
        Assert.Equal(content, afterPress);
        Assert.Equal([("Cut", 1), ("Cut", 1)], withMenu);
    }

    /// <summary>
    /// In the selected bar, Left and Right move between the items that show, round at the ends; Down, Enter, and Space open the
    /// item's menu with its first item focused, and Up with its last; Esc in an open menu closes it alone, and the bar stays selected.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bar of <see cref="Start"/>: "File", a disabled "Edit", "View".
    ///
    /// Steps:
    /// - F10; Right, Right, Left, reading the focus after each.
    /// - Down, Esc, Up, Esc, Enter, Esc, Space, reading the focus and the popups after each.
    ///
    /// Expected:
    /// - "View", "File", "View".
    /// - "Zoom" in one popup; "View" with none; "Reset"; "View"; "Zoom"; "View"; "Zoom".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBar_SelectedKeys_MoveRoundAndOpen()
    {
        await using Bar bar = await Start();
        HeadlessApp app = bar.App;
        _ = app.Keyboard.Press(Key.F10);
        var moves = new List<NodeHandle>();
        foreach (Key key in (Key[])[Key.Right, Key.Right, Key.Left])
        {
            _ = app.Keyboard.Press(key);
            moves.Add(app.Tree.Focus.Focused);
        }

        var opens = new List<(string Focused, int Popups)>();
        foreach (Key key in (Key[])[Key.Down, Key.Escape, Key.Up, Key.Escape, Key.Enter, Key.Escape, Key.Space])
        {
            _ = app.Keyboard.Press(key);
            _ = app.PumpAndSettle();
            opens.Add((bar.NameOf(app.Tree.Focus.Focused), app.Popups.Count));
        }

        Assert.Equal([bar.View.Node, bar.File.Node, bar.View.Node], moves);
        Assert.Equal([("Zoom", 1), ("View", 0), ("Reset", 1), ("View", 0), ("Zoom", 1), ("View", 0), ("Zoom", 1)], opens);
    }

    /// <summary>
    /// While a menu of the bar is open, Left and Right that the menu does not take open the next item's menu, passing over the
    /// disabled one: Right on an item with no submenu, Left in the first panel. In a submenu, Right opens it and Left closes it. Esc
    /// in the menu moved to closes it and leaves the focus on its item of the bar.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bar of <see cref="Start"/>; "File"'s menu: "New", a submenu "Recent" of "One", and "Exit".
    ///
    /// Steps:
    /// - F10, Down; Right; Left; Down, Right (opens "Recent"); Left; Right; Right (on "One"); Esc.
    ///
    /// Expected:
    /// - Focused after each, with the popups: "View" menu's "Zoom" (1); "New" (1); "Recent" (1) then "One" (2); "Recent" (1);
    ///   "One" (2); "Zoom" (1); "View" (0).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBar_OpenMenu_LeftAndRightMoveToTheNextMenu()
    {
        await using Bar bar = await Start();
        HeadlessApp app = bar.App;
        _ = app.Keyboard.Press(Key.F10);
        _ = app.Keyboard.Press(Key.Down);
        _ = app.PumpAndSettle();
        var steps = new List<(string Focused, int Popups)>();
        foreach (Key key in (Key[])[Key.Right, Key.Left, Key.Down, Key.Right, Key.Left, Key.Right, Key.Right, Key.Escape])
        {
            _ = app.Keyboard.Press(key);
            _ = app.PumpAndSettle();
            steps.Add((bar.NameOf(app.Tree.Focus.Focused), app.Popups.Count));
        }

        Assert.Equal([("Zoom", 1), ("New", 1), ("Recent", 1), ("One", 2), ("Recent", 1), ("One", 2), ("Zoom", 1), ("View", 0)], steps);
    }

    /// <summary>
    /// An item's access key, Alt and its letter, from anywhere in the window selects the bar and opens the item's menu with its first
    /// item focused; Esc closes the menu, and Esc again leaves the bar. In the selected bar, the letter alone opens it. Choosing an
    /// item of the menu leaves the bar, gives the focus back, and runs the action. A modal dialog keeps the bar's keys out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bar of <see cref="Start"/>, "View"'s "Zoom" counting its choices, and a dialog of a focusable box, closed.
    ///
    /// Steps:
    /// - Alt+V; Esc; Esc; reading the focus and the popups after each.
    /// - F10; V; Enter; reading the focus, the popups, and the count.
    /// - Open the dialog; Alt+V, reading whether it was handled; Alt alone; F10; reading the focus and the popups after each.
    ///
    /// Expected:
    /// - "Zoom" (1); "View" (0); the box (0).
    /// - "Zoom" (1); then the box (0) and one choice.
    /// - Alt+V not handled; the focus in the dialog, in the one popup, after each.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBar_AccessKeys_OpenTheMenu_AndAChoiceLeavesTheBar()
    {
        await using Bar bar = await Start();
        HeadlessApp app = bar.App;
        var steps = new List<(string Focused, int Popups)>();
        void Read()
        {
            _ = app.PumpAndSettle();
            steps.Add((bar.NameOf(app.Tree.Focus.Focused), app.Popups.Count));
        }

        _ = app.Keyboard.Press(Key.V, KeyModifiers.Alt);
        Read();
        _ = app.Keyboard.Press(Key.Escape);
        Read();
        _ = app.Keyboard.Press(Key.Escape);
        Read();
        _ = app.Keyboard.Press(Key.F10);
        _ = app.Keyboard.Press(Key.V);
        Read();
        _ = app.Keyboard.Press(Key.Enter);
        Read();

        bar.Dialog.Set(true);
        _ = app.PumpAndSettle();
        bool handled = app.Keyboard.Press(Key.V, KeyModifiers.Alt);
        var inDialog = new List<(string Focused, int Popups)>();
        void ReadDialog()
        {
            _ = app.PumpAndSettle();
            inDialog.Add((bar.NameOf(app.Tree.Focus.Focused), app.Popups.Count));
        }

        ReadDialog();
        PressAlt(app);
        ReadDialog();
        _ = app.Keyboard.Press(Key.F10);
        ReadDialog();

        Assert.Equal([("Zoom", 1), ("View", 0), ("content", 0), ("Zoom", 1), ("content", 0)], steps);
        Assert.Equal(1, bar.Zooms);
        Assert.False(handled);
        Assert.Equal([("dialog", 1), ("dialog", 1), ("dialog", 1)], inDialog);
    }

    /// <summary>
    /// A press on an item opens its menu, and a press on it while the menu is open closes it; the pointer coming over another item
    /// opens that one's menu in its place only while a menu is open.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bar of <see cref="Start"/>.
    ///
    /// Steps:
    /// - Move over "View"; click "File"; move over "View"; click "View"; reading the popups, and the anchor of the one open.
    ///
    /// Expected:
    /// - No popup; "File"'s; "View"'s; none.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBar_Pointer_PressTogglesAndOverSwitches()
    {
        await using Bar bar = await Start();
        HeadlessApp app = bar.App;
        var steps = new List<string>();
        void Read()
        {
            _ = app.PumpAndSettle();
            steps.Add(app.Popups.Count == 0 ? "none" : bar.NameOf(app.Tree.Popups.Opened[0].Anchor));
        }

        MoveOver(app, bar.View);
        Read();
        app.Pointer.Click(bar.File);
        Read();
        MoveOver(app, bar.View);
        Read();
        app.Pointer.Click(bar.View);
        Read();

        Assert.Equal(["none", "File", "View", "none"], steps);
    }

    /// <summary>
    /// The bar that is not selected takes no stop of Tab; the focus moving out of the selected bar leaves it, with no focus given back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The bar of <see cref="Start"/>, above the focusable box and a second focusable box.
    ///
    /// Steps:
    /// - Tab three times, reading the focus; then F10 and a press on the second box; Tab.
    ///
    /// Expected:
    /// - The second box, the box, the second box. After the press, the second box; after Tab, the box: no item of the bar.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBar_Tab_DoesNotStop_AndTheFocusLeavingLeavesTheBar()
    {
        await using Bar bar = await Start();
        HeadlessApp app = bar.App;
        var tabs = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            _ = app.Keyboard.Press(Key.Tab);
            tabs.Add(bar.NameOf(app.Tree.Focus.Focused));
        }

        _ = app.Keyboard.Press(Key.F10);
        app.Pointer.Click(bar.Second);
        _ = app.PumpAndSettle();
        string afterPress = bar.NameOf(app.Tree.Focus.Focused);
        _ = app.Keyboard.Press(Key.Tab);
        _ = app.PumpAndSettle();

        Assert.Equal(["second", "content", "second"], tabs);
        Assert.Equal("second", afterPress);
        Assert.Equal("content", bar.NameOf(app.Tree.Focus.Focused));
    }

    // Alt down and up with nothing between, as a press of Alt alone sends it.
    private static void PressAlt(HeadlessApp app)
    {
        _ = app.Keyboard.Down(Key.LeftAlt, KeyModifiers.Alt);
        _ = app.Keyboard.Up(Key.LeftAlt);
        _ = app.PumpAndSettle();
    }

    private static void MoveOver(HeadlessApp app, Element element)
    {
        (int x, int y) = app.CenterOf(element);
        app.Pointer.Move(x, y);
    }

    // A window with a menu bar of "_File" (New, a submenu "Recent" of "One", Exit), a disabled "_Edit", and "_View" (Zoom, Reset),
    // above a focusable box, focused, and a second focusable box with a menu of "Cut"; a dialog of a focusable box; the menu and the
    // dialog open while their signals are true.
    private static async Task<Bar> Start()
    {
        var bar = new Bar();
        bar.App = await HeadlessApp.StartAsync(
            () =>
            {
                bar.File = MenuBarItem(AccessText("_File"), () => FilePanel(bar));
                bar.Edit = MenuBarItem(AccessText("_Edit"), () => Column(MenuItem(Elements.Text("Undo"), () => { }))).Disabled();
                bar.View = MenuBarItem(AccessText("_View"), () => ViewPanel(bar));
                bar.Content = Box().Width(100).Height(20).Focusable();
                bar.Second = Box()
                    .Width(100)
                    .Height(20)
                    .Focusable()
                    .Menu(bar.SecondMenu, () => Column(bar.Named("Cut", MenuItem(Elements.Text("Cut"), () => { }))));
                return Column(
                    MenuBar(bar.File, bar.Edit, bar.View),
                    bar.Content,
                    bar.Second,
                    Dialog(bar.Dialog, () => bar.Named("dialog", Box().Width(50).Height(20).Focusable()))
                );
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = bar.App.PumpAndSettle();
        _ = bar.App.Focus(bar.Content);
        _ = bar.App.PumpAndSettle();
        bar.Names[bar.File.Node] = "File";
        bar.Names[bar.Edit.Node] = "Edit";
        bar.Names[bar.View.Node] = "View";
        bar.Names[bar.Content.Node] = "content";
        bar.Names[bar.Second.Node] = "second";
        return bar;
    }

    private static Element FilePanel(Bar bar) =>
        Column(
            bar.Named("New", MenuItem(Elements.Text("New"), () => { })),
            bar.Named("Recent", SubmenuItem(Elements.Text("Recent"), () => Column(bar.Named("One", MenuItem(Elements.Text("One"), () => { }))))),
            bar.Named("Exit", MenuItem(Elements.Text("Exit"), () => { }))
        );

    private static Element ViewPanel(Bar bar) =>
        Column(
            bar.Named("Zoom", MenuItem(Elements.Text("Zoom"), () => bar.Zooms++)),
            bar.Named("Reset", MenuItem(Elements.Text("Reset"), () => { }))
        );

    private sealed class Bar : IAsyncDisposable
    {
        public HeadlessApp App { get; set; } = null!;

        public Element File { get; set; }

        public Element Edit { get; set; }

        public Element View { get; set; }

        public Element Content { get; set; }

        public Element Second { get; set; }

        public Signal<bool> Dialog { get; } = Signal.Create(false);

        public Signal<bool> SecondMenu { get; } = Signal.Create(false);

        public int Zooms { get; set; }

        public Dictionary<NodeHandle, string> Names { get; } = [];

        public Element Named(string name, Element element)
        {
            Names[element.Node] = name;
            return element;
        }

        public string NameOf(NodeHandle node) => Names.TryGetValue(node, out string? name) ? name : "other";

        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
