using static XylemUI.Tests.LayoutTestTree;

namespace XylemUI.Tests;

// The worklist of layout roots (S6b, design §2.4.1).
[Collection(UiThreadGroup.Name)]
public sealed class LayoutWorklistTests(UiThreadFixture ui)
{
    /// <summary>
    /// When a boundary and an ancestor are both layout roots, the ancestor goes first and the boundary is laid out once, with
    /// the input from the ancestor's new size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root (block) → middle (block, auto) → boundary (fixed 100 x 50) → leaf. Laid out at 400 x 300.
    /// - A hook counts how often each node's layout runs.
    ///
    /// Steps:
    /// - Invalidate the leaf (the boundary becomes a root), then resize the window to 80 x 300 (the root becomes a root). The
    ///   boundary's request comes first in the list.
    /// - Run the pass.
    ///
    /// Expected:
    /// - The boundary and the leaf ran once each. The boundary's last input has the parent width 80; it keeps its fixed width
    ///   100 and overflows (CSS; S6b clamped it to the constraint).
    /// </remarks>
    [Fact]
    public void NestedRoots_AreLaidOutShallowestFirstAndOnce() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle middle = Box(tree, root, new LayoutStyle());
            NodeHandle boundary = Box(tree, middle, Fixed(100, 50));
            NodeHandle leaf = Leaf(tree, boundary, 10, 10);
            LayOut(tree, root, 400, 300);
            var runs = new Dictionary<NodeHandle, int>();
            tree.LayoutRunHook = node => runs[node] = runs.GetValueOrDefault(node) + 1;

