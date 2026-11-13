using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// Menu items that name actions (row KM2c, design §7, "Menu items that name actions"): an item reads its keys and whether its
// action can run at the node the focus was on before its menus opened, and dispatches the action from there.
public sealed class MenuActionItemTests
{
    /// <summary>
    /// The items of a menu bar's menu read and dispatch at the node the focus was on before the bar was selected: an item is
    /// enabled while that node can take its action, shows the keys that ask for the action there, and choosing it closes the menu,
    /// gives the focus back, and dispatches the action from that node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A menu bar with one item, whose menu has items that name <c>text::Copy</c>, <c>text::Undo</c>, and <c>Ping</c>, an action
    ///   no node handles. Below the bar, a focused text field with "abc", all selected, and nothing to undo.
    ///
    /// Steps:
    /// - Press F10 and Down, settle, and read the three items: disabled or not, and the keys each content was given. Press Enter
    ///   (the copy has the focus) and settle.
    ///
    /// Expected:
    /// - The copy is enabled with Ctrl+C; the undo is disabled with Ctrl+Z; <c>Ping</c> is disabled with no keys. After Enter: no
    ///   popup, "abc" on the clipboard, and the field has the focus.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBarMenuItems_ReadAndDispatch_WhereTheFocusWasBeforeTheBar()
    {
        var items = new Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)>();
        Element field = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element edit = MenuBarItem(
                    AccessText("_Edit"),
                    () =>
                        Column(
                            Named(items, "copy", new TextActions.Copy()),
                            Named(items, "undo", new TextActions.Undo()),
                            Named(items, "ping", new Ping())
                        )
                );
                field = TextField(Signal.Create("abc")).Width(200);
                return Column(MenuBar(edit), field);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(field));
        _ = app.PumpAndSettle();

        _ = app.Keyboard.Press(Key.F10);
        _ = app.Keyboard.Press(Key.Down);
        _ = app.PumpAndSettle();
        (bool, string?)[] seen = [State(app, items["copy"]), State(app, items["undo"]), State(app, items["ping"])];
        _ = app.Keyboard.Press(Key.Enter);
        _ = app.PumpAndSettle();

        Assert.Equal([(true, "Ctrl+C"), (false, "Ctrl+Z"), (false, null)], seen);
        Assert.Empty(app.Popups);
        Assert.Equal("abc", app.Clipboard.Text);
        Assert.Equal(field.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// A context menu whose node the focus is not in reads and dispatches at its node; one whose node the focus is in reads and
    /// dispatches at the focused node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box with a handler of <c>Ping</c> that logs "box" and a context menu of one item that names <c>Ping</c>; in the box, a
    ///   focusable box with a handler of <c>Ping</c> that logs "inner". Beside the box, a focused box with no handler.
    ///
    /// Steps:
    /// - Click the right button on the outer box, away from the inner one; settle and read the item; press Enter and settle. Focus
    ///   the inner box; click the right button at the same place, settle, press Enter, and settle.
    ///
    /// Expected:
    /// - The item is enabled the first time, with the focus outside the box. The log is "box", then "inner".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ContextMenuItems_ReadAndDispatch_AtTheMenusNode_UnlessTheFocusIsInIt()
    {
        var log = new List<string>();
        var items = new Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)>();
        Element inner = default;
        Element other = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                inner = Box().Width(20).Height(20).Focusable().OnAction((in ActionEvent<Ping> _) => log.Add("inner"));
                Element box = Row(inner)
                    .Width(200)
                    .Height(40)
                    .OnAction((in ActionEvent<Ping> _) => log.Add("box"))
                    .ContextMenu(() => Column(Named(items, "ping", new Ping())));
                other = Box().Width(20).Height(20).Focusable();
                return Column(box, other);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(other));
        _ = app.PumpAndSettle();

        app.Pointer.Click(150, 20, PointerButton.Right);
        _ = app.PumpAndSettle();
        bool enabled = State(app, items["ping"]).Enabled;
        _ = app.Keyboard.Press(Key.Enter);
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(inner));
        _ = app.PumpAndSettle();
        app.Pointer.Click(150, 20, PointerButton.Right);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.Enter);
        _ = app.PumpAndSettle();

        Assert.True(enabled);
        Assert.Equal(["box", "inner"], log);
    }

    /// <summary>
    /// The items of a submenu have the context of the menu it opens from, a press on an item chooses it as Enter does, and an
    /// item whose action cannot run is passed over by the keys.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box that opens a menu by a signal: an item that names <c>Other</c>, which no node handles, then a submenu item whose
    ///   submenu has one item that names <c>Ping</c>. A focused box beside it handles <c>Ping</c> and logs it.
    ///
    /// Steps:
    /// - Open the menu and settle; read the focus (the first enabled item). Press Right and settle; read both items that name
    ///   actions. Click the submenu's and settle.
    ///
    /// Expected:
    /// - The focus is on the submenu item, not on the disabled one. The submenu's item is enabled. After the click: one
    ///   <c>Ping</c> in the log, no popup, and the focus is back on the box that had it.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SubmenuItems_HaveTheOuterMenusContext_AndAPressChooses()
    {
        var log = new List<string>();
        var items = new Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)>();
        var open = Signal.Create(false);
        Element more = default;
        Element focused = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element anchor = Box().Width(60).Height(20);
                _ = anchor.Menu(
                    open,
                    () =>
                        Column(
                            Named(items, "other", new Other()),
                            more = SubmenuItem(Box().Width(80).Height(20), () => Column(Named(items, "ping", new Ping())))
                        )
                );
                focused = Box().Width(60).Height(20).Focusable().OnAction((in ActionEvent<Ping> _) => log.Add("ping"));
                return Column(anchor, focused);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(focused));
        _ = app.PumpAndSettle();

        open.Set(true);
        _ = app.PumpAndSettle();
        NodeHandle first = app.Tree.Focus.Focused;
        _ = app.Keyboard.Press(Key.Right);
        _ = app.PumpAndSettle();
        (bool, bool) enabled = (State(app, items["other"]).Enabled, State(app, items["ping"]).Enabled);
        app.Pointer.Click(items["ping"].Item);
        _ = app.PumpAndSettle();

        Assert.Equal(more.Node, first);
        Assert.Equal((false, true), enabled);
        Assert.Equal(["ping"], log);
        Assert.Empty(app.Popups);
        Assert.Equal(focused.Node, app.Tree.Focus.Focused);
    }

    /// <summary>
    /// The keys an item shows follow the keymap while its menu is open, are those of a row with an equal action for an action
    /// that compares values, and are read at the context node, not at the item.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box that opens a menu by a signal, with items that name <c>Ping</c> and <c>menubar::Open</c> with <c>Last</c> set. A
    ///   focused box beside it has the context <c>Side</c> and handles <c>Ping</c>. Rows: <c>ctrl-p</c> for <c>Ping</c> in
    ///   <c>Side</c>; <c>ctrl-o</c> for <c>menubar::Open</c> and <c>ctrl-l</c> for <c>menubar::Open</c> with <c>Last</c> set.
    ///
    /// Steps:
    /// - Open the menu, settle, and read the keys of both items. Take the context off the focused box, read <c>Ping</c>'s keys,
    ///   and give it back. Take the <c>ctrl-p</c> row out and read <c>Ping</c>'s keys; bind
    ///   <c>ctrl-q</c> to <c>Ping</c> in <c>Menu</c>, the context of the item itself, and read them again.
    ///
    /// Expected:
    /// - Ctrl+P and Ctrl+L. None without the context, and Ctrl+P with it again. None after the row is taken out, and none with the row of the menu's context.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ItemKeys_FollowTheKeymap_AndAreReadAtTheContextNode()
    {
        var items = new Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)>();
        var open = Signal.Create(false);
        Element focused = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element anchor = Box().Width(60).Height(20);
                _ = anchor.Menu(open, () => Column(Named(items, "ping", new Ping()), Named(items, "open", new MenuBarActions.Open { Last = true })));
                focused = Box().Width(60).Height(20).Focusable().KeyContext("Side").OnAction((in ActionEvent<Ping> _) => { });
                return Column(anchor, focused);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(focused));
        _ = app.PumpAndSettle();
        KeyBinding row = app.Keymap.Bind("ctrl-p", new Ping(), "Side");
        _ = app.Keymap.Bind("ctrl-o", new MenuBarActions.Open());
        _ = app.Keymap.Bind("ctrl-l", new MenuBarActions.Open { Last = true });
        _ = app.Keymap.Bind("ctrl-o", new MenuBarActions.Open());

        open.Set(true);
        _ = app.PumpAndSettle();
        (string?, string?) first = (items["ping"].Keys.Get()?.ToDisplayString(), items["open"].Keys.Get()?.ToDisplayString());
        _ = focused.KeyContext(null);
        string? withoutContext = items["ping"].Keys.Get()?.ToDisplayString();
        _ = focused.KeyContext("Side");
        string? withContext = items["ping"].Keys.Get()?.ToDisplayString();
        Assert.True(app.Keymap.Remove(row));
        string? removed = items["ping"].Keys.Get()?.ToDisplayString();
        _ = app.Keymap.Bind("ctrl-q", new Ping(), "Menu");
        string? inMenu = items["ping"].Keys.Get()?.ToDisplayString();
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();

        Assert.Equal(("Ctrl+P", "Ctrl+L"), first);
        Assert.Equal((null, "Ctrl+P"), (withoutContext, withContext));
        Assert.Null(removed);
        Assert.Null(inMenu);
    }

    /// <summary>
    /// An item made outside a menu's panel reads and dispatches at the focused node, and an item of a menu whose context node
    /// was removed while the menu is open reads where a route starts with no focus, at the node the root function returned.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An item that names <c>Ping</c> in the window's column, and a box that opens a menu by a signal with another. Two focusable
    ///   boxes: one handles <c>Ping</c> and is shown by a signal; the other does not. The root handles <c>Other</c>, which a third
    ///   item, in the menu, names.
    ///
    /// Steps:
    /// - Focus the box with no handler and read the item of the column; focus the handling box and read it again. Open the menu,
    ///   settle, and read its two items; hide the handling box, settle, and read them again. Close the menu, focus the box that is
    ///   left, which handles <c>Third</c>, and show an item that names <c>Third</c> in the window's column.
    ///
    /// Expected:
    /// - The item of the column: disabled, then enabled. The menu's items: <c>Ping</c> enabled and <c>Other</c> enabled; after the
    ///   context node is removed, <c>Ping</c> disabled and <c>Other</c>, which the root handles, enabled. The item made after the
    ///   menu closed is enabled: it is outside a panel again and reads at the focused box.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Items_OutsideAMenu_ReadAtTheFocus_AndWithARemovedContextNode_AtTheRoot()
    {
        var items = new Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)>();
        var open = Signal.Create(false);
        var shown = Signal.Create(true);
        var late = Signal.Create(false);
        Element handles = default;
        Element plain = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element anchor = Box().Width(60).Height(20);
                _ = anchor.Menu(open, () => Column(Named(items, "ping", new Ping()), Named(items, "other", new Other())));
                plain = Box().Width(60).Height(20).Focusable().OnAction((in ActionEvent<Third> _) => { });
                return Column(
                        anchor,
                        Named(items, "outside", new Ping()),
                        plain,
                        Show(shown, () => handles = Box().Width(60).Height(20).Focusable().OnAction((in ActionEvent<Ping> _) => { })),
                        Show(late, () => Named(items, "late", new Third()))
                    )
                    .OnAction((in ActionEvent<Other> _) => { });
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();

        Assert.True(app.Focus(plain));
        _ = app.PumpAndSettle();
        bool withPlain = State(app, items["outside"]).Enabled;
        Assert.True(app.Focus(handles));
        _ = app.PumpAndSettle();
        bool withHandler = State(app, items["outside"]).Enabled;
        open.Set(true);
        _ = app.PumpAndSettle();
        (bool, bool) inMenu = (State(app, items["ping"]).Enabled, State(app, items["other"]).Enabled);
        shown.Set(false);
        _ = app.PumpAndSettle();
        (bool, bool) removed = (State(app, items["ping"]).Enabled, State(app, items["other"]).Enabled);
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(plain));
        late.Set(true);
        _ = app.PumpAndSettle();

        Assert.False(withPlain);
        Assert.True(withHandler);
        Assert.Equal((true, true), inMenu);
        Assert.Equal((false, true), removed);
        Assert.True(State(app, items["late"]).Enabled);
    }

    /// <summary>
    /// A text field answers whether it can take an undo and a redo from its history, and the computation that asked runs again
    /// after an edit.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused text field with "abc" and no edit yet. Two computations made in the root function read <c>CanDispatch</c> of
    ///   <c>text::Undo</c> and <c>text::Redo</c>.
    ///
    /// Steps:
    /// - Read both. Type "d" and read both. Press Control+Z and read both.
    ///
    /// Expected:
    /// - No and no; yes and no after the edit; no and yes after the undo.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Field_AnswersUndoAndRedo_FromItsHistory()
    {
        Element field = default;
        Computed<bool>? undo = null;
        Computed<bool>? redo = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                field = TextField(Signal.Create("abc")).Width(200);
                undo = Computed.Create(() => field.CanDispatch(new TextActions.Undo()));
                redo = Computed.Create(() => field.CanDispatch(new TextActions.Redo()));
                return Column(field);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(field));
        _ = app.PumpAndSettle();

        (bool, bool) fresh = (undo!.Get(), redo!.Get());
        _ = app.Keyboard.Press(Key.End);
        _ = app.Keyboard.Type("d");
        _ = app.PumpAndSettle();
        (bool, bool) edited = (undo.Get(), redo.Get());
        _ = app.Keyboard.Press(Key.Z, KeyModifiers.Control);
        _ = app.PumpAndSettle();

        Assert.Equal((false, false), fresh);
        Assert.Equal((true, false), edited);
        Assert.Equal((false, true), (undo.Get(), redo.Get()));
    }

    /// <summary>
    /// An item made while its menu is open, by a block of the menu's panel, has the menu's context as the items made with the
    /// panel have, in a menu and in its submenu.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box that opens a menu by a signal. Its panel has an item that names <c>Ping</c>, a block that shows another such item
    ///   by a signal, and a submenu item whose submenu's panel has a block that shows a third by the same signal. A focused box
    ///   beside the first handles <c>Ping</c> and logs it.
    ///
    /// Steps:
    /// - Open the menu, press Down to the submenu item and Right to open the submenu, and settle. Set the signal, settle, and read
    ///   the three items. Click the item shown in the menu and settle.
    ///
    /// Expected:
    /// - All three are enabled: the two made after the menus opened read at the box that had the focus, not at the menu's item
    ///   that has it now. After the click: one <c>Ping</c> in the log and no popup.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ItemMadeWhileTheMenuIsOpen_HasTheMenusContext()
    {
        var log = new List<string>();
        var items = new Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)>();
        var open = Signal.Create(false);
        var more = Signal.Create(false);
        Element focused = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element anchor = Box().Width(60).Height(20);
                _ = anchor.Menu(
                    open,
                    () =>
                        Column(
                            Named(items, "first", new Ping()),
                            Show(more, () => Named(items, "late", new Ping())),
                            SubmenuItem(Box().Width(80).Height(20), () => Column(Show(more, () => Named(items, "deep", new Ping()))))
                        )
                );
                focused = Box().Width(60).Height(20).Focusable().OnAction((in ActionEvent<Ping> _) => log.Add("ping"));
                return Column(anchor, focused);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(focused));
        _ = app.PumpAndSettle();

        open.Set(true);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.Down);
        _ = app.Keyboard.Press(Key.Right);
        _ = app.PumpAndSettle();
        int popups = app.Popups.Count;
        more.Set(true);
        _ = app.PumpAndSettle();
        (bool, bool, bool) enabled = (State(app, items["first"]).Enabled, State(app, items["late"]).Enabled, State(app, items["deep"]).Enabled);
        app.Pointer.Click(items["late"].Item);
        _ = app.PumpAndSettle();

        Assert.Equal(2, popups);
        Assert.Equal((true, true, true), enabled);
        Assert.Equal(["ping"], log);
        Assert.Empty(app.Popups);
    }

    /// <summary>
    /// The items of a menu do not read again when the focus moves between them: they read at their context node, which a move of
    /// the focus does not change (the menu's frame of a key stays without allocations, row KM2c).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box that opens a menu by a signal, with three items that name <c>Ping</c>. A focused box beside it has one handler of
    ///   <c>Ping</c> and its derived types, whose answer counts how often it is asked and is yes.
    ///
    /// Steps:
    /// - Open the menu and settle; read the count. Press Down three times, settling after each; read the count. Give the focused
    ///   box's parent a context and settle; read the count.
    ///
    /// Expected:
    /// - Three after the menu opened, one for each item: the handlers and the context the panel gets after its items were made
    ///   are on no reader's route yet. Still three after the moves of the focus, which went round to the first item; six after
    ///   the context changed, which each item reads again for.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ItemReads_AreNotMadeAgain_WhenTheFocusMovesInTheMenu()
    {
        var items = new Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)>();
        var open = Signal.Create(false);
        int asked = 0;
        Element focused = default;
        Element parent = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element anchor = Box().Width(60).Height(20);
                _ = anchor.Menu(
                    open,
                    () => Column(Named(items, "one", new Ping()), Named(items, "two", new Ping()), Named(items, "three", new Ping()))
                );
                focused = Box()
                    .Width(60)
                    .Height(20)
                    .Focusable()
                    .OnActionsOf(
                        (in ActionEvent<Ping> _) => { },
                        _ =>
                        {
                            asked++;
                            return true;
                        }
                    );
                return parent = Column(anchor, focused);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(focused));
        _ = app.PumpAndSettle();

        open.Set(true);
        _ = app.PumpAndSettle();
        int opened = asked;
        NodeHandle first = app.Tree.Focus.Focused;
        for (int i = 0; i < 3; i++)
        {
            _ = app.Keyboard.Press(Key.Down);
            _ = app.PumpAndSettle();
        }

        int moved = asked;
        NodeHandle afterMoves = app.Tree.Focus.Focused;
        _ = parent.KeyContext("Side");
        _ = app.PumpAndSettle();
        int changed = asked;
        _ = app.Keyboard.Press(Key.Escape);
        _ = app.PumpAndSettle();

        Assert.Equal(3, opened);
        Assert.Equal(items["one"].Item.Node, first);
        Assert.Equal(items["one"].Item.Node, afterMoves);
        Assert.Equal(3, moved);
        Assert.Equal(6, changed);
    }

    /// <summary>
    /// A text field answers for the actions that depend on its options as it handles them: an edit not in a read-only field, a
    /// move by lines and a new line in a field of several lines only, and a submit in a field of one line with a function for it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Six focused fields with "abc", each opened alone, with one edit made where the field takes it: a plain one; a read-only
    ///   one; one of several lines; one of one line with <c>OnSubmit</c>; a read-only one of several lines; one of several lines
    ///   with <c>OnSubmit</c>.
    ///
    /// Steps:
    /// - In each, read <c>CanDispatch</c> of <c>text::DeleteBackward</c>, <c>text::DeleteWordForward</c>, <c>text::MoveUp</c>,
    ///   <c>text::MovePageDown</c>, <c>text::InsertNewline</c>, <c>text::Submit</c>, <c>text::Undo</c>, and <c>text::MoveLeft</c>.
    ///
    /// Expected:
    /// - Plain: the two deletions, the undo, and the move left. Read-only: the move left only. Several lines: all but the submit.
    ///   With <c>OnSubmit</c>: the two deletions, the submit, the undo, and the move left. Read-only of several lines: the two
    ///   moves by lines and the move left. Several lines with <c>OnSubmit</c>: all but the submit.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Field_AnswersForTheActionsThatDependOnItsOptions()
    {
        TextFieldOptions[] cases =
        [
            new TextFieldOptions(),
            new TextFieldOptions { ReadOnly = true },
            new TextFieldOptions { Multiline = true },
            new TextFieldOptions { OnSubmit = static _ => { } },
            new TextFieldOptions { Multiline = true, ReadOnly = true },
            new TextFieldOptions { Multiline = true, OnSubmit = static _ => { } },
        ];
        var seen = new List<bool[]>();

        foreach (TextFieldOptions options in cases)
        {
            Element field = default;
            await using HeadlessApp app = await HeadlessApp.StartAsync(
                () => field = TextField(Signal.Create("abc"), options).Width(200).Height(60),
                new HeadlessOptions { Width = 400, Height = 300 }
            );
            _ = app.PumpAndSettle();
            Assert.True(app.Focus(field));
            _ = app.Keyboard.Press(Key.End);
            _ = app.Keyboard.Type("d");
            _ = app.PumpAndSettle();
            IAction[] actions =
            [
                new TextActions.DeleteBackward(),
                new TextActions.DeleteWordForward(),
                new TextActions.MoveUp(),
                new TextActions.MovePageDown(),
                new TextActions.InsertNewline(),
                new TextActions.Submit(),
                new TextActions.Undo(),
                new TextActions.MoveLeft(),
            ];
            seen.Add([.. actions.Select(action => field.CanDispatch(action))]);
        }

        Assert.Equal([true, true, false, false, false, false, true, true], seen[0]);
        Assert.Equal([false, false, false, false, false, false, false, true], seen[1]);
        Assert.Equal([true, true, true, true, true, false, true, true], seen[2]);
        Assert.Equal([true, true, false, false, false, true, true, true], seen[3]);
        Assert.Equal([false, false, true, true, false, false, false, true], seen[4]);
        Assert.Equal([true, true, true, true, true, false, true, true], seen[5]);
    }

    /// <summary>
    /// A selection area answers for the copy and the select-all only, and the computation that asked runs again when its
    /// selection changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused area around a leaf of "nnnn", and a focused area around a leaf with no text, each opened alone. A computation made
    ///   in the root function reads <c>CanDispatch</c> of <c>text::Copy</c> at the area.
    ///
    /// Steps:
    /// - In the first: read the copy, the select-all, and <c>text::MoveLeft</c>; dispatch <c>text::SelectAll</c>, settle, and read
    ///   the copy again. In the second: read the select-all.
    ///
    /// Expected:
    /// - The first: the copy no, the select-all yes, the move no; the copy yes after all is selected. The second: the select-all
    ///   no, with no text to select.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Area_AnswersForTheCopyAndTheSelectAll()
    {
        Element area = default;
        Computed<bool>? copy = null;
        (bool, bool, bool) first;
        bool copyAfter;
        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(
                () =>
                {
                    area = SelectionArea(Elements.Text("nnnn"));
                    copy = Computed.Create(() => area.CanDispatch(new TextActions.Copy(), area));
                    return area;
                },
                new HeadlessOptions { Width = 400, Height = 300 }
            )
        )
        {
            _ = app.PumpAndSettle();
            Assert.True(app.Focus(area));
            _ = app.PumpAndSettle();
            first = (copy!.Get(), area.CanDispatch(new TextActions.SelectAll()), area.CanDispatch(new TextActions.MoveLeft()));
            Assert.True(area.Dispatch(new TextActions.SelectAll()));
            _ = app.PumpAndSettle();
            copyAfter = copy.Get();
        }

        bool selectAllWithNoText;
        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(
                () => area = SelectionArea(Elements.Text(string.Empty)),
                new HeadlessOptions { Width = 400, Height = 300 }
            )
        )
        {
            _ = app.PumpAndSettle();
            Assert.True(app.Focus(area));
            _ = app.PumpAndSettle();
            selectAllWithNoText = area.CanDispatch(new TextActions.SelectAll());
        }

        Assert.Equal((false, true, false), first);
        Assert.True(copyAfter);
        Assert.False(selectAllWithNoText);
    }

    // An item that names an action, kept by a name with the keys its content was given.
    /// <summary>
    /// An item made outside a menu's panel and put in the panel later reads from the focused node, not from the menu's context:
    /// the context is found where an item is made (design §7, "Where the context node is kept").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box that opens a menu by a signal. The root function makes an item that names <c>Ping</c>; the panel holds that item
    ///   and a second such item made in the panel. A focused box beside the first handles <c>Ping</c>.
    ///
    /// Steps:
    /// - Open the menu and settle, so that the focus moves into the menu, and read both items.
    ///
    /// Expected:
    /// - The item made in the panel is enabled: it reads at the box that had the focus. The item made outside is disabled: it
    ///   reads at the focused menu item, whose route does not pass the box that handles <c>Ping</c>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ItemMadeOutsideThePanel_ReadsFromTheFocus()
    {
        var items = new Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)>();
        var open = Signal.Create(false);
        Element focused = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element outside = Named(items, "outside", new Ping());
                Element anchor = Box().Width(60).Height(20);
                _ = anchor.Menu(open, () => Column(Named(items, "inside", new Ping()), outside));
                focused = Box().Width(60).Height(20).Focusable().OnAction((in ActionEvent<Ping> _) => { });
                return Column(anchor, focused);
            },
            new HeadlessOptions { Width = 400, Height = 300 }
        );
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(focused));
        _ = app.PumpAndSettle();

        open.Set(true);
        _ = app.PumpAndSettle();

        Assert.True(State(app, items["inside"]).Enabled);
        Assert.False(State(app, items["outside"]).Enabled);
    }

    private static Element Named(Dictionary<string, (Element Item, Computed<KeyBinding?> Keys)> items, string name, IAction action)
    {
        Computed<KeyBinding?>? given = null;
        Element item = MenuItem(
            action,
            keys =>
            {
                given = keys;
                return Box().Width(120).Height(20);
            }
        );
        items[name] = (item, given!);
        return item;
    }

    // Whether an item is enabled, and the keys its content was given.
    private static (bool Enabled, string? Keys) State(HeadlessApp app, (Element Item, Computed<KeyBinding?> Keys) item) =>
        ((app.Tree.Nodes.GetRef(item.Item.Node).Pseudo & PseudoState.Disabled) == 0, item.Keys.Get()?.ToDisplayString());

    private sealed class Ping : IAction;

    private sealed class Other : IAction;

    private sealed class Third : IAction;
}
