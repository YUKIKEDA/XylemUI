using System.Numerics;

namespace XylemUI.Core.Tests;

// What the display list writes for clips and transforms (roadmap row R4d, design §5.0, "The API of layers, clips, transforms, and
// DrawingContext"): the scopes that record nothing, the moves it applies as it writes, the clips it takes together, the layer past
// the limit of rounded clips, the bounds of a group through a transform and a clip, and the values and calls it refuses.
public sealed class ClipListTests
{
    private static readonly Color Red = Color.FromRgb(0xCC3333);

    /// <summary>
    /// The identity transform, and a clip or a transform with no command in it, record nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Push the identity, fill (0, 0, 10, 10), pop; push a clip and pop; push a rotation and pop; push a rotation, push a clip, pop,
    ///   pop.
    ///
    /// Expected:
    /// - The list holds the one rectangle and no state.
    /// </remarks>
    [Fact]
    public void Scope_IdentityOrEmpty_RecordsNothing()
    {
        var list = new DisplayList();

        list.PushTransform(Matrix3x2.Identity);
        list.FillRect(0, 0, 10, 10, Red);
        list.Pop();
        list.PushClip(0, 0, 10, 10, new CornerRadii(2));
        list.Pop();
        list.PushTransform(Matrix3x2.CreateRotation(1));
        list.Pop();
        list.PushTransform(Matrix3x2.CreateRotation(1));
        list.PushClip(0, 0, 10, 10, default);
        list.Pop();
        list.Pop();

        Assert.Equal([CommandTag.FillRect], Tags(list));
        Assert.Equal(0, list.StateCount);
    }

