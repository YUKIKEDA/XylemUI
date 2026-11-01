using System.Numerics;
using XylemUI.Testing;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Primitives.Tests;

// The select (row P02d2, design §13.1, "Select") over a headless window of 800 × 600: the keys of its closed box and of its open
// list, what chooses and what cancels, where the list goes, its scrolling, the pointer, and typing. The options are strings; each
// one's look is a 60 × 20 box, and the select's box is 150 wide at (0, 0), with a focusable 300 × 30 box after it.
public sealed class SelectTests
{
    private static readonly string[] Fruits = ["apple", "banana", "cherry", "date", "elder"];

    /// <summary>
    /// While the list is closed, Up and Down change the value to the enabled option before and after, stopping at the ends; a value
    /// that is no option goes to the first enabled option with Down and to the last with Up.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of the five fruits with "cherry" not enabled, its value "banana", focused.
    ///
    /// Steps:
    /// - Down, Up, Up, Up. Set the value to "fig"; Down. Set it to "fig"; Up. Read the value after each, and the popups.
    ///
    /// Expected:
    /// - "date" (past "cherry"), "banana", "apple", "apple"; "apple"; "elder". No popup.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ClosedKeys_ChangeTheValue_PastDisabled_StoppingAtTheEnds()
    {
        var value = Signal.Create("banana");
        await using Scene scene = await Scene.StartAsync(value, new SelectOptions<string> { Enabled = f => f != "cherry" });
        var seen = new List<string>();

        foreach (Key key in new[] { Key.Down, Key.Up, Key.Up, Key.Up })
        {
            _ = scene.App.Keyboard.Press(key);
            seen.Add(value.Get());
        }

        value.Set("fig");
        _ = scene.App.Keyboard.Press(Key.Down);
        seen.Add(value.Get());
        value.Set("fig");
        _ = scene.App.Keyboard.Press(Key.Up);
        seen.Add(value.Get());
        _ = scene.App.PumpAndSettle();

        Assert.Equal(["date", "banana", "apple", "apple", "apple", "elder"], seen);
        Assert.Empty(scene.App.Popups);
    }

    /// <summary>
    /// Alt+Down, F4, Enter, and Space open the list with the value's option focused; Alt+Up and F4 close it with the value kept.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of the five fruits, its value "cherry", focused.
    ///
    /// Steps:
    /// - Alt+Down, then Alt+Up; F4, then F4; Enter, then Esc; Space, then Esc. Read the popups and the focused row after each opening
    ///   and the popups and the focus after each closing.
    ///
    /// Expected:
    /// - Each opening: one popup, the third row focused. Each closing: no popup, the box focused, the value "cherry".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task OpenAndCloseKeys_FocusTheValuesRow_AndKeepTheValue()
    {
        var value = Signal.Create("cherry");
        await using Scene scene = await Scene.StartAsync(value);
        var seen = new List<string>();

        foreach (
            (Key open, KeyModifiers openMods, Key close, KeyModifiers closeMods) in new[]
            {
                (Key.Down, KeyModifiers.Alt, Key.Up, KeyModifiers.Alt),
                (Key.F4, KeyModifiers.None, Key.F4, KeyModifiers.None),
                (Key.Enter, KeyModifiers.None, Key.Escape, KeyModifiers.None),
                (Key.Space, KeyModifiers.None, Key.Escape, KeyModifiers.None),
            }
        )
        {
            _ = scene.App.Keyboard.Press(open, openMods);
            _ = scene.App.PumpAndSettle();
            seen.Add($"{scene.App.Popups.Count} {scene.FocusedRow()}");
            _ = scene.App.Keyboard.Press(close, closeMods);
            _ = scene.App.PumpAndSettle();
            seen.Add($"{scene.App.Popups.Count} {(scene.App.Tree.Focus.Focused == scene.Box.Node ? "box" : "other")} {value.Get()}");
        }

        Assert.Equal(["1 2", "0 box cherry", "1 2", "0 box cherry", "1 2", "0 box cherry", "1 2", "0 box cherry"], seen);
    }

