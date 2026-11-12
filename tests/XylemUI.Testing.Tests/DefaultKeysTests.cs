using System.Numerics;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// The default keys as rows of the keymap and actions the tree takes (design §7, "The default keys and actions", row KM2a). The
// source generator runs on this project: its module initializer registered the default rows of XylemUI.Core.
public sealed class DefaultKeysTests
{
    /// <summary>
    /// Every keymap starts with the default rows of <c>XylemUI.Core</c>, each with the condition <c>Window</c>, and a key whose
    /// row is taken out is a plain key again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with two focusable boxes, the first focused, each with a key handler that records the key; its own keymap.
    ///
    /// Steps:
    /// - Read the rows. Press Tab and Shift+Tab. Take the row of <c>tab</c> out and press Tab.
    ///
    /// Expected:
    /// - The rows are <c>tab</c>, <c>shift-tab</c>, <c>escape</c>, <c>up</c>, <c>down</c>, <c>left</c>, <c>right</c>,
    ///   <c>pageup</c>, <c>pagedown</c>, <c>home</c>, <c>end</c>, with the actions <c>focus::Next</c>, <c>focus::Previous</c>,
    ///   <c>popup::Dismiss</c>, and the eight of <c>scroll::</c>, all in <c>Window</c>. Tab moves the focus to the second box and
    ///   Shift+Tab back, and no key handler sees them. With the row out, Tab is not handled, the focus stays, and the first box's
    ///   handler sees it.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keymap_StartsWithTheDefaultRows_AndAKeyWithoutItsRowIsPlain()
    {
        var log = new List<string>();
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            first = Box().Width(10).Height(10).Focusable().On(KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent e) => log.Add($"first {e.Key}"));
            second = Box().Width(10).Height(10).Focusable().On(KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent e) => log.Add($"second {e.Key}"));
            return Column(first, second);
        });
        Assert.True(app.Focus(first));

        Assert.Equal(
            [
                ("tab", "focus::Next"),
                ("shift-tab", "focus::Previous"),
                ("escape", "popup::Dismiss"),
                ("up", "scroll::LineUp"),
                ("down", "scroll::LineDown"),
                ("left", "scroll::LineLeft"),
                ("right", "scroll::LineRight"),
                ("pageup", "scroll::PageUp"),
                ("pagedown", "scroll::PageDown"),
                ("home", "scroll::ToStart"),
                ("end", "scroll::ToEnd"),
            ],
            app.Keymap.Bindings.Select(static row =>
                (row.ToString(), ActionRegistry.TryGetName(row.Action!.GetType(), out string? name) ? name : null)
            )
        );
        Assert.All(app.Keymap.Bindings, static row => Assert.Equal("Window", row.Context));

        Assert.True(app.Keyboard.Press(Key.Tab));
        Assert.Equal(second.Node, app.Tree.Focus.Focused);
        Assert.True(app.Keyboard.Press(Key.Tab, KeyModifiers.Shift));
        Assert.Equal(first.Node, app.Tree.Focus.Focused);
        Assert.Empty(log);

        Assert.True(app.Keymap.Remove(app.Keymap.Bindings[0]));
        Assert.False(app.Keyboard.Press(Key.Tab));
        _ = app.PumpAndSettle();
        Assert.Equal(first.Node, app.Tree.Focus.Focused);
        Assert.Equal(["first Tab"], log);
    }

    /// <summary>
    /// A row with a node's context wins over the default rows, also when the focus is on a descendant of the node that has the
    /// context, and so does a row with no condition; the default rows are tried when the winning row's action is not handled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 100 high over a list 400 high with the context <c>List</c>, which handles <c>Pick</c> and records its
    ///   number; a focused row inside the list, with no context of its own.
    ///
    /// Steps:
    /// - Press Down. Bind <c>down</c> to <c>Pick(1)</c> in <c>List</c> and press Down. Bind <c>down</c> to <c>Other</c> (which no
    ///   node handles) in <c>List</c> and press Down. Bind <c>down</c> to <c>Pick(2)</c> with no condition and press Down. Bind
    ///   <c>down</c> to no action with no condition and press Down.
    ///
    /// Expected:
    /// - The first Down scrolls to 50; none of the others scrolls. The second is <c>Pick(1)</c>. The third is <c>Pick(1)</c>
    ///   again: the row of <c>Other</c> wins and is not handled, and the next row is that of <c>Pick(1)</c>, before the default
    ///   row. The fourth is <c>Pick(2)</c>: a row with no condition counts as deep as the list's, and was added later. The fifth
    ///   is not handled: the row with no action ends the search before the default row.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ContextRow_WinsOverTheDefaultRows_AlsoFromADescendant()
    {
        var picked = new List<int>();
        var state = new ScrollState();
        Element row = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            row = Box().Width(100).Height(20).Focusable();
            Element list = Column(row, Box().Width(100).Height(380))
                .KeyContext("List")
                .OnAction((in ActionEvent<Pick> e) => picked.Add(e.Action.Number));
            return ScrollView(list, state).Width(100).Height(100);
        });
        Assert.True(app.Focus(row));
        _ = app.PumpAndSettle();
        var offsets = new List<float>();
        var handled = new List<bool>();
        void Down()
        {
            handled.Add(app.Keyboard.Press(Key.Down));
            offsets.Add(state.Offset.Get().Y);
        }

        Down();
        _ = app.Keymap.Bind("down", new Pick(1), "List");
        Down();
        _ = app.Keymap.Bind("down", new Other(), "List");
        Down();
        _ = app.Keymap.Bind("down", new Pick(2));
        Down();
        _ = app.Keymap.Bind("down", null);
        Down();
        _ = app.PumpAndSettle();

        Assert.Equal([50f, 50f, 50f, 50f, 50f], offsets);
        Assert.Equal([true, true, true, true, false], handled);
        Assert.Equal([1, 1, 2], picked);
    }

    /// <summary>
    /// The tree takes an action of its own only when no handler on the route took it: a handler takes it first, and one that
    /// passes it on leaves it to the tree.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 100 high over content 400 high with a focused box; the content handles <c>scroll::LineDown</c> in the
    ///   bubble phase, counts, and passes the action on while a flag is set.
    ///
    /// Steps:
    /// - Press Down. Set the flag and press Down. Dispatch <c>scroll::PageDown</c> from the box in code, and ask whether it can be
    ///   dispatched.
    ///
    /// Expected:
    /// - The first Down is handled, counted, and does not scroll. The second is counted and scrolls to 50. The dispatch scrolls
    ///   to 130 (0.8 of 100 more) and returns true, and the action can be dispatched.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CoreAction_IsTakenByAHandlerFirst_ThenByTheTree()
    {
        int count = 0;
        bool pass = false;
        var state = new ScrollState();
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            box = Box().Width(100).Height(20).Focusable();
            Element content = Column(box, Box().Width(100).Height(380))
                .OnAction(
                    (in ActionEvent<ScrollActions.LineDown> e) =>
                    {
                        count++;
                        if (pass)
                        {
                            e.Propagate();
                        }
                    }
                );
            return ScrollView(content, state).Width(100).Height(100);
        });
        Assert.True(app.Focus(box));
        _ = app.PumpAndSettle();

        Assert.True(app.Keyboard.Press(Key.Down));
        Assert.Equal((1, 0f), (count, state.Offset.Get().Y));
        pass = true;
        Assert.True(app.Keyboard.Press(Key.Down));
        Assert.Equal((2, 50f), (count, state.Offset.Get().Y));

        Assert.True(box.Dispatch(new ScrollActions.PageDown()));
        _ = app.PumpAndSettle();
        Assert.Equal(130f, state.Offset.Get().Y);
        Assert.True(box.CanDispatch<ScrollActions.PageDown>());
    }

    /// <summary>
    /// A scrolling action that moves no container, and Esc with no popup open, are not handled: the key goes on to the key
    /// handlers. The up of a key whose down the keymap took reaches the key handlers too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 100 high over content 400 high with a focused box that records the downs and the ups of its keys.
    ///
    /// Steps:
    /// - Press Up (the view is at its start), Esc, Left (the view does not scroll its width), and Down.
    ///
    /// Expected:
    /// - Up, Esc, and Left are not handled; the box sees their downs and their ups. Down is handled and scrolls to 50; the box
    ///   sees its up and not its down.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ActionThatChangesNothing_LeavesTheKeyToTheKeyHandlers_AndTheUpIsDelivered()
    {
        var log = new List<string>();
        var state = new ScrollState();
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            box = Box()
                .Width(100)
                .Height(20)
                .Focusable()
                .On(KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent e) => log.Add($"down {e.Key}"))
                .On(KeyEventKind.Up, EventPhase.Bubble, (in KeyEvent e) => log.Add($"up {e.Key}"));
            return ScrollView(Column(box, Box().Width(100).Height(380)), state).Width(100).Height(100);
        });
        Assert.True(app.Focus(box));
        _ = app.PumpAndSettle();

        Assert.False(app.Keyboard.Press(Key.Up));
        Assert.False(app.Keyboard.Press(Key.Escape));
        Assert.False(app.Keyboard.Press(Key.Left));
        Assert.True(app.Keyboard.Down(Key.Down));
        Assert.False(app.Keyboard.Up(Key.Down));
        _ = app.PumpAndSettle();

        Assert.Equal(50f, state.Offset.Get().Y);
        Assert.Equal(["down Up", "up Up", "down Escape", "up Escape", "down Left", "up Left", "up Down"], log);
    }

    /// <summary>
    /// The keys shown for an action of the tree are those of its default row, until a row that wins takes them away.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with a focused box; an effect that reads the keys shown for <c>focus::Next</c>.
    ///
    /// Steps:
    /// - Read. Bind <c>tab</c> to no action with no condition; read.
    ///
    /// Expected:
    /// - <c>Tab</c>, then none.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task KeyBindingFor_ACoreAction_IsItsDefaultRow()
    {
        var shown = new List<string?>();
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            box = Box().Width(10).Height(10).Focusable();
            Effect.Create(() => shown.Add(box.KeyBindingFor<FocusActions.Next>()?.ToDisplayString()));
            return Column(box);
        });
        Assert.True(app.Focus(box));
        _ = app.PumpAndSettle();

        _ = app.Keymap.Bind("tab", null);
        _ = app.PumpAndSettle();

        Assert.Equal("Tab", shown[0]);
        Assert.Null(shown[^1]);
    }

    /// <summary>
    /// A method registered for the default rows a second time adds its rows once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The module initializer registered <c>CoreKeyBindings.AddDefaults</c>.
    ///
    /// Steps:
    /// - Make a keymap; register <c>CoreKeyBindings.AddDefaults</c> again; make another keymap.
    ///
    /// Expected:
    /// - Both keymaps have the same rows, eleven of them.
    /// </remarks>
    [UiFact]
    public void RegisterDefaults_WithAMethodAlreadyRegistered_AddsItsRowsOnce()
    {
        var before = new Keymap();

        Keymap.RegisterDefaults(CoreKeyBindings.AddDefaults);
        var after = new Keymap();

        Assert.Equal(11, before.Bindings.Count);
        Assert.Equal(before.Bindings.Select(static row => row.ToString()), after.Bindings.Select(static row => row.ToString()));
    }

    /// <summary>
    /// A keymap is made on any thread, with the default rows; its rows are added on the UI thread alone.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The module initializer registered <c>CoreKeyBindings.AddDefaults</c>.
    ///
    /// Steps:
    /// - On a thread of the pool, make a keymap, then add a row to it.
    ///
    /// Expected:
    /// - The keymap has the eleven default rows. Adding the row there throws <see cref="InvalidOperationException"/>, and the
    ///   keymap keeps eleven rows.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keymap_MadeOffTheUiThread_HasTheDefaultRows()
    {
        (Keymap keymap, Exception? thrown) = await Task.Run(static () =>
        {
            var made = new Keymap();
            return (made, Record.Exception(() => made.Bind("ctrl-s", new Other())));
        });

        _ = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(11, keymap.Bindings.Count);
    }

    /// <summary>
    /// An app with no default rows registered does not start, and the error says what registers them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Ask for the default rows with a count of 0 registered methods, then with 1.
    ///
    /// Expected:
    /// - The first throws <see cref="InvalidOperationException"/> whose message names <c>Keymap.RegisterDefaults</c>; the second
    ///   does not throw. (An app that starts in these tests passed the check with the methods the module initializer registered.)
    /// </remarks>
    [Fact]
    public void RequireDefaults_WithNoneRegistered_Throws()
    {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => Keymap.RequireDefaults(0));
        Assert.Contains("Keymap.RegisterDefaults", thrown.Message, StringComparison.Ordinal);

        Keymap.RequireDefaults(1);
    }

    /// <summary>
    /// The context of a part and the context the app gives the same node are one context for the conditions, and each is set
    /// and taken away by itself.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused box that handles <c>Pick</c> and records its number. Rows for <c>ctrl-p</c>: <c>Pick(1)</c> in <c>Part</c>,
    ///   <c>Pick(2)</c> in <c>App</c>, <c>Pick(3)</c> in <c>Part &amp;&amp; App</c>, <c>Pick(4)</c> in <c>kind == mine</c>.
    ///
    /// Steps:
    /// - Give the box the part's context <c>Part kind = parts</c> and press. Give it the app's context <c>App</c> and press.
    ///   Give it the app's context <c>App kind = mine</c> and press. Take the part's context away and press. Take the app's away
    ///   and press.
    ///
    /// Expected:
    /// - 1 (the part's alone); 3 (both names; the row added later of those that hold); 4 (the app's value for a key both have);
    ///   4 (the app's stays); nothing, and the key is not handled.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task KeyContext_OfThePartAndOfTheApp_AreOne()
    {
        var picked = new List<int>();
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(box = Box().Width(10).Height(10).Focusable().OnAction((in ActionEvent<Pick> e) => picked.Add(e.Action.Number)))
        );
        Assert.True(app.Focus(box));
        _ = app.Keymap.Bind("ctrl-p", new Pick(1), "Part");
        _ = app.Keymap.Bind("ctrl-p", new Pick(2), "App");
        _ = app.Keymap.Bind("ctrl-p", new Pick(3), "Part && App");
        _ = app.Keymap.Bind("ctrl-p", new Pick(4), "kind == mine");

        _ = box.PartKeyContext("Part kind = parts");
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = box.KeyContext("App");
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = box.KeyContext("App kind = mine");
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = box.PartKeyContext(null);
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = box.KeyContext(null);
        Assert.False(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = app.PumpAndSettle();

        Assert.Equal([1, 3, 4, 4], picked);
    }

    /// <summary>
    /// A handler a part adds for a base type of its actions is called for the types derived from it, and makes them available;
    /// a handler added for one type is called for that type alone.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused box with a handler added for <c>Shape</c> and its derived types, which records the action's type, and a handler
    ///   added for <c>Circle</c> alone on its parent. <c>Circle</c> and <c>Square</c> derive from <c>Shape</c>; <c>Round</c>
    ///   derives from <c>Circle</c>.
    ///
    /// Steps:
    /// - Dispatch a <c>Circle</c>, a <c>Square</c>, a <c>Round</c>, and an <c>Other</c> from the box. Ask whether a <c>Square</c>
    ///   and an <c>Other</c> can be dispatched. Dispose the scope of the box's handler and dispatch a <c>Square</c> and a
    ///   <c>Circle</c>.
    ///
    /// Expected:
    /// - The box's handler records Circle, Square, and Round, and the three are handled; <c>Other</c> is not. A <c>Square</c> can
    ///   be dispatched and an <c>Other</c> cannot. After the scope is disposed, the <c>Square</c> is not handled, and the
    ///   <c>Circle</c> reaches the parent's handler, which the <c>Round</c> did not.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task OnActionsOf_ABaseType_TakesTheDerivedTypes()
    {
        var log = new List<string>();
        Element box = default;
        ScopeHandle scope = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            box = Box().Width(10).Height(10).Focusable();
            scope = Scope.Create(() => _ = box.OnActionsOf((in ActionEvent<Shape> e) => log.Add(e.Action.GetType().Name)));
            return Column(box).OnAction((in ActionEvent<Circle> _) => log.Add("parent"));
        });
        Assert.True(app.Focus(box));

        Assert.True(box.Dispatch(new Circle()));
        Assert.True(box.Dispatch(new Square()));
        Assert.True(box.Dispatch(new Round()));
        Assert.False(box.Dispatch(new Other()));
        Assert.True(box.CanDispatch<Square>());
        Assert.False(box.CanDispatch<Other>());
        Assert.Equal(["Circle", "Square", "Round"], log);

        Scope.Dispose(scope);
        Assert.False(box.Dispatch(new Square()));
        Assert.False(box.Dispatch(new Round()));
        Assert.True(box.Dispatch(new Circle()));
        _ = app.PumpAndSettle();
        Assert.Equal(["Circle", "Square", "Round", "parent"], log);
    }

    private sealed class Other : IAction;

    private class Shape : IAction;

    private class Circle : Shape;

    private sealed class Square : Shape;

    private sealed class Round : Circle;

    private sealed class Pick(int number) : IAction
    {
        public int Number { get; } = number;
    }
}
