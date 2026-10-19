using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// What layout may not do, and what it costs in steady state (S6b, design §2.4.1, §2.5).
[Collection(UiThreadGroup.Name)]
public sealed class LayoutGuardTests(UiThreadFixture ui)
{
    private static readonly LayoutStyle Percent = new()
    {
        Width = Length.Percent(0.01f),
        Padding = EdgeInsets<LengthPercentage>.All(LengthPercentage.Percent(0.001f)),
    };

    private static readonly LayoutStyle Wrapping = new()
    {
        Algorithm = new LayoutAlgorithm.Flex(
            new FlexProps
            {
                Wrap = FlexWrap.Wrap,
                JustifyContent = new AlignContent(AlignContentKeyword.Center),
                ColumnGap = LengthPercentage.Px(2),
                RowGap = LengthPercentage.Px(2),
            }
        ),
    };

    private static readonly LayoutStyle AtRightBottom = new()
    {
        Position = Position.Absolute,
        Inset = new EdgeInsets<LengthPercentageAuto>(
            LengthPercentageAuto.Auto,
            LengthPercentageAuto.Auto,
            LengthPercentageAuto.Px(0),
            LengthPercentageAuto.Px(0)
        ),
    };

    private static readonly MeasureFunc TenByTen = static (_, _) => MeasureResult.Of(10, 10);

    private static readonly IntrinsicWidthsFunc TenWide = static _ => new IntrinsicWidths(10, 10, DependsOnHeight: false);

