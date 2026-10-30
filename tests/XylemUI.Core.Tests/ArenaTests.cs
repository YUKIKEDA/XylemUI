namespace XylemUI.Tests;

public sealed class ArenaTests
{
    private static int _sink;

    /// <summary>
    /// A handle stays stale after its value is freed, also after the slot is reused by a new value.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An arena with one allocated value.
    ///
    /// Steps:
    /// - Free the value, then allocate a second value, which reuses the slot.
    /// - Call TryGet with both handles.
    ///
    /// Expected:
    /// - The handles share the index and differ in generation.
    /// - TryGet fails for the old handle and returns the second value for the new one.
    /// </remarks>
    [Fact]
    public void TryGet_AfterFreeAndSlotReuse_RejectsTheOldHandle()
    {
        var arena = new Arena<int>();
        Handle<int> first = arena.Allocate(1);
        arena.Free(first);
        Handle<int> second = arena.Allocate(2);

        Assert.Equal(first.Index, second.Index);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.False(arena.TryGet(first, out _));
        Assert.True(arena.TryGet(second, out int value));
        Assert.Equal(2, value);
    }

    /// <summary>
    /// The default handle, <see langword="default"/>, never points to a value, also while the first slot is in use.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An arena whose first slot (index 0) holds a value, freed and reallocated once so that its generation moved on.
    ///
    /// Steps:
    /// - Check IsAlive and TryGet for <see langword="default"/>, before and after the reuse.
    ///
    /// Expected:
    /// - <see langword="default"/> is <see cref="Handle{T}.IsNone"/> and is never alive.
    /// - A handle the arena returned is never IsNone.
    /// </remarks>
    [Fact]
    public void DefaultHandle_WhileTheFirstSlotIsInUse_IsNeverAlive()
    {
        var arena = new Arena<int>();
        Handle<int> first = arena.Allocate(1);

        Assert.Equal(0, first.Index);
        Assert.False(first.IsNone);
        Assert.True(default(Handle<int>).IsNone);
        Assert.False(arena.IsAlive(default(Handle<int>)));
        Assert.False(arena.TryGet(default, out _));

        arena.Free(first);
        Handle<int> reused = arena.Allocate(2);
        Assert.Equal(0, reused.Index);
        Assert.False(reused.IsNone);
        Assert.False(arena.IsAlive(default(Handle<int>)));
    }

    /// <summary>
    /// GetRef and Free treat a stale handle as a broken invariant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release builds end the process instead).
    /// - An arena with one freed value.
    ///
    /// Steps:
    /// - Call GetRef with the freed handle.
    /// - Call Free with the freed handle.
    ///
    /// Expected:
    /// - Both throw <see cref="InvariantViolationException"/>.
    /// </remarks>
    [Fact]
    public void GetRefAndFree_StaleHandle_FailTheInvariant()
    {
        RequireDebugBuild();
        var arena = new Arena<int>();
        Handle<int> handle = arena.Allocate(1);
        arena.Free(handle);

        Assert.Throws<InvariantViolationException>(() => arena.GetRef(handle));
        Assert.Throws<InvariantViolationException>(() => arena.Free(handle));
    }

    /// <summary>
    /// The copy-links, recurse, write-back pattern computes a recursive value over a tree of 100,000 nodes correctly.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree of 100,000 nodes with up to 8 children each, built breadth first, every node of weight 1.
    ///
    /// Steps:
    /// - Compute every subtree total recursively: copy FirstChild and NextSibling, recurse, then GetRef again and write the total.
    /// - Compute the same totals independently: walk the nodes in reverse creation order and add each total to its parent.
    ///
    /// Expected:
    /// - Every node's recursive total equals the independent total, and the root's total is 100,000.
    /// </remarks>
    [Fact]
    public void RecursiveWriteBack_TreeOf100000Nodes_MatchesIndependentTotals()
    {
        const int NodeCount = 100_000;
        var arena = new Arena<TreeNode>(initialCapacity: 16);
        Handle<TreeNode>[] nodes = BuildTree(arena, NodeCount, branching: 8);

        using (arena.ForbidAllocation())
        {
            Assert.Equal(NodeCount, ComputeTotals(arena, nodes[0]));
        }

        int[] expected = new int[NodeCount];
        for (int i = NodeCount - 1; i >= 0; i--)
        {
            expected[i] += 1;
            Handle<TreeNode> parent = arena.GetRef(nodes[i]).Parent;
            if (!parent.IsNone)
            {
                expected[parent.Index] += expected[i];
            }
        }

        for (int i = 0; i < NodeCount; i++)
        {
            Assert.Equal(expected[i], arena.GetRef(nodes[i]).Total);
        }
    }

