using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// Show and For (row V3a, design §2.7, "The shape of V3"): the parts and rows in place with no box around them, their scopes, the
// diff's moves, and the costs: no sibling walk when For removes rows, and no allocation when rows only move or change value.
[Collection(UiThreadGroup.Name)]
public sealed class ShowForTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);

    /// <summary>
    /// Show puts its part in the parent's place itself, with no box around it: the true part, then an empty hidden node, then a new
    /// true part made again; a value of the same truth changes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a box, a Show of a box 10 high (its render counts), and a box. The condition is true.
    ///
    /// Steps:
    /// - Read the column's children. Set the condition false; read them. Set it false again. Set it true; read them.
    ///
    /// Expected:
    /// - The Show's place holds its part's node first; then a hidden node with no child; then a node other than the first part's,
    ///   and the render ran twice. The first part's node is freed. The column has three children each time.
    /// </remarks>
    [Fact]
    public void Show_SwapsThePartInPlace_AndMakesItAgainOnEachTrue() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var when = Signal.Create(true);
            int renders = 0;
            Element column = default;
            scene.Mount(() =>
                column = Column(
                    Box(),
                    Show(
                        when,
                        () =>
                        {
                            renders++;
                            return Box().Height(10);
                        }
                    ),
                    Box()
                )
            );

            NodeHandle first = scene.ChildrenOf(column.Node)[1];
            when.Set(false);
            List<NodeHandle> hidden = scene.ChildrenOf(column.Node);
            (bool Hidden, bool Empty, bool FirstAlive) placeholder = (
                scene.Tree.Nodes.GetRef(hidden[1]).Layout.Style.Hidden,
                scene.Tree.Nodes.GetRef(hidden[1]).FirstChild.IsNone,
                scene.Tree.Nodes.IsAlive(first)
            );
            when.Set(false);
            when.Set(true);
            List<NodeHandle> again = scene.ChildrenOf(column.Node);

            Assert.Equal(3, hidden.Count);
            Assert.Equal((true, true, false), placeholder);
            Assert.False(scene.Tree.Nodes.IsAlive(hidden[1]));
            Assert.Equal(3, again.Count);
            Assert.NotEqual(first, again[1]);
            Assert.False(scene.Tree.Nodes.GetRef(again[1]).Layout.Style.Hidden);
            Assert.Equal(2, renders);
        });

    /// <summary>
    /// The fallback takes the false state's place, and a part's scope goes with it: its effects stop and its nodes are freed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column holding a Show whose true part makes an effect that counts the reads of a signal, and whose fallback is a box 20 high.
    ///
    /// Steps:
    /// - Set the condition false; read the column's child. Set the counted signal.
    ///
    /// Expected:
    /// - The child is the fallback's box (20 high, not hidden); the effect ran once (when it was made) and not after the signal changed.
    /// </remarks>
    [Fact]
    public void Show_Fallback_TakesThePlace_AndThePartsScopeGoes() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var when = Signal.Create(true);
            var read = Signal.Create(0);
            int runs = 0;
            Element column = default;
            scene.Mount(() =>
                column = Column(
                    Show(
                        when,
                        () =>
                        {
                            _ = Effect.Create(() =>
                            {
                                _ = read.Get();
                                runs++;
                            });
                            return Box();
                        },
                        fallback: () => Box().Height(20)
                    )
                )
            );

            when.Set(false);
            NodeHandle child = Assert.Single(scene.ChildrenOf(column.Node));
            read.Set(1);

            Assert.Equal(Length.Px(20), scene.Tree.Nodes.GetRef(child).Layout.Style.Height);
            Assert.False(scene.Tree.Nodes.GetRef(child).Layout.Style.Hidden);
            Assert.Equal(1, runs);
        });

    /// <summary>
    /// A condition changed before Show's node has a parent is reported, and the part stays: the node the caller holds is the one it
    /// puts in a parent.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root function that makes a Show (true), sets its condition false, and then puts the Show's node in a column. A handler of
    ///   unhandled exceptions that records them.
    ///
    /// Steps:
    /// - Mount the root function.
    ///
    /// Expected:
    /// - One <see cref="InvalidOperationException"/> reported; the column's child is the true part's node, alive.
    /// </remarks>
    [Fact]
    public void Show_ChangedBeforeAParent_IsReported() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var when = Signal.Create(true);
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            Element shown = default;
            Element column = default;
            try
            {
                scene.Mount(() =>
                {
                    shown = Show(when, () => Box());
                    when.Set(false);
                    return column = Column(shown);
                });
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }

            Assert.IsType<InvalidOperationException>(Assert.Single(reported));
            Assert.Equal(shown.Node, Assert.Single(scene.ChildrenOf(column.Node)));
            Assert.True(scene.Tree.Nodes.IsAlive(shown.Node));
        });

    /// <summary>
    /// For makes a row for each item in order; a key that stays keeps its row and takes the new value through its signal; a key that
    /// comes makes a row; a key that goes frees its row.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column filled by For over items (1, "a"), (2, "b"), (3, "c") keyed by the number; each row records its value signal.
    ///
    /// Steps:
    /// - Read the rows. Set the items to (1, "a"), (3, "C"), (4, "d").
    ///
    /// Expected:
    /// - Three rows first, made three times. After the change: rows of 1 and 3 are the same nodes, the row of 2 is freed, a row of 4
    ///   is made (four makes), and the signal of 3's row has "C". The rows are in the order 1, 3, 4.
    /// </remarks>
    [Fact]
    public void For_KeepsRowsByKey_AndWritesTheNewValue() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var items = Signal.Create<IReadOnlyList<Item>>([new(1, "a"), new(2, "b"), new(3, "c")]);
            var made = new Dictionary<int, IReadOnlySignal<Item>>();
            int makes = 0;
            Element column = default;
            scene.Mount(() =>
                column = Column()
                    .For(
                        items,
                        item => item.Id,
                        item =>
                        {
                            makes++;
                            made[item.Get().Id] = item;
                            return Box().Height(10);
                        }
                    )
            );
            List<NodeHandle> before = scene.ChildrenOf(column.Node);

            items.Set([new(1, "a"), new(3, "C"), new(4, "d")]);
            List<NodeHandle> after = scene.ChildrenOf(column.Node);

            Assert.Equal(3, before.Count);
            Assert.Equal([before[0], before[2]], after.Take(2));
            Assert.False(scene.Tree.Nodes.IsAlive(before[1]));
            Assert.Equal(3, after.Count);
            Assert.Equal(4, makes);
            Assert.Equal("C", made[3].Get().Name);
        });

    /// <summary>
    /// Rows that change places move only when they are off the longest increasing subsequence of their old places, and a moved row
    /// keeps its node and its focus; the place signals follow.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column filled by For (the overload with places) over the keys A, B, C, D; the row of A is focusable and focused; the window
    ///   is active. A hook counts the moves.
    ///
    /// Steps:
    /// - Set the keys to B, C, D, A. Then to D, C, B, A (the reverse).
    ///
    /// Expected:
    /// - One move first (A), with A still focused and no focus-out; the places of A and B are 3 and 0. Then two moves: the old
    ///   places of D, C, B, A are 2, 1, 0, 3, whose longest increasing subsequence has two. The rows are in the order of the keys each time.
    /// </remarks>
    [Fact]
    public void For_MovesOnlyRowsOffTheLongestIncreasingSubsequence() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var items = Signal.Create<IReadOnlyList<string>>(["A", "B", "C", "D"]);
            var rows = new Dictionary<string, NodeHandle>();
            var places = new Dictionary<string, IReadOnlySignal<int>>();
            int focusOuts = 0;
            Element column = default;
            scene.Mount(() =>
                column = Column()
                    .For(
                        items,
                        key => key,
                        (item, place) =>
                        {
                            Element row = Box().Height(10);
                            if (item.Get() == "A")
                            {
                                _ = row.Focusable().On(FocusEventKind.FocusOut, EventPhase.Bubble, (in FocusEvent _) => focusOuts++);
                            }

                            rows[item.Get()] = row.Node;
                            places[item.Get()] = place;
                            return row;
                        }
                    )
            );
            scene.Input.SetActive(true);
            Assert.True(scene.Tree.Focus.Focus(rows["A"]));
            int moves = 0;
            scene.Tree.RowMovedHook = _ => moves++;

            items.Set(["B", "C", "D", "A"]);
            int firstMoves = moves;
            bool focused = scene.Tree.Focus.Focused == rows["A"];
            List<NodeHandle> first = scene.ChildrenOf(column.Node);
            (int A, int B) firstPlaces = (places["A"].Get(), places["B"].Get());
            moves = 0;
            items.Set(["D", "C", "B", "A"]);

            Assert.Equal(1, firstMoves);
            Assert.True(focused);
            Assert.Equal(0, focusOuts);
            Assert.Equal([rows["B"], rows["C"], rows["D"], rows["A"]], first);
            Assert.Equal((3, 0), firstPlaces);
            Assert.Equal(2, moves);
            Assert.Equal([rows["D"], rows["C"], rows["B"], rows["A"]], scene.ChildrenOf(column.Node));
        });

    /// <summary>
    /// A key given twice maps to the rows of that key in order, and a mix of a move, a removal, and a new row at the front keeps the
    /// rest in place.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column filled by For over the keys x, x, y; each row records its node in the order made.
    ///
    /// Steps:
    /// - Set the keys to z, x, y, x.
    ///
    /// Expected:
    /// - The rows are: a new row (z), the first x's row, y's row, the second x's row; four rows were made in all.
    /// </remarks>
    [Fact]
    public void For_RepeatedKeys_MapInOrder() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var items = Signal.Create<IReadOnlyList<string>>(["x", "x", "y"]);
            var made = new List<NodeHandle>();
            Element column = default;
            scene.Mount(() =>
                column = Column()
                    .For(
                        items,
                        key => key,
                        _ =>
                        {
                            Element row = Box();
                            made.Add(row.Node);
                            return row;
                        }
                    )
            );

            items.Set(["z", "x", "y", "x"]);

            Assert.Equal(4, made.Count);
            Assert.Equal([made[3], made[0], made[2], made[1]], scene.ChildrenOf(column.Node));
        });

    /// <summary>
    /// A removed row's scope goes with what it made: its nodes, its handlers, and its effects; and removing rows one by one, and
    /// disposing the whole list, walk no siblings.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column filled by For over 10,000 numbers; each row is a box with a click handler and an effect that counts the reads of a
    ///   shared signal. The live nodes and scopes are counted after mounting.
    ///
    /// Steps:
    /// - Remove the items one at a time from the front, 500 times; set the shared signal. Then dispose the root scope.
    ///
    /// Expected:
    /// - 500 fewer live nodes and 1,000 fewer scopes (each row's own and its effect's); the effect ran for each of the 9,500 rows
    ///   left, and not for the removed ones; no sibling was walked, in the removals or in the disposal, so a removal's work in the
    ///   tree does not grow with the rows.
    /// </remarks>
    [Fact]
    public void For_RemovedRows_LeaveNothing_AndWalkNoSiblings() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var items = Signal.Create<IReadOnlyList<int>>(Enumerable.Range(0, 10_000).ToList());
            var shared = Signal.Create(0);
            int runs = 0;
            scene.Mount(() =>
                Column()
                    .For(
                        items,
                        n => n,
                        row =>
                        {
                            _ = Effect.Create(() =>
                            {
                                _ = shared.Get();
                                runs++;
                            });
                            return Box().OnClick(() => { });
                        }
                    )
            );
            int nodes = scene.Tree.Nodes.Count;
            int scopes = Scope.LiveCount;
            long walks = scene.Tree.SiblingWalks;

            for (int i = 1; i <= 500; i++)
            {
                items.Set(Enumerable.Range(i, 10_000 - i).ToList());
            }

            runs = 0;
            shared.Set(1);
            (int Nodes, int Scopes, int Runs, long Walks) afterRemovals = (scene.Tree.Nodes.Count, Scope.LiveCount, runs, scene.Tree.SiblingWalks);
            Scope.Dispose(scene.RootScope);

            Assert.Equal((nodes - 500, scopes - 1_000, 9_500, walks), afterRemovals);
            Assert.Equal(walks, scene.Tree.SiblingWalks);
        });

    /// <summary>
    /// A focused row that is removed, and a focused part that Show hides, lose the focus with a focus-out while they are still in the
    /// tree (design §7, "Removed, hidden, or disabled").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a Show (true) of a focusable box and a column filled by For over 1, 2 with focusable rows; the root logs each
    ///   focus-out with whether its target was still in the tree. The window is active.
    ///
    /// Steps:
    /// - Focus row 1; set the items to 2. Focus the Show's box; set its condition false.
    ///
    /// Expected:
    /// - Two focus-outs, each of a node in the tree; the focus is empty after each.
    /// </remarks>
    [Fact]
    public void RemovedRowAndHiddenPart_LoseTheFocus() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var items = Signal.Create<IReadOnlyList<int>>([1, 2]);
            var when = Signal.Create(true);
            var rows = new Dictionary<int, NodeHandle>();
            Element part = default;
            var log = new List<bool>();
            scene.Mount(() =>
                Column(
                        Show(when, () => part = Box().Focusable()),
                        Column()
                            .For(
                                items,
                                n => n,
                                item =>
                                {
                                    Element row = Box().Focusable();
                                    rows[item.Get()] = row.Node;
                                    return row;
                                }
                            )
                    )
                    .On(FocusEventKind.FocusOut, EventPhase.Bubble, (in FocusEvent e) => log.Add(scene.Tree.Nodes.GetRef(e.TargetNode).IsAttached))
            );
            scene.Input.SetActive(true);

            Assert.True(scene.Tree.Focus.Focus(rows[1]));
            items.Set([2]);
            bool emptyAfterRow = scene.Tree.Focus.Focused.IsNone;
            Assert.True(part.Focus());
            when.Set(false);

            Assert.Equal([true, true], log);
            Assert.True(emptyAfterRow);
            Assert.True(scene.Tree.Focus.Focused.IsNone);
        });

    /// <summary>
    /// The effect that reads the items does not read what the rows' render reads: a signal a row reads changes no update of the list.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column filled by For over items given through a signal that counts its reads; each row's render reads a shared signal.
    ///
    /// Steps:
    /// - Count the reads after mounting; set the shared signal.
    ///
    /// Expected:
    /// - No more reads of the items.
    /// </remarks>
    [Fact]
    public void For_RowsReads_DoNotRunTheListAgain() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var items = new CountingSignal<IReadOnlyList<int>>(Signal.Create<IReadOnlyList<int>>([1, 2]));
            var shared = Signal.Create(0);
            scene.Mount(() =>
                Column()
                    .For(
                        items,
                        n => n,
                        item =>
                        {
                            _ = shared.Get() + item.Get();
                            return Box();
                        }
                    )
            );
            int reads = items.Reads;

            shared.Set(1);

            Assert.Equal(reads, items.Reads);
        });

    /// <summary>
    /// The effect that reads Show's condition does not read what the part reads: a signal the part reads makes no part again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A Show over a condition given through a signal that counts its reads; its part reads a shared signal and counts its
    ///   renders.
    ///
    /// Steps:
    /// - Count the reads after mounting; set the shared signal.
    ///
    /// Expected:
    /// - No more reads of the condition; one render.
    /// </remarks>
    [Fact]
    public void Show_PartsReads_DoNotRunTheConditionAgain() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var when = new CountingSignal<bool>(Signal.Create(true));
            var shared = Signal.Create(0);
            int renders = 0;
            scene.Mount(() =>
                Column(
                    Show(
                        when,
                        () =>
                        {
                            renders++;
                            _ = shared.Get();
                            return Box();
                        }
                    )
                )
            );
            int reads = when.Reads;

            shared.Set(1);

            Assert.Equal(reads, when.Reads);
            Assert.Equal(1, renders);
        });

    /// <summary>
    /// For fills only a box with no child, and only once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree mounting a root function.
    ///
    /// Steps:
    /// - Call For on a column that holds a box; call For over no items on an empty column, so that it stays with no child, and call
    ///   For on it again.
    ///
    /// Expected:
    /// - <see cref="ArgumentException"/> both times.
    /// </remarks>
    [Fact]
    public void For_BoxWithAChildOrASecondFor_Throws() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var items = Signal.Create<IReadOnlyList<int>>([1]);
            var none = Signal.Create<IReadOnlyList<int>>([]);
            scene.Mount(() =>
            {
                Assert.Throws<ArgumentException>(() => Column(Box()).For(items, n => n, _ => Box()));
                Element column = Column().For(none, n => n, _ => Box());
                Assert.Throws<ArgumentException>(() => column.For(items, n => n, _ => Box()));
                return column;
            });
        });

    /// <summary>
    /// A render that returns no node, a node with a parent, or a node of another tree is reported, and its row keeps its place
    /// with an empty hidden node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The handler of unhandled exceptions collects them. A column filled by For over 0, 1, 2: row 0 returns no node, row 1 a box
    ///   already in a column, row 2 a box of another tree.
    ///
    /// Steps:
    /// - Mount.
    ///
    /// Expected:
    /// - Three <see cref="InvalidOperationException"/> reported; the column has three children, each hidden.
    /// </remarks>
    [Fact]
    public void For_RenderReturningAnUnfitNode_IsReported_AndTheRowIsHidden() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var other = new UiTree();
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                var items = Signal.Create<IReadOnlyList<int>>([0, 1, 2]);
                Element column = default;
                scene.Mount(() =>
                    column = Column()
                        .For(
                            items,
                            n => n,
                            item =>
                            {
                                switch (item.Get())
                                {
                                    case 0:
                                        return default;
                                    case 1:
                                        Element held = Box();
                                        _ = Column(held);
                                        return held;
                                    default:
                                        return new Element(other, other.CreateNode(new LayoutStyle()));
                                }
                            }
                        )
                );

                Assert.Equal(3, reported.Count);
                Assert.All(reported, e => Assert.IsType<InvalidOperationException>(e));
                List<NodeHandle> rows = scene.ChildrenOf(column.Node);
                Assert.Equal(3, rows.Count);
                Assert.All(rows, row => Assert.True(scene.Tree.Nodes.GetRef(row).Layout.Style.Hidden));
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// A For that goes away while one of its rows has the focus loses the focus with a focus-out while the row is still in the tree.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column, which logs each focus-out with whether its target was still in the tree, filled by For over 1, 2 with focusable
    ///   rows; the For is made in a scope of its own under the root's, so that the column's handler outlives it. The window is
    ///   active, and row 1 has the focus.
    ///
    /// Steps:
    /// - Dispose the For's scope.
    ///
    /// Expected:
    /// - One focus-out, of a node in the tree; the focus is empty; the column has no child.
    /// </remarks>
    [Fact]
    public void For_DisposedWithAFocusedRow_LosesTheFocus() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            var items = Signal.Create<IReadOnlyList<int>>([1, 2]);
            var rows = new Dictionary<int, NodeHandle>();
            var log = new List<bool>();
            Element column = default;
            ScopeHandle list = default;
            scene.Mount(() =>
            {
                column = Column()
                    .On(FocusEventKind.FocusOut, EventPhase.Bubble, (in FocusEvent e) => log.Add(scene.Tree.Nodes.GetRef(e.TargetNode).IsAttached));
                list = Scope.Create(() =>
                    column.For(
                        items,
                        n => n,
                        item =>
                        {
                            Element row = Box().Focusable();
                            rows[item.Get()] = row.Node;
                            return row;
                        }
                    )
                );
                return column;
            });
            scene.Input.SetActive(true);
            Assert.True(scene.Tree.Focus.Focus(rows[1]));

            Scope.Dispose(list);

            Assert.Empty(scene.ChildrenOf(column.Node));
            Assert.Equal([true], log);
            Assert.True(scene.Tree.Focus.Focused.IsNone);
        });

    /// <summary>
    /// The rows are laid out and drawn in the items' order after a reorder.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column filled by For over three items, each a box 10 high with its own background (gray, red, blue).
    ///
    /// Steps:
    /// - Reverse the items; run the frame.
    ///
    /// Expected:
    /// - The rectangles at y = 0, 10, 20 are blue, red, gray.
    /// </remarks>
    [Fact]
    public void DisplayList_AfterAReorder_DrawsTheRowsInTheNewOrder() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Color red = Color.FromRgb(0xCC3333);
            Color blue = Color.FromRgb(0x3333CC);
            var items = Signal.Create<IReadOnlyList<Color>>([Gray, red, blue]);
            scene.Mount(() => Column().Width(50).For(items, color => color, color => Box().Height(10).Background(color.Get())));

            items.Set([blue, red, Gray]);
            scene.Scheduler.Run();

            Assert.Equal([(0f, blue), (10f, red), (20f, Gray)], scene.Fills());
        });

    /// <summary>
    /// Moving rows and changing their values allocate nothing in the steady state: the diff and the frame that draws it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column filled by For over 100 items keyed by number; four lists made beforehand: the order, the reverse, the order with
    ///   new values, and the reverse with new values. Warmed up with the same iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: set the next of the four lists, and run the frame.
    ///
    /// Expected:
    /// - 0 bytes in every window; no row was made after the first 100.
    /// </remarks>
    [Fact]
    public void SteadyState_ReorderAndValueFrames_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            List<Item> forward = Enumerable.Range(0, 100).Select(n => new Item(n, "a")).ToList();
            IReadOnlyList<Item>[] lists =
            [
                forward,
                Enumerable.Reverse(forward).ToList(),
                forward.Select(i => i with { Name = "b" }).ToList(),
                Enumerable.Reverse(forward).Select(i => i with { Name = "b" }).ToList(),
            ];
            var items = Signal.Create(lists[0]);
            int makes = 0;
            scene.Mount(() =>
                Column()
                    .For(
                        items,
                        item => item.Id,
                        _ =>
                        {
                            makes++;
                            return Box().Height(1);
                        }
                    )
            );

            void Iteration(int index)
            {
                // The helper's unmeasured runs have negative indices.
                items.Set(lists[((index % 4) + 4) % 4]);
                scene.Scheduler.Run();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            PerformanceAssert.DoesNotAllocate(1_000, Iteration);
            Assert.Equal(100, makes);
        });

    private sealed record Item(int Id, string Name);

    // A signal that counts how often it is read.
    private sealed class CountingSignal<T>(IReadOnlySignal<T> inner) : IReadOnlySignal<T>
    {
        public int Reads { get; private set; }

        public T Get()
        {
            Reads++;
            return inner.Get();
        }
    }

    // A window of 200 x 100 logical pixels with no text: its tree, frames, and input, and the root scope of what it mounted.
    private sealed class Scene : IDisposable
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

        // The rectangles the last frame filled: their top and color, in order.
        public List<(float Y, Color Color)> Fills()
        {
            var fills = new List<(float, Color)>();
            DisplayListReader reader = Frames.List.GetReader();
            while (reader.TryNext(out CommandTag tag))
            {
                switch (tag)
                {
                    case CommandTag.FillRect:
                        FillRectCommand fill = reader.ReadFillRect();
                        fills.Add((fill.Y, fill.Color));
                        break;
                    case CommandTag.PushState:
                        _ = reader.ReadState(out _);
                        break;
                    case CommandTag.Pop:
                        reader.ReadPop();
                        break;
                    case CommandTag.Shape:
                        _ = reader.ReadShape();
                        break;
                    default:
                        throw new InvalidOperationException($"The test reads no {tag}.");
                }
            }

            return fills;
        }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
