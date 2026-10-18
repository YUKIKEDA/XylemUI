using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

[Collection(UiThreadGroup.Name)]
public sealed class LayoutRandomTests(UiThreadFixture ui)
{
    /// <summary>
    /// An incremental layout after a change gives the same result as a layout of the same tree from the start (design §2.4.1,
    /// row L5): the independent reference for every cache, boundary, and stop of the incremental passes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - For each of 40 seeds: a random tree of 30 to 60 nodes. Containers are rows, columns, wrapping rows, and blocks, with
    ///   random alignment, gaps, padding, and fixed or automatic sizes. Leaves have content of a random size, some with content
    ///   widths, some with a first baseline, some with a percentage width.
    ///
    /// Steps:
    /// - Lay the tree out at a random window size. Then 30 times: change a random leaf's content (half the time to the same
    ///   size, half to a new one) and invalidate it, or resize the window; run the pass; build the same tree from the start
    ///   with the same contents and lay it out once.
    ///
    /// Expected:
    /// - After every step, every node has the same rectangle in both trees, and every leaf the same first baseline.
    /// </remarks>
    [Fact]
    public void IncrementalLayout_EqualsALayoutFromTheStart() =>
        ui.Invoke(() =>
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var random = new Random(seed);
                var spec = new TreeSpec(random);
                (UiTree tree, List<NodeHandle> nodes) = spec.Build();
                Size window = new(100 + random.Next(300), 100 + random.Next(300));
                LayOut(tree, nodes[0], window.Width, window.Height);
                for (int step = 0; step < 30; step++)
                {
                    if (random.Next(6) == 0)
                    {
                        window = new Size(100 + random.Next(300), 100 + random.Next(300));
                        LayOut(tree, nodes[0], window.Width, window.Height);
                    }
                    else
                    {
                        int leaf = spec.RandomLeaf(random);
                        spec.Change(leaf, random, sameSize: random.Next(2) == 0);
                        Invalidation.Invalidate(tree, nodes[leaf], ChangeKind.TextContent);
                        LayoutEngine.RunLayoutPass(tree);
                    }

                    (UiTree fresh, List<NodeHandle> freshNodes) = spec.Build();
                    LayOut(fresh, freshNodes[0], window.Width, window.Height);
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        Assert.True(
                            fresh.GetRect(freshNodes[i]) == tree.GetRect(nodes[i]),
                            $"seed {seed}, step {step}, node {i}: {tree.GetRect(nodes[i])} instead of {fresh.GetRect(freshNodes[i])}"
                        );
                        Assert.True(
                            Maybe.Same(fresh.Nodes.GetRef(freshNodes[i]).Layout.FirstBaseline, tree.Nodes.GetRef(nodes[i]).Layout.FirstBaseline),
                            $"seed {seed}, step {step}, node {i}: a different first baseline"
                        );
                    }
                }
            }
        });

    // A random tree, described so that it can be built again from the start with the leaves' current contents.
    private sealed class TreeSpec
    {
        private readonly List<int> _parents = [];
        private readonly List<LayoutStyle> _styles = [];
        private readonly List<Leaf?> _leaves = [];

        public TreeSpec(Random random)
        {
            Add(-1, ContainerStyle(random, root: true), null);
            int count = 30 + random.Next(31);
            while (_parents.Count < count)
            {
                int parent = RandomContainer(random);
                if (random.Next(3) == 0)
                {
                    Add(parent, ContainerStyle(random, root: false), null);
                }
                else
                {
                    var leaf = new Leaf(random);
                    Add(parent, Leaf.Style(random), leaf);
                }
            }
        }

        public (UiTree Tree, List<NodeHandle> Nodes) Build()
        {
            var tree = new UiTree();
            var nodes = new List<NodeHandle>();
            for (int i = 0; i < _parents.Count; i++)
            {
                Leaf? leaf = _leaves[i];
                NodeHandle node = leaf is null
                    ? tree.CreateNode(_styles[i])
                    : tree.CreateNode(_styles[i], leaf.Measure, leaf.HasWidths ? leaf.Widths : null);
                if (_parents[i] >= 0)
                {
                    tree.AppendChild(nodes[_parents[i]], node);
                }

                nodes.Add(node);
            }

            return (tree, nodes);
        }

        public int RandomLeaf(Random random)
        {
            while (true)
            {
                int i = random.Next(_parents.Count);
                if (_leaves[i] is not null)
                {
                    return i;
                }
            }
        }

        public void Change(int index, Random random, bool sameSize) => _leaves[index]!.Change(random, sameSize);

        private void Add(int parent, LayoutStyle style, Leaf? leaf)
        {
            _parents.Add(parent);
            _styles.Add(style);
            _leaves.Add(leaf);
        }

        private int RandomContainer(Random random)
        {
            while (true)
            {
                int i = random.Next(_parents.Count);
                if (_leaves[i] is null)
                {
                    return i;
                }
            }
        }

        private static LayoutStyle ContainerStyle(Random random, bool root)
        {
            LayoutAlgorithm algorithm = random.Next(4) switch
            {
                0 => LayoutAlgorithm.Block.Instance,
                1 => new LayoutAlgorithm.Flex(
                    new FlexProps
                    {
                        Direction = FlexDirection.Column,
                        AlignItems = new AlignItems(Pick(random, AlignItemsKeyword.Normal, AlignItemsKeyword.Start, AlignItemsKeyword.Center)),
                        RowGap = LengthPercentage.Px(random.Next(3) * 2),
                    }
                ),
                2 => new LayoutAlgorithm.Flex(
                    new FlexProps
                    {
                        Wrap = FlexWrap.Wrap,
                        AlignContent = new AlignContent(AlignContentKeyword.FlexStart),
                        ColumnGap = LengthPercentage.Px(random.Next(3) * 2),
                    }
                ),
                _ => new LayoutAlgorithm.Flex(
                    new FlexProps
                    {
                        AlignItems = new AlignItems(
                            Pick(random, AlignItemsKeyword.Normal, AlignItemsKeyword.Start, AlignItemsKeyword.Baseline, AlignItemsKeyword.End)
                        ),
                        JustifyContent = new AlignContent(
                            Pick(random, AlignContentKeyword.Normal, AlignContentKeyword.Center, AlignContentKeyword.SpaceBetween)
                        ),
                    }
                ),
            };
            var style = new LayoutStyle { Algorithm = algorithm, Padding = EdgeInsets.All(random.Next(3) * 3) };
            if (!root && random.Next(4) == 0)
            {
                style = style with { Width = Length.Px(40 + random.Next(120)), Height = Length.Px(30 + random.Next(80)) };
            }

            return style;
        }

        private static T Pick<T>(Random random, params T[] values) => values[random.Next(values.Length)];
    }

    // A leaf whose content can change: a run of characters 10 wide each, broken into lines at the width given, as Ahem text.
    private sealed class Leaf
    {
        private readonly bool _withBaseline;
        private int _characters;
        private int _longest;

        public Leaf(Random random)
        {
            _characters = 1 + random.Next(8);
            _longest = 1 + random.Next(_characters);
            _withBaseline = random.Next(2) == 0;
            HasWidths = random.Next(2) == 0;
            Measure = (known, available) =>
            {
                float content = available.Width.Kind switch
                {
                    AvailableSpaceKind.MinContent => _longest * 10,
                    AvailableSpaceKind.MaxContent => _characters * 10,
                    _ => Math.Clamp(available.Width.Value, _longest * 10, _characters * 10),
                };
                float width = Maybe.Or(known.Width, content);
                int perLine = Math.Max(_longest, (int)Math.Floor(Math.Max(width, 10) / 10));
                float height = Maybe.Or(known.Height, (float)Math.Ceiling((double)_characters / perLine) * 10);
                return new MeasureResult(new Size(width, height), _withBaseline ? 8 : float.NaN);
            };
            Widths = _ => new IntrinsicWidths(_longest * 10, _characters * 10, DependsOnHeight: false);
        }

        public bool HasWidths { get; }

        public MeasureFunc Measure { get; }

        public IntrinsicWidthsFunc Widths { get; }

        public static LayoutStyle Style(Random random) =>
            random.Next(5) switch
            {
                0 => new LayoutStyle { Width = Length.Percent(0.2f + (0.1f * random.Next(4))) },
                1 => new LayoutStyle { MinWidth = LengthPercentageAuto.Px(0) },
                2 => new LayoutStyle { FlexGrow = 1 },
                _ => new LayoutStyle(),
            };

        // The same characters in another order keeps the size; another count changes it.
        public void Change(Random random, bool sameSize)
        {
            if (!sameSize)
            {
                _characters = 1 + random.Next(8);
                _longest = 1 + random.Next(_characters);
            }
        }
    }
}