    /// <summary>
    /// Reading or setting a signal, or reading a computed value, during layout fails the invariant, and the pass is closed
    /// afterwards.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release ends the process). A signal and a computed value. Three trees whose leaf's measure function
    ///   reads the signal, sets it, or reads the computed value.
    ///
    /// Steps:
    /// - Lay out each tree.
    ///
    /// Expected:
    /// - Each throws <see cref="InvariantViolationException"/>; after them, the signal can be read and set again.
    /// </remarks>
    [Fact]
    public void SignalUseDuringLayout_FailsTheInvariant()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
        ui.Invoke(() =>
        {
            Signal<int> signal = Signal.Create(1);
            Computed<int> computed = Computed.Create(() => signal.Get() + 1);
            Action[] uses = [() => _ = signal.Get(), () => signal.Set(2), () => _ = computed.Get()];

            foreach (Action use in uses)
            {
                var tree = new UiTree();
                NodeHandle root = tree.CreateNode(new LayoutStyle());
                NodeHandle leaf = tree.CreateNode(
                    new LayoutStyle(),
                    (_, _) =>
                    {
                        use();
                        return MeasureResult.Of(10, 10);
                    }
                );
                tree.AppendChild(root, leaf);

                Assert.Throws<InvariantViolationException>(() => LayOut(tree, root, 100, 100));
            }

            signal.Set(5);
            Assert.Equal(6, computed.Get());
        });
    }

    /// <summary>
    /// Setting a signal while a computed value computes throws, in every build: a computed value has no side effects.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal; a computed value whose function sets it.
    ///
    /// Steps:
    /// - Read the computed value.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/> (kept by the computed value and thrown by the read); the signal kept its value.
    /// </remarks>
    [Fact]
    public void SetInsideAComputedValue_Throws() =>
        ui.Invoke(() =>
        {
            Signal<int> signal = Signal.Create(1);
            Computed<int> sideEffect = Computed.Create(() =>
            {
                signal.Set(2);
                return 0;
            });

            Assert.Throws<InvalidOperationException>(() => sideEffect.Get());
            Assert.Equal(1, signal.Get());
        });

    /// <summary>
    /// In a tree of about 10,000 nodes, laying out again after one leaf changes allocates nothing in steady state, with CSS's
    /// defaults (stretch, the automatic minimum size), with percentages, with wrapping rows, with children out of the flow, and
    /// with minimums and maximums that are sizing keywords.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root (column) → 100 rows → 100 leaves each (10,101 nodes), laid out at 2,000 x 2,000. With percent, every leaf other
    ///   than the changed one is 1 % wide with a 0.1 % padding. With wrap, the rows wrap, every other leaf is 30 wide (3,000
    ///   in a row of 2,000: two lines), and the items are centered with a gap of 2. With positioned, every tenth leaf is
    ///   absolutely positioned at its row's right and bottom, and every tenth from the fifth is a hidden row holding 10 leaves
    ///   (row L2). With keywords, every tenth leaf has a minimum width of max-content and every tenth from the fifth a maximum
    ///   width of min-content (row L3). Warmed up with 20 changes.
    ///
    /// Steps:
    /// - 1,100 times: set one leaf's width to 10 or 20 in turn, and run the pass. Measure the bytes allocated on this thread in
    ///   the last 1,000.
    ///
    /// Expected:
    /// - 0 bytes; the leaf has the last width.
    /// </remarks>
    [Theory]
    [InlineData("defaults")]
    [InlineData("percent")]
    [InlineData("wrap")]
    [InlineData("positioned")]
    [InlineData("keywords")]
    public void SteadyState_RelayoutAfterOneChange_AllocatesNothing(string scene) =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle target = default;
            for (int r = 0; r < 100; r++)
            {
                NodeHandle row = Box(tree, root, scene == "wrap" ? Wrapping : Row());
                for (int c = 0; c < 100; c++)
                {
                    LayoutStyle style = scene switch
                    {
                        "percent" => Percent,
                        "wrap" => new LayoutStyle { Width = Length.Px(30) },
                        "positioned" when c % 10 == 0 => AtRightBottom,
                        "positioned" when c % 10 == 5 => new LayoutStyle { Hidden = true },
                        "keywords" when c % 10 == 0 => new LayoutStyle { MinWidth = Length.MaxContent },
                        "keywords" when c % 10 == 5 => new LayoutStyle { MaxWidth = Length.MinContent },
                        _ => new LayoutStyle(),
                    };
                    NodeHandle leaf = scene == "positioned" && c % 10 == 5 ? tree.CreateNode(style) : tree.CreateNode(style, TenByTen, TenWide);
                    tree.AppendChild(row, leaf);
                    if (scene == "positioned" && c % 10 == 5)
                    {
                        for (int h = 0; h < 10; h++)
                        {
                            tree.AppendChild(leaf, tree.CreateNode(new LayoutStyle(), TenByTen, TenWide));
                        }
                    }

                    if (r == 50 && c == 50)
                    {
                        target = leaf;
                    }
                }
            }

            LayOut(tree, root, 2_000, 2_000);
            var narrow = new LayoutStyle { Width = Length.Px(10) };
            var wide = new LayoutStyle { Width = Length.Px(20) };
            for (int i = 0; i < 20; i++)
            {
                tree.SetStyle(target, i % 2 == 0 ? wide : narrow);
                LayoutEngine.RunLayoutPass(tree);
            }

            SteadyStateAllocation.AssertNone(
                1_000,
                i =>
                {
                    tree.SetStyle(target, i % 2 == 0 ? wide : narrow);
                    LayoutEngine.RunLayoutPass(tree);
                }
            );

            Assert.Equal(10, tree.GetRect(target).Width);
        });

    /// <summary>
    /// In the same tree, laying out again after one leaf's content changes allocates nothing in steady state, whether the
    /// change stops at the leaf (the same size) or goes on to its row (a new size): the kept values of row L5 reuse their
    /// storage.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root (column) → 100 rows → 100 leaves each (10,101 nodes) with CSS's defaults, laid out at 2,000 x 2,000. The changed
    ///   leaf's content is 10 x 10, or with "new size" 10 or 20 wide in turn. Warmed up with 20 changes.
    ///
    /// Steps:
    /// - 1,100 times: change the leaf's content, invalidate it (a text change), and run the pass. Measure the bytes allocated
    ///   on this thread in the last 1,000. Nothing takes the paint updates yet (a later row): the leaf stays in them once.
    ///
    /// Expected:
    /// - 0 bytes; the leaf has the last width.
    /// </remarks>
    [Theory]
    [InlineData("same size")]
    [InlineData("new size")]
    public void SteadyState_RelayoutAfterAContentChange_AllocatesNothing(string change) =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle target = default;
            float width = 10;
            for (int r = 0; r < 100; r++)
            {
                NodeHandle row = Box(tree, root, Row());
                for (int c = 0; c < 100; c++)
                {
                    NodeHandle leaf =
                        r == 50 && c == 50
                            ? tree.CreateNode(
                                new LayoutStyle(),
                                (_, _) => MeasureResult.Of(width, 10),
                                _ => new IntrinsicWidths(width, width, DependsOnHeight: false)
                            )
                            : tree.CreateNode(new LayoutStyle(), TenByTen, TenWide);
                    tree.AppendChild(row, leaf);
                    if (r == 50 && c == 50)
                    {
                        target = leaf;
                    }
                }
            }

            LayOut(tree, root, 2_000, 2_000);
            bool newSize = change == "new size";
            void Change(int i)
            {
                width = newSize && i % 2 == 0 ? 20 : 10;
                Invalidation.Invalidate(tree, target, ChangeKind.TextContent);
                LayoutEngine.RunLayoutPass(tree);
            }

            for (int i = 0; i < 20; i++)
            {
                Change(i);
            }

            SteadyStateAllocation.AssertNone(1_000, Change);

            Assert.Equal(10, tree.GetRect(target).Width);
        });
}
