using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// The keymap (design §7, "Shortcuts and actions", row KM1b): which row a key asks for, keys pressed one after another, and the
// order of the keymap, the key handlers, and the defaults. The trees here hold no text, so these tests run where HarfBuzz is not
// built; the rules about a text field are tested where there is one (XylemUI.Desktop.Tests).
public sealed class KeymapTests
{
    /// <summary>
    /// A key with a binding asks for the binding's action before the key handlers get it; when no handler takes the action, the
    /// key goes on to the key handlers.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf with a key handler; its parent has a bubble handler of <c>Save</c>. The keymap has <c>ctrl-s</c> for
    ///   <c>Save</c> and <c>ctrl-w</c> for <c>Close</c>, which no node handles.
    ///
    /// Steps:
    /// - Press Ctrl+S; press Ctrl+W; press Ctrl+Q.
    ///
    /// Expected:
    /// - Ctrl+S: handled, the <c>Save</c> handler ran with the row's own action, the key handler did not. Ctrl+W and Ctrl+Q: not
    ///   handled, and the key handler got each.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Key_WithABinding_AsksForItsActionBeforeTheKeyHandlers()
    {
        var log = new List<string>();
        var save = new Save();
        await using HeadlessApp app = await Start(
            log,
            (leaf, parent) => parent.OnAction((in ActionEvent<Save> e) => log.Add(ReferenceEquals(e.Action, save) ? "save" : "another"))
        );
        _ = app.Keymap.Bind("ctrl-s", save);
        _ = app.Keymap.Bind("ctrl-w", new Close());

        Assert.True(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        Assert.Equal(["save"], log);

        log.Clear();
        Assert.False(app.Keyboard.Press(Key.W, KeyModifiers.Control));
        Assert.False(app.Keyboard.Press(Key.Q, KeyModifiers.Control));
        Assert.Equal(["key W", "key Q"], log);
    }

    /// <summary>
    /// Among the rows a key matches, the one whose condition holds deeper wins; at the same depth the one added later wins; a
    /// row with no condition counts as the deepest.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf with the context <c>Editor</c> under a parent with the context <c>Pane</c>; the parent handles
    ///   <c>Pick</c> and records its number. Rows for <c>ctrl-p</c>, added in this order: <c>Pick(1)</c> in <c>Editor</c>,
    ///   <c>Pick(2)</c> in <c>Pane</c>, <c>Pick(3)</c> in <c>Pane</c>.
    ///
    /// Steps:
    /// - Press Ctrl+P. Add <c>Pick(4)</c> in <c>Editor</c> and press. Add <c>Pick(5)</c> with no condition and press. Add
    ///   <c>Pick(6)</c> in <c>Pane</c> and press. Add <c>Pick(7)</c> in <c>Other</c> and press.
    ///
    /// Expected:
    /// - 1 (the deeper condition, though added first); 4 (same depth, added later); 5 (no condition is the deepest, added later);
    ///   5 (a row in <c>Pane</c> is shallower); 5 (a condition that holds nowhere is no match).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Key_WithSeveralRows_TheDeepestThenTheLatestWins()
    {
        var picked = new List<int>();
        await using HeadlessApp app = await Start(
            [],
            (leaf, parent) =>
            {
                _ = leaf.KeyContext("Editor");
                _ = parent.KeyContext("Pane").OnAction((in ActionEvent<Pick> e) => picked.Add(e.Action.Number));
            }
        );
        _ = app.Keymap.Bind("ctrl-p", new Pick(1), "Editor");
        _ = app.Keymap.Bind("ctrl-p", new Pick(2), "Pane");
        _ = app.Keymap.Bind("ctrl-p", new Pick(3), "Pane");

        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = app.Keymap.Bind("ctrl-p", new Pick(4), "Editor");
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = app.Keymap.Bind("ctrl-p", new Pick(5));
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = app.Keymap.Bind("ctrl-p", new Pick(6), "Pane");
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));
        _ = app.Keymap.Bind("ctrl-p", new Pick(7), "Other");
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));

        Assert.Equal([1, 4, 5, 5, 5], picked);
    }

    /// <summary>
    /// When the action of the winning row is not handled, the next row is tried; a row with no action ends the search, and a row
    /// that is taken out is tried no more.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf with the context <c>Editor</c> and a key handler; its parent handles <c>Save</c>. Rows for <c>ctrl-s</c>:
    ///   <c>Save</c> with no condition, then <c>Close</c> (which no node handles) in <c>Editor</c>.
    ///
    /// Steps:
    /// - Press Ctrl+S. Add a row with no action in <c>Editor</c> and press. Take that row out and press. Take the <c>Save</c> row
    ///   out and press.
    ///
    /// Expected:
    /// - Handled by <c>Save</c> (the <c>Close</c> row won and was not handled). Not handled, and the key handler got the key (the
    ///   row with no action won). Handled by <c>Save</c> again. Not handled, and the key handler got the key. Taking a row out
    ///   returns true the first time and false the second.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Key_WhoseRowIsNotHandledOrHasNoAction_GoesOnOrStops()
    {
        var log = new List<string>();
        await using HeadlessApp app = await Start(
            log,
            (leaf, parent) =>
            {
                _ = leaf.KeyContext("Editor");
                _ = parent.OnAction((in ActionEvent<Save> e) => log.Add("save"));
            }
        );
        KeyBinding save = app.Keymap.Bind("ctrl-s", new Save());
        _ = app.Keymap.Bind("ctrl-s", new Close(), "Editor");

        Assert.True(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        KeyBinding none = app.Keymap.Bind("ctrl-s", null, "Editor");
        Assert.False(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        Assert.True(app.Keymap.Remove(none));
        Assert.False(app.Keymap.Remove(none));
        Assert.True(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        Assert.True(app.Keymap.Remove(save));
        Assert.False(app.Keyboard.Press(Key.S, KeyModifiers.Control));

        Assert.Equal(["save", "key S", "save", "key S"], log);
    }

    /// <summary>
    /// Keys pressed one after another ask for their row's action at the last key; the keys before it are kept and reach no
    /// handler, and a modifier key pressed between them does not end the run.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf with a key handler; its parent handles <c>Save</c>. The keymap has <c>ctrl-k ctrl-s</c> for <c>Save</c>.
    ///
    /// Steps:
    /// - Press Ctrl+K; press the Control key alone; press Ctrl+S.
    ///
    /// Expected:
    /// - Ctrl+K is handled and one key is kept. The Control key is not handled and reaches the key handler; one key is still kept.
    ///   Ctrl+S is handled, <c>Save</c> ran, and no key is kept. The key handler got only the Control key.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keys_PressedOneAfterAnother_AskForTheActionAtTheLastKey()
    {
        var log = new List<string>();
        await using HeadlessApp app = await Start(log, (leaf, parent) => parent.OnAction((in ActionEvent<Save> e) => log.Add("save")));
        _ = app.Keymap.Bind("ctrl-k ctrl-s", new Save());

        Assert.True(app.Keyboard.Press(Key.K, KeyModifiers.Control));
        Assert.Equal(1, app.Input.PendingKeyCount);
        Assert.False(app.Keyboard.Press(Key.LeftCtrl, KeyModifiers.Control));
        Assert.Equal(1, app.Input.PendingKeyCount);
        Assert.True(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        Assert.Equal(0, app.Input.PendingKeyCount);

        Assert.Equal(["key LeftCtrl", "save"], log);
    }

    /// <summary>
    /// When the next key is no part of a binding, the keys kept are delivered as plain keys, with the text they type, and the next
    /// key is looked up from the start.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf with a key handler and a text handler; its parent handles <c>Save</c> and <c>Pick</c>. The keymap has
    ///   <c>g g</c> for <c>Save</c> and <c>ctrl-p</c> for <c>Pick</c>.
    ///
    /// Steps:
    /// - Press G; press Ctrl+P.
    ///
    /// Expected:
    /// - G is handled (kept). Ctrl+P is handled: the kept G goes to the key handler and then to the text handler as "g", and
    ///   <c>Pick</c> runs after it. No key is kept.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keys_ThatStopBeingABinding_AreDeliveredAsPlainKeysAndText()
    {
        var log = new List<string>();
        await using HeadlessApp app = await Start(
            log,
            (leaf, parent) => parent.OnAction((in ActionEvent<Save> e) => log.Add("save")).OnAction((in ActionEvent<Pick> e) => log.Add("pick"))
        );
        _ = app.Keymap.Bind("g g", new Save());
        _ = app.Keymap.Bind("ctrl-p", new Pick(1));

        Assert.True(app.Keyboard.Press(Key.G));
        Assert.Empty(log);
        Assert.True(app.Keyboard.Press(Key.P, KeyModifiers.Control));

        Assert.Equal(["key G", "text g", "pick"], log);
        Assert.Equal(0, app.Input.PendingKeyCount);
    }

    /// <summary>
    /// After one second with no next key, the keys kept are delivered: the longest run of them that is a binding asks for its
    /// action, and a key in none goes to the key handlers.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf with a key handler; its parent handles <c>Save</c> and <c>Pick</c>. The keymap has <c>ctrl-k</c> for
    ///   <c>Pick(1)</c>, <c>ctrl-k ctrl-s</c> for <c>Save</c>, and <c>ctrl-j ctrl-s</c> for <c>Save</c>.
    ///
    /// Steps:
    /// - Press Ctrl+K; advance the clock by 999 ms; advance it by 1 ms. Press Ctrl+K and Ctrl+S. Press Ctrl+J and advance the
    ///   clock by 1 s.
    ///
    /// Expected:
    /// - Ctrl+K is kept, and nothing runs for 999 ms; at 1 s <c>Pick</c> runs (the shorter row). Ctrl+K then Ctrl+S runs
    ///   <c>Save</c>, not <c>Pick</c>. Ctrl+J is kept, and at 1 s it goes to the key handler.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keys_KeptForOneSecond_AreDeliveredAsTheLongestBindingOrPlainKeys()
    {
        var log = new List<string>();
        await using HeadlessApp app = await Start(
            log,
            (leaf, parent) => parent.OnAction((in ActionEvent<Save> e) => log.Add("save")).OnAction((in ActionEvent<Pick> e) => log.Add("pick"))
        );
        _ = app.Keymap.Bind("ctrl-k", new Pick(1));
        _ = app.Keymap.Bind("ctrl-k ctrl-s", new Save());
        _ = app.Keymap.Bind("ctrl-j ctrl-s", new Save());

        Assert.True(app.Keyboard.Press(Key.K, KeyModifiers.Control));

        // No pump here: a pump runs the clock on to the timers that are due next.
        app.Time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(log);
        app.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["pick"], log);
        _ = app.PumpAndSettle();
        Assert.Equal(0, app.Input.PendingKeyCount);

        log.Clear();
        Assert.True(app.Keyboard.Press(Key.K, KeyModifiers.Control));
        Assert.True(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        Assert.Equal(["save"], log);

        log.Clear();
        Assert.True(app.Keyboard.Press(Key.J, KeyModifiers.Control));
        app.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["key J"], log);
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// The keymap is tried before the defaults of a key: a binding for Tab that is handled keeps the focus where it is, and
    /// without one Tab moves it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two focusable boxes under a parent that handles <c>Save</c>; the first has the focus.
    ///
    /// Steps:
    /// - Press Tab. Focus the first box, bind <c>tab</c> to <c>Save</c>, and press Tab.
    ///
    /// Expected:
    /// - The first Tab moves the focus to the second box. The second runs <c>Save</c> and leaves the focus on the first.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keymap_IsTriedBeforeTheDefaultOfTab()
    {
        int saves = 0;
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            first = Box().Width(10).Height(10).Focusable();
            second = Box().Width(10).Height(10).Focusable();
            return Column(first, second).OnAction((in ActionEvent<Save> e) => saves++);
        });
        Assert.True(app.Focus(first));

        Assert.True(app.Keyboard.Press(Key.Tab));
        Assert.True(second.IsFocused().Get());

        Assert.True(app.Focus(first));
        _ = app.Keymap.Bind("tab", new Save());
        Assert.True(app.Keyboard.Press(Key.Tab));
        _ = app.PumpAndSettle();
        Assert.True(first.IsFocused().Get());
        Assert.Equal(1, saves);
    }

    /// <summary>
    /// The keys a menu shows for an action are those of the row a key would find: for exactly that type, and a computation that
    /// read them runs again when a row is added or taken out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app whose effect records the display text of the keys for <c>Save</c>, or "none". <c>SaveAs</c> derives from
    ///   <c>Save</c>.
    ///
    /// Steps:
    /// - Bind <c>ctrl-shift-s</c> to <c>SaveAs</c>; bind <c>ctrl-s</c> to <c>Save</c>; bind <c>ctrl-k ctrl-s</c> to <c>Save</c>;
    ///   take the last row out.
    ///
    /// Expected:
    /// - After the start, the effect recorded "none" (a row for the derived type is not the type's), "Ctrl+S", "Ctrl+K Ctrl+S"
    ///   (the row added later), "Ctrl+S".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task KeyBindingFor_ReadInAnEffect_FollowsTheRows()
    {
        var seen = new List<string>();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element root = Box().Width(10).Height(10);
            _ = Effect.Create(() => seen.Add(root.KeyBindingFor<Save>()?.ToDisplayString() ?? "none"));
            return root;
        });

        // The start ran the effect with no row.
        Assert.All(seen, static text => Assert.Equal("none", text));
        seen.Clear();

        _ = app.Keymap.Bind("ctrl-shift-s", new SaveAs());
        _ = app.Keymap.Bind("ctrl-s", new Save());
        KeyBinding last = app.Keymap.Bind("ctrl-k ctrl-s", new Save());
        Assert.Equal("ctrl-k ctrl-s", last.ToString());
        _ = app.Keymap.Remove(last);

        Assert.Equal(["none", "Ctrl+S", "Ctrl+K Ctrl+S", "Ctrl+S"], seen);
    }

    /// <summary>
    /// The keys shown for an action are not those of a row whose binding was taken away, whose keys another action took, or whose
    /// condition does not hold at the focus; they follow the focus.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two focusable boxes, the first with the context <c>Editor</c>. An effect records the display text of the keys for
    ///   <c>Save</c>, or "none". Rows: <c>ctrl-s</c> for <c>Save</c> with no condition; <c>f2</c> for <c>Save</c> in
    ///   <c>Editor</c>.
    ///
    /// Steps:
    /// - Focus the first box; focus the second. Add a row with no action for <c>ctrl-s</c>. Take it out and add
    ///   <c>ctrl-s</c> for <c>Close</c> in <c>Editor</c>; focus the first box.
    ///
    /// Expected:
    /// - In the first box "F2" (the later row at the same depth); in the second "Ctrl+S" (the row of <c>Editor</c> does not hold
    ///   there). With the keys taken away: "none". With the keys given to <c>Close</c> in <c>Editor</c>: "Ctrl+S" in the second
    ///   box, and "F2" in the first, where <c>ctrl-s</c> asks for <c>Close</c>. The keys shown ask for <c>Save</c> when pressed.
    ///   When the first box drops its context, an effect that read the keys runs once more and records "Ctrl+S"; when it takes
    ///   the context again, "F2".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task KeyBindingFor_IsTheRowAKeyWouldFindAtTheFocus()
    {
        var seen = new List<string>();
        var asked = new List<string>();
        var auto = new List<string>();
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            first = Box().Width(10).Height(10).Focusable().KeyContext("Editor");
            second = Box().Width(10).Height(10).Focusable();
            Element own = first;
            _ = Effect.Create(() => auto.Add(own.KeyBindingFor<Save>()?.ToDisplayString() ?? "none"));
            return Column(first, second)
                .OnAction((in ActionEvent<Save> e) => asked.Add("save"))
                .OnAction((in ActionEvent<Close> e) => asked.Add("close"));
        });
        _ = app.Keymap.Bind("ctrl-s", new Save());
        _ = app.Keymap.Bind("f2", new Save(), "Editor");
        string Shown() => first.KeyBindingFor<Save>()?.ToDisplayString() ?? "none";

        Assert.True(app.Focus(first));
        seen.Add(Shown());
        Assert.True(app.Focus(second));
        seen.Add(Shown());
        KeyBinding none = app.Keymap.Bind("ctrl-s", null);
        seen.Add(Shown());
        Assert.True(app.Keymap.Remove(none));
        _ = app.Keymap.Bind("ctrl-s", new Close(), "Editor");
        seen.Add(Shown());
        Assert.True(app.Focus(first));
        seen.Add(Shown());
        Assert.Equal(["F2", "Ctrl+S", "none", "Ctrl+S", "F2"], seen);

        Assert.True(app.Keyboard.Press(Key.F2));
        Assert.True(app.Keyboard.Press(Key.S, KeyModifiers.Control));
        Assert.Equal(["save", "close"], asked);

        // The node stops calling itself an editor: the rows of that context hold no more, and a computation that read the keys runs again.
        auto.Clear();
        _ = first.KeyContext(null);
        Assert.Equal(["Ctrl+S"], auto);
        _ = first.KeyContext("Editor");
        Assert.Equal(["Ctrl+S", "F2"], auto);
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// Keys kept for one node are not delivered to another: when the focus moves, they are dropped, at the next key and at the
    /// end of the wait.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two focusable boxes that log their keys and text under a root that handles <c>Save</c>. The keymap has <c>g g</c> for
    ///   <c>Save</c>. The first box has the focus.
    ///
    /// Steps:
    /// - Press G; focus the second box; press G. Then press G, focus the first box, and advance the clock by one second.
    ///
    /// Expected:
    /// - The second G is kept anew (one key kept), and <c>Save</c> does not run: the first G was dropped. After the second focus
    ///   move and the second, no key is kept and nothing was delivered to either box.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keys_KeptWhenTheFocusMoves_AreDropped()
    {
        var log = new List<string>();
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element Logging(string name) =>
                Box()
                    .Width(10)
                    .Height(10)
                    .Focusable()
                    .On(KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent e) => log.Add($"{name} key {e.Key}"))
                    .OnTextInput(EventPhase.Bubble, (in TextInputEvent e) => log.Add($"{name} text {e.Text}"));
            first = Logging("first");
            second = Logging("second");
            return Column(first, second).OnAction((in ActionEvent<Save> e) => log.Add("save"));
        });
        _ = app.Keymap.Bind("g g", new Save());
        Assert.True(app.Focus(first));

        Assert.True(app.Keyboard.Press(Key.G));
        Assert.True(app.Focus(second));
        Assert.True(app.Keyboard.Press(Key.G));
        Assert.Equal(1, app.Input.PendingKeyCount);
        Assert.Empty(log);

        Assert.True(app.Focus(first));
        app.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, app.Input.PendingKeyCount);
        Assert.Empty(log);
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// A row's keys and condition are refused when they are not written in their text form.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with an empty keymap.
    ///
    /// Steps:
    /// - Bind an empty text, a text of spaces, <c>ctrl-</c>, and <c>ctrl-s</c> with the condition <c>Editor &amp;&amp;</c>.
    ///
    /// Expected:
    /// - Each throws <see cref="FormatException"/>, and the keymap keeps the rows it started with, the default ones.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Bind_KeysOrConditionNotInTheirTextForm_Throws()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(10).Height(10));
        int rows = app.Keymap.Bindings.Count;

        _ = Assert.Throws<FormatException>(() => app.Keymap.Bind(string.Empty, new Save()));
        _ = Assert.Throws<FormatException>(() => app.Keymap.Bind("  ", new Save()));
        _ = Assert.Throws<FormatException>(() => app.Keymap.Bind("ctrl-", new Save()));
        _ = Assert.Throws<FormatException>(() => app.Keymap.Bind("ctrl-s", new Save(), "Editor &&"));
        Assert.Equal(rows, app.Keymap.Bindings.Count);
    }

    /// <summary>
    /// A key that asks for a binding's action allocates nothing, from the key to the action's handler (design §11.3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf with the context <c>Editor</c>, three levels under a root with the context <c>Workspace</c> that handles
    ///   <c>Save</c>. The keymap has 50 rows for other keys, <c>ctrl-s</c> for <c>Save</c> in <c>Workspace &gt; Editor</c>, and
    ///   <c>ctrl-k ctrl-s</c> for <c>Save</c>.
    ///
    /// Steps:
    /// - Under <c>PerformanceAssert.DoesNotAllocate</c>, 1,000 times: press Ctrl+S; press Ctrl+K and Ctrl+S.
    ///
    /// Expected:
    /// - No allocation, and <c>Save</c> ran twice each time.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Key_WithABinding_DoesNotAllocate()
    {
        int saves = 0;
        Element leaf = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            leaf = Box().Width(10).Height(10).Focusable().KeyContext("Editor");
            return Column(Column(Column(leaf))).KeyContext("Workspace").OnAction((in ActionEvent<Save> e) => saves++);
        });
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();
        for (int i = 0; i < 50; i++)
        {
            _ = app.Keymap.Bind($"ctrl-alt-f{(i % 24) + 1}", new Pick(i), i % 2 == 0 ? "Editor" : null);
        }

        _ = app.Keymap.Bind("ctrl-s", new Save(), "Workspace > Editor");
        _ = app.Keymap.Bind("ctrl-k ctrl-s", new Save());
        var ctrlS = new KeyInput(KeyEventKind.Down, Key.S, PhysicalKey.None, default, KeyModifiers.Control, false, TimeSpan.Zero);
        var ctrlK = new KeyInput(KeyEventKind.Down, Key.K, PhysicalKey.None, default, KeyModifiers.Control, false, TimeSpan.Zero);

        int runs = PerformanceAssert.DoesNotAllocate(
            1_000,
            index =>
            {
                _ = app.Input.Handle(ctrlS);
                _ = app.Input.Handle(ctrlK);
                _ = app.Input.Handle(ctrlS);
            }
        );

        Assert.Equal(runs * 2, saves);
    }

    // An app of a focusable leaf under a parent under a root; the leaf logs its keys and its text, and `setup` adds to the leaf
    // and the parent.
    private static async Task<HeadlessApp> Start(List<string> log, Action<Element, Element> setup)
    {
        Element leaf = default;
        HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            leaf = Box()
                .Width(10)
                .Height(10)
                .Focusable()
                .On(KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent e) => log.Add($"key {e.Key}"))
                .OnTextInput(EventPhase.Bubble, (in TextInputEvent e) => log.Add($"text {e.Text}"));
            Element parent = Column(leaf);
            setup(leaf, parent);
            return Column(parent);
        });
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();
        return app;
    }

    private class Save : IAction;

    private sealed class SaveAs : Save;

    private sealed class Close : IAction;

    private sealed class Pick(int number) : IAction
    {
        public int Number { get; } = number;
    }
}