            Invalidation.Invalidate(tree, leaf, ChangeKind.Size);
            Invalidation.NotifyWindowResized(tree, root, new Size(80, 300));
            Assert.Equal(boundary, tree.LayoutRoots[0].Node);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(1, runs[boundary]);
            Assert.Equal(1, runs[leaf]);
            Assert.Equal(80, tree.Nodes.GetRef(boundary).Layout.LastInput.ParentSize.Width);
            Assert.Equal(100, tree.GetRect(boundary).Width);
            Assert.Empty(tree.LayoutRoots);
        });

    /// <summary>
    /// A node marked again while it is laid out keeps its flags and is laid out again in the next cycle; a node marked from
    /// inside the pass is laid out in the next cycle too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - root → boundary A (fixed) → leaf X; root → boundary B (fixed) → leaf Y. Laid out once.
    /// - X's measure function, on its first call after the setup, marks X itself and Y dirty.
    ///
    /// Steps:
    /// - Invalidate X; run the pass.
    ///
    /// Expected:
    /// - X was measured twice and Y once in the pass; both are clean; no request is left.
    /// </remarks>
    [Fact]
    public void MarkedDuringThePass_IsLaidOutInTheNextCycle() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle a = Box(tree, root, Fixed(100, 50));
            NodeHandle b = Box(tree, root, Fixed(100, 50));
            NodeHandle y = Leaf(tree, b, 10, 10);
            bool armed = false;
            int xMeasures = 0;
            NodeHandle x = default;
            x = tree.CreateNode(
                new LayoutStyle(),
                (_, _) =>
                {
                    xMeasures++;
                    if (armed)
                    {
                        armed = false;
                        Invalidation.Invalidate(tree, x, ChangeKind.Size);
                        Invalidation.Invalidate(tree, y, ChangeKind.Size);
                    }

                    return MeasureResult.Of(10, 10);
                }
            );
            tree.AppendChild(a, x);
            LayOut(tree, root, 400, 300);
            var runs = new Dictionary<NodeHandle, int>();
            tree.LayoutRunHook = node => runs[node] = runs.GetValueOrDefault(node) + 1;
            xMeasures = 0;
            armed = true;

            Invalidation.Invalidate(tree, x, ChangeKind.Size);
            LayoutEngine.RunLayoutPass(tree);

            Assert.Equal(2, xMeasures);
            Assert.Equal(1, runs[y]);
            Assert.True(IsLayoutClean(tree, x));
            Assert.True(IsLayoutClean(tree, y));
            Assert.Empty(tree.LayoutRoots);
        });

    /// <summary>
    /// A measure function that throws is reported; the other roots of the pass are laid out, and the failed request waits for
    /// the next pass instead of being lost.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A handler that records what it receives. root → boundary A (fixed) → leaf X whose measure function throws while a flag
    ///   is set; root → boundary B (fixed) → leaf Y. Laid out once with the flag clear.
    ///
    /// Steps:
    /// - Set the flag; invalidate X and Y; run a pass. Clear the flag; run a pass.
    ///
    /// Expected:
    /// - The first pass reported one exception, laid out Y, and kept one request (A); X is still dirty.
    /// - The second pass laid out X; nothing is left.
    /// </remarks>
    [Fact]
    public void MeasureThatThrows_IsReportedAndItsRequestWaitsForTheNextPass() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle a = Box(tree, root, Fixed(100, 50));
            NodeHandle b = Box(tree, root, Fixed(100, 50));
            bool fail = false;
            NodeHandle x = tree.CreateNode(
                new LayoutStyle(),
                (_, _) => fail ? throw new InvalidOperationException("Thrown from a measure function on purpose.") : MeasureResult.Of(10, 10)
            );
            tree.AppendChild(a, x);
            NodeHandle y = Leaf(tree, b, 10, 10);
            LayOut(tree, root, 400, 300);
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                fail = true;
                Invalidation.Invalidate(tree, x, ChangeKind.Size);
                tree.SetStyle(y, new LayoutStyle { Height = Length.Px(25) });
                LayoutEngine.RunLayoutPass(tree);

                Assert.IsType<InvalidOperationException>(Assert.Single(reported));
                Assert.Equal(25, tree.GetRect(y).Height);
                Assert.Equal(a, Assert.Single(tree.LayoutRoots).Node);
                Assert.False(IsLayoutClean(tree, x));

                fail = false;
                LayoutEngine.RunLayoutPass(tree);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }

            Assert.True(IsLayoutClean(tree, x));
            Assert.Empty(tree.LayoutRoots);
        });

    /// <summary>
    /// A leaf whose measurement keeps throwing the same exception is reported once and counted after; a change of the leaf, while it
    /// is still dirty from the failure, or another type of exception, is reported again (design §2.4.1).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A handler that records what it receives. root → leaf X whose measure function throws the exception it is given while one
    ///   is set. Laid out once with none.
    ///
    /// Steps:
    /// - Set InvalidOperationException; invalidate X; run 2 passes; resize the window and run a pass.
    /// - Replace X's style (X is still dirty); run a pass. Mark X dirty; run a pass.
    /// - Set ArgumentException; run a pass.
    /// - Set none; run a pass.
    ///
    /// Expected:
    /// - After the first three passes, 1 report and 2 counted.
    /// - The style and the mark make 1 report each: 3 reports, 2 counted.
    /// - The new type makes a 4th report.
    /// - The last pass lays X out; nothing is left.
    /// </remarks>
    [Fact]
    public void MeasureThatKeepsThrowing_IsReportedOncePerChangeAndType() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            Exception? failure = null;
            NodeHandle x = tree.CreateNode(new LayoutStyle(), (_, _) => failure is null ? MeasureResult.Of(10, 10) : throw failure);
            tree.AppendChild(root, x);
            LayOut(tree, root, 400, 300);
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                failure = new InvalidOperationException("Thrown from a measure function on purpose.");
                Invalidation.Invalidate(tree, x, ChangeKind.Size);
                LayoutEngine.RunLayoutPass(tree);
                LayoutEngine.RunLayoutPass(tree);
                LayOut(tree, root, 300, 300);
                Assert.Same(failure, Assert.Single(reported));
                Assert.Equal(2, tree.SuppressedLayoutExceptions);

                tree.SetStyle(x, new LayoutStyle { Height = Length.Px(25) });
                LayoutEngine.RunLayoutPass(tree);
                Invalidation.MarkDirty(tree, x, DirtyFlags.Layout);
                LayoutEngine.RunLayoutPass(tree);
                Assert.Equal(3, reported.Count);
                Assert.Equal(2, tree.SuppressedLayoutExceptions);

                failure = new ArgumentException("Another type, on purpose.");
                LayoutEngine.RunLayoutPass(tree);
                Assert.Equal(4, reported.Count);
                Assert.Same(failure, reported[^1]);

                failure = null;
                LayoutEngine.RunLayoutPass(tree);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }

            Assert.True(IsLayoutClean(tree, x));
            Assert.Empty(tree.LayoutRoots);
        });

    /// <summary>
    /// A request put back after its layout threw merges with a request for the same node: a window resize, with its input,
    /// wins over a content change either way round.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A laid-out root.
    ///
    /// Steps:
    /// - With a content-change request for the root queued, put back a window-resize request (300 x 200) for it.
    /// - With that resize queued, put back a content-change request for it.
    ///
    /// Expected:
    /// - Each time one request is left: the window resize with the exact 300 x 200 input.
    /// </remarks>
    [Fact]
    public void RestoredRequest_MergesAndKeepsTheResize() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            LayOut(tree, root, 400, 300);
            var resize = new LayoutRequest
            {
                Node = root,
                Input = LayoutInput.Exact(new Size(300, 200)),
                Reason = LayoutReason.WindowResize,
            };

            Invalidation.MarkDirty(tree, root, DirtyFlags.Layout);
            Invalidation.RestoreRequest(tree, resize);
            Assert.Equal(resize, Assert.Single(tree.LayoutRoots));

            Invalidation.RestoreRequest(tree, new LayoutRequest { Node = root, Reason = LayoutReason.IntrinsicSizeChanged });
            Assert.Equal(resize, Assert.Single(tree.LayoutRoots));
        });

    /// <summary>
    /// A pass that keeps marking itself stops at the cycle limit and reports it; the request is left for the next pass.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A handler that records what it receives. root → leaf whose measure function marks itself dirty every time.
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - The leaf was measured once per cycle up to the limit; one <see cref="LayoutStabilizationException"/>; one request left.
    /// </remarks>
    [Fact]
    public void PassThatNeverSettles_StopsAtTheLimitAndReports() =>
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            int measures = 0;
            NodeHandle leaf = default;
            leaf = tree.CreateNode(
                new LayoutStyle(),
                (_, _) =>
                {
                    measures++;
                    Invalidation.Invalidate(tree, leaf, ChangeKind.Size);
                    return MeasureResult.Of(10, 10);
                }
            );
            tree.AppendChild(root, leaf);
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            try
            {
                LayOut(tree, root, 400, 300);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }

            Assert.Equal(LayoutEngine.MaxLayoutIterations, measures);
            Assert.IsType<LayoutStabilizationException>(Assert.Single(reported));
            Assert.Single(tree.LayoutRoots);
        });

    /// <summary>
    /// The pass forbids creating nodes: a measure function that creates one fails the invariant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release ends the process). root → leaf whose measure function creates a node.
    ///
    /// Steps:
    /// - Lay out.
    ///
    /// Expected:
    /// - <see cref="InvariantViolationException"/>; after it, signals can be read again (the pass was closed).
    /// </remarks>
    [Fact]
    public void CreatingANodeDuringThePass_FailsTheInvariant()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
        ui.Invoke(() =>
        {
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle leaf = tree.CreateNode(
                new LayoutStyle(),
                (_, _) =>
                {
                    _ = tree.CreateNode(new LayoutStyle());
                    return MeasureResult.Of(10, 10);
                }
            );
            tree.AppendChild(root, leaf);

            Assert.Throws<InvariantViolationException>(() => LayOut(tree, root, 400, 300));
            Assert.Equal(1, Signal.Create(1).Get());
        });
    }
}
