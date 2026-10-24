namespace XylemUI.Core.Tests;

// What the display list writes for opacity groups (roadmap row R4c, design §5.0, "The API of layers, clips, transforms, and
// DrawingContext"): whether a group is a layer, from its children's bounds; groups that record nothing; and the values and calls
// it refuses.
public sealed class GroupListTests
{
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// A group is a layer when a child's bounds meet the union of the bounds of the children before it, and not when they are
    /// apart or only touch; its bounds are their union.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Write a group of opacity 0.5 with the case's rectangles, and read the group.
    ///
    /// Expected:
    /// - The group's Layer is the case's, and its bounds are the union of the rectangles.
    /// </remarks>
    /// <param name="second">The second rectangle's left edge (the first is (0, 0, 20, 20); the second is 20 by 20 at y 0).</param>
    /// <param name="layer">Whether the group is a layer.</param>
    [Theory]
    [InlineData(30, false)]
    [InlineData(20, false)]
    [InlineData(19, true)]
    public void Group_ChildrenBounds_DecideTheLayer(float second, bool layer)
    {
        var list = new DisplayList();

        list.PushOpacity(0.5f);
        list.FillRect(0, 0, 20, 20, Red);
        list.FillRect(second, 0, 20, 20, Red);
        list.Pop();

        GroupCommand group = ReadGroup(list);
        Assert.Equal(new GroupCommand(0, 0, second + 20, 20, 0.5f, layer), group);
    }

    /// <summary>
    /// The union is what is compared, so two children apart from each other but both meeting the union of those before them make
    /// a layer (Flutter's check, which never takes overlapping children as apart).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - In a group of 0.5, fill (0, 0, 10, 10), (20, 20, 10, 10), and (0, 20, 10, 10): the third meets neither alone, but meets
    ///   the union (0, 0, 30, 30).
    ///
    /// Expected:
    /// - The group is a layer.
    /// </remarks>
    [Fact]
    public void Group_ChildMeetingTheUnion_IsALayer()
    {
        var list = new DisplayList();

        list.PushOpacity(0.5f);
        list.FillRect(0, 0, 10, 10, Red);
        list.FillRect(20, 20, 10, 10, Red);
        list.FillRect(0, 20, 10, 10, Red);
        list.Pop();

        Assert.True(ReadGroup(list).Layer);
    }

    /// <summary>
    /// A shadow's bounds are what it draws: the shape moved, grown by the spread, and three sigmas past it; a nested group counts
    /// as one child of its bounds.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Group A of 0.5: fill (0, 0, 20, 20); the shadow of (40, 0, 20, 20) with blur 20 (sigma 10: it reaches 30 past the shape,
    ///   to x 10). Group B of 0.5: fill (0, 0, 20, 20); a group of 0.5 with (30, 0, 10, 10) and (15, 0, 10, 10) (its bounds reach
    ///   x 15).
    ///
    /// Expected:
    /// - Both groups are layers.
    /// </remarks>
    [Fact]
    public void Group_ShadowAndNestedGroupBounds_CountAsDrawn()
    {
        var shadows = new DisplayList();
        shadows.PushOpacity(0.5f);
        shadows.FillRect(0, 0, 20, 20, Red);
        shadows.DrawShadow(40, 0, 20, 20, default, new BoxShadow(Red, default, 20));
        shadows.Pop();
        var nested = new DisplayList();
        nested.PushOpacity(0.5f);
        nested.FillRect(0, 0, 20, 20, Red);
        nested.PushOpacity(0.5f);
        nested.FillRect(30, 0, 10, 10, Red);
        nested.FillRect(15, 0, 10, 10, Red);
        nested.Pop();
        nested.Pop();

        Assert.True(ReadGroup(shadows).Layer);
        Assert.True(ReadGroup(nested).Layer);
    }

    /// <summary>
    /// An opacity of 1 records nothing, and its children count for the group around it; a group with no children leaves the list.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Push 0.5; push 1; fill (0, 0, 10, 10) and (5, 0, 10, 10); pop; push 0.4; push 0.3; pop; pop; pop. Read the list.
    ///
    /// Expected:
    /// - PushOpacity (a layer, since the rectangles inside the group of 1 overlap), FillRect, FillRect, Pop: the group of 1 and the
    ///   two empty groups wrote nothing.
    /// </remarks>
    [Fact]
    public void Group_OpaqueOrEmpty_RecordsNothing()
    {
        var list = new DisplayList();

        list.PushOpacity(0.5f);
        list.PushOpacity(1);
        list.FillRect(0, 0, 10, 10, Red);
        list.FillRect(5, 0, 10, 10, Red);
        list.Pop();
        list.PushOpacity(0.4f);
        list.PushOpacity(0.3f);
        list.Pop();
        list.Pop();
        list.Pop();

        var tags = new List<CommandTag>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            tags.Add(tag);
            switch (tag)
            {
                case CommandTag.PushOpacity:
                    Assert.True(reader.ReadGroup().Layer);
                    break;
                case CommandTag.Pop:
                    reader.ReadPop();
                    break;
                default:
                    _ = reader.ReadFillRect();
                    break;
            }
        }

        Assert.Equal([CommandTag.PushOpacity, CommandTag.FillRect, CommandTag.FillRect, CommandTag.Pop], tags);
    }

    /// <summary>
    /// An opacity that is not a number from 0 to 1 is refused, and so is a pop with nothing pushed; a list read with a group
    /// pushed fails the invariant; Reset forgets what was pushed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list.
    ///
    /// Steps:
    /// - Push NaN, -0.1, and 1.1; pop with nothing pushed; push 0.5, fill a rectangle, and read; reset and read.
    ///
    /// Expected:
    /// - ArgumentOutOfRangeException three times and InvalidOperationException; the read with the group pushed fails the
    ///   invariant (checked in Debug builds); after Reset the list reads no command.
    /// </remarks>
    [Fact]
    public void Group_BadOpacityPopAndOpenRead_AreRefused()
    {
        var list = new DisplayList();

        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushOpacity(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushOpacity(-0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushOpacity(1.1f));
        Assert.Throws<InvalidOperationException>(list.Pop);
        list.PushOpacity(0.5f);
        list.FillRect(0, 0, 10, 10, Red);
#if DEBUG
        Exception failed = Assert.ThrowsAny<Exception>(() => list.GetReader());
        Assert.Equal("InvariantViolationException", failed.GetType().Name);
#endif

        list.Reset();

        Assert.False(list.GetReader().TryNext(out _));
        Assert.Throws<InvalidOperationException>(list.Pop);
    }

    private static GroupCommand ReadGroup(DisplayList list)
    {
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag tag));
        Assert.Equal(CommandTag.PushOpacity, tag);
        return reader.ReadGroup();
    }
}