    /// <summary>
    /// In the open list, Up and Down move the focus between the enabled rows and stop at the ends, Home and End go to the first and
    /// the last, and the moves leave the value; Enter chooses the focused row, closes the list, and gives the focus back to the box.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of the five fruits with "date" not enabled, its value "banana", focused, opened with Enter.
    ///
    /// Steps:
    /// - Down, Down, Down, Up, Home, Up, End; read the focused row and the value after each. Enter.
    ///
    /// Expected:
    /// - Rows 2, 4 (past "date"), 4, 2, 0, 0, 4, with the value "banana" throughout. After Enter: the value "elder", no popup, the box
    ///   focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ListKeys_MoveThePastDisabled_StopAtTheEnds_AndEnterChooses()
    {
        var value = Signal.Create("banana");
        await using Scene scene = await Scene.StartAsync(value, new SelectOptions<string> { Enabled = f => f != "date" });
        _ = scene.App.Keyboard.Press(Key.Enter);
        _ = scene.App.PumpAndSettle();
        var seen = new List<string>();

        foreach (Key key in new[] { Key.Down, Key.Down, Key.Down, Key.Up, Key.Home, Key.Up, Key.End })
        {
            _ = scene.App.Keyboard.Press(key);
            seen.Add($"{scene.FocusedRow()} {value.Get()}");
        }

        _ = scene.App.Keyboard.Press(Key.Enter);
        _ = scene.App.PumpAndSettle();

        Assert.Equal(["2 banana", "4 banana", "4 banana", "2 banana", "0 banana", "0 banana", "4 banana"], seen);
        Assert.Equal("elder", value.Get());
        Assert.Empty(scene.App.Popups);
        Assert.Equal(scene.Box.Node, scene.App.Tree.Focus.Focused);
    }

    /// <summary>
    /// Esc and a press outside close the list with the value kept, and the press does not reach the node under it; Tab closes it and
    /// moves the focus on from the box.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of the five fruits, its value "banana", focused; the focusable 300 × 30 box after it at (0, 20), whose clicks count.
    ///
    /// Steps:
    /// - Open with Enter, Down, Esc. Open, Down, click at (250, 30) on the box after it, past the list's 150 px. Open, Down, Tab. Read the value, the popups,
    ///   and the focus after each.
    ///
    /// Expected:
    /// - After Esc: "banana", no popup, the select focused. After the click: "banana", no popup, no click counted. After Tab:
    ///   "banana", no popup, the box after it focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task EscOutsideAndTab_CloseWithTheValueKept()
    {
        var value = Signal.Create("banana");
        await using Scene scene = await Scene.StartAsync(value);
        var seen = new List<string>();

        for (int way = 0; way < 3; way++)
        {
            _ = scene.App.Focus(scene.Box);
            _ = scene.App.Keyboard.Press(Key.Enter);
            _ = scene.App.PumpAndSettle();
            _ = scene.App.Keyboard.Press(Key.Down);
            if (way == 0)
            {
                _ = scene.App.Keyboard.Press(Key.Escape);
            }
            else if (way == 1)
            {
                scene.App.Pointer.Click(250, 30);
            }
            else
            {
                _ = scene.App.Keyboard.Press(Key.Tab);
            }

            _ = scene.App.PumpAndSettle();
            NodeHandle focused = scene.App.Tree.Focus.Focused;
            string at =
                focused == scene.Box.Node ? "select"
                : focused == scene.After.Node ? "after"
                : "other";
            seen.Add($"{value.Get()} {scene.App.Popups.Count} {at}");
        }

        Assert.Equal(["banana 0 select", "banana 0 select", "banana 0 after"], seen);
        Assert.Equal(0, scene.AfterClicks);
    }

    /// <summary>
    /// The list opens below the box, at least as wide as it; past the most height it scrolls, with the value's row in view; PageDown
    /// moves by the rows that fit in it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of 20 options "o0" to "o19" (rows 20 high, 400 in all), its value "o15", focused.
    ///
    /// Steps:
    /// - Open with Enter; read the list's top left and size, the scroll position, and the value's row's place in the list's view.
    ///   Home, then PageDown; read the focused row.
    ///
    /// Expected:
    /// - The list at (0, 20), 150 × 200 (the box's width, the most height); scrolled down, the row "o15" inside the 200 px view. After
    ///   PageDown from "o0": row 10.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task List_BelowTheBox_AsWide_ScrollsToTheValue_AndPages()
    {
        string[] many = Enumerable.Range(0, 20).Select(i => $"o{i}").ToArray();
        var value = Signal.Create("o15");
        await using Scene scene = await Scene.StartAsync(value, choices: many);
        _ = scene.App.Keyboard.Press(Key.Enter);
        _ = scene.App.PumpAndSettle();

        UiTree tree = scene.App.Tree;
        NodeHandle list = tree.Nodes.GetRef(tree.Nodes.GetRef(scene.App.Popups[0].Node).FirstChild).FirstChild;
        Vector2 at = TopLeft(tree, list);
        Rect box = tree.GetRect(list);
        float scrolled = tree.ScrollOffsetOf(list).Y;
        float rowTop = TopLeft(tree, tree.Focus.Focused).Y - at.Y;
        _ = scene.App.Keyboard.Press(Key.Home);
        _ = scene.App.Keyboard.Press(Key.PageDown);
        int paged = scene.FocusedRow();
        _ = scene.App.PumpAndSettle();

        Assert.Equal(new Vector2(0, 20), at);
        Assert.Equal((150f, 200f), (box.Width, box.Height));
        Assert.True(scrolled > 0);
        Assert.InRange(rowTop, 0, 180);
        Assert.Equal(10, paged);
    }

