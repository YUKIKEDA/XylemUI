namespace XylemUI.Tests;

public sealed class ScratchStackTests
{
    /// <summary>
    /// A span taken before the stack grows keeps its elements: growing replaces the buffer for the spans taken after, and the
    /// earlier spans stay in the old one (PR #102's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty stack.
    ///
    /// Steps:
    /// - Take 4 elements and write 1 to 4 into them; take 1,000 more (the buffer grows) and write 9 into them.
    ///
    /// Expected:
    /// - The first span still reads 1 to 4, and Top is 1,004.
    /// </remarks>
    [Fact]
    public void Push_PastTheBuffer_KeepsTheSpansTakenBefore()
    {
        var stack = new ScratchStack<int>();
        Span<int> first = stack.Push(4);
        for (int i = 0; i < 4; i++)
        {
            first[i] = i + 1;
        }

        Span<int> second = stack.Push(1_000);
        second.Fill(9);

        Assert.Equal([1, 2, 3, 4], first.ToArray());
        Assert.Equal(1_004, stack.Top);
    }

    /// <summary>
    /// Giving back to an earlier top makes the next take start there, as a nested run's span starts above its container's
    /// (PR #102's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty stack. Take 3 elements (the container's) and note Top.
    ///
    /// Steps:
    /// - Take 5 elements (a nested run's), give back to the noted top, and take 5 again.
    ///
    /// Expected:
    /// - Both takes of 5 start at the same element: a write through the second shows through the first.
    /// </remarks>
    [Fact]
    public void PopTo_AnEarlierTop_GivesTheSameElementsToTheNextPush()
    {
        var stack = new ScratchStack<int>();
        _ = stack.Push(3);
        int top = stack.Top;

        Span<int> nested = stack.Push(5);
        stack.PopTo(top);
        Span<int> again = stack.Push(5);
        again[0] = 42;

        Assert.Equal(42, nested[0]);
        Assert.Equal(top + 5, stack.Top);
    }
}
