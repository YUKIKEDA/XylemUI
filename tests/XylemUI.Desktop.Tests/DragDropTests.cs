using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// Drag and drop in the app and the explicit capture (row DD1, design §7, "Drag and drop") in a headless app.
public sealed class DragDropTests
{
    /// <summary>
    /// The drag's preview, drawn under the pointer, is hit by nothing: a move over a target after the preview's frame keeps the target,
    /// and the drop reaches it (PR #599's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A source 100 x 30 dragging "a" with a preview 80 x 30; a target 200 x 60 below it that takes strings and logs its drops.
    ///
    /// Steps:
    /// - Press the source 5 px from its top left, so that the preview lies under the pointer; move 10 px; move into the target and
    ///   settle, which draws the preview there; move 1 px within the target; read its drag-over state; release.
    ///
    /// Expected:
    /// - The target keeps the drag-over state, and its log holds "drop a".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_PreviewUnderThePointer_IsHitByNothing()
    {
        var drops = new List<string>();
        Element source = default;
        Element target = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                source = Box()
                    .Width(100)
                    .Height(30)
                    .DragSource(() => "a", new DragSourceOptions { Preview = () => Box().Width(80).Height(30).Background(Color.FromRgb(0x3366CC)) });
                target = Box()
                    .Width(200)
                    .Height(60)
                    .DropTarget(new DropTargetOptions<string> { Drop = (in DragEvent<string> e) => drops.Add($"drop {e.Value}") });
                return Column(source, target);
            },
            new HeadlessOptions { Width = 300, Height = 200 }
        );
        _ = app.PumpAndSettle();

        app.Pointer.Move(5, 5);
        app.Pointer.Down();
        app.Pointer.Move(15, 5);
        app.Pointer.Move(50, 60);
        _ = app.PumpAndSettle();
        app.Pointer.Move(51, 60);
        _ = app.PumpAndSettle();
        bool over = Has(app, target, PseudoState.DragOver);
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.True(over);
        Assert.Equal(["drop a"], drops);
    }

    /// <summary>
    /// A mouse's press on a drag source starts a drag once it moves past the drag size (4 physical pixels here, Windows' default), not
    /// before; the target gets the enter, the moves, and the drop with the value and the effect; the source's end runs with the
    /// effect; the press gives no click.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A source box 100 x 30 that counts clicks and drags "a", logging its end; a target box 100 x 30 below it that takes strings
    ///   and logs each call with the value and the effect.
    ///
    /// Steps:
    /// - Move to the source's center and press; move 4 px right and read whether a drag runs; 5 px right and read it; move to the
    ///   target's center, then 1 px further; release.
    ///
    /// Expected:
    /// - No drag after 4 px, a drag after 5. The log: "enter a Move", "over a Move", "drop a Move", "ended Move". No click; no drag
    ///   after the release.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_StartsPastTheDragSize_DropsOnTheTarget_AndGivesNoClick()
    {
        var log = new List<string>();
        int clicks = 0;
        Element source = default;
        Element target = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            source = Box()
                .Width(100)
                .Height(30)
                .OnClick(() => clicks++)
                .DragSource(() => "a", new DragSourceOptions { Ended = effect => log.Add($"ended {effect}") });
            target = Box().Width(100).Height(30).DropTarget(Logging(log));
            return Column(source, target);
        });
        _ = app.PumpAndSettle();
        (int x, int y) = app.CenterOf(source);

        app.Pointer.Move(x, y);
        app.Pointer.Down();
        app.Pointer.Move(x + 4, y);
        bool afterFour = app.Input.Dragging;
        app.Pointer.Move(x + 5, y);
        bool afterFive = app.Input.Dragging;
        (int tx, int ty) = app.CenterOf(target);
        app.Pointer.Move(tx, ty);
        app.Pointer.Move(tx + 1, ty);
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.False(afterFour);
        Assert.True(afterFive);
        Assert.Equal(["enter a Move", "over a Move", "drop a Move", "ended Move"], log);
        Assert.Equal(0, clicks);
        Assert.False(app.Input.Dragging);
    }

    /// <summary>
    /// The drop target is the innermost node, from the one hit up, that takes the value: one of another type, a disabled one, and one
    /// whose CanDrop is false are passed over for the one around them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A source dragging "a"; an outer target of strings holding four boxes in a row, 50 wide: a target of strings, a target of
    ///   integers, a disabled target of strings, and a target of strings whose CanDrop is false. Each logs its name on a drop.
    ///
    /// Steps:
    /// - Drag from the source to the center of each of the four boxes and release, one drag each.
    ///
    /// Expected:
    /// - "inner", "outer", "outer", "outer".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_TakesTheInnermostTargetThatTakesTheValue()
    {
        var drops = new List<string>();
        Element source = default;
        var boxes = new Element[4];
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            source = Box().Width(100).Height(30).DragSource(() => "a");
            boxes[0] = Box().Width(50).Height(30).DropTarget(new DropTargetOptions<string> { Drop = (in DragEvent<string> _) => drops.Add("inner") });
            boxes[1] = Box().Width(50).Height(30).DropTarget(new DropTargetOptions<int> { Drop = (in DragEvent<int> _) => drops.Add("int") });
            boxes[2] = Box()
                .Width(50)
                .Height(30)
                .DropTarget(new DropTargetOptions<string> { Drop = (in DragEvent<string> _) => drops.Add("disabled") })
                .Disabled();
            boxes[3] = Box()
                .Width(50)
                .Height(30)
                .DropTarget(new DropTargetOptions<string> { CanDrop = _ => false, Drop = (in DragEvent<string> _) => drops.Add("refuses") });
            Element outer = Row(boxes).DropTarget(new DropTargetOptions<string> { Drop = (in DragEvent<string> _) => drops.Add("outer") });
            return Column(source, outer);
        });
        _ = app.PumpAndSettle();

        foreach (Element box in boxes)
        {
            DragTo(app, source, box);
        }

        Assert.Equal(["inner", "outer", "outer", "outer"], drops);
    }

    /// <summary>
    /// The effect is chosen from the effects both sides allow by the modifier keys: Control for Copy, Alt for Link, Move with neither;
    /// a change of the modifiers during the drag chooses again; a target that accepts none of the source's effects takes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A source dragging "a" that allows all three effects and logs its end; a target of strings that accepts all three and logs its
    ///   calls; a second source dragging "b" that allows Move alone, and a target beside the first that accepts Copy alone.
    ///
    /// Steps:
    /// - Drag from the first source to the target with Control, then with Alt. Drag again with no modifier, press Control over the
    ///   target, and release. Drag from the second source to the second target and release.
    ///
    /// Expected:
    /// - Drops with Copy and Link; then an over with Copy after the Control's down, and a drop with Copy. The second drag ends with
    ///   None and the second target gets nothing.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_ChoosesTheEffectByTheModifiers_AndBothSides()
    {
        var log = new List<string>();
        Element all = default;
        Element moveOnly = default;
        Element target = default;
        Element copyOnly = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            DragEffects every = DragEffects.Copy | DragEffects.Move | DragEffects.Link;
            all = Box().Width(100).Height(30).DragSource(() => "a", new DragSourceOptions { Allowed = every, Ended = e => log.Add($"ended {e}") });
            moveOnly = Box().Width(100).Height(30).DragSource(() => "b", new DragSourceOptions { Ended = e => log.Add($"ended {e}") });
            target = Box().Width(100).Height(30).DropTarget(Logging(log));
            copyOnly = Box()
                .Width(100)
                .Height(30)
                .DropTarget(new DropTargetOptions<string> { Accepts = DragEffects.Copy, Drop = (in DragEvent<string> e) => log.Add("copy-only") });
            return Column(all, moveOnly, Row(target, copyOnly));
        });
        _ = app.PumpAndSettle();

        DragTo(app, all, target, KeyModifiers.Control);
        DragTo(app, all, target, KeyModifiers.Alt);
        log.Clear();
        Start(app, all);
        (int tx, int ty) = app.CenterOf(target);
        app.Pointer.Move(tx, ty);
        _ = app.Keyboard.Down(Key.LeftCtrl, KeyModifiers.Control);
        app.Pointer.Up(modifiers: KeyModifiers.Control);
        _ = app.Keyboard.Up(Key.LeftCtrl);
        _ = app.PumpAndSettle();
        List<string> modifierChange = [.. log];
        log.Clear();
        DragTo(app, moveOnly, copyOnly);

        Assert.Equal(["enter a Move", "over a Copy", "drop a Copy", "ended Copy"], modifierChange);
        Assert.Equal(["ended None"], log);
    }

    /// <summary>
    /// Esc, the window's deactivation, and a release over no target end the drag with no drop: the target gets a leave, the source its
    /// end with None, the target loses its drag-over state, and the preview closes; Esc is taken by the drag.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A source dragging "a" with a preview, logging its end; a target of strings that logs its calls and has a drag-over look.
    ///
    /// Steps:
    /// - Drag over the target, read the popups and the drag-over state, press Esc and read whether it was handled, release. Drag over
    ///   the target and deactivate the window, activate it, release. Drag to empty space and release.
    ///
    /// Expected:
    /// - One popup and the drag-over state during the drag; Esc handled; after each end: no popup, no drag-over state, no drag. The
    ///   log: "enter a Move", "leave a Move", "ended None" twice, then "ended None".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_EscDeactivationAndNoTarget_EndWithNone()
    {
        var log = new List<string>();
        Element source = default;
        Element target = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                source = Box()
                    .Width(100)
                    .Height(30)
                    .DragSource(() => "a", new DragSourceOptions { Preview = () => Box().Width(40).Height(10), Ended = e => log.Add($"ended {e}") });
                target = Box().Width(100).Height(30).DropTarget(Logging(log)).DragOver(new PaintStyle { Background = Color.FromRgb(0x336699) });
                return Column(source, target, Box().Width(100).Height(60));
            },
            new HeadlessOptions { Width = 300, Height = 200 }
        );
        _ = app.PumpAndSettle();
        (int tx, int ty) = app.CenterOf(target);

        Start(app, source);
        app.Pointer.Move(tx, ty);
        _ = app.PumpAndSettle();
        int popups = app.Popups.Count;
        bool over = Has(app, target, PseudoState.DragOver);
        bool escHandled = app.Keyboard.Press(Key.Escape);
        app.Pointer.Up();
        _ = app.PumpAndSettle();
        var afterEsc = (app.Popups.Count, Has(app, target, PseudoState.DragOver), app.Input.Dragging);

        Start(app, source);
        app.Pointer.Move(tx, ty);
        app.SetActive(false);
        app.SetActive(true);
        app.Pointer.Up();
        _ = app.PumpAndSettle();
        var afterDeactivation = (app.Popups.Count, Has(app, target, PseudoState.DragOver), app.Input.Dragging);

        Start(app, source);
        app.Pointer.Move(50, 150);
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.Equal((1, true, true), (popups, over, escHandled));
        Assert.Equal((0, false, false), afterEsc);
        Assert.Equal((0, false, false), afterDeactivation);
        Assert.Equal(["enter a Move", "leave a Move", "ended None", "enter a Move", "leave a Move", "ended None", "ended None"], log);
    }

    /// <summary>
    /// A touch starts a drag after a press held still for 500 ms; a touch that moves past 18 logical pixels first starts none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A source box 100 x 30 dragging "a"; a target of strings below it that logs its drops.
    ///
    /// Steps:
    /// - Touch the source; pump 499 ms and read whether a drag runs; pump 2 ms and read it; move the touch to the target and lift it.
    ///   Touch the source again, move it 19 px, pump 600 ms, read whether a drag runs, lift it.
    ///
    /// Expected:
    /// - No drag at 499 ms, a drag at 501; the target gets "a". No drag after the move past the slop.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_TouchStartsAfterTheLongPress_NotAfterAMove()
    {
        var drops = new List<string>();
        Element source = default;
        Element target = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            source = Box().Width(100).Height(30).DragSource(() => "a");
            target = Box().Width(100).Height(30).DropTarget(new DropTargetOptions<string> { Drop = (in DragEvent<string> e) => drops.Add(e.Value) });
            return Column(source, target);
        });
        _ = app.PumpAndSettle();
        (int x, int y) = app.CenterOf(source);
        (int tx, int ty) = app.CenterOf(target);

        Touch(app, PointerInputKind.Down, x, y);
        app.Pump(TimeSpan.FromMilliseconds(499));
        bool early = app.Input.Dragging;
        app.Pump(TimeSpan.FromMilliseconds(2));
        bool late = app.Input.Dragging;
        Touch(app, PointerInputKind.Move, tx, ty);
        Touch(app, PointerInputKind.Up, tx, ty);
        _ = app.PumpAndSettle();

        Touch(app, PointerInputKind.Down, x, y);
        Touch(app, PointerInputKind.Move, x + 19, y);
        app.Pump(TimeSpan.FromMilliseconds(600));
        bool moved = app.Input.Dragging;
        Touch(app, PointerInputKind.Up, x + 19, y);
        _ = app.PumpAndSettle();

        Assert.Equal((false, true, false), (early, late, moved));
        Assert.Equal(["a"], drops);
    }

    /// <summary>
    /// A press a node inside the source stops starts no drag (a text field stops its own); a drag that starts takes the capture from
    /// the node pressed, which gets a cancel.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A source 200 x 60 dragging "a", holding a text field and a box 50 x 20 that logs the cancels it gets.
    ///
    /// Steps:
    /// - Press the field and move 20 px; read whether a drag runs; release. Press the box and move 20 px; read whether a drag runs and
    ///   the box's cancels; release.
    ///
    /// Expected:
    /// - No drag from the field; a drag from the box, which got one cancel.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_NotFromAFieldInside_AndTheNodePressedGetsACancel()
    {
        int cancels = 0;
        Element field = default;
        Element box = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            field = TextField(Signal.Create("text")).Width(100);
            box = Box().Width(50).Height(20).On(PointerEventKind.Cancel, EventPhase.Bubble, (in PointerEvent _) => cancels++);
            return Column(field, box).Width(200).Height(60).DragSource(() => "a");
        });
        _ = app.PumpAndSettle();

        (int fx, int fy) = app.CenterOf(field);
        app.Pointer.Move(fx, fy);
        app.Pointer.Down();
        app.Pointer.Move(fx + 20, fy);
        bool fromField = app.Input.Dragging;
        app.Pointer.Up();
        (int bx, int by) = app.CenterOf(box);
        app.Pointer.Move(bx, by);
        app.Pointer.Down();
        app.Pointer.Move(bx + 20, by);
        bool fromBox = app.Input.Dragging;
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.Equal((false, true, 1), (fromField, fromBox, cancels));
    }

    /// <summary>
    /// A drag that starts inside a selection area ends the area's selection by the press: the area's text under the drag is not
    /// selected while it moves.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A selection area holding a source 200 x 30 with the text "abcd", dragging "a", and the text "efgh" below it.
    ///
    /// Steps:
    /// - Press the source's text and move 10 px; move to the second text; read its selection; release.
    ///
    /// Expected:
    /// - A drag runs, and the second text has no selection.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_InsideASelectionArea_EndsTheAreasSelection()
    {
        Element source = default;
        Element below = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                source = Box(Elements.Text("abcd")).Width(200).Height(30).DragSource(() => "a");
                below = Elements.Text("efgh");
                return SelectionArea(Column(source, below));
            },
            new HeadlessOptions { Width = 300, Height = 200 }
        );
        _ = app.PumpAndSettle();

        Start(app, source);
        (int x, int y) = app.CenterOf(below);
        app.Pointer.Move(x + 5, y);
        _ = app.PumpAndSettle();
        bool dragging = app.Input.Dragging;
        bool selected = app.Tree.Nodes.GetRef(below.Node).Text!.Selection is { IsEmpty: false };
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.True(dragging);
        Assert.False(selected);
    }

    /// <summary>
    /// A touch that drags a value does not scroll the container it is in.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 200 x 150 holding a column 1,000 tall of a source 200 x 30 dragging "a" and a filler.
    ///
    /// Steps:
    /// - Touch the source and hold it 500 ms; move the touch 40 px up and 40 px down, inside the view; read the offset; lift it.
    ///
    /// Expected:
    /// - A drag runs, and the offset is 0.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_ByTouch_DoesNotScroll()
    {
        Element source = default;
        Element view = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                source = Box().Width(200).Height(30).DragSource(() => "a");
                view = ScrollView(Column(Box().Width(200).Height(60), source, Box().Width(200).Height(910))).Width(200).Height(150);
                return Column(view);
            },
            new HeadlessOptions { Width = 300, Height = 300 }
        );
        _ = app.PumpAndSettle();
        (int x, int y) = app.CenterOf(source);

        Touch(app, PointerInputKind.Down, x, y);
        app.Pump(TimeSpan.FromMilliseconds(501));
        bool dragging = app.Input.Dragging;
        Touch(app, PointerInputKind.Move, x, y - 40);
        Touch(app, PointerInputKind.Move, x, y + 40);
        _ = app.PumpAndSettle();
        float offset = app.Tree.ScrollOffsetOf(view.Node).Y;
        Touch(app, PointerInputKind.Up, x, y + 40);
        _ = app.PumpAndSettle();

        Assert.True(dragging);
        Assert.Equal(0, offset);
    }

    /// <summary>
    /// A handler moves the pointer's capture with <see cref="PointerEvent.Capture"/>: the moves then route from the new node, the old
    /// one gets a cancel, and null releases it; the capture ends with the release.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Box a, whose down moves the capture to box b; box b logs its moves, and on its second move releases the capture; box a logs
    ///   its cancels and moves. Both 50 x 30, side by side.
    ///
    /// Steps:
    /// - Press a; move within a three times; release.
    ///
    /// Expected:
    /// - a got one cancel; b got two moves, and a the third (the capture released goes back to the node hit).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Capture_MovesTheRoute_CancelsTheOld_AndNullReleases()
    {
        var log = new List<string>();
        Element a = default;
        Element b = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            int bMoves = 0;
            b = Box()
                .Width(50)
                .Height(30)
                .On(
                    PointerEventKind.Move,
                    EventPhase.Bubble,
                    (in PointerEvent e) =>
                    {
                        log.Add("b move");
                        if (++bMoves == 2)
                        {
                            e.Capture(null);
                        }
                    }
                );
            Element captureTo = b;
            a = Box()
                .Width(50)
                .Height(30)
                .On(PointerEventKind.Down, EventPhase.Bubble, (in PointerEvent e) => e.Capture(captureTo))
                .On(PointerEventKind.Cancel, EventPhase.Bubble, (in PointerEvent _) => log.Add("a cancel"))
                .On(PointerEventKind.Move, EventPhase.Bubble, (in PointerEvent _) => log.Add("a move"));
            return Row(a, b);
        });
        _ = app.PumpAndSettle();
        (int x, int y) = app.CenterOf(a);

        app.Pointer.Move(x, y);
        log.Clear();
        app.Pointer.Down();
        app.Pointer.Move(x + 1, y);
        app.Pointer.Move(x + 2, y);
        app.Pointer.Move(x + 3, y);
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.Equal(["a cancel", "b move", "b move", "a move"], log);
    }

    /// <summary>
    /// While the drag's preview is past the bottom of a scroll container last under the pointer, the container scrolls by the
    /// overscroll, at most 20 logical pixels, times 50 a second, and stops when it is back inside.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll view 200 x 100 holding a column 1,000 tall of a source 200 x 30 dragging "a" and a filler. The window is 300 x 300.
    ///
    /// Steps:
    /// - Start a drag; move into the scroll view, then 50 px below it; pump 100 ms in frames of 10 ms; read the offset. Move back
    ///   inside; pump 100 ms; read the offset again; release.
    ///
    /// Expected:
    /// - The first offset about 20 × 50 × 0.09 s (one frame starts the clock): between 80 and 100; the second the same as the first.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_AtTheEdge_ScrollsTheContainer_AndStopsInside()
    {
        Element source = default;
        Element view = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                source = Box().Width(200).Height(30).DragSource(() => "a");
                view = ScrollView(Column(source, Box().Width(200).Height(970))).Width(200).Height(100);
                return Column(view);
            },
            new HeadlessOptions { Width = 300, Height = 300 }
        );
        _ = app.PumpAndSettle();

        Start(app, source);
        app.Pointer.Move(100, 50);
        app.Pointer.Move(100, 150);
        for (int i = 0; i < 10; i++)
        {
            app.Pump(TimeSpan.FromMilliseconds(10));
        }

        float scrolled = app.Tree.ScrollOffsetOf(view.Node).Y;
        app.Pointer.Move(100, 50);
        for (int i = 0; i < 10; i++)
        {
            app.Pump(TimeSpan.FromMilliseconds(10));
        }

        float after = app.Tree.ScrollOffsetOf(view.Node).Y;
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.InRange(scrolled, 80, 100);
        Assert.Equal(scrolled, after);
    }

    /// <summary>
    /// The drag goes on when its source leaves the tree, and its end still runs; the drop reaches the target.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A source dragging "a" in a Show of a signal, logging its end; a target of strings that logs its drops.
    ///
    /// Steps:
    /// - Start a drag; set the signal false and settle; move to the target; release.
    ///
    /// Expected:
    /// - The log: "drop a", "ended Move".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Drag_GoesOnWhenTheSourceLeaves()
    {
        var log = new List<string>();
        var shown = Signal.Create(true);
        Element source = default;
        Element target = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            target = Box()
                .Width(100)
                .Height(30)
                .DropTarget(new DropTargetOptions<string> { Drop = (in DragEvent<string> e) => log.Add($"drop {e.Value}") });
            return Column(
                target,
                Show(
                    shown,
                    () => source = Box().Width(100).Height(30).DragSource(() => "a", new DragSourceOptions { Ended = e => log.Add($"ended {e}") })
                )
            );
        });
        _ = app.PumpAndSettle();

        Start(app, source);
        shown.Set(false);
        _ = app.PumpAndSettle();
        (int tx, int ty) = app.CenterOf(target);
        app.Pointer.Move(tx, ty);
        app.Pointer.Up();
        _ = app.PumpAndSettle();

        Assert.Equal(["drop a", "ended Move"], log);
    }

    private static DropTargetOptions<string> Logging(List<string> log) =>
        new()
        {
            Enter = (in DragEvent<string> e) => log.Add($"enter {e.Value} {e.Effect}"),
            Over = (in DragEvent<string> e) => log.Add($"over {e.Value} {e.Effect}"),
            Leave = (in DragEvent<string> e) => log.Add($"leave {e.Value} {e.Effect}"),
            Drop = (in DragEvent<string> e) => log.Add($"drop {e.Value} {e.Effect}"),
        };

    private static bool Has(HeadlessApp app, Element element, PseudoState state) => (app.Tree.Nodes.GetRef(element.Node).Pseudo & state) != 0;

    // Presses the source's center and moves 10 px right, past the drag size.
    private static void Start(HeadlessApp app, Element source, KeyModifiers modifiers = KeyModifiers.None)
    {
        (int x, int y) = app.CenterOf(source);
        app.Pointer.Move(x, y, modifiers);
        app.Pointer.Down(modifiers: modifiers);
        app.Pointer.Move(x + 10, y, modifiers);
        Assert.True(app.Input.Dragging);
    }

    // A whole drag from a source to a target's center, released there.
    private static void DragTo(HeadlessApp app, Element source, Element target, KeyModifiers modifiers = KeyModifiers.None)
    {
        Start(app, source, modifiers);
        (int x, int y) = app.CenterOf(target);
        app.Pointer.Move(x, y, modifiers);
        app.Pointer.Up(modifiers: modifiers);
        _ = app.PumpAndSettle();
    }

    private static void Touch(HeadlessApp app, PointerInputKind kind, int x, int y) =>
        app.Input.Handle(
            new PointerInput(
                kind,
                x,
                y,
                kind == PointerInputKind.Move ? PointerButton.None : PointerButton.Left,
                kind == PointerInputKind.Up ? PointerButtons.None : PointerButtons.Left,
                KeyModifiers.None,
                1,
                app.Time.Elapsed,
                PointerType.Touch,
                PointerId: 1
            )
        );
}