    /// <summary>
    /// A press on the box opens the list, a row the pointer comes over takes the focus, a press on a row chooses it, and a press on the
    /// box while the list is open only closes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of the five fruits, its value "apple" (rows from (0, 20), 20 high).
    ///
    /// Steps:
    /// - Click the box; move to (30, 70), over row 2; read the focused row. Click (30, 90), row 3. Click the box; click it again.
    ///
    /// Expected:
    /// - Row 2 focused. Then the value "date", no popup. The box's first click opens the list, the second closes it: no popup.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Pointer_OpensHoversChooses_AndAPressOnTheBoxCloses()
    {
        var value = Signal.Create("apple");
        await using Scene scene = await Scene.StartAsync(value);

        scene.App.Pointer.Click(scene.Box);
        _ = scene.App.PumpAndSettle();
        scene.App.Pointer.Move(30, 70);
        _ = scene.App.PumpAndSettle();
        int hovered = scene.FocusedRow();
        scene.App.Pointer.Click(30, 90);
        _ = scene.App.PumpAndSettle();
        string chosen = value.Get();
        int afterChoice = scene.App.Popups.Count;
        scene.App.Pointer.Click(scene.Box);
        _ = scene.App.PumpAndSettle();
        int opened = scene.App.Popups.Count;
        scene.App.Pointer.Click(scene.Box);
        _ = scene.App.PumpAndSettle();

        Assert.Equal(2, hovered);
        Assert.Equal(("date", 0), (chosen, afterChoice));
        Assert.Equal(1, opened);
        Assert.Empty(scene.App.Popups);
    }

    /// <summary>
    /// Typing finds the enabled option that starts with the letters typed within a second, ignoring case: closed, it changes the
    /// value; open, it moves the focus; a letter typed again goes on to the next option of that letter.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of "apple", "avocado", "banana", "blueberry", "cherry" found by their own text, its value "apple", focused.
    ///
    /// Steps:
    /// - Closed: type "b"; type "L" at once; wait 1 s; type "a"; type "a" after 1 s. Open with Enter; wait 1 s; type "c". Read the value, or
    ///   the focused row, after each.
    ///
    /// Expected:
    /// - "banana", "blueberry" ("bl"), "apple" (a fresh "a" after "blueberry", round the end), "avocado" (the next "a"); then
    ///   row 4.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Typing_FindsTheOption_ClosedAndOpen()
    {
        string[] fruits = ["apple", "avocado", "banana", "blueberry", "cherry"];
        var value = Signal.Create("apple");
        await using Scene scene = await Scene.StartAsync(value, new SelectOptions<string> { TextOf = f => f }, fruits);
        var seen = new List<string>();

        _ = scene.App.Keyboard.Type("b");
        seen.Add(value.Get());
        _ = scene.App.Keyboard.Type("L");
        seen.Add(value.Get());
        scene.App.Pump(TimeSpan.FromSeconds(1.1));
        _ = scene.App.Keyboard.Type("a");
        seen.Add(value.Get());
        scene.App.Pump(TimeSpan.FromSeconds(1.1));
        _ = scene.App.Keyboard.Type("a");
        seen.Add(value.Get());
        _ = scene.App.Keyboard.Press(Key.Enter);
        _ = scene.App.PumpAndSettle();
        scene.App.Pump(TimeSpan.FromSeconds(1.1));
        _ = scene.App.Keyboard.Type("c");
        seen.Add($"row {scene.FocusedRow()}");
        _ = scene.App.PumpAndSettle();

        Assert.Equal(["banana", "blueberry", "apple", "avocado", "row 4"], seen);
    }

