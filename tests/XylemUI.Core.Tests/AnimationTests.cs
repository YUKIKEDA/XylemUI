using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The animations of design §2.14 on a window's frames with no GPU, on the virtual clock: the curve, the animation that the caller
// moves, the frames asked for only while something runs, reduced motion, and the transitions of paint values. A frame here is run by
// hand at a time the test sets, as the window's frame clock runs it.
[Collection(UiThreadGroup.Name)]
public sealed class AnimationTests(UiThreadFixture ui)
{
    private static readonly Color Red = Color.FromRgb(0xFF0000);
    private static readonly Color Blue = Color.FromRgb(0x0000FF);
    private static readonly Color Gray = Color.FromRgb(0x808080);

    /// <summary>
    /// A curve maps 0 to 0 and 1 to 1, clamps outside them, and has the shape of its control points; an x outside 0 to 1 throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The linear curve, ease-in-out, ease-out, and a cubic Bézier of (0.25, 0.1, 0.25, 1).
    ///
    /// Steps:
    /// - Transform 0, 1, −1, 2, and 0.5 with each; make a curve with x1 = 1.5.
    ///
    /// Expected:
    /// - 0 → 0 and 1 → 1 for all, clamped outside; linear 0.5 → 0.5, ease-in-out 0.5 → 0.5 (it is symmetric), ease-out 0.5 above
    ///   0.5; CSS's ease at 0.5 is 0.8024 (the CSS reference value) within 0.001; x1 = 1.5 throws.
    /// </remarks>
    [Fact]
    public void Curve_MapsTheEnds_AndHasTheShapeOfItsPoints()
    {
        foreach (Curve curve in new[] { Curve.Linear, Curve.EaseInOut, Curve.EaseOut, Curve.Ease })
        {
            Assert.Equal(0, curve.Transform(0));
            Assert.Equal(1, curve.Transform(1));
            Assert.Equal(0, curve.Transform(-1));
            Assert.Equal(1, curve.Transform(2));
        }

        Assert.Equal(0.5f, Curve.Linear.Transform(0.5f));
        Assert.Equal(0.5f, Curve.EaseInOut.Transform(0.5f), 0.001f);
        Assert.True(Curve.EaseOut.Transform(0.5f) > 0.5f);
        Assert.Equal(0.8024f, Curve.CubicBezier(0.25f, 0.1f, 0.25f, 1).Transform(0.5f), 0.001f);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Curve.CubicBezier(1.5f, 0, 0.5f, 1));
    }

    /// <summary>
    /// A move starts on the next frame and follows the frames' time; the frames are asked for while it moves, and none after.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose root makes an animation of 100 ms, linear, at 0; the first frame drawn.
    ///
    /// Steps:
    /// - Animate to 1. Run frames at +0, +25, +50, and +100 ms; then run whatever frame is asked for.
    ///
    /// Expected:
    /// - Animating asks for a frame; the frames read 0 (the move's start), 0.25, 0.5, and 1; after the last no frame is asked for and
    ///   the animation is not animating.
    /// </remarks>
    [Fact]
    public void AnimateTo_MovesOnTheFrames_AndAsksForFramesOnlyWhileItMoves() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Animation animation = null!;
            scene.Mount(() =>
            {
                animation = Animation.Create(TimeSpan.FromMilliseconds(100));
                return Box();
            });

            animation.AnimateTo(1);
            Assert.True(scene.Scheduler.Pending);
            Assert.True(animation.IsAnimating);
            float[] read = new float[4];
            int[] at = [0, 25, 25, 50];
            for (int i = 0; i < 4; i++)
            {
                scene.Frame(at[i]);
                read[i] = animation.Value.Get();
            }

            Assert.Equal([0, 0.25f, 0.5f, 1], read);
            Assert.False(animation.IsAnimating);
            Assert.False(scene.Scheduler.Pending);
        });

    /// <summary>
    /// Forward from halfway takes half the duration, as the distance left is half; reverse from there takes the other half.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 100 ms, linear, at 0.5.
    ///
    /// Steps:
    /// - Forward; frames at +0 and +50 ms. Reverse; frames at +0 and +50 and +50 ms.
    ///
    /// Expected:
    /// - Forward reaches 1 at +50 ms; reverse is at 0.5 after 50 ms of its 100 and at 0 after 100.
    /// </remarks>
    [Fact]
    public void ForwardAndReverse_TakeThePartOfTheDurationLeft() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Animation animation = null!;
            scene.Mount(() =>
            {
                animation = Animation.Create(TimeSpan.FromMilliseconds(100), value: 0.5f);
                return Box();
            });

            animation.Forward();
            scene.Frame(0);
            scene.Frame(50);
            Assert.Equal(1, animation.Value.Get());
            Assert.False(animation.IsAnimating);

            animation.Reverse();
            scene.Frame(0);
            scene.Frame(50);
            Assert.Equal(0.5f, animation.Value.Get(), 0.0001f);
            scene.Frame(50);
            Assert.Equal(0, animation.Value.Get());
        });

    /// <summary>
    /// Setting the value stops the move and sets it at once; stopping keeps the value where it is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 100 ms, linear, moving to 1 and read at 0.3 after 30 ms.
    ///
    /// Steps:
    /// - Stop; run a frame 30 ms later. Animate to 1 again, then set 0.8 before the next frame.
    ///
    /// Expected:
    /// - After the stop the value stays 0.3 and no frame is asked for; after the set it is 0.8 at once, not animating.
    /// </remarks>
    [Fact]
    public void StopAndSet_EndTheMove() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Animation animation = null!;
            scene.Mount(() =>
            {
                animation = Animation.Create(TimeSpan.FromMilliseconds(100));
                return Box();
            });

            animation.AnimateTo(1);
            scene.Frame(0);
            scene.Frame(30);
            Assert.Equal(0.3f, animation.Value.Get(), 0.0001f);
            animation.Stop();
            scene.Frame(30);
            Assert.Equal(0.3f, animation.Value.Get(), 0.0001f);
            Assert.False(scene.Scheduler.Pending);

            animation.AnimateTo(1);
            animation.Set(0.8f);
            Assert.Equal(0.8f, animation.Value.Get());
            Assert.False(animation.IsAnimating);
        });

    /// <summary>
    /// A fling's spring settles at its end; a new target from the spring keeps the speed of now, so the value goes on the way it was
    /// going before it turns.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation at 0, flung at 5 a second (to 1) with the default spring.
    ///
    /// Steps:
    /// - Run frames every 16 ms until it rests; then fling again at 5 a second from 0 (set first), run two frames, spring to 0,
    ///   and run one frame.
    ///
    /// Expected:
    /// - The first fling rests at 1. After the turn, the frame's value is above the value before it: the speed toward 1 carried.
    /// </remarks>
    [Fact]
    public void Spring_SettlesAtItsEnd_AndCarriesItsSpeedToANewTarget() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Animation animation = null!;
            scene.Mount(() =>
            {
                animation = Animation.Create(TimeSpan.FromMilliseconds(100));
                return Box();
            });

            animation.Fling(5);
            for (int i = 0; i < 200 && animation.IsAnimating; i++)
            {
                scene.Frame(16);
            }

            Assert.False(animation.IsAnimating);
            Assert.Equal(1, animation.Value.Get());

            animation.Set(0);
            animation.Fling(5);
            scene.Frame(0);
            scene.Frame(16);
            scene.Frame(16);
            float before = animation.Value.Get();
            animation.SpringTo(0);
            scene.Frame(16);
            Assert.True(animation.Value.Get() > before);
        });

    /// <summary>
    /// When the OS asks for less motion, a move and a fling jump to their ends and ask for no frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose animations' reduce motion is on; an animation of 100 ms at 0.
    ///
    /// Steps:
    /// - Animate to 1; then fling at −3.
    ///
    /// Expected:
    /// - The value is 1 at once, then 0 at once; it is never animating and no frame is asked for.
    /// </remarks>
    [Fact]
    public void ReducedMotion_JumpsToTheEnd() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            scene.Tree.Animations.ReduceMotion = true;
            Animation animation = null!;
            scene.Mount(() =>
            {
                animation = Animation.Create(TimeSpan.FromMilliseconds(100));
                return Box();
            });

            animation.AnimateTo(1);
            Assert.Equal(1, animation.Value.Get());
            animation.Fling(-3);
            Assert.Equal(0, animation.Value.Get());
            Assert.False(animation.IsAnimating);
            Assert.False(scene.Scheduler.Pending);
        });

    /// <summary>
    /// An animation stops when the scope that made it is disposed, and a value bound to a property moves the property.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Show whose part makes an animation of 100 ms and binds its value to the part's opacity; the condition true.
    ///
    /// Steps:
    /// - Animate to 1 from 0; run frames at +0 and +50 ms; read the part's opacity. Make the condition false.
    ///
    /// Expected:
    /// - The opacity is 0.5 at +50 ms; after the part is gone the animation is not animating and no frame is asked for.
    /// </remarks>
    [Fact]
    public void Animation_BoundToAProperty_MovesIt_AndStopsWithItsScope() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var shown = Signal.Create(true);
            Animation animation = null!;
            Element part = default;
            scene.Mount(() =>
                Column(
                    Show(
                        shown,
                        () =>
                        {
                            animation = Animation.Create(TimeSpan.FromMilliseconds(100));
                            return part = Box().Width(10).Height(10).Bind(NodeProperties.Opacity, animation.Value);
                        }
                    )
                )
            );

            animation.AnimateTo(1);
            scene.Frame(0);
            scene.Frame(50);
            Assert.Equal(0.5f, scene.Tree.Nodes.GetRef(part.Node).Visual!.Opacity, 0.0001f);

            shown.Set(false);
            Assert.False(animation.IsAnimating);
            scene.Frame(16);
            Assert.False(scene.Scheduler.Pending);
        });

    /// <summary>
    /// A transition draws the first value as it is; a change moves from the value on the screen along the curve, and a new target
    /// while it moves starts the duration again from there.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of 10 x 10 whose background is a signal at red, with a transition of the background, 100 ms, linear; drawn once.
    ///
    /// Steps:
    /// - Set blue; frames at +0, +50. Set gray; frames at +0, +50, +50.
    ///
    /// Expected:
    /// - The first frame drew red. At +0 after blue the fill is red, at +50 it is halfway (0x80, 0, 0x80). After gray it moves from
    ///   that halfway color: at +50 of the new 100 ms it is halfway between them, and at +100 it is gray; then no frame is asked for.
    /// </remarks>
    [Fact]
    public void Transition_MovesFromTheShownValue_AndRestartsOnANewTarget() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var paint = Signal.Create<Paint>(Red);
            scene.Mount(() => Box().Width(10).Height(10).Background(paint).Transition(NodeProperties.Background, TimeSpan.FromMilliseconds(100)));
            Assert.Equal([Red], scene.Fills());

            paint.Set(Blue);
            scene.Frame(0);
            Assert.Equal([Red], scene.Fills());
            scene.Frame(50);
            var half = new Color(0x80, 0, 0x80);
            Assert.Equal([half], scene.Fills());

            paint.Set(Gray);
            scene.Frame(0);
            Assert.Equal([half], scene.Fills());
            scene.Frame(50);
            Assert.Equal([new Color(0x80, 0x40, 0x80)], scene.Fills());
            scene.Frame(50);
            Assert.Equal([Gray], scene.Fills());
            scene.Frame(16);
            Assert.False(scene.Scheduler.Pending);
        });

    /// <summary>
    /// A transition follows a pseudo-state's look too: the hovered background comes in over the duration and goes back the same way.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of 40 x 40 with a red background, blue when hovered, and a transition of the background, 100 ms, linear.
    ///
    /// Steps:
    /// - Move the pointer onto the box; frames at +0 and +50. Move it off; frames at +0 and +100.
    ///
    /// Expected:
    /// - At +50 on the box the fill is halfway between red and blue; after leaving, it is back at red after 100 ms.
    /// </remarks>
    [Fact]
    public void Transition_FollowsAPseudoStatesLook() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            scene.Mount(() =>
                Box()
                    .Width(40)
                    .Height(40)
                    .Background(new Paint(Red))
                    .Hovered(new PaintStyle { Background = Blue })
                    .Transition(NodeProperties.Background, TimeSpan.FromMilliseconds(100))
            );

            scene.Move(20, 20);
            scene.Frame(0);
            scene.Frame(50);
            Assert.Equal([new Color(0x80, 0, 0x80)], scene.Fills());

            scene.Move(150, 80);
            scene.Frame(0);
            scene.Frame(100);
            Assert.Equal([Red], scene.Fills());
        });

    /// <summary>
    /// Only paint values transition: a value that changes the layout or no look throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window being mounted.
    ///
    /// Steps:
    /// - Give a box a transition of <see cref="NodeProperties.Disabled"/>, and one of <see cref="NodeProperties.Text"/>.
    ///
    /// Expected:
    /// - Both throw <see cref="ArgumentException"/>.
    /// </remarks>
    [Fact]
    public void Transition_OfANonPaintValue_Throws() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            scene.Mount(() =>
            {
                _ = Assert.Throws<ArgumentException>(() => Box().Transition(NodeProperties.Disabled, TimeSpan.FromMilliseconds(10)));
                _ = Assert.Throws<ArgumentException>(() => Box().Transition(NodeProperties.Text, TimeSpan.FromMilliseconds(10)));
                return Box();
            });
        });

    /// <summary>
    /// With less motion, a transition jumps to the new value.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Reduce motion on; a box whose background is a signal at red with a transition of 100 ms.
    ///
    /// Steps:
    /// - Set blue; run the frame asked for.
    ///
    /// Expected:
    /// - The fill is blue in that frame, and no frame is asked for after it.
    /// </remarks>
    [Fact]
    public void Transition_WithReducedMotion_Jumps() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            scene.Tree.Animations.ReduceMotion = true;
            var paint = Signal.Create<Paint>(Red);
            scene.Mount(() => Box().Width(10).Height(10).Background(paint).Transition(NodeProperties.Background, TimeSpan.FromMilliseconds(100)));

            paint.Set(Blue);
            scene.Frame(16);
            Assert.Equal([Blue], scene.Fills());
            Assert.False(scene.Scheduler.Pending);
        });

    /// <summary>
    /// A frame that moves one animation and one transition allocates nothing (design §11.3, the Zero-Alloc scenario 5).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box whose opacity is bound to an animation of an hour moving to 1, and whose background is a signal with a transition of
    ///   an hour; the background set once so that its transition runs. Warmed up with 20 frames.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: advance the clock by 16 ms and run the frame.
    ///
    /// Expected:
    /// - 0 bytes in every window; both still run after them.
    /// </remarks>
    [Fact]
    public void SteadyState_RunningAnimationAndTransition_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var paint = Signal.Create<Paint>(Red);
            Animation animation = null!;
            scene.Mount(() =>
            {
                animation = Animation.Create(TimeSpan.FromHours(1));
                return Box()
                    .Width(10)
                    .Height(10)
                    .Background(paint)
                    .Transition(NodeProperties.Background, TimeSpan.FromHours(1))
                    .Bind(NodeProperties.Opacity, animation.Value);
            });

            animation.AnimateTo(1);
            paint.Set(Blue);
            for (int i = 0; i < 20; i++)
            {
                scene.Frame(16);
            }

            _ = PerformanceAssert.DoesNotAllocate(1_000, _ => scene.Frame(16));
            Assert.True(animation.IsAnimating);
            Assert.Equal(1, scene.Tree.Animations.Count - 1);
        });

    /// <summary>
    /// With a motion, the part shown first does not enter; a false condition makes the part leave over the duration, paused, before
    /// the fallback enters from nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Show with a fade of 100 ms (linear), a red box as the part, and a blue box as the fallback; the part has an effect that
    ///   counts its runs and reads a signal.
    ///
    /// Steps:
    /// - Read the first frame. Make the condition false; frames at +0 and +50; set the signal the part's effect reads. Frames at +50
    ///   (the exit's end, the swap) and +50.
    ///
    /// Expected:
    /// - The first frame draws red with no opacity (it did not enter). At +50 the red box is drawn at opacity 0.5 and its effect did
    ///   not run for the signal (its scope is paused). After 100 ms the blue box is there, entering: at +50 of its entry it is drawn
    ///   at opacity 0.5.
    /// </remarks>
    [Fact]
    public void ShowMotion_TheShownPartLeavesPaused_ThenTheOtherEnters() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var shown = Signal.Create(true);
            var watched = Signal.Create(0);
            int runs = 0;
            scene.Mount(() =>
                Column(
                    Show(
                        shown,
                        () =>
                        {
                            _ = Effect.Create(() =>
                            {
                                _ = watched.Get();
                                runs++;
                            });
                            return Box().Width(10).Height(10).Background(new Paint(Red));
                        },
                        () => Box().Width(10).Height(10).Background(new Paint(Blue)),
                        ShowMotion.Fade(TimeSpan.FromMilliseconds(100), Curve.Linear)
                    )
                )
            );
            Assert.Equal([Red], scene.Fills());
            Assert.Empty(scene.Opacities());

            shown.Set(false);
            scene.Frame(0);
            scene.Frame(50);
            Assert.Equal([Red], scene.Fills());
            Assert.Equal([0.5f], scene.Opacities());
            int before = runs;
            watched.Set(1);
            Assert.Equal(before, runs);

            scene.Frame(50);
            scene.Frame(50);
            Assert.Equal([Blue], scene.Fills());
            Assert.Equal([0.5f], scene.Opacities());
        });

    /// <summary>
    /// A condition that comes back while the part leaves turns its exit into an entry, from where it is, with the same node; the
    /// effects the pause held run then.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Show with a fade of 100 ms (linear) and a red box with a counting effect as the part; no fallback.
    ///
    /// Steps:
    /// - Make the condition false; frames at +0 and +60 (presence 0.4); set the signal the effect reads; make the condition true;
    ///   frames at +0 and +30.
    ///
    /// Expected:
    /// - The Show's node is the same node all along; the effect ran once when the condition came back (the change it held); at
    ///   +30 of the way back the box is at opacity 0.7 (0.4 + 0.3 of the way of 100 ms).
    /// </remarks>
    [Fact]
    public void ShowMotion_AConditionBackMidExit_EntersAgainFromWhereItIs() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var shown = Signal.Create(true);
            var watched = Signal.Create(0);
            int runs = 0;
            scene.Mount(() =>
                Column(
                    Show(
                        shown,
                        () =>
                        {
                            _ = Effect.Create(() =>
                            {
                                _ = watched.Get();
                                runs++;
                            });
                            return Box().Width(10).Height(10).Background(new Paint(Red));
                        },
                        motion: ShowMotion.Fade(TimeSpan.FromMilliseconds(100), Curve.Linear)
                    )
                )
            );
            NodeHandle node = scene.Tree.Nodes.GetRef(scene.Tree.Nodes.GetRef(scene.Root).FirstChild).FirstChild;

            shown.Set(false);
            scene.Frame(0);
            scene.Frame(60);
            Assert.Equal(0.4f, scene.Opacities()[0], 0.001f);
            int before = runs;
            watched.Set(1);
            shown.Set(true);
            Assert.Equal(before + 1, runs);
            scene.Frame(0);
            scene.Frame(30);
            Assert.Equal(node, scene.Tree.Nodes.GetRef(scene.Tree.Nodes.GetRef(scene.Root).FirstChild).FirstChild);
            Assert.Equal(0.7f, scene.Opacities()[0], 0.001f);
        });

    /// <summary>
    /// With less motion, a Show changes its part at once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Reduce motion on; a Show with a fade of 100 ms, a red part, and a blue fallback.
    ///
    /// Steps:
    /// - Make the condition false; run the frame asked for.
    ///
    /// Expected:
    /// - The blue box is drawn with no opacity, and no frame is asked for after it.
    /// </remarks>
    [Fact]
    public void ShowMotion_WithReducedMotion_ChangesAtOnce() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            scene.Tree.Animations.ReduceMotion = true;
            var shown = Signal.Create(true);
            scene.Mount(() =>
                Column(
                    Show(
                        shown,
                        () => Box().Width(10).Height(10).Background(new Paint(Red)),
                        () => Box().Width(10).Height(10).Background(new Paint(Blue)),
                        ShowMotion.Fade(TimeSpan.FromMilliseconds(100))
                    )
                )
            );

            shown.Set(false);
            scene.Frame(16);
            Assert.Equal([Blue], scene.Fills());
            Assert.Empty(scene.Opacities());
            Assert.False(scene.Scheduler.Pending);
        });

    // A window of 200 x 100 logical pixels with no text, on a virtual clock: a frame runs at a time the test moves the clock to.
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            Tree = new UiTree();
            Root = Tree.CreateNode(new LayoutStyle());
            Atlas = new GlyphAtlas(new NoGlyphs());
            Frames = new WindowFrames(Tree, Root, new CountingTarget(), Atlas, Scheduler, Gray, time: Time);
            Input = new WindowInput(Tree, Frames, new NoPointerPlatform());
            Input.SetActive(true);
        }

        public UiTree Tree { get; }

        public NodeHandle Root { get; }

        public GlyphAtlas Atlas { get; }

        public TestTime Time { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public WindowFrames Frames { get; }

        public WindowInput Input { get; }

        private ScopeHandle RootScope { get; set; }

        public void Mount(Func<Element> root)
        {
            UiTree.Current = Tree;
            try
            {
                RootScope = Scope.Create(() => Tree.AppendChild(Root, root().Node));
            }
            finally
            {
                UiTree.Current = null;
            }

            Scheduler.CancelFrame();
            Frames.Resize(new Size(200, 100), 1);
        }

        // Moves the clock and runs the frame asked for, if one is.
        public void Frame(int milliseconds)
        {
            Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            if (Scheduler.Pending)
            {
                Scheduler.Run();
            }
        }

        // The colors of the rectangles the last frame drew.
        public List<Color> Fills()
        {
            var fills = new List<Color>();
            DisplayListReader reader = Frames.List.GetReader();
            while (reader.TryNext(out CommandTag tag))
            {
                switch (tag)
                {
                    case CommandTag.FillRect:
                        fills.Add(reader.ReadFillRect().Color);
                        break;
                    case CommandTag.PushOpacity:
                        _ = reader.ReadGroup();
                        break;
                    case CommandTag.Pop:
                        reader.ReadPop();
                        break;
                    default:
                        throw new InvalidOperationException($"The test reads no {tag}.");
                }
            }

            return fills;
        }

        // The opacities of the groups the last frame pushed, in order.
        public List<float> Opacities()
        {
            var alphas = new List<float>();
            DisplayListReader reader = Frames.List.GetReader();
            while (reader.TryNext(out CommandTag tag))
            {
                switch (tag)
                {
                    case CommandTag.FillRect:
                        _ = reader.ReadFillRect();
                        break;
                    case CommandTag.PushOpacity:
                        alphas.Add(reader.ReadGroup().Alpha);
                        break;
                    case CommandTag.Pop:
                        reader.ReadPop();
                        break;
                    default:
                        throw new InvalidOperationException($"The test reads no {tag}.");
                }
            }

            return alphas;
        }

        public void Move(int x, int y) =>
            Input.Handle(
                new PointerInput(
                    PointerInputKind.Move,
                    x,
                    y,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    Time.Elapsed,
                    PointerType.Mouse
                )
            );

        public void Dispose()
        {
            if (Scope.IsAlive(RootScope))
            {
                Scope.Dispose(RootScope);
            }

            Atlas.Dispose();
        }
    }

    private sealed class NoPointerPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
