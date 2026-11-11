using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The parts beside the default rows of XylemUI.Core (row KM2a, design §7, "The default keys and actions"): the keymap is tried
// before the key handlers, so a part inside a page that scrolls keeps the keys it handles only because it takes their default
// rows away in its own context (PrimitiveKeyBindings).
public sealed class DefaultKeysPartsTests
{
    /// <summary>
    /// A text field inside a page that scrolls keeps the keys it handles: they move its caret and do not scroll the page. A key
    /// the field does not handle scrolls the page.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 100 high over a column 600 high: a field of many lines with the text of three lines, a field of one
    ///   line with "abc", and a box 500 high. Each field has a selection signal.
    ///
    /// Steps:
    /// - With the focus in the field of many lines, whose caret is at the end (index 5): press Up, then Home. With the focus
    ///   in the field of one line: press Down, then Home, then End.
    ///
    /// Expected:
    /// - In the field of many lines, Up moves the caret to the second line (index 3) and Home to that line's start (index 2);
    ///   the page stays at 0. In the field of one line, Down is not the field's key and scrolls the page by 50; Home moves the
    ///   caret to 0 and End to 3, and the page stays where Down left it.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task TextField_InAPageThatScrolls_KeepsItsKeys()
    {
        var page = new ScrollState();
        var manySelection = Signal.Create(default(TextSelection));
        var oneSelection = Signal.Create(default(TextSelection));
        Element many = default;
        Element one = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                many = TextField(Signal.Create("a\nb\nc"), new TextFieldOptions { Multiline = true, Selection = manySelection }).Width(200);
                one = TextField(Signal.Create("abc"), new TextFieldOptions { Selection = oneSelection }).Width(200);
                return ScrollView(Column(many, one, Box().Width(200).Height(500)), page).Width(300).Height(100);
            },
            new HeadlessOptions
            {
                Width = 400,
                Height = 200,
                CaretBlink = false,
            }
        );
        _ = app.PumpAndSettle();
        var log = new List<(int Caret, float Page)>();
        void Press(Key key, Signal<TextSelection> selection)
        {
            _ = app.Keyboard.Press(key);
            _ = app.PumpAndSettle();
            log.Add((selection.Get().Caret, page.Offset.Get().Y));
        }

        Assert.True(app.Focus(many));
        _ = app.PumpAndSettle();
        Assert.Equal(5, manySelection.Get().Caret);
        Press(Key.Up, manySelection);
        Press(Key.Home, manySelection);
        Assert.True(app.Focus(one));
        _ = app.PumpAndSettle();
        float before = page.Offset.Get().Y;
        Press(Key.Down, oneSelection);
        Press(Key.Home, oneSelection);
        Press(Key.End, oneSelection);

        Assert.Equal([(3, 0f), (2, 0f)], log[..2]);
        Assert.Equal(before + 50, log[2].Page);
        Assert.Equal([(0, before + 50), (3, before + 50)], log[3..]);
    }

    /// <summary>
    /// A menu open from a node inside a page that scrolls keeps its arrows, Home, and End: they move the focus among its items
    /// and do not scroll the page, whose scroll view is on the route of the menu's keys through the menu's anchor. Esc closes
    /// the menu.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 100 high over a column 600 high whose first box is the anchor of a menu of three items; the menu is open
    ///   and the focus is in it.
    ///
    /// Steps:
    /// - Press End, Home, Down, Up, and Esc.
    ///
    /// Expected:
    /// - The focus is on the third item, the first, the second, and the first; the page stays at 0. After Esc no popup is open.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Menu_OpenFromAPageThatScrolls_KeepsItsKeys()
    {
        var page = new ScrollState();
        var open = Signal.Create(false);
        var items = new Element[3];
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element anchor = Box()
                    .Width(100)
                    .Height(20)
                    .Focusable()
                    .Menu(
                        open,
                        () =>
                        {
                            for (int i = 0; i < items.Length; i++)
                            {
                                items[i] = MenuItem(Box().Width(80).Height(20), static () => { });
                            }

                            return Column(items);
                        }
                    );
                return ScrollView(Column(anchor, Box().Width(100).Height(580)), page).Width(300).Height(100);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        open.Set(true);
        _ = app.PumpAndSettle();
        Assert.Single(app.Popups);
        var focused = new List<int>();
        foreach (Key key in new[] { Key.End, Key.Home, Key.Down, Key.Up })
        {
            _ = app.Keyboard.Press(key);
            _ = app.PumpAndSettle();
            focused.Add(Array.FindIndex(items, item => item.Node == app.Tree.Focus.Focused));
        }

        Assert.Equal([2, 0, 1, 0], focused);
        Assert.Equal(0f, page.Offset.Get().Y);

        Assert.True(app.Keyboard.Press(Key.Escape));
        _ = app.PumpAndSettle();
        Assert.Empty(app.Popups);
    }
}