    /// <summary>
    /// A write through a reference taken before the arena grows is lost, and the growth is visible through GrowthCount.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An arena of capacity 1 holding one node.
    ///
    /// Steps:
    /// - Take a reference to the node, allocate a second node (the array grows), and write through the old reference.
    ///
    /// Expected:
    /// - GrowthCount went up by one.
    /// - The value read through a fresh GetRef is unchanged: the write went to the old array (Invariant 12).
    /// </remarks>
    [Fact]
    public void GetRef_HeldAcrossGrowth_LosesTheWrite()
    {
        var arena = new Arena<TreeNode>(initialCapacity: 1);
        Handle<TreeNode> handle = arena.Allocate(new TreeNode { Total = 1 });
        int growthBefore = arena.GrowthCount;

        ref TreeNode stale = ref arena.GetRef(handle);
        _ = arena.Allocate(default);
        stale.Total = 42;

        Assert.Equal(growthBefore + 1, arena.GrowthCount);
        Assert.Equal(1, arena.GetRef(handle).Total);
    }

    /// <summary>
    /// An allocation inside ForbidAllocation fails the invariant, and allocation works again after the region ends.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release builds end the process instead).
    ///
    /// Steps:
    /// - Allocate inside a nested ForbidAllocation region.
    /// - End both regions and allocate again.
    ///
    /// Expected:
    /// - The allocation inside throws <see cref="InvariantViolationException"/>.
    /// - The allocation after the regions succeeds.
    /// </remarks>
    [Fact]
    public void Allocate_InsideForbidAllocation_FailsTheInvariant()
    {
        RequireDebugBuild();
        var arena = new Arena<int>();

        using (arena.ForbidAllocation())
        {
            using (arena.ForbidAllocation())
            {
                Assert.Throws<InvariantViolationException>(() => arena.Allocate(1));
            }
        }

        Handle<int> handle = arena.Allocate(2);
        Assert.True(arena.IsAlive(handle));
    }

    /// <summary>
    /// TryGet, GetRef, and a recursive traversal allocate nothing in steady state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree of 10,000 nodes; one warm-up pass of every operation.
    ///
    /// Steps:
    /// - 110 rounds of TryGet and GetRef over every node and one recursive pass. Measure the bytes allocated on this thread in
    ///   the last 100.
    ///
    /// Expected:
    /// - Zero bytes.
    /// </remarks>
    [Fact]
    public void SteadyState_LookupsAndTraversal_AllocateNothing()
    {
        var arena = new Arena<TreeNode>();
        Handle<TreeNode>[] nodes = BuildTree(arena, 10_000, branching: 8);
        RunLookupsAndTraversal(arena, nodes);

        PerformanceAssert.DoesNotAllocate(100, _ => RunLookupsAndTraversal(arena, nodes));
    }

    private static void RunLookupsAndTraversal(Arena<TreeNode> arena, Handle<TreeNode>[] nodes)
    {
        int sum = 0;
        foreach (Handle<TreeNode> node in nodes)
        {
            if (arena.TryGet(node, out TreeNode copy))
            {
                sum += copy.Total;
            }

            sum += arena.GetRef(node).Total;
        }

        // A static sink keeps the work observable. GC.KeepAlive(object) would box the int (24 bytes) in debug builds.
        _sink += sum + ComputeTotals(arena, nodes[0]);
    }

    // Children are allocated after their parent, breadth first, so node i's parent has a smaller index.
    private static Handle<TreeNode>[] BuildTree(Arena<TreeNode> arena, int count, int branching)
    {
        var nodes = new Handle<TreeNode>[count];
        nodes[0] = arena.Allocate(default);
        for (int i = 1; i < count; i++)
        {
            Handle<TreeNode> parent = nodes[(i - 1) / branching];
            Handle<TreeNode> child = arena.Allocate(new TreeNode { Parent = parent });
            nodes[i] = child;

            // Prepend: the child becomes the parent's first child. Take the reference after the allocation (Invariant 12).
            ref TreeNode parentNode = ref arena.GetRef(parent);
            arena.GetRef(child).NextSibling = parentNode.FirstChild;
            parentNode.FirstChild = child;
        }

        return nodes;
    }

    // The layout pattern of design §2.4.1: copy the links, recurse, then take the reference again to write back.
    private static int ComputeTotals(Arena<TreeNode> arena, Handle<TreeNode> handle)
    {
        int total = 1;
        Handle<TreeNode> child = arena.GetRef(handle).FirstChild;
        while (!child.IsNone)
        {
            total += ComputeTotals(arena, child);
            child = arena.GetRef(child).NextSibling;
        }

        arena.GetRef(handle).Total = total;
        return total;
    }

    private static void RequireDebugBuild()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
    }

    private struct TreeNode
    {
        public Handle<TreeNode> Parent;
        public Handle<TreeNode> FirstChild;
        public Handle<TreeNode> NextSibling;
        public int Total;
    }
}