    /// <summary>
    /// The panel given wraps the open list: the popup shows the panel, which holds the list, and the list's keys still work.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of the five fruits whose panel puts the list in a box with a padding of 4 and a background; its value "apple",
    ///   focused.
    ///
    /// Steps:
    /// - Open with Enter; read the popup's content and its child; press Down; read the focused row and the value.
    ///
    /// Expected:
    /// - The content is the panel's box, and its child is the list; row 1 focused, the value "apple".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Panel_WrapsTheList_AndItsKeysWork()
    {
        var value = Signal.Create("apple");
        Element panel = default;
        await using Scene scene = await Scene.StartAsync(
            value,
            new SelectOptions<string> { Panel = list => panel = Box(list).Padding(4).Background(new Paint(Color.FromRgb(0x2B3245))) }
        );
        _ = scene.App.Keyboard.Press(Key.Enter);
        _ = scene.App.PumpAndSettle();

        UiTree tree = scene.App.Tree;
        NodeHandle content = tree.Nodes.GetRef(tree.Nodes.GetRef(scene.App.Popups[0].Node).FirstChild).FirstChild;
        NodeHandle child = tree.Nodes.GetRef(content).FirstChild;
        NodeHandle firstRow = tree.Nodes.GetRef(tree.Nodes.GetRef(child).FirstChild).FirstChild;
        _ = scene.App.Keyboard.Press(Key.Down);
        NodeHandle focused = tree.Focus.Focused;
        _ = scene.App.PumpAndSettle();

        Assert.Equal(panel.Node, content);
        Assert.Equal(tree.Nodes.GetRef(firstRow).NextSibling, focused);
        Assert.Equal("apple", value.Get());
    }

    /// <summary>
    /// With the list open, the frames of the keys and of a hover that move the focus between its rows allocate nothing (design §13.1,
    /// "Checks").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A select of the five fruits, its value "apple", opened with Enter; warmed up with the iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: move the pointer over row 1 or row 3 by the
    ///   iteration's number, press Down, and settle.
    ///
    /// Expected:
    /// - No allocation.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SteadyState_KeysAndHoverInTheList_AllocateNothing()
    {
        var value = Signal.Create("apple");
        await using Scene scene = await Scene.StartAsync(value);
        _ = scene.App.Keyboard.Press(Key.Enter);
        _ = scene.App.PumpAndSettle();

        void Step(int i)
        {
            scene.App.Pointer.Move(30, 50 + (40 * (i & 1)));
            _ = scene.App.Keyboard.Press(Key.Down);
            _ = scene.App.PumpAndSettle();
        }

        for (int i = 0; i < 100; i++)
        {
            Step(i);
        }

        _ = PerformanceAssert.DoesNotAllocate(1_000, Step);
    }

    private static Vector2 TopLeft(UiTree tree, NodeHandle node)
    {
        Rect rect = tree.GetRect(node);
        var point = new Vector2(rect.X, rect.Y);
        for (NodeHandle parent = tree.Nodes.GetRef(node).Parent; !parent.IsNone; parent = tree.Nodes.GetRef(parent).Parent)
        {
            Rect box = tree.GetRect(parent);
            point += new Vector2(box.X, box.Y) - tree.ScrollOffsetOf(parent);
        }

        return point;
    }

    // A select 150 wide over a focusable box whose clicks count, settled, with the select focused.
    private sealed class Scene : IAsyncDisposable
    {
        private Scene(HeadlessApp app, Parts parts)
        {
            App = app;
            Box = parts.Select;
            After = parts.After;
            Clicks = parts;
        }

        public HeadlessApp App { get; }

        public Element Box { get; }

        public Element After { get; }

        public int AfterClicks => Clicks.AfterClicks;

        private Parts Clicks { get; }

        public static async Task<Scene> StartAsync(Signal<string> value, SelectOptions<string>? options = null, string[]? choices = null)
        {
            var parts = new Parts();
            HeadlessApp app = await HeadlessApp.StartAsync(() =>
            {
                parts.Select = Select(value, choices ?? Fruits, _ => Box().Width(60).Height(20), options).Width(150);
                parts.After = Box().Width(300).Height(30).Focusable().OnClick(() => parts.AfterClicks++);
                return Column(parts.Select, parts.After);
            });
            _ = app.PumpAndSettle();
            _ = app.Focus(parts.Select);
            _ = app.PumpAndSettle();
            return new Scene(app, parts);
        }

        // The index of the focused row in the open list, or -1.
        public int FocusedRow()
        {
            if (App.Popups.Count == 0)
            {
                return -1;
            }

            UiTree tree = App.Tree;
            NodeHandle list = tree.Nodes.GetRef(tree.Nodes.GetRef(App.Popups[0].Node).FirstChild).FirstChild;
            int index = 0;
            for (
                NodeHandle row = tree.Nodes.GetRef(tree.Nodes.GetRef(list).FirstChild).FirstChild;
                !row.IsNone;
                row = tree.Nodes.GetRef(row).NextSibling
            )
            {
                if (row == tree.Focus.Focused)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }

        public ValueTask DisposeAsync() => App.DisposeAsync();

        private sealed class Parts
        {
            public Element Select { get; set; }

            public Element After { get; set; }

            public int AfterClicks { get; set; }
        }
    }
}