    /// <summary>
    /// A transform that only moves is applied as the commands are written, with no state: by whole physical pixels the edges stay
    /// on pixels, as without it; by part of a pixel they are not put on pixels.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.25.
    ///
    /// Steps:
    /// - Push the move by (8, 4) logical pixels (10 and 5 physical); fill (10.3, 5.2, 20, 10); pop. Push the move by (0.3, 0); fill
    ///   the same; pop. Read the rectangles.
    ///
    /// Expected:
    /// - The first is each edge times 1.25 to the nearest pixel, moved by (10, 5); the second is the edges times 1.25, moved by
    ///   0.375, not rounded. No state.
    /// </remarks>
    [Fact]
    public void Transform_Move_IsWrittenIntoTheCommands()
    {
        var list = new DisplayList();
        list.Reset(1.25f);

        list.PushTransform(Matrix3x2.CreateTranslation(8, 4));
        list.FillRect(10.3f, 5.2f, 20, 10, Red);
        list.Pop();
        list.PushTransform(Matrix3x2.CreateTranslation(0.3f, 0));
        list.FillRect(10.3f, 5.2f, 20, 10, Red);
        list.Pop();

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out _));
        FillRectCommand whole = reader.ReadFillRect();
        Assert.True(reader.TryNext(out _));
        FillRectCommand part = reader.ReadFillRect();
        Assert.False(reader.TryNext(out _));
        float left = MathF.Round(10.3f * 1.25f);
        float top = MathF.Round(5.2f * 1.25f);
        Assert.Equal(new FillRectCommand(left + 10, top + 5, MathF.Round(30.3f * 1.25f) - left, MathF.Round(15.2f * 1.25f) - top, Red), whole);
        Assert.Equal((10.3f * 1.25f) + 0.375f, part.X, 4);
        Assert.Equal(5.2f * 1.25f, part.Y, 4);
        Assert.Equal(20 * 1.25f, part.Width, 4);
        Assert.Equal(0, list.StateCount);
    }

    /// <summary>
    /// A transform other than a move starts a state: the transform of the commands' positions to physical pixels, its move times
    /// the scale, and the text's scale, the larger of the transform's scales along its axes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 2.
    ///
    /// Steps:
    /// - Push the scale by (3, 1.5) and the move by (5, 7) after it (a matrix of 3, 0, 0, 1.5, 5, 7); fill (1, 1, 4, 4); pop. Read
    ///   the state and the rectangle.
    ///
    /// Expected:
    /// - The state's transform is 3, 0, 0, 1.5, 10, 14; its text scale is 3; the rectangle is (2, 2, 8, 8), before the transform.
    /// </remarks>
    [Fact]
    public void Transform_Scale_StartsAState()
    {
        var list = new DisplayList();
        list.Reset(2);

        list.PushTransform(new Matrix3x2(3, 0, 0, 1.5f, 5, 7));
        list.FillRect(1, 1, 4, 4, Red);
        list.Pop();

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag tag) && tag == CommandTag.PushState);
        StateCommand state = reader.ReadState(out ReadOnlySpan<ClipCommand> clips);
        Assert.Equal(new Matrix3x2(3, 0, 0, 1.5f, 10, 14), state.Transform);
        Assert.Equal(3, state.TextScale);
        Assert.Equal(0, clips.Length);
        Assert.True(reader.TryNext(out _));
        Assert.Equal(new FillRectCommand(2, 2, 8, 8, Red), reader.ReadFillRect());
        Assert.True(reader.TryNext(out tag) && tag == CommandTag.Pop);
        reader.ReadPop();
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// Rectangles that clip with axes along the screen's are taken together into one rectangle, with no clip per pixel.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Push the clip (0, 0, 100, 100) and inside it (50, 20, 100, 100); fill (0, 0, 10, 10); pop twice. Read the second state.
    ///
    /// Expected:
    /// - Its rectangle is (50, 20) to (100, 100), its bounds the same, and it has no clip per pixel.
    /// </remarks>
    [Fact]
    public void Clips_Rectangles_AreOneRectangle()
    {
        var list = new DisplayList();

        list.PushClip(0, 0, 100, 100, default);
        list.PushClip(50, 20, 100, 100, default);
        list.FillRect(0, 0, 10, 10, Red);
        list.Pop();
        list.Pop();

        (StateCommand state, int clips) = States(list)[1];
        Assert.Equal(new Vector4(50, 20, 100, 100), state.ClipRect);
        Assert.Equal(new Vector4(50, 20, 100, 100), state.Bounds);
        Assert.Equal(0, clips);
    }

    /// <summary>
    /// Rounded clips that contain one another are taken together into the inner one (Skia's ClipStack); others are kept, each
    /// applied per pixel; a rounded clip that contains the rectangle that clips is left out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Case "inner after": the rounded clip (0, 0, 100, 100) of radius 10, then (20, 20, 40, 40) of radius 5.
    /// - Case "outer after": the two the other way round.
    /// - Case "apart": (0, 0, 100, 100) of radius 30, then (60, 60, 100, 100) of radius 30.
    /// - Case "around the rectangle": the clip (10, 10, 20, 20), then (0, 0, 100, 100) of radius 10.
    /// - In each, fill a rectangle and pop twice. Read the second state.
    ///
    /// Expected:
    /// - "inner after" and "outer after": one clip per pixel, (20, 20, 40, 40); "apart": two; "around the rectangle": none.
    /// </remarks>
    /// <param name="case">The case.</param>
    /// <param name="count">The clips per pixel of the second state.</param>
    [Theory]
    [InlineData("inner after", 1)]
    [InlineData("outer after", 1)]
    [InlineData("apart", 2)]
    [InlineData("around the rectangle", 0)]
    public void Clips_Rounded_ContainingOneAnotherAreOne(string @case, int count)
    {
        var list = new DisplayList();
        (Vector4 Rect, float Radius) big = (new Vector4(0, 0, 100, 100), 10);
        (Vector4 Rect, float Radius) small = (new Vector4(20, 20, 40, 40), 5);
        ((Vector4 Rect, float Radius) first, (Vector4 Rect, float Radius) second) = @case switch
        {
            "inner after" => (big, small),
            "outer after" => (small, big),
            "apart" => ((new Vector4(0, 0, 100, 100), 30f), (new Vector4(60, 60, 100, 100), 30f)),
            _ => ((new Vector4(10, 10, 20, 20), 0f), big),
        };

        list.PushClip(first.Rect.X, first.Rect.Y, first.Rect.Z, first.Rect.W, new CornerRadii(first.Radius));
        list.PushClip(second.Rect.X, second.Rect.Y, second.Rect.Z, second.Rect.W, new CornerRadii(second.Radius));
        list.FillRect(0, 0, 200, 200, Red);
        list.Pop();
        list.Pop();

        DisplayListReader reader = list.GetReader();
        int states = 0;
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.PushState)
            {
                _ = reader.ReadState(out ReadOnlySpan<ClipCommand> clips);
                if (++states == 2)
                {
                    Assert.Equal(count, clips.Length);
                    if (count == 1)
                    {
                        Assert.Equal((20f, 20f, 40f, 40f), (clips[0].X, clips[0].Y, clips[0].Width, clips[0].Height));
                    }
                }
            }
            else
            {
                Skip(ref reader, tag);
            }
        }

        Assert.Equal(2, states);
    }

    /// <summary>
    /// A rounded clip past <see cref="DisplayList.MaxRoundedClips"/> starts a clip's layer: a group of alpha 1 that is a layer, in
    /// which a state applies that clip alone; its Pop ends both.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Push MaxRoundedClips + 1 rounded clips, none inside another ((k, 0, 100, 100) of radius 30 for each k); fill a rectangle;
    ///   pop as many times. Read the commands.
    ///
    /// Expected:
    /// - MaxRoundedClips states, a group (alpha 1, a layer, a clip's), a state with one clip, the rectangle, and a Pop for each;
    ///   the states before the group apply 1 to MaxRoundedClips clips.
    /// </remarks>
    [Fact]
    public void Clips_PastTheLimit_StartAClipsLayer()
    {
        var list = new DisplayList();
        const int Count = DisplayList.MaxRoundedClips + 1;

        for (int k = 0; k < Count; k++)
        {
            list.PushClip(k, 0, 100, 100, new CornerRadii(30));
        }

        list.FillRect(0, 0, 10, 10, Red);
        for (int k = 0; k < Count; k++)
        {
            list.Pop();
        }

        DisplayListReader reader = list.GetReader();
        var seen = new List<string>();
        while (reader.TryNext(out CommandTag tag))
        {
            switch (tag)
            {
                case CommandTag.PushState:
                    _ = reader.ReadState(out ReadOnlySpan<ClipCommand> clips);
                    seen.Add($"state {clips.Length}");
                    break;
                case CommandTag.PushOpacity:
                    GroupCommand group = reader.ReadGroup();
                    seen.Add($"group {group.Alpha} {group.Layer} {group.Clip}");
                    break;
                default:
                    Skip(ref reader, tag);
                    seen.Add(tag.ToString());
                    break;
            }
        }

        string[] expected =
        [
            .. Enumerable.Range(1, DisplayList.MaxRoundedClips).Select(n => $"state {n}"),
            "group 1 True True",
            "state 1",
            nameof(CommandTag.FillRect),
            .. Enumerable.Repeat(nameof(CommandTag.Pop), Count + 1),
        ];
        Assert.Equal(expected, seen);
    }

    /// <summary>
    /// A group's bounds are its children's in physical pixels, through the transform and cut to the clip: what its layer must hold.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Push the rotation by 90 degrees about (50, 50); push the clip (0, 0, 100, 40); push opacity 0.5; fill (10, 10, 40, 20) and
    ///   (20, 25, 40, 20); pop three times. Read the group.
    ///
    /// Expected:
    /// - The group is a layer (the rectangles overlap); its bounds are the union of the turned rectangles, each cut to the turned
    ///   clip, (60, 0) to (100, 100): (70, 10) to (90, 50) and (60, 20) to (75, 60), so (60, 10, 30, 50).
    /// </remarks>
    [Fact]
    public void Group_InATransformAndAClip_IsBoundedThroughThem()
    {
        var list = new DisplayList();

        list.PushTransform(Matrix3x2.CreateRotation(MathF.PI / 2, new Vector2(50, 50)));
        list.PushClip(0, 0, 100, 40, default);
        list.PushOpacity(0.5f);
        list.FillRect(10, 10, 40, 20, Red);
        list.FillRect(20, 25, 40, 20, Red);
        list.Pop();
        list.Pop();
        list.Pop();

        DisplayListReader reader = list.GetReader();
        GroupCommand? group = null;
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.PushOpacity)
            {
                group = reader.ReadGroup();
            }
            else
            {
                Skip(ref reader, tag);
            }
        }

        Assert.NotNull(group);
        Assert.True(group.Value.Layer);
        Assert.Equal(60, group.Value.X, 3);
        Assert.Equal(10, group.Value.Y, 3);
        Assert.Equal(30, group.Value.Width, 3);
        Assert.Equal(50, group.Value.Height, 3);
    }

    /// <summary>
    /// Inside a transform that flattens the plane (a scale of 0), which has no inverse, a rounded clip shows nothing: its state's
    /// rectangle and bounds are empty numbers, not NaN, and a group inside has empty bounds, so no layer is made for it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Push the scale by 0; push the rounded clip (0, 0, 100, 100) of radius 10; push opacity 0.5; fill (10, 10, 40, 20) and
    ///   (20, 20, 40, 20); pop three times. Read the states and the group.
    ///
    /// Expected:
    /// - No exception; every state's rectangle and bounds are numbers, and the last one's bounds are empty; the group's width and
    ///   height are 0.
    /// </remarks>
    [Fact]
    public void Transform_Flattening_ShowsNothingInside()
    {
        var list = new DisplayList();

        list.PushTransform(Matrix3x2.CreateScale(0));
        list.PushClip(0, 0, 100, 100, new CornerRadii(10));
        list.PushOpacity(0.5f);
        list.FillRect(10, 10, 40, 20, Red);
        list.FillRect(20, 20, 40, 20, Red);
        list.Pop();
        list.Pop();
        list.Pop();

        List<(StateCommand State, int Clips)> states = States(list);
        Assert.NotEmpty(states);
        foreach ((StateCommand state, _) in states)
        {
            Assert.False(HasNaN(state.ClipRect));
            Assert.False(HasNaN(state.Bounds));
        }

        Vector4 last = states[^1].State.Bounds;
        Assert.True(last.Z <= last.X && last.W <= last.Y);
        DisplayListReader reader = list.GetReader();
        GroupCommand? group = null;
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.PushOpacity)
            {
                group = reader.ReadGroup();
            }
            else
            {
                Skip(ref reader, tag);
            }
        }

        Assert.NotNull(group);
        Assert.Equal((0f, 0f), (group.Value.Width, group.Value.Height));
    }

    /// <summary>
    /// Clips with edges or a size that are not numbers, a negative size or radius, a transform with an element that is not a
    /// number, and a Pop with nothing pushed are refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list.
    ///
    /// Steps:
    /// - Push a clip with x NaN, with width -1, with height infinite, with a radius -1, and with a radius NaN; push a transform with
    ///   M31 NaN and with M11 infinite; call Pop on the empty list.
    ///
    /// Expected:
    /// - ArgumentOutOfRangeException for each push, InvalidOperationException for the Pop; the list has no command.
    /// </remarks>
    [Fact]
    public void Scope_BadValues_AreRefused()
    {
        var list = new DisplayList();

        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushClip(float.NaN, 0, 10, 10, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushClip(0, 0, -1, 10, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushClip(0, 0, 10, float.PositiveInfinity, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushClip(0, 0, 10, 10, new CornerRadii(-1, 0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushClip(0, 0, 10, 10, new CornerRadii(0, float.NaN, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushTransform(new Matrix3x2(1, 0, 0, 1, float.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.PushTransform(new Matrix3x2(float.PositiveInfinity, 0, 0, 1, 0, 0)));
        Assert.Throws<InvalidOperationException>(list.Pop);
        Assert.Equal(0, list.CommandCount);
    }

    private static List<(StateCommand State, int Clips)> States(DisplayList list)
    {
        var states = new List<(StateCommand, int)>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.PushState)
            {
                StateCommand state = reader.ReadState(out ReadOnlySpan<ClipCommand> clips);
                states.Add((state, clips.Length));
            }
            else
            {
                Skip(ref reader, tag);
            }
        }

        return states;
    }

    private static List<CommandTag> Tags(DisplayList list)
    {
        var tags = new List<CommandTag>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            tags.Add(tag);
            Skip(ref reader, tag);
        }

        return tags;
    }

    // Each edge on its own: the edges of no clip are infinite, and their sum is not a number.
    private static bool HasNaN(Vector4 rect) => float.IsNaN(rect.X) || float.IsNaN(rect.Y) || float.IsNaN(rect.Z) || float.IsNaN(rect.W);

    private static void Skip(ref DisplayListReader reader, CommandTag tag)
    {
        switch (tag)
        {
            case CommandTag.FillRect:
                _ = reader.ReadFillRect();
                break;
            case CommandTag.PushState:
                _ = reader.ReadState(out _);
                break;
            case CommandTag.PushOpacity:
                _ = reader.ReadGroup();
                break;
            case CommandTag.Pop:
                reader.ReadPop();
                break;
            default:
                throw new InvalidOperationException($"No reader for {tag}.");
        }
    }
}
