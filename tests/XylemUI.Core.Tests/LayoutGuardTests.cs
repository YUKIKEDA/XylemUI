using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// What layout may not do, and what it costs in steady state (S6b, design §2.4.1, §2.5).
[Collection(UiThreadGroup.Name)]
public sealed class LayoutGuardTests(UiThreadFixture ui)
{
    private static readonly MeasureFunc TenByTen = static (_, _) => MeasureResult.Of(10, 10);

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
    /// In a tree of about 10,000 nodes, laying out again after one leaf changes allocates nothing in steady state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root (column) → 100 rows → 100 leaves each (10,101 nodes), laid out at 2,000 x 2,000. Warmed up with 20 changes.
    ///
    /// Steps:
    /// - 1,000 times: set one leaf's width to 10 or 20 in turn, and run the pass. Measure the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes; the leaf has the last width.
    /// </remarks>
    [Fact]
    public void SteadyState_RelayoutAfterOneChange_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(Column());
            NodeHandle target = default;
            for (int r = 0; r < 100; r++)
            {
                NodeHandle row = Box(tree, root, Row());
                for (int c = 0; c < 100; c++)
                {
                    NodeHandle leaf = tree.CreateNode(new LayoutStyle(), TenByTen);
                    tree.AppendChild(row, leaf);
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

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                tree.SetStyle(target, i % 2 == 0 ? wide : narrow);
                LayoutEngine.RunLayoutPass(tree);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(10, tree.GetRect(target).Width);
        });
}
