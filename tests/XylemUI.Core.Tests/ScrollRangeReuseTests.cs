using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// The scroll ranges of unchanged subtrees, used again (row V3b, design §3.4.1, "the mark that makes a remembered value stale"):
// a range read after a change equals the range of the same tree built and laid out from the start, and finding it costs the
// path that changed.
[Collection(UiThreadGroup.Name)]
public sealed class ScrollRangeReuseTests(UiThreadFixture ui)
{
    /// <summary>
    /// After random changes, the range of every scroll container read equals the range of the same tree laid out from the start.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - For each of 60 seeds: a random tree of 30 to 60 nodes under a root that scrolls vertically. Containers are blocks,
    ///   columns, and rows, with random padding, some of fixed size, some absolutely positioned, and a random overflow on each
    ///   axis (Visible most often, Clip, Hidden, or Scroll). Leaves have content of a random size, some in a box of fixed size
    ///   that the content overflows.
    ///
    /// Steps:
    /// - Lay the tree out at a random window size. Then 40 times, one of: resize the window; hide or show a node; change the
    ///   content of a leaf; change a container's overflow; change a container's fixed size; append a new leaf to a container
    ///   (it may take a freed node's slot); remove a node and its subtree. Run the layout pass, and read the range of each
    ///   shown scroll container with a chance of one half, so that some ranges are not read for several steps. Build the same
    ///   tree from the start and lay it out.
    ///
    /// Expected:
    /// - Every range read equals the range of the same container in the tree built from the start.
    /// </remarks>
    [Fact]
    public void RangeAfterChanges_EqualsTheRangeOfATreeLaidOutFromTheStart() =>
        ui.Invoke(() =>
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var random = new Random(seed);
                var spec = new TreeSpec(random);
                (UiTree tree, NodeHandle[] nodes) = spec.Build();
                Size window = new(100 + random.Next(300), 100 + random.Next(300));
                LayOut(tree, nodes[0], window.Width, window.Height);
                for (int step = 0; step < 40; step++)
                {
                    switch (random.Next(7))
                    {
                        case 0:
                            window = new Size(100 + random.Next(300), 100 + random.Next(300));
                            Invalidation.NotifyWindowResized(tree, nodes[0], window);
                            break;
                        case 1:
                            if (spec.RandomNode(random, containers: false) is int shown and > 0)
                            {
                                tree.SetStyle(nodes[shown], spec.ToggleHidden(shown));
                            }

                            break;
                        case 2:
                            if (spec.RandomLeaf(random) is int leaf and >= 0)
                            {
                                spec.Leaves[leaf]!.Change(random);
                                Invalidation.Invalidate(tree, nodes[leaf], ChangeKind.Size);
                            }

                            break;
                        case 3:
                            if (spec.RandomNode(random, containers: true) is int overflowed and > 0)
                            {
                                tree.SetStyle(nodes[overflowed], spec.NewOverflow(overflowed, random));
                            }

                            break;
                        case 4:
                            if (spec.RandomNode(random, containers: true) is int sized and > 0)
                            {
                                tree.SetStyle(nodes[sized], spec.NewSize(sized, random));
                            }

                            break;
                        case 5:
                            int parent = spec.RandomNode(random, containers: true);
                            int added = spec.AddLeaf(parent, random);
                            nodes = Grow(nodes, added + 1);
                            nodes[added] = tree.CreateNode(spec.Styles[added], spec.Leaves[added]!.Measure);
                            tree.AppendChild(nodes[parent], nodes[added]);
                            break;
                        default:
                            if (spec.RandomNode(random, containers: false) is int removed and > 0)
                            {
                                tree.Remove(nodes[removed]);
                                spec.Remove(removed);
                            }

                            break;
                    }

                    LayoutEngine.RunLayoutPass(tree);
                    (UiTree fresh, NodeHandle[] freshNodes) = spec.Build();
                    LayOut(fresh, freshNodes[0], window.Width, window.Height);
                    for (int i = 0; i < spec.Count; i++)
                    {
                        if (!spec.IsShownScrollContainer(i) || random.Next(2) == 0)
                        {
                            continue;
                        }

                        Size expected = fresh.MaxScrollOffsetOf(freshNodes[i]);
                        Size actual = tree.MaxScrollOffsetOf(nodes[i]);
                        Assert.True(expected == actual, $"seed {seed}, step {step}, node {i}: {actual} instead of {expected}");
                    }
                }
            }
        });

    /// <summary>
    /// A node made in a freed node's slot starts with its remembered overflow stale, even when a range reads it before it was ever
    /// laid out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scroll container 100 x 100 (a block, no state) holds a box 10 high whose child is 50 x 400, and a leaf 50 x 10;
    ///   laid out, and its range read (300 down), so that the box's overflow is remembered. Then the leaf becomes 50 x 20 and the
    ///   tree is laid out again: the container is laid out and marked, and the box is taken from its cache and keeps its memo,
    ///   which the test checks.
    ///
    /// Steps:
    /// - Remove the box; make a new box, with a child, in the freed slot (the same index); append it to the container. Read
    ///   the container's range before any layout pass.
    ///
    /// Expected:
    /// - The new box has the freed box's index. The range is 0 down: the new box and its child have no layout yet, the leaf fits,
    ///   and the freed box's 400 is not taken from the slot.
    /// </remarks>
    [Fact]
    public void ReusedSlot_DoesNotTakeTheFreedNodesOverflow() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle scroller = Box(tree, root, Scroller(100, 100));
            NodeHandle box = Box(tree, scroller, new LayoutStyle { Height = Length.Px(10) });
            _ = Leaf(tree, box, 50, 400);
            float height = 10;
            NodeHandle sibling = tree.CreateNode(new LayoutStyle(), (_, _) => MeasureResult.Of(50, height));
            tree.AppendChild(scroller, sibling);
            LayOut(tree, root, 400, 300);
            Assert.Equal(300, tree.MaxScrollOffsetOf(scroller).Height);
            height = 20;
            Invalidation.Invalidate(tree, sibling, ChangeKind.Size);
            LayoutEngine.RunLayoutPass(tree);
            Assert.False(tree.OverflowOf(scroller).Known);
            Assert.True(tree.OverflowOf(box).Known);

            tree.Remove(box);
            NodeHandle again = tree.CreateNode(new LayoutStyle { Height = Length.Px(10) });
            _ = Leaf(tree, again, 50, 20);
            tree.AppendChild(scroller, again);

            Assert.Equal(box.Index, again.Index);
            Assert.Equal(0, tree.MaxScrollOffsetOf(scroller).Height);
        });

    /// <summary>
    /// <summary>
    /// A layout that throws after it laid out some children leaves no remembered overflow stale and known: the range read after it
    /// equals the range found with no memo at all.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The handler of unhandled exceptions collects them. A root holds a scroll container 100 x 100 (a block, no state), which
    ///   holds a block of fixed size 80 x 50 that lets its overflow out, the layout root of a change inside it. The block holds
    ///   leaf A (50 x 20) and leaf B (50 x 10), in that order. Laid out, and the range read (0 down).
    ///
    /// Steps:
    /// - Make A 50 x 300 and make B's measurement throw; invalidate both; run the layout pass (A is laid out, then B throws).
    ///   Read the range. Then make every memo stale and read it again.
    ///
    /// Expected:
    /// - One exception reported. The two ranges are equal, and A's new box is in them (more than 0 down).
    /// </remarks>
    [Fact]
    public void LayoutThatThrowsHalfway_LeavesNoStaleMemoKnown() =>
        ui.Invoke(() =>
        {
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                var tree = new UiTree();
                NodeHandle root = tree.CreateNode(new LayoutStyle());
                NodeHandle scroller = Box(tree, root, Scroller(100, 100));
                NodeHandle block = Box(tree, scroller, Fixed(80, 50));
                float height = 20;
                bool throws = false;
                NodeHandle a = tree.CreateNode(new LayoutStyle(), (_, _) => MeasureResult.Of(50, height));
                NodeHandle b = tree.CreateNode(
                    new LayoutStyle(),
                    (_, _) => throws ? throw new InvalidOperationException("Thrown on purpose.") : MeasureResult.Of(50, 10)
                );
                tree.AppendChild(block, a);
                tree.AppendChild(block, b);
                LayOut(tree, root, 400, 300);
                Assert.Equal(0, tree.MaxScrollOffsetOf(scroller).Height);

                height = 300;
                throws = true;
                Invalidation.Invalidate(tree, a, ChangeKind.Size);
                Invalidation.Invalidate(tree, b, ChangeKind.Size);
                LayoutEngine.RunLayoutPass(tree);
                float range = tree.MaxScrollOffsetOf(scroller).Height;
                foreach (NodeHandle node in new[] { root, scroller, block })
                {
                    tree.OverflowOf(node).Known = false;
                }

                Assert.Single(reported);
                Assert.Equal(tree.MaxScrollOffsetOf(scroller).Height, range);
                Assert.True(range > 0);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// Writing a range after one change finds again only the containers on the path from the change to the scroll container.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root holds a scroll container (a column, 200 x 100) of 50 rows, each a row of 5 leaves 10 x 10; laid out, and the
    ///   range read once, so that every row's overflow is remembered.
    ///
    /// Steps:
    /// - Make one leaf of row 7 20 high; run the layout pass; read the range, count the containers found again; read it again.
    ///
    /// Expected:
    /// - The range is 410 down (500 + 10 less the 100 of the viewport). Two containers were found again: the scroll container
    ///   and row 7. The second read found none.
    /// </remarks>
    [Fact]
    public void OneChange_FindsOnlyThePathAgain() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle scroller = Box(tree, root, Scroller(200, 100) with { Algorithm = Column().Algorithm });
            var heights = new float[5];
            NodeHandle changed = default;
            for (int r = 0; r < 50; r++)
            {
                NodeHandle row = Box(tree, scroller, Row());
                for (int c = 0; c < 5; c++)
                {
                    if (r == 7 && c == 2)
                    {
                        changed = tree.CreateNode(new LayoutStyle(), (_, _) => MeasureResult.Of(10, heights[0]));
                        heights[0] = 10;
                        tree.AppendChild(row, changed);
                    }
                    else
                    {
                        _ = Leaf(tree, row, 10, 10);
                    }
                }
            }

            LayOut(tree, root, 400, 300);
            _ = tree.MaxScrollOffsetOf(scroller);
            heights[0] = 20;
            Invalidation.Invalidate(tree, changed, ChangeKind.Size);
            LayoutEngine.RunLayoutPass(tree);
            long before = tree.OverflowFinds;

            float range = tree.MaxScrollOffsetOf(scroller).Height;
            long found = tree.OverflowFinds - before;
            _ = tree.MaxScrollOffsetOf(scroller);

            Assert.Equal(410, range);
            Assert.Equal(2, found);
            Assert.Equal(before + 2, tree.OverflowFinds);
        });

    /// <summary>
    /// A change inside a scroll container stops at it: an outer scroll container keeps its remembered overflow, and the inner
    /// one's range, read later, has the change.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root holds an outer scroll container 200 x 100 (a column), which holds an inner one 100 x 50 (a block); neither has a
    ///   state. The inner one holds a column of fixed size 80 x 40 that lets its overflow out, which holds a leaf 50 x 80. Laid
    ///   out, and both ranges read. A block does not read its fixed child's content, so the fixed column is a layout boundary.
    ///
    /// Steps:
    /// - Make the leaf 50 x 120, and check that the fixed column is the only layout root, below the inner scroll container. Run
    ///   the layout pass. Read the outer range and count the containers found again; then read the inner range.
    ///
    /// Expected:
    /// - The outer range is 0 down, and no container was found again for it. The inner range is 70 down (120 less 50).
    /// </remarks>
    [Fact]
    public void ChangeInsideAScrollContainer_StopsThere() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle outer = Box(tree, root, Scroller(200, 100) with { Algorithm = Column().Algorithm });
            NodeHandle inner = Box(tree, outer, Scroller(100, 50));
            NodeHandle fixedBox = Box(tree, inner, Fixed(80, 40) with { Algorithm = Column().Algorithm });
            float height = 80;
            NodeHandle leaf = tree.CreateNode(new LayoutStyle { FlexShrink = 0 }, (_, _) => MeasureResult.Of(50, height));
            tree.AppendChild(fixedBox, leaf);
            LayOut(tree, root, 400, 300);
            _ = tree.MaxScrollOffsetOf(outer);
            Assert.Equal(30, tree.MaxScrollOffsetOf(inner).Height);
            height = 120;
            Invalidation.Invalidate(tree, leaf, ChangeKind.Size);
            Assert.Equal(fixedBox, Assert.Single(tree.LayoutRoots).Node);
            LayoutEngine.RunLayoutPass(tree);
            long before = tree.OverflowFinds;

            float outerRange = tree.MaxScrollOffsetOf(outer).Height;
            long found = tree.OverflowFinds - before;

            Assert.Equal(0, outerRange);
            Assert.Equal(0, found);
            Assert.Equal(70, tree.MaxScrollOffsetOf(inner).Height);
        });

    /// <summary>
    /// A frame that changes one leaf inside a scroll container, lays the tree out, and writes the range allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root holds a scroll container (a column, 200 x 100, with a state) of 20 rows of 5 leaves; one leaf's height
    ///   alternates between 10 and 20. Warmed up with the same iterations.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: change the leaf, run the layout pass, write the ranges.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    [Fact]
    public void SteadyState_ChangeLayoutAndRange_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle scroller = Box(tree, root, Scroller(200, 100) with { Algorithm = Column().Algorithm });
            tree.SetScrollState(scroller, new ScrollState());
            float height = 10;
            NodeHandle changed = default;
            for (int r = 0; r < 20; r++)
            {
                NodeHandle row = Box(tree, scroller, Row());
                for (int c = 0; c < 5; c++)
                {
                    NodeHandle leaf = tree.CreateNode(new LayoutStyle(), (_, _) => MeasureResult.Of(10, r == 3 && c == 1 ? height : 10));
                    tree.AppendChild(row, leaf);
                    changed = r == 3 && c == 1 ? leaf : changed;
                }
            }

            LayOut(tree, root, 400, 300);

            void Iteration(int index)
            {
                height = (index & 1) == 0 ? 20 : 10;
                Invalidation.Invalidate(tree, changed, ChangeKind.Size);
                LayoutEngine.RunLayoutPass(tree);
                tree.PublishScrollRanges();
            }

            for (int i = 0; i < 20; i++)
            {
                Iteration(i);
            }

            PerformanceAssert.DoesNotAllocate(1_000, Iteration);
        });

    private static LayoutStyle Scroller(float width, float height) => Fixed(width, height) with { OverflowY = Overflow.Scroll, ScrollbarWidth = 0 };

    private static NodeHandle[] Grow(NodeHandle[] nodes, int length) =>
        nodes.Length >= length ? nodes : [.. nodes, .. new NodeHandle[length - nodes.Length]];

    // A random tree with overflow on its containers, described so that it can be built again from the start.
    private sealed class TreeSpec
    {
        private readonly List<int> _parents = [];
        private readonly List<bool> _alive = [];

        public TreeSpec(Random random)
        {
            Add(-1, ContainerStyle(random, root: true), null);
            int count = 30 + random.Next(31);
            while (_parents.Count < count)
            {
                int parent = RandomNode(random, containers: true);
                if (random.Next(3) == 0)
                {
                    Add(parent, ContainerStyle(random, root: false), null);
                }
                else
                {
                    _ = AddLeaf(parent, random);
                }
            }
        }

        public List<LayoutStyle> Styles { get; } = [];

        public List<SizedLeaf?> Leaves { get; } = [];

        public int Count => _parents.Count;

        public (UiTree Tree, NodeHandle[] Nodes) Build()
        {
            var tree = new UiTree();
            var nodes = new NodeHandle[_parents.Count];
            for (int i = 0; i < _parents.Count; i++)
            {
                if (!_alive[i])
                {
                    continue;
                }

                nodes[i] = Leaves[i] is { } leaf ? tree.CreateNode(Styles[i], leaf.Measure) : tree.CreateNode(Styles[i]);
                if (_parents[i] >= 0)
                {
                    tree.AppendChild(nodes[_parents[i]], nodes[i]);
                }
            }

            return (tree, nodes);
        }

        // A random live node (a container, when asked), or -1 when there is none.
        public int RandomNode(Random random, bool containers)
        {
            for (int tries = 0; tries < 100; tries++)
            {
                int i = random.Next(_parents.Count);
                if (_alive[i] && (!containers || Leaves[i] is null))
                {
                    return i;
                }
            }

            return containers ? 0 : -1;
        }

        public int RandomLeaf(Random random)
        {
            for (int tries = 0; tries < 100; tries++)
            {
                int i = random.Next(_parents.Count);
                if (_alive[i] && Leaves[i] is not null)
                {
                    return i;
                }
            }

            return -1;
        }

        public int AddLeaf(int parent, Random random)
        {
            var leaf = new SizedLeaf(random);
            LayoutStyle style = random.Next(4) switch
            {
                0 => Fixed(10 + random.Next(30), 10 + random.Next(30)),
                1 => new LayoutStyle
                {
                    Position = Position.Absolute,
                    Inset = new EdgeInsets<LengthPercentageAuto>(
                        LengthPercentageAuto.Px(random.Next(40)),
                        LengthPercentageAuto.Auto,
                        LengthPercentageAuto.Auto,
                        LengthPercentageAuto.Px(random.Next(60))
                    ),
                },
                _ => new LayoutStyle(),
            };
            Add(parent, style, leaf);
            return _parents.Count - 1;
        }

        // Takes a node and its subtree out of the description.
        public void Remove(int index)
        {
            _alive[index] = false;
            for (int i = index + 1; i < _parents.Count; i++)
            {
                if (_alive[i] && _parents[i] >= 0 && !_alive[_parents[i]])
                {
                    _alive[i] = false;
                }
            }
        }

        public LayoutStyle ToggleHidden(int index) => Styles[index] = Styles[index] with { Hidden = !Styles[index].Hidden };

        public LayoutStyle NewOverflow(int index, Random random) =>
            Styles[index] = Styles[index] with { OverflowX = RandomOverflow(random), OverflowY = RandomOverflow(random) };

        public LayoutStyle NewSize(int index, Random random) =>
            Styles[index] =
                random.Next(3) == 0
                    ? Styles[index] with
                    {
                        Width = Length.Auto,
                        Height = Length.Auto,
                    }
                    : Styles[index] with
                    {
                        Width = Length.Px(20 + random.Next(150)),
                        Height = Length.Px(20 + random.Next(150)),
                    };

        // A live scroll container with no hidden node on its path to the root: the one whose range a frame writes.
        public bool IsShownScrollContainer(int index)
        {
            if (!_alive[index] || Leaves[index] is not null || !Styles[index].IsScrollContainer)
            {
                return false;
            }

            for (int i = index; i >= 0; i = _parents[i])
            {
                if (Styles[i].Hidden)
                {
                    return false;
                }
            }

            return true;
        }

        private void Add(int parent, LayoutStyle style, SizedLeaf? leaf)
        {
            _parents.Add(parent);
            Styles.Add(style);
            Leaves.Add(leaf);
            _alive.Add(true);
        }

        private static LayoutStyle ContainerStyle(Random random, bool root)
        {
            LayoutAlgorithm algorithm = random.Next(3) switch
            {
                0 => LayoutAlgorithm.Block.Instance,
                1 => Column().Algorithm,
                _ => Row().Algorithm,
            };
            var style = new LayoutStyle
            {
                Algorithm = algorithm,
                Padding = EdgeInsets.All(random.Next(3) * 3),
                ScrollbarWidth = 0,
            };
            if (root)
            {
                return style with { OverflowY = Overflow.Scroll };
            }

            if (random.Next(3) == 0)
            {
                style = style with { Width = Length.Px(20 + random.Next(150)), Height = Length.Px(20 + random.Next(150)) };
            }

            if (random.Next(8) == 0)
            {
                style = style with
                {
                    Position = Position.Absolute,
                    Inset = new EdgeInsets<LengthPercentageAuto>(
                        LengthPercentageAuto.Px(random.Next(30)),
                        LengthPercentageAuto.Auto,
                        LengthPercentageAuto.Auto,
                        LengthPercentageAuto.Px(random.Next(30))
                    ),
                };
            }

            return style with
            {
                OverflowX = RandomOverflow(random),
                OverflowY = RandomOverflow(random),
            };
        }

        private static Overflow RandomOverflow(Random random) =>
            random.Next(6) switch
            {
                0 => Overflow.Clip,
                1 => Overflow.Hidden,
                2 => Overflow.Scroll,
                _ => Overflow.Visible,
            };
    }

    // A leaf whose content has a size that can change.
    private sealed class SizedLeaf
    {
        private float _width;
        private float _height;

        public SizedLeaf(Random random)
        {
            Change(random);
            Measure = (known, _) => MeasureResult.Of(Maybe.Or(known.Width, _width), Maybe.Or(known.Height, _height));
        }

        public MeasureFunc Measure { get; }

        public void Change(Random random)
        {
            _width = 5 + random.Next(120);
            _height = 5 + random.Next(120);
        }
    }
}
