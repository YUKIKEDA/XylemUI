using System.Numerics;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// The headless app (design §12.2, §12.4, §12.5): its frames and timers on the virtual clock, the input it sends, the nodes it finds,
// and what it checks at its end. The trees here hold no text, so these tests run where HarfBuzz is not built.
public sealed class HeadlessAppTests
{
    private static readonly Color Red = Color.FromRgb(0xFF0000);
    private static readonly Color Blue = Color.FromRgb(0x0000FF);

    /// <summary>
    /// A frame asked for runs in one iteration that advances the clock by one frame interval; then the app is settled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a box whose background is a signal; the first frame has run.
    ///
    /// Steps:
    /// - Set the signal, which asks for a frame; pump until settled.
    ///
    /// Expected:
    /// - One iteration; the clock advanced by 1/60 s; one more frame presented; a second pump runs nothing.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PumpAndSettle_AFrameAskedFor_RunsItOneIntervalLater()
    {
        var color = Signal.Create<Paint>(Red);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(10).Height(10).Background(color));
        int presents = app.PresentCount;
        TimeSpan before = app.Time.Elapsed;

        color.Set(Blue);
        Assert.Equal(1, app.PumpAndSettle());
        Assert.Equal(before + new HeadlessOptions().FrameInterval, app.Time.Elapsed);
        Assert.Equal(presents + 1, app.PresentCount);
        Assert.Equal(0, app.PumpAndSettle());
    }

    /// <summary>
    /// The start runs the frame that the root function's nodes asked for and leaves none: a pump right after it runs nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root function of a column of two boxes with a background, which ask for a frame as they are made.
    ///
    /// Steps:
    /// - Start the app; pump until settled.
    ///
    /// Expected:
    /// - The first frame presented, and the pump ran 0 iterations.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task StartAsync_RunsTheFirstFrame_AndLeavesNoneAsked()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Box().Width(10).Height(10).Background(Red), Box().Width(10).Height(10).Background(Blue))
        );
        Assert.Equal(1, app.PresentCount);
        Assert.Equal(0, app.PumpAndSettle());
    }

    /// <summary>
    /// A pump waits for a running animation, frame by frame on the virtual clock, and an app that ends with one running fails at its
    /// disposal (design §2.14, "Tests").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two apps of a box whose root makes an animation of 100 ms, at a frame interval of 1/60 s.
    ///
    /// Steps:
    /// - In the first, animate to 1 and pump until settled. In the second, animate to 1 and dispose it without a pump.
    ///
    /// Expected:
    /// - The first pump runs 8 iterations and ends with the value at 1: the first frame starts the move, and 1/60 s is 166,666
    ///   ticks, so 6 frames reach 99.9996 ms and the 7th reaches 100 ms; the second disposal throws <see cref="InvalidOperationException"/> naming a frame.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PumpAndSettle_WaitsForAnAnimation_AndARunningOneFailsTheEnd()
    {
        Animation animation = null!;
        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(() =>
            {
                animation = Animation.Create(TimeSpan.FromMilliseconds(100));
                return Box();
            })
        )
        {
            animation.AnimateTo(1);
            Assert.Equal(8, app.PumpAndSettle());
            Assert.Equal(1, animation.Value.Get());
        }

        HeadlessApp running = await HeadlessApp.StartAsync(() =>
        {
            animation = Animation.Create(TimeSpan.FromMilliseconds(100));
            return Box();
        });
        animation.AnimateTo(1);
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => running.DisposeAsync().AsTask());
        Assert.Contains("a frame", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no frame asked for, a pump jumps the clock to the next timer and fires it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a box, settled; a one-shot timer due 5 s later on the app's clock.
    ///
    /// Steps:
    /// - Pump until settled.
    ///
    /// Expected:
    /// - The timer fired, at 5 s after it was made; one iteration.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PumpAndSettle_NoFrame_JumpsToTheNextTimer()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box());
        TimeSpan start = app.Time.Elapsed;
        TimeSpan firedAt = TimeSpan.MinValue;
        using ITimer timer = app.Time.CreateTimer(_ => firedAt = app.Time.Elapsed, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        Assert.Equal(1, app.PumpAndSettle());
        Assert.Equal(start + TimeSpan.FromSeconds(5), firedAt);
    }

    /// <summary>
    /// Work posted to the UI thread's synchronization context runs in a pump.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a box, settled.
    ///
    /// Steps:
    /// - Post a callback to <see cref="SynchronizationContext.Current"/>; pump until settled.
    ///
    /// Expected:
    /// - The callback ran.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PumpAndSettle_RunsTheUiQueue()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box());
        bool ran = false;
        SynchronizationContext.Current!.Post(_ => ran = true, null);

        _ = app.PumpAndSettle();
        Assert.True(ran);
    }

    /// <summary>
    /// A timer that never stops keeps the app from settling: the pump throws after the iterations allowed and names what was left.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a box whose background a periodic timer of 10 ms flips, with at most 50 iterations.
    ///
    /// Steps:
    /// - Pump until settled; stop the timer and pump again.
    ///
    /// Expected:
    /// - The first pump throws <see cref="InvalidOperationException"/> naming 50 iterations; after the timer stops, the app
    ///   settles.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PumpAndSettle_NeverSettles_ThrowsAfterTheIterationsAllowed()
    {
        var color = Signal.Create<Paint>(Red);
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => Box().Width(10).Height(10).Background(color),
            new HeadlessOptions { MaxIterations = 50 }
        );
        using ITimer timer = app.Time.CreateTimer(
            _ => color.Set(color.Get().Color == Red ? Blue : Red),
            null,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10)
        );

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => app.PumpAndSettle());
        Assert.Contains("50 iterations", error.Message, StringComparison.Ordinal);
        timer.Dispose();
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// A pump of a duration fires the timers due in it and runs one frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a box whose background a one-shot timer due 100 ms later changes.
    ///
    /// Steps:
    /// - Pump 50 ms; then pump 50 ms more.
    ///
    /// Expected:
    /// - After 50 ms nothing presented; after 100 ms the timer fired and one frame presented, and no frame is left.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Pump_FiresTheTimersDue_AndRunsOneFrame()
    {
        var color = Signal.Create<Paint>(Red);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box().Width(10).Height(10).Background(color));
        using ITimer timer = app.Time.CreateTimer(_ => color.Set(Blue), null, TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
        int presents = app.PresentCount;

        app.Pump(TimeSpan.FromMilliseconds(50));
        Assert.Equal(presents, app.PresentCount);
        app.Pump(TimeSpan.FromMilliseconds(50));
        Assert.Equal(presents + 1, app.PresentCount);
        Assert.Equal(0, app.PumpAndSettle());
    }

    /// <summary>
    /// An app that ends with a timer still due fails at its disposal, and one with a frame asked for does too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two apps of a box: one with a timer due 1 s later, one whose background was set without a pump.
    ///
    /// Steps:
    /// - Dispose each.
    ///
    /// Expected:
    /// - Each disposal throws <see cref="InvalidOperationException"/>: "1 timers" for the first, "a frame" for the second.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task DisposeAsync_AFrameOrATimerLeft_Throws()
    {
        HeadlessApp timed = await HeadlessApp.StartAsync(() => Box());
        ITimer timer = timed.Time.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        InvalidOperationException left = await Assert.ThrowsAsync<InvalidOperationException>(() => timed.DisposeAsync().AsTask());
        Assert.Contains("1 timers", left.Message, StringComparison.Ordinal);
        timer.Dispose();

        var color = Signal.Create<Paint>(Red);
        HeadlessApp framed = await HeadlessApp.StartAsync(() => Box().Width(10).Height(10).Background(color));
        color.Set(Blue);
        InvalidOperationException frame = await Assert.ThrowsAsync<InvalidOperationException>(() => framed.DisposeAsync().AsTask());
        Assert.Contains("a frame", frame.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Find returns the one node of a test ID and throws for none and for two; FindAll returns them in preorder, or none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a column: a box "a", a row holding boxes "b" and "c", and a box "b" after the row.
    ///
    /// Steps:
    /// - Find "a", "b", and "z"; find all of "b" and of "z".
    ///
    /// Expected:
    /// - "a" is the first box; "b" and "z" throw <see cref="InvalidOperationException"/> (two and none); all of "b" are the box in
    ///   the row and then the last box; all of "z" is empty.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Find_ByTestId_OneOrThrows_AndFindAllIsInPreorder()
    {
        Element a = default;
        Element inRow = default;
        Element last = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(a = Box().TestId("a"), Row(inRow = Box().TestId("b"), Box().TestId("c")), last = Box().TestId("b"))
        );

        Assert.Equal(a, app.Find("a"));
        Assert.Contains("2 nodes", Assert.Throws<InvalidOperationException>(() => app.Find("b")).Message, StringComparison.Ordinal);
        Assert.Contains("No node", Assert.Throws<InvalidOperationException>(() => app.Find("z")).Message, StringComparison.Ordinal);
        Assert.Equal([inRow, last], app.FindAll("b"));
        Assert.Empty(app.FindAll("z"));
    }

    /// <summary>
    /// A test ID set again replaces the old one, and a node that is removed takes its test ID with it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a column holding a box "old" given the ID "new" after, and a box "shown" that a signal shows.
    ///
    /// Steps:
    /// - Find "old" and "new"; hide the box; pump; find all of "shown".
    ///
    /// Expected:
    /// - "old" finds nothing and "new" the box; after the box is hidden, "shown" finds nothing.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task TestId_Replaced_AndGoneWithItsNode()
    {
        var visible = Signal.Create(true);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Box().TestId("old").TestId("new"), Show(visible, () => Box().TestId("shown")))
        );
        Assert.Empty(app.FindAll("old"));
        _ = app.Find("new");
        _ = app.Find("shown");

        visible.Set(false);
        _ = app.PumpAndSettle();
        Assert.Empty(app.FindAll("shown"));
    }

    /// <summary>
    /// A click goes to the node at the center of the box it names, through hit testing, as the OS's mouse messages go.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a row of two boxes of 40 x 20, "left" and "right", each counting its clicks.
    ///
    /// Steps:
    /// - Click "right" twice and "left" once.
    ///
    /// Expected:
    /// - "right" counted 2 and "left" 1; the pointer is at the center of "left", (20, 10).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Click_GoesToTheNodeAtTheCenterOfTheBox()
    {
        int left = 0;
        int right = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Row(Box().Width(40).Height(20).TestId("left").OnClick(() => left++), Box().Width(40).Height(20).TestId("right").OnClick(() => right++))
        );

        app.Pointer.Click(app.Find("right"));
        app.Pointer.Click(app.Find("right"));
        app.Pointer.Click(app.Find("left"));
        Assert.Equal((1, 2), (left, right));
        Assert.Equal((20, 10), app.Pointer.Position);
        _ = app.PumpAndSettle();
    }

    /// <summary>
    /// The center of a node in a scrolled container is moved by the scroll position, so a click reaches the node that shows there.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a scroll view 100 high over a column of five boxes 100 high, "b0" to "b4", each counting its clicks; a scale of 2.
    ///
    /// Steps:
    /// - Scroll to 100; pump; click "b1".
    ///
    /// Expected:
    /// - The pointer is at (50, 50) logical, (100, 100) physical, and "b1" counted 1, no other box.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Click_InAScrolledContainer_ReachesTheNodeShownThere()
    {
        var clicks = new int[5];
        var state = new ScrollState();
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                var boxes = new Element[5];
                for (int i = 0; i < 5; i++)
                {
                    int index = i;
                    boxes[i] = Box().Width(100).Height(100).TestId($"b{i}").OnClick(() => clicks[index]++);
                }

                return ScrollView(Column(boxes), state).Width(100).Height(100);
            },
            new HeadlessOptions { Scale = 2 }
        );

        state.ScrollTo(new Vector2(0, 100));
        _ = app.PumpAndSettle();
        app.Pointer.Click(app.Find("b1"));
        Assert.Equal((100, 100), app.Pointer.Position);
        Assert.Equal([0, 1, 0, 0, 0], clicks);
    }

    /// <summary>
    /// A key goes to the focused node, and the window's activation moves the focus out and back in.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a focusable box "box" that records its key downs and its focus events.
    ///
    /// Steps:
    /// - Focus it; press Enter; make the window inactive, then active.
    ///
    /// Expected:
    /// - Focus returns true; the press reported handled is false, and the box saw Enter; the focus events are FocusIn, FocusOut,
    ///   FocusIn.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Keyboard_GoesToTheFocusedNode_AndActivationMovesTheFocus()
    {
        var keys = new List<Key>();
        var focus = new List<FocusEventKind>();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(10)
                .Height(10)
                .TestId("box")
                .Focusable()
                .On(KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent e) => keys.Add(e.Key))
                .On(FocusEventKind.FocusIn, EventPhase.Bubble, (in FocusEvent e) => focus.Add(e.Kind))
                .On(FocusEventKind.FocusOut, EventPhase.Bubble, (in FocusEvent e) => focus.Add(e.Kind))
        );

        Assert.True(app.Focus(app.Find("box")));
        Assert.False(app.Keyboard.Press(Key.Enter));
        app.SetActive(false);
        app.SetActive(true);
        _ = app.PumpAndSettle();
        Assert.Equal([Key.Enter], keys);
        Assert.Equal([FocusEventKind.FocusIn, FocusEventKind.FocusOut, FocusEventKind.FocusIn], focus);
    }

    /// <summary>
    /// The leak check passes for scopes the root owns, and names an effect that nothing owns.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two apps of a box: one with an effect made in its root function, one whose root function posts work to the UI queue that
    ///   makes an effect there, where no scope is current, so nothing owns it.
    ///
    /// Steps:
    /// - Check the first; pump the second and check it.
    ///
    /// Expected:
    /// - The first passes; the second throws <see cref="InvalidOperationException"/> naming one scope, an effect.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AssertNoLeakedScopes_OwnedPasses_AnUnownedEffectIsNamed()
    {
        await using (
            HeadlessApp owned = await HeadlessApp.StartAsync(() =>
            {
                _ = Effect.Create(() => { });
                return Box();
            })
        )
        {
            owned.AssertNoLeakedScopes();
        }

        Effect? leaked = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            SynchronizationContext.Current!.Post(_ => leaked = Effect.Create(() => { }, "Leaky"), null);
            return Box();
        });
        _ = app.PumpAndSettle();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(app.AssertNoLeakedScopes);
        Assert.Contains("1 scopes", error.Message, StringComparison.Ordinal);
        Assert.Contains("Leaky (effect)", error.Message, StringComparison.Ordinal);
        leaked!.Dispose();
    }

    /// <summary>
    /// An app starts only on the UI thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A test thread of xUnit's, not the UI thread.
    ///
    /// Steps:
    /// - Start an app.
    ///
    /// Expected:
    /// - It throws <see cref="InvalidOperationException"/> that names HeadlessSession.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task StartAsync_OffTheUiThread_Throws()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => HeadlessApp.StartAsync(() => Box()));
        Assert.Contains("HeadlessSession", error.Message, StringComparison.Ordinal);
    }
}
