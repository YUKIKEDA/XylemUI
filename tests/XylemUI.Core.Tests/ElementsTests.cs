using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The component API of row V1d (design §2.13): the elements of the current tree, the chain, the binding of signals, the scope that
// owns the elements and runs their handlers, and the draw functions' exceptions. The text elements are tested in XylemUI.Text.Tests,
// which loads HarfBuzz.
[Collection(UiThreadGroup.Name)]
public sealed class ElementsTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Blue = Color.FromRgb(0x3366CC);

    /// <summary>
    /// The elements are made only while a window's root function runs: outside it, and in a handler, which runs after it, they throw.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No tree is current; then a scene whose root function makes a box with a click handler that tries to make a box.
    ///
    /// Steps:
    /// - Call <c>Box</c>, <c>Column</c>, and <c>Row</c> with no tree current. Mount the scene and click the box.
    ///
    /// Expected:
    /// - Each call throws <see cref="InvalidOperationException"/>; the handler's call throws it too.
    /// </remarks>
    [Fact]
    public void Elements_OutsideTheRootFunction_Throw() =>
        ui.Invoke(() =>
        {
            Assert.Null(UiTree.Current);
            Assert.Throws<InvalidOperationException>(() => Box());
            Assert.Throws<InvalidOperationException>(() => Column());
            Assert.Throws<InvalidOperationException>(() => Row());

            var scene = new Scene();
            Exception? thrown = null;
            Element box = default;
            scene.Mount(() =>
                box = Box()
                    .Width(50)
                    .Height(50)
                    .OnClick(() =>
                    {
                        try
                        {
                            _ = Box();
                        }
                        catch (InvalidOperationException exception)
                        {
                            thrown = exception;
                        }
                    })
            );
            Assert.False(box.IsNone);
            scene.Click(10, 10);
            Assert.NotNull(thrown);
            scene.Dispose();
        });

    /// <summary>
    /// An element is made in the current scope, and an element made with no scope throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree set as the current tree by hand, with no current scope.
    ///
    /// Steps:
    /// - Call <c>Box</c>.
    ///
    /// Expected:
    /// - It throws <see cref="InvalidOperationException"/>, and the tree has no node.
    /// </remarks>
    [Fact]
    public void Elements_WithNoScope_Throw() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            UiTree.Current = tree;
            try
            {
                Assert.Throws<InvalidOperationException>(() => Box());
                Assert.Equal(0, tree.Nodes.Count);
            }
            finally
            {
                UiTree.Current = null;
            }
        });

    /// <summary>
    /// A column and a row take their children in the order given, as a flex column and a flex row; a box takes its one child; a
    /// column with no child is an empty container; a child of another tree or one that has a parent throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene, and a second tree with a box.
    ///
    /// Steps:
    /// - Make a column of three boxes, a row of two, a box of one, and an empty column. Then pass a child that has a parent, the
    ///   default element, and the second tree's box.
    ///
    /// Expected:
    /// - The children are in the order given and the algorithms are Flex column and row; the empty column has no child; each bad
    ///   child throws <see cref="ArgumentException"/>.
    /// </remarks>
    [Fact]
    public void Containers_TakeTheirChildrenInOrder_AndRefuseOthers() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var other = new Scene();
            Element foreign = default;
            other.Mount(() => foreign = Box());
            scene.Mount(() =>
            {
                Element a = Box();
                Element b = Box();
                Element c = Box();
                Element column = Column(a, b, c);
                Assert.Equal([a.Node, b.Node, c.Node], scene.ChildrenOf(column.Node));
                Assert.Equal(LayoutAlgorithm.Flex.Column, scene.Tree.Nodes.GetRef(column.Node).Layout.Style.Algorithm);

                Element d = Box();
                Element e = Box();
                Element row = Row(d, e);
                Assert.Equal([d.Node, e.Node], scene.ChildrenOf(row.Node));
                Assert.Equal(LayoutAlgorithm.Flex.Row, scene.Tree.Nodes.GetRef(row.Node).Layout.Style.Algorithm);

                Element inner = Box();
                Element box = Box(inner);
                Assert.Equal([inner.Node], scene.ChildrenOf(box.Node));
                Assert.Empty(scene.ChildrenOf(Column().Node));

                Assert.Throws<ArgumentException>(() => Column(a));
                Assert.Throws<ArgumentException>(() => Box(default(Element)));
                Assert.Throws<ArgumentException>(() => Row(foreign));
                return Column(column, row, box);
            });
            scene.Dispose();
            other.Dispose();
        });

    /// <summary>
    /// The chain writes each value to the node's style or look through the tree's setters and returns the same element; a gap on a
    /// node that is not a flex container and a size of text on a node that is not text throw.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene.
    ///
    /// Steps:
    /// - On a row: width 40, height 30, padding 4 and 2, gap 6, a border of 1 in red, the blue background, corner radius 3, opacity
    ///   0.5, the hand cursor, and the pointer blocked. Then call gap and size of text on a box.
    ///
    /// Expected:
    /// - The chain returns the row each time; the style has the sizes, the padding, the gap, and the border; the look has the
    ///   background, the border's paint, the radii, and the opacity; the input has the cursor and blocks the pointer. The two calls
    ///   on the box throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Chain_WritesTheNode_AndReturnsTheSameElement() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            scene.Mount(() =>
            {
                Element row = Row();
                Element same = row.Width(40)
                    .Height(30)
                    .Padding(4, 2)
                    .Gap(6)
                    .Border(1, new Paint(Red))
                    .Background(new Paint(Blue))
                    .CornerRadius(3)
                    .Opacity(0.5f)
                    .Cursor(CursorKind.Hand)
                    .BlocksPointer();
                Assert.Equal(row, same);
                ref UiNode node = ref scene.Tree.Nodes.GetRef(row.Node);
                LayoutStyle style = node.Layout.Style;
                Assert.Equal(Length.Px(40), style.Width);
                Assert.Equal(Length.Px(30), style.Height);
                Assert.Equal(LengthPercentage.Px(4), style.Padding.Left);
                Assert.Equal(LengthPercentage.Px(2), style.Padding.Top);
                var flex = (LayoutAlgorithm.Flex)style.Algorithm;
                Assert.Equal((LengthPercentage.Px(6), LengthPercentage.Px(6)), (flex.Props.RowGap, flex.Props.ColumnGap));
                Assert.Equal(LengthPercentage.Px(1), style.Border.Left);
                Assert.Equal(new Paint(Blue), node.Visual!.Background);
                Assert.Equal(new Paint(Red), node.Visual.BorderPaint);
                Assert.Equal(new CornerRadii(3), node.Visual.Radii);
                Assert.Equal(0.5f, node.Visual.Opacity);
                Assert.Equal(CursorKind.Hand, node.Input!.Cursor);
                Assert.True(node.Input.BlocksPointer);

                Element box = Box();
                Assert.Throws<InvalidOperationException>(() => box.Gap(1));
                Assert.Throws<InvalidOperationException>(() => box.FontSize(12));
                return Column(row, box);
            });
            scene.Dispose();
        });

    /// <summary>
    /// <c>Bind</c> writes the signal's value to the node when it is made, and once for each batch that changes the signal, however
    /// many times the batch set it; a value equal to the one written writes again through the property but changes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box bound to a signal of colors through a property that counts its writes and writes the background.
    ///
    /// Steps:
    /// - Mount. In one batch, set the signal to red, gray, then blue.
    ///
    /// Expected:
    /// - One write at the mount; one more after the batch, of blue, which the background has.
    /// </remarks>
    [Fact]
    public void Bind_OneBatch_WritesOnce() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var color = Signal.Create(Gray);
            var written = new List<Color>();
            var property = new NodeProperty<Color>(
                "Counted",
                (tree, node, value) =>
                {
                    written.Add(value);
                    tree.SetBackground(node, new Paint(value));
                }
            );
            Element box = default;
            scene.Mount(() => box = Box().Bind(property, color));
            Assert.Equal([Gray], written);

            ReactiveRuntime.Batch(() =>
            {
                color.Set(Red);
                color.Set(Gray);
                color.Set(Blue);
            });
            Assert.Equal([Gray, Blue], written);
            Assert.Equal(new Paint(Blue), scene.Tree.Nodes.GetRef(box.Node).Visual!.Background);
            scene.Dispose();
        });

    /// <summary>
    /// The overloads that take a signal bind it: the background and the color of text follow the signal, and the frame draws the new
    /// background.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of 50 x 50 whose background and foreground take signals.
    ///
    /// Steps:
    /// - Mount and draw. Set both signals; run the frame asked for.
    ///
    /// Expected:
    /// - The look has the new background and foreground; the frame presents.
    /// </remarks>
    [Fact]
    public void SignalOverloads_FollowTheSignal() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var background = Signal.Create(new Paint(Gray));
            var foreground = Signal.Create(Gray);
            Element box = default;
            scene.Mount(() => box = Box().Width(50).Height(50).Background(background).Foreground(foreground));
            int presents = scene.Target.Presents;

            background.Set(new Paint(Red));
            foreground.Set(Blue);
            scene.Scheduler.Run();
            NodeVisual visual = scene.Tree.Nodes.GetRef(box.Node).Visual!;
            Assert.Equal(new Paint(Red), visual.Background);
            Assert.Equal(Blue, visual.Foreground);
            Assert.Equal(presents + 1, scene.Target.Presents);
            scene.Dispose();
        });

    /// <summary>
    /// A click's handler runs in the scope it was registered in: what it makes belongs to that scope, so disposing the scope disposes
    /// it; after the scope is current again outside the handler.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box whose click handler reads the current scope and makes an effect that counts its runs on a signal.
    ///
    /// Steps:
    /// - Mount; click outside any scope. Set the signal. Dispose the root scope; set the signal again.
    ///
    /// Expected:
    /// - The handler saw the root scope; the scope is none again after the click. The effect ran once at the click and once for the
    ///   first set, and not after the dispose.
    /// </remarks>
    [Fact]
    public void Handler_RunsInTheScopeItWasRegisteredIn() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var signal = Signal.Create(0);
            ScopeHandle seen = default;
            int runs = 0;
            scene.Mount(() =>
                Box()
                    .Width(50)
                    .Height(50)
                    .OnClick(() =>
                    {
                        seen = Scope.Current;
                        _ = Effect.Create(() =>
                        {
                            _ = signal.Get();
                            runs++;
                        });
                    })
            );

            scene.Click(10, 10);
            Assert.Equal(scene.RootScope, seen);
            Assert.True(Scope.Current.IsNone);
            signal.Set(1);
            Assert.Equal(2, runs);

            scene.Dispose();
            signal.Set(2);
            Assert.Equal(2, runs);
        });

    /// <summary>
    /// A handler whose scope was disposed is not called: the one <c>OnClick</c> registered is removed with the scope, and one left on
    /// the node by the tree's own call is skipped by the delivery.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of the root scope. An inner scope adds a click handler with <c>OnClick</c> and one with the tree's
    ///   <c>AddPointerHandler</c>, which registers no cleanup.
    ///
    /// Steps:
    /// - Click; dispose the inner scope; click again.
    ///
    /// Expected:
    /// - The first click calls both; the second calls neither, and the node keeps one handler entry, the tree's.
    /// </remarks>
    [Fact]
    public void Handler_OfADisposedScope_IsNotCalled() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            int chained = 0;
            int direct = 0;
            ScopeHandle inner = default;
            scene.Mount(() =>
            {
                Element box = Box().Width(50).Height(50);
                inner = Scope.Create(() =>
                {
                    _ = box.OnClick(() => chained++);
                    scene.Tree.AddPointerHandler(box.Node, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => direct++);
                });
                return box;
            });

            scene.Click(10, 10);
            Assert.Equal((1, 1), (chained, direct));
            Scope.Dispose(inner);
            scene.Click(10, 10);
            Assert.Equal((1, 1), (chained, direct));
            scene.Dispose();
        });

    /// <summary>
    /// Disposing the root scope leaves nothing: the nodes the root function made are out of the tree and freed, with their handlers,
    /// and no effect or scope of it is alive.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scope tracker on. A column of a box with a click handler, a box bound to a signal, and an inner scope's box.
    ///
    /// Steps:
    /// - Mount and draw; read the nodes and the scopes. Dispose the root scope; run the frame asked for.
    ///
    /// Expected:
    /// - Before: the root and four nodes, and live scopes. After: the root alone with no child, no live scope of the mount, and the
    ///   frame draws the empty root.
    /// </remarks>
    [Fact]
    public void RootScope_Disposed_LeavesNothing() =>
        ui.Invoke(() =>
        {
            ScopeTracker.IsEnabled = true;
            try
            {
                int scopesBefore = LiveScopes();
                var scene = new Scene();
                var color = Signal.Create(new Paint(Gray));
                scene.Mount(() =>
                {
                    Element inner = default;
                    _ = Scope.Create(() => inner = Box());
                    return Column(Box().OnClick(() => { }), Box().Background(color), inner);
                });
                Assert.Equal(5, scene.Tree.Nodes.Count);
                Assert.True(LiveScopes() > scopesBefore);

                Scope.Dispose(scene.RootScope);
                Assert.Equal(1, scene.Tree.Nodes.Count);
                Assert.Empty(scene.ChildrenOf(scene.Root));
                Assert.Equal(scopesBefore, LiveScopes());
                int presents = scene.Target.Presents;
                scene.Scheduler.Run();
                Assert.Equal(presents + 1, scene.Target.Presents);
            }
            finally
            {
                ScopeTracker.IsEnabled = false;
            }
        });

    /// <summary>
    /// A draw function that throws is reported; the scopes it opened are closed, its node's children and siblings are drawn, and the
    /// frame presents (design §5.0, "The exceptions of draw functions").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a box whose draw function opens an opacity, fills a gray square in it, and throws, with a red child, and a blue
    ///   sibling; the reactive
    ///   runtime's handler records what it is given.
    ///
    /// Steps:
    /// - Mount and draw.
    ///
    /// Expected:
    /// - One exception reported, the one thrown; the frame presented; the list's scopes are all closed: the opacity with the gray
    ///   square, its pop, then the red child's rectangle and the blue sibling's.
    /// </remarks>
    [Fact]
    public void DrawFunction_Throws_IsReported_AndTheFrameGoesOn() =>
        ui.Invoke(() =>
        {
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                var thrown = new InvalidOperationException("thrown by the draw function");
                var scene = new Scene();
                scene.Mount(() =>
                    Column(
                        Box(Box().Width(10).Height(10).Background(new Paint(Red)))
                            .Draw(
                                (DrawingContext context, Rect box) =>
                                {
                                    _ = context.PushOpacity(0.5f);
                                    context.FillRect(0, 0, 5, 5, Gray);
                                    throw thrown;
                                }
                            ),
                        Box().Width(20).Height(20).Background(new Paint(Blue))
                    )
                );

                Assert.Equal([thrown], reported);
                Assert.Equal(1, scene.Target.Presents);
                Assert.Equal(0, scene.Frames.List.ScopeDepth);
                Assert.Equal(
                    [CommandTag.PushOpacity, CommandTag.FillRect, CommandTag.Pop, CommandTag.FillRect, CommandTag.FillRect],
                    Tags(scene.Frames.List, out List<Color> fills)
                );
                Assert.Equal([Gray, Red, Blue], fills);
                scene.Dispose();
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// The steady state of the component API allocates nothing: a frame that a signal changes the background in, and a click whose
    /// handler sets a signal, delivered with the enter of its scope.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A box of 50 x 50 whose background is bound to a signal and whose click handler sets it; warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: press and release on the box, then run the frame asked for.
    ///
    /// Expected:
    /// - 0 bytes in every window; each iteration clicked once and presented once.
    /// </remarks>
    [Fact]
    public void SteadyState_ClickThatChangesTheBackground_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            var red = new Paint(Red);
            var gray = new Paint(Gray);
            var background = Signal.Create(gray);
            int clicks = 0;
            scene.Mount(() =>
                Box()
                    .Width(50)
                    .Height(50)
                    .Background(background)
                    .OnClick(() =>
                    {
                        clicks++;
                        background.Set((clicks & 1) == 0 ? gray : red);
                    })
            );

            void Iteration(int index)
            {
                scene.Click(10, 10);
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            int presents = scene.Target.Presents;
            clicks = 0;
            SteadyStateAllocation.AssertNone(1_000, Iteration);

            // The helper's unmeasured window runs 100 iterations before the 1,000.
            Assert.Equal(1_100, clicks);
            Assert.Equal(presents + 1_100, scene.Target.Presents);
            scene.Dispose();
        });

    /// <summary>
    /// An element is a tree and a node: the default one is none, and two are equal when both are.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scene.
    ///
    /// Steps:
    /// - Make two boxes; compare them, and the first with itself and the default.
    ///
    /// Expected:
    /// - The default is none; a box equals itself and not the other box or the default.
    /// </remarks>
    [Fact]
    public void Element_EqualsByTreeAndNode() =>
        ui.Invoke(() =>
        {
            Assert.True(default(Element).IsNone);
            var scene = new Scene();
            scene.Mount(() =>
            {
                Element a = Box();
                Element b = Box();
                Element same = a;
                Assert.False(a.IsNone);
                Assert.True(a == same);
                Assert.True(a != b);
                Assert.NotEqual(default, a);
                Assert.Equal(a.GetHashCode(), same.GetHashCode());
                return Column(a, b);
            });
            scene.Dispose();
        });

    private static int LiveScopes()
    {
        int count = 0;
        ScopeTracker.ForEachActiveScope(_ => count++);
        return count;
    }

    // The tags of the list's commands in order, and the colors of its rectangles.
    private static List<CommandTag> Tags(DisplayList list, out List<Color> fills)
    {
        var tags = new List<CommandTag>();
        fills = [];
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            tags.Add(tag);
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

        return tags;
    }

    // A window of 200 x 100 logical pixels with no text: its tree, frames, and input, and the root scope of what it mounted.
    private sealed class Scene
    {
        public Scene()
        {
            Tree = new UiTree();
            Root = Tree.CreateNode(new LayoutStyle());
            Atlas = new GlyphAtlas(new NoGlyphs());
            Frames = new WindowFrames(Tree, Root, Target, Atlas, Scheduler, Gray);
            Input = new WindowInput(Tree, Frames, new NoPlatform());
        }

        public UiTree Tree { get; }

        public NodeHandle Root { get; }

        public GlyphAtlas Atlas { get; }

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        public WindowFrames Frames { get; }

        public WindowInput Input { get; }

        public ScopeHandle RootScope { get; private set; }

        // Runs the root function as the window's host does, puts its element in the root, and draws the first frame.
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

            Frames.Resize(new Size(200, 100), 1);
        }

        public void Dispose()
        {
            if (Scope.IsAlive(RootScope))
            {
                Scope.Dispose(RootScope);
            }

            Atlas.Dispose();
        }

        public List<NodeHandle> ChildrenOf(NodeHandle node)
        {
            var children = new List<NodeHandle>();
            for (NodeHandle child = Tree.Nodes.GetRef(node).FirstChild; !child.IsNone; child = Tree.Nodes.GetRef(child).NextSibling)
            {
                children.Add(child);
            }

            return children;
        }

        public void Click(int x, int y)
        {
            Input.Handle(
                new PointerInput(
                    PointerInputKind.Down,
                    x,
                    y,
                    PointerButton.Left,
                    PointerButtons.Left,
                    KeyModifiers.None,
                    1,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
            Input.Handle(
                new PointerInput(
                    PointerInputKind.Up,
                    x,
                    y,
                    PointerButton.Left,
                    PointerButtons.None,
                    KeyModifiers.None,
                    1,
                    TimeSpan.Zero,
                    PointerType.Mouse
                )
            );
        }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
