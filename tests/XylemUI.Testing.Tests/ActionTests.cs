using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// Actions (design §7, "Shortcuts and actions", row KM1a): their handlers on nodes, the route from the focused node, and whether an
// action can be dispatched. The trees here hold no text, so these tests run where HarfBuzz is not built.
public sealed class ActionTests
{
    /// <summary>
    /// An action dispatched with the focus on a node goes down from the root in the capture phase and up from the node in the bubble
    /// phase, and the first bubble handler ends the route.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root with a capture handler and a bubble handler of <c>Save</c>, a middle node with both, and a focused leaf with both.
    ///
    /// Steps:
    /// - Dispatch a <c>Save</c> from the root element.
    ///
    /// Expected:
    /// - The handlers called are, in order: the root's, the middle's, and the leaf's capture handlers, then the leaf's bubble handler.
    ///   The middle's and the root's bubble handlers are not called. The dispatch returns true, each handler got the same action, its
    ///   target was the leaf, and its current target was its own node.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dispatch_FromTheFocusedNode_CapturesDownThenTheFirstBubbleHandlerEndsIt()
    {
        var calls = new List<string>();
        var save = new Save();
        Element leaf = default;
        Element root = default;
        void Record(string name, in ActionEvent<Save> e, Element own)
        {
            Assert.Same(save, e.Action);
            Assert.Equal(leaf.Node, e.TargetNode);
            Assert.Equal(own.Node, e.CurrentNode);
            calls.Add(name);
        }

        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            leaf = Box().Width(10).Height(10).Focusable();
            leaf.OnAction((in ActionEvent<Save> e) => Record("leaf capture", e, leaf), EventPhase.Capture)
                .OnAction((in ActionEvent<Save> e) => Record("leaf bubble", e, leaf));
            Element middle = Column(leaf);
            middle
                .OnAction((in ActionEvent<Save> e) => Record("middle capture", e, middle), EventPhase.Capture)
                .OnAction((in ActionEvent<Save> e) => Record("middle bubble", e, middle));
            root = Column(middle);
            root.OnAction((in ActionEvent<Save> e) => Record("root capture", e, root), EventPhase.Capture)
                .OnAction((in ActionEvent<Save> e) => Record("root bubble", e, root));
            return root;
        });
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();

        Assert.True(root.Dispatch(save));
        Assert.Equal(["root capture", "middle capture", "leaf capture", "leaf bubble"], calls);
    }

    /// <summary>
    /// A bubble handler that propagates passes the action to the next handler, and a dispatch that every handler passes on is not
    /// handled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf whose bubble handler of <c>Save</c> calls <c>Propagate</c>, under a root whose bubble handler calls it only
    ///   while a flag is set.
    ///
    /// Steps:
    /// - Dispatch a <c>Save</c> with the flag clear; then again with the flag set.
    ///
    /// Expected:
    /// - First: both handlers are called, the leaf's first, and the dispatch returns true (the root handled it). Second: both are
    ///   called and the dispatch returns false.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dispatch_ABubbleHandlerThatPropagates_PassesItOn()
    {
        var calls = new List<string>();
        bool rootPropagates = false;
        Element leaf = default;
        Element root = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            leaf = Box()
                .Width(10)
                .Height(10)
                .Focusable()
                .OnAction(
                    (in ActionEvent<Save> e) =>
                    {
                        calls.Add("leaf");
                        e.Propagate();
                    }
                );
            root = Column(leaf)
                .OnAction(
                    (in ActionEvent<Save> e) =>
                    {
                        calls.Add("root");
                        if (rootPropagates)
                        {
                            e.Propagate();
                        }
                    }
                );
            return root;
        });
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();

        Assert.True(root.Dispatch(new Save()));
        Assert.Equal(["leaf", "root"], calls);

        calls.Clear();
        rootPropagates = true;
        Assert.False(root.Dispatch(new Save()));
        Assert.Equal(["leaf", "root"], calls);
    }

    /// <summary>
    /// A capture handler that stops the action ends the route before the bubble phase.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root whose capture handler of <c>Save</c> calls <c>StopPropagation</c>, over a focused leaf with a bubble handler.
    ///
    /// Steps:
    /// - Dispatch a <c>Save</c>.
    ///
    /// Expected:
    /// - Only the root's handler is called, and the dispatch returns true.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dispatch_ACaptureHandlerThatStops_EndsTheRoute()
    {
        var calls = new List<string>();
        Element leaf = default;
        Element root = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            leaf = Box().Width(10).Height(10).Focusable().OnAction((in ActionEvent<Save> e) => calls.Add("leaf"));
            root = Column(leaf)
                .OnAction(
                    (in ActionEvent<Save> e) =>
                    {
                        calls.Add("root");
                        e.StopPropagation();
                    },
                    EventPhase.Capture
                );
            return root;
        });
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();

        Assert.True(root.Dispatch(new Save()));
        Assert.Equal(["root"], calls);
    }

    /// <summary>
    /// A handler is called for its own action type only, and an action no handler takes is not handled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf with a bubble handler of <c>Save</c>. <c>SaveAs</c> derives from <c>Save</c>; <c>Close</c> does not.
    ///
    /// Steps:
    /// - Dispatch a <c>Close</c>, then a <c>SaveAs</c>, then a <c>Save</c>.
    ///
    /// Expected:
    /// - The first two return false and call nothing; the third returns true and calls the handler once.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dispatch_AnotherActionType_CallsNoHandler()
    {
        int calls = 0;
        Element leaf = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            leaf = Box().Width(10).Height(10).Focusable().OnAction((in ActionEvent<Save> e) => calls++)
        );
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();

        Assert.False(leaf.Dispatch(new Close()));
        Assert.False(leaf.Dispatch(new SaveAs()));
        Assert.Equal(0, calls);
        Assert.True(leaf.Dispatch(new Save()));
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// With no focused node the route is the root alone, and a disabled node's handler is skipped while its ancestor's is called.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root with a bubble handler of <c>Save</c> over a middle node with one, disabled by a signal, over a focusable leaf.
    ///
    /// Steps:
    /// - Dispatch a <c>Save</c> with no focus. Focus the leaf and dispatch. Disable the middle node, focus the leaf through the tree
    ///   (a disabled subtree takes no focus from the app), and dispatch.
    ///
    /// Expected:
    /// - No focus: the root's handler alone. Leaf focused: the middle's handler alone. Middle disabled: the focus leaves the leaf, and
    ///   the root's handler alone is called.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dispatch_NoFocusOrADisabledNode_UsesTheHandlersThatAreLeft()
    {
        var calls = new List<string>();
        var disabled = Signal.Create(false);
        Element leaf = default;
        Element root = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            leaf = Box().Width(10).Height(10).Focusable();
            Element middle = Column(leaf).Disabled(disabled).OnAction((in ActionEvent<Save> e) => calls.Add("middle"));
            root = Column(middle).OnAction((in ActionEvent<Save> e) => calls.Add("root"));
            return root;
        });

        Assert.True(root.Dispatch(new Save()));
        Assert.Equal(["root"], calls);

        calls.Clear();
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();
        Assert.True(root.Dispatch(new Save()));
        Assert.Equal(["middle"], calls);

        calls.Clear();
        disabled.Set(true);
        _ = app.PumpAndSettle();
        Assert.True(root.Dispatch(new Save()));
        Assert.Equal(["root"], calls);
    }

    /// <summary>
    /// A handler stops being called when its scope is disposed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf. Inside a <c>Show</c> whose condition is a signal, a child adds a bubble handler of <c>Save</c> to the leaf's
    ///   parent.
    ///
    /// Steps:
    /// - Dispatch a <c>Save</c>; set the condition to false; dispatch again.
    ///
    /// Expected:
    /// - The first is handled and calls the handler once; the second is not handled and calls nothing.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task OnAction_ScopeDisposed_HandlerIsRemoved()
    {
        int calls = 0;
        var shown = Signal.Create(true);
        Element leaf = default;
        Element root = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            leaf = Box().Width(10).Height(10).Focusable();
            root = Column(leaf);
            Element owner = root;
            return Column(
                root,
                Show(
                    shown,
                    () =>
                    {
                        _ = owner.OnAction((in ActionEvent<Save> e) => calls++);
                        return Box().Width(1).Height(1);
                    }
                )
            );
        });
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();

        Assert.True(root.Dispatch(new Save()));
        Assert.Equal(1, calls);

        shown.Set(false);
        _ = app.PumpAndSettle();
        Assert.False(root.Dispatch(new Save()));
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// An action dispatched with the focus in a popup goes on to the popup's anchor and its ancestors, as a key does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An anchor under a root with a bubble handler of <c>Save</c>; the anchor's popup, open, holds a focusable box.
    ///
    /// Steps:
    /// - Focus the box in the popup; dispatch a <c>Save</c>.
    ///
    /// Expected:
    /// - The root's handler is called, with the popup's box as the target.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dispatch_FromAPopup_ReachesTheAnchorsAncestors()
    {
        var open = Signal.Create(true);
        Element inPopup = default;
        Element root = default;
        Element target = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            Element anchor = Box().Width(10).Height(10).Popup(open, () => inPopup = Box().Width(10).Height(10).Focusable());
            root = Column(anchor).OnAction((in ActionEvent<Save> e) => target = e.Target);
            return root;
        });
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(inPopup));
        _ = app.PumpAndSettle();

        Assert.True(root.Dispatch(new Save()));
        Assert.Equal(inPopup, target);
    }

    /// <summary>
    /// Whether an action can be dispatched follows the focus, the handlers, and a node's disabled state, and a computation that read
    /// it runs again only when one of them changed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two focusable leaves under a root; the first leaf's parent has a bubble handler of <c>Save</c> and is disabled by a signal.
    ///   An effect records <c>CanDispatch&lt;Save&gt;</c> each time it runs.
    ///
    /// Steps:
    /// - Start (no focus). Focus the first leaf. Focus the second. Focus the first again. Disable the handler's node. Pump twice with
    ///   no change.
    ///
    /// Expected:
    /// - The recorded values are false, true, false, true, false: one run for each change and none for the pumps.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task CanDispatch_ReadInAnEffect_RunsAgainForEachChangeOnly()
    {
        var seen = new List<bool>();
        var disabled = Signal.Create(false);
        Element first = default;
        Element second = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            first = Box().Width(10).Height(10).Focusable();
            second = Box().Width(10).Height(10).Focusable();
            Element holder = Column(first).Disabled(disabled).OnAction((in ActionEvent<Save> e) => { });
            Element root = Column(holder, second);
            _ = Effect.Create(() => seen.Add(root.CanDispatch<Save>()));
            return root;
        });
        Assert.Equal([false], seen);

        Assert.True(app.Focus(first));
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(second));
        _ = app.PumpAndSettle();
        Assert.True(app.Focus(first));
        _ = app.PumpAndSettle();
        disabled.Set(true);
        _ = app.PumpAndSettle();
        _ = app.PumpAndSettle();
        Assert.Equal([false, true, false, true, false], seen);
    }

    /// <summary>
    /// Dispatching an action allocates nothing (design §11.3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused leaf three levels under a root; the root has a capture handler and the leaf's parent a bubble handler of
    ///   <c>Save</c>. One action instance.
    ///
    /// Steps:
    /// - Dispatch the action 1,000 times under <c>PerformanceAssert.DoesNotAllocate</c>.
    ///
    /// Expected:
    /// - No allocation, and both handlers ran each time.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dispatch_AfterWarmUp_DoesNotAllocate()
    {
        int calls = 0;
        var save = new Save();
        Element leaf = default;
        Element root = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            leaf = Box().Width(10).Height(10).Focusable();
            Element parent = Column(leaf).OnAction((in ActionEvent<Save> e) => calls++);
            root = Column(Column(parent)).OnAction((in ActionEvent<Save> e) => calls++, EventPhase.Capture);
            return root;
        });
        Assert.True(app.Focus(leaf));
        _ = app.PumpAndSettle();

        int runs = PerformanceAssert.DoesNotAllocate(1_000, _ => root.Dispatch(save));
        Assert.Equal(runs * 2, calls);
    }

    /// <summary>
    /// The registry gives an action for its name and a name for its type, and refuses a name or a type registered twice.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - <c>Registered</c> is registered as <c>test::Registered</c> by this test.
    ///
    /// Steps:
    /// - Create the action by its name and by an unknown name; read the type's name; register the same pair again; register the same
    ///   type under another name; register another type under the same name.
    ///
    /// Expected:
    /// - The name gives a <c>Registered</c>; the unknown name gives none; the type's name is <c>test::Registered</c>; the same pair is
    ///   accepted; the other two throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void ActionRegistry_NamesAndTypes_AreOneToOne()
    {
        ActionRegistry.Register("test::Registered", static () => new Registered());

        Assert.True(ActionRegistry.TryCreate("test::Registered", out IAction? action));
        _ = Assert.IsType<Registered>(action);
        Assert.False(ActionRegistry.TryCreate("test::Unknown", out IAction? none));
        Assert.Null(none);
        Assert.True(ActionRegistry.TryGetName(typeof(Registered), out string? name));
        Assert.Equal("test::Registered", name);

        ActionRegistry.Register("test::Registered", static () => new Registered());
        _ = Assert.Throws<InvalidOperationException>(() => ActionRegistry.Register("test::Other", static () => new Registered()));
        _ = Assert.Throws<InvalidOperationException>(() => ActionRegistry.Register("test::Registered", static () => new Unregistered()));
    }

    private class Save : IAction;

    private sealed class SaveAs : Save;

    private sealed class Close : IAction;

    private sealed class Registered : IAction;

    private sealed class Unregistered : IAction;
}
