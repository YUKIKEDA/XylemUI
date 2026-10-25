using System.Numerics;
using XylemUI.Tests;

namespace XylemUI.Core.Tests;

// The hit regions of the display list and the hit test (roadmap row R4e, design §5.0, "Hit regions and DrawingContext", and §7,
// "Hit testing"): where a point hits a rounded region in clips, transforms, and clips' layers, which of several regions it hits,
// the scopes that hold only regions, the values refused, and DrawingContext and DrawingScope.
public sealed class HitTestTests
{
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly NodeHandle First = new(1, 1);
    private static readonly NodeHandle Second = new(2, 1);

    /// <summary>
    /// A point hits a rounded region inside its corners' arcs and its edges, and misses it just outside them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1 with the region of a node at (10, 10, 100, 60) with corners of radius 20.
    ///
    /// Steps:
    /// - Test points 0.37 pixels apart over the region and a band of 5 pixels around it.
    ///
    /// Expected:
    /// - A point hits where it is inside the rectangle and, in a corner's square, within 20 pixels of the arc's center, computed with
    ///   the circle's equation; points within 0.01 of an edge or an arc are skipped. At least one point in each corner's square
    ///   misses and one hits.
    /// </remarks>
    [Fact]
    public void HitTest_RoundedRegion_HitsInsideTheCornersArcs()
    {
        var list = new DisplayList();
        list.HitRegion(First, 10, 10, 100, 60, new CornerRadii(20));

        int cornerMisses = 0;
        int cornerHits = 0;
        for (float y = 5; y <= 75; y += 0.37f)
        {
            for (float x = 5; x <= 115; x += 0.37f)
            {
                float? margin = RoundedMargin(new Vector2(x, y), new Vector4(10, 10, 100, 60), 20, out bool inCorner);
                if (margin is not { } m || MathF.Abs(m) < 0.01f)
                {
                    continue;
                }

                Assert.Equal(m < 0 ? First : default, list.HitTest(new Vector2(x, y)));
                cornerMisses += inCorner && m > 0 ? 1 : 0;
                cornerHits += inCorner && m < 0 ? 1 : 0;
            }
        }

        Assert.True(cornerMisses > 0 && cornerHits > 0);
    }

    /// <summary>
    /// A region in a rotated rounded clip is hit inside the clip as it is drawn turned, and missed outside it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1. A rotation by 30 degrees about (60, 60), a clip (20, 20, 80, 80) with corners of radius 30 in it,
    ///   and the region (0, 0, 200, 200) of a node inside the clip.
    ///
    /// Steps:
    /// - Test points 0.53 pixels apart over (−20, −20) to (140, 140).
    ///
    /// Expected:
    /// - A point hits where the point turned back by 30 degrees about (60, 60) is inside the clip's rounded rectangle by the circle's
    ///   equation; points within 0.01 of its edge are skipped. Both hits and misses occur.
    /// </remarks>
    [Fact]
    public void HitTest_InARotatedRoundedClip_FollowsTheTurnedClip()
    {
        var list = new DisplayList();
        Matrix3x2 turn = Matrix3x2.CreateRotation(MathF.PI / 6, new Vector2(60, 60));
        list.PushTransform(turn);
        list.PushClip(20, 20, 80, 80, new CornerRadii(30));
        list.HitRegion(First, 0, 0, 200, 200, default);
        list.Pop();
        list.Pop();

        Assert.True(Matrix3x2.Invert(turn, out Matrix3x2 back));
        int hits = 0;
        int misses = 0;
        for (float y = -20; y <= 140; y += 0.53f)
        {
            for (float x = -20; x <= 140; x += 0.53f)
            {
                float? margin = RoundedMargin(Vector2.Transform(new Vector2(x, y), back), new Vector4(20, 20, 80, 80), 30, out _);
                if (margin is { } m && MathF.Abs(m) < 0.01f)
                {
                    continue;
                }

                bool inside = margin is < 0;
                Assert.Equal(inside ? First : default, list.HitTest(new Vector2(x, y)));
                hits += inside ? 1 : 0;
                misses += inside ? 0 : 1;
            }
        }

        Assert.True(hits > 0 && misses > 0);
    }

    /// <summary>
    /// Past the limit of rounded clips, a region in the clip's layer is hit only inside the clips around the layer too, which the
    /// layer's composite applies.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1 with three nested rounded clips that do not contain one another, (0, 0, 100, 100), (20, 0, 100,
    ///   100), and (0, 20, 100, 100), each with corners of radius 40, and the region (0, 0, 200, 200) of a node inside them.
    ///
    /// Steps:
    /// - Read the group the third clip starts. Test (50, 110), inside the third clip only; then points 0.61 pixels apart over (−10,
    ///   −10) to (130, 130).
    ///
    /// Expected:
    /// - The list has one clip's layer. (50, 110) misses. A point hits where it is inside all three clips by the circle's equation;
    ///   points within 0.01 of an edge are skipped.
    /// </remarks>
    [Fact]
    public void HitTest_InAClipsLayer_TakesTheClipsAroundTheLayer()
    {
        var list = new DisplayList();
        Vector4[] clips = [new(0, 0, 100, 100), new(20, 0, 100, 100), new(0, 20, 100, 100)];
        foreach (Vector4 clip in clips)
        {
            list.PushClip(clip.X, clip.Y, clip.Z, clip.W, new CornerRadii(40));
        }

        list.HitRegion(First, 0, 0, 200, 200, default);
        foreach (Vector4 _ in clips)
        {
            list.Pop();
        }

        Assert.Equal(1, list.GroupCount);
        Assert.Equal(default, list.HitTest(new Vector2(50, 110)));
        for (float y = -10; y <= 130; y += 0.61f)
        {
            for (float x = -10; x <= 130; x += 0.61f)
            {
                bool inside = true;
                bool near = false;
                foreach (Vector4 clip in clips)
                {
                    float? margin = RoundedMargin(new Vector2(x, y), clip, 40, out _);
                    inside &= margin is < 0;
                    near |= margin is { } m && MathF.Abs(m) < 0.01f;
                }

                if (!near)
                {
                    Assert.Equal(inside ? First : default, list.HitTest(new Vector2(x, y)));
                }
            }
        }
    }

    /// <summary>
    /// Nothing is hit inside a transform that flattens the plane, with or without a clip.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Push a scale of 0; write the region (0, 0, 100, 100) of one node; push the clip (0, 0, 100, 100) with corners of radius 10;
    ///   write the same region of another; pop both. Test (0, 0), (50, 50), and (99, 1).
    ///
    /// Expected:
    /// - No point hits either node.
    /// </remarks>
    [Fact]
    public void HitTest_InAFlatteningTransform_HitsNothing()
    {
        var list = new DisplayList();
        list.PushTransform(Matrix3x2.CreateScale(0));
        list.HitRegion(First, 0, 0, 100, 100, default);
        list.PushClip(0, 0, 100, 100, new CornerRadii(10));
        list.HitRegion(Second, 0, 0, 100, 100, default);
        list.Pop();
        list.Pop();

        Assert.Equal(default, list.HitTest(new Vector2(0, 0)));
        Assert.Equal(default, list.HitTest(new Vector2(50, 50)));
        Assert.Equal(default, list.HitTest(new Vector2(99, 1)));
    }

    /// <summary>
    /// Of overlapping regions, the one written last, the front-most, is hit; a point only the one behind holds hits it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1 with the region (0, 0, 50, 50) of one node, then (25, 25, 50, 50) of another.
    ///
    /// Steps:
    /// - Test (30, 30), (10, 10), (60, 60), and (90, 90).
    ///
    /// Expected:
    /// - The second node, the first, the second, and none.
    /// </remarks>
    [Fact]
    public void HitTest_OverlappingRegions_HitsTheFrontMost()
    {
        var list = new DisplayList();
        list.HitRegion(First, 0, 0, 50, 50, default);
        list.HitRegion(Second, 25, 25, 50, 50, default);

        Assert.Equal(Second, list.HitTest(new Vector2(30, 30)));
        Assert.Equal(First, list.HitTest(new Vector2(10, 10)));
        Assert.Equal(Second, list.HitTest(new Vector2(60, 60)));
        Assert.Equal(default, list.HitTest(new Vector2(90, 90)));
    }

    /// <summary>
    /// A region's edges are where a rectangle's are drawn: on pixels at a scale, and through a move by whole pixels or by part of
    /// one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.25.
    ///
    /// Steps:
    /// - For no move, a move by (8, 4) logical pixels (whole physical pixels), and a move by (0.3, 0): reset the list, push the move,
    ///   fill (10.3, 5.2, 20, 10) and write the same region, pop. Read the rectangle. Test the points 0.05 pixels inside and outside
    ///   each of its edges, at the middle of the other axis.
    ///
    /// Expected:
    /// - Each point inside hits the node, and each outside misses.
    /// </remarks>
    [Fact]
    public void HitRegion_Edges_AreTheDrawnRectangles()
    {
        var list = new DisplayList();
        foreach (Vector2 move in new Vector2[] { default, new(8, 4), new(0.3f, 0) })
        {
            list.Reset(1.25f);
            list.PushTransform(Matrix3x2.CreateTranslation(move));
            list.FillRect(10.3f, 5.2f, 20, 10, Red);
            list.HitRegion(First, 10.3f, 5.2f, 20, 10, default);
            list.Pop();

            FillRectCommand rect = ReadRect(list);
            float middleX = rect.X + (rect.Width / 2);
            float middleY = rect.Y + (rect.Height / 2);
            foreach (
                (Vector2 inside, Vector2 outside) in new (Vector2, Vector2)[]
                {
                    (new(rect.X + 0.05f, middleY), new(rect.X - 0.05f, middleY)),
                    (new(rect.X + rect.Width - 0.05f, middleY), new(rect.X + rect.Width + 0.05f, middleY)),
                    (new(middleX, rect.Y + 0.05f), new(middleX, rect.Y - 0.05f)),
                    (new(middleX, rect.Y + rect.Height - 0.05f), new(middleX, rect.Y + rect.Height + 0.05f)),
                }
            )
            {
                Assert.Equal(First, list.HitTest(inside));
                Assert.Equal(default, list.HitTest(outside));
            }
        }
    }

    /// <summary>
    /// A clip, a transform, or an opacity group that holds only hit regions stays in the list, and the regions in it are tested with
    /// it; one with neither commands nor regions still leaves.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Push the clip (0, 0, 50, 50) with corners of radius 10, write the region (0, 0, 100, 100), pop. Push an opacity of 0.5, write
    ///   the region (200, 0, 10, 10) of another node, pop. Push the clip (300, 0, 20, 20) with no radius, write the region (300, 0,
    ///   100, 100) of the first node, pop. Push a rotation, pop. Read the commands; test (25, 25), (60, 60), (1, 1), (205, 5),
    ///   (310, 5), and (330, 5).
    ///
    /// Expected:
    /// - The commands are the clip's state and its pop, the group and its pop, and the second clip's state and its pop; two states.
    ///   (25, 25) hits the first node; (60, 60) and the clip's corner (1, 1) miss; (205, 5) hits the second; (310, 5), inside the
    ///   rectangle that clips, hits the first, and (330, 5), outside it, misses.
    /// </remarks>
    [Fact]
    public void Scope_WithOnlyHitRegions_Stays()
    {
        var list = new DisplayList();
        list.PushClip(0, 0, 50, 50, new CornerRadii(10));
        list.HitRegion(First, 0, 0, 100, 100, default);
        list.Pop();
        list.PushOpacity(0.5f);
        list.HitRegion(Second, 200, 0, 10, 10, default);
        list.Pop();
        list.PushClip(300, 0, 20, 20, default);
        list.HitRegion(First, 300, 0, 100, 100, default);
        list.Pop();
        list.PushTransform(Matrix3x2.CreateRotation(1));
        list.Pop();

        Assert.Equal(
            [CommandTag.PushState, CommandTag.Pop, CommandTag.PushOpacity, CommandTag.Pop, CommandTag.PushState, CommandTag.Pop],
            Tags(list)
        );
        Assert.Equal(2, list.StateCount);
        Assert.Equal(First, list.HitTest(new Vector2(25, 25)));
        Assert.Equal(default, list.HitTest(new Vector2(60, 60)));
        Assert.Equal(default, list.HitTest(new Vector2(1, 1)));
        Assert.Equal(Second, list.HitTest(new Vector2(205, 5)));
        Assert.Equal(First, list.HitTest(new Vector2(310, 5)));
        Assert.Equal(default, list.HitTest(new Vector2(330, 5)));
    }

    /// <summary>
    /// A region whose edges are not numbers or whose size or radius is negative or not a number is refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list.
    ///
    /// Steps:
    /// - Write regions with an edge of NaN, an edge of infinity, a width of −1, a height of NaN, and a radius of −1 and of NaN.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentOutOfRangeException"/>, and no region is written.
    /// </remarks>
    [Fact]
    public void HitRegion_InvalidValues_Throw()
    {
        var list = new DisplayList();

        Assert.Throws<ArgumentOutOfRangeException>(() => list.HitRegion(First, float.NaN, 0, 10, 10, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.HitRegion(First, 0, float.PositiveInfinity, 10, 10, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.HitRegion(First, 0, 0, -1, 10, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.HitRegion(First, 0, 0, 10, float.NaN, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.HitRegion(First, 0, 0, 10, 10, new CornerRadii(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.HitRegion(First, 0, 0, 10, 10, new CornerRadii(float.NaN)));
        Assert.Equal(0, list.HitEntries.Length);
    }

    /// <summary>
    /// Reset forgets the hit regions.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list with the region (0, 0, 10, 10) of a node inside a clip.
    ///
    /// Steps:
    /// - Reset the list; test (5, 5).
    ///
    /// Expected:
    /// - No region is left, and the point hits nothing.
    /// </remarks>
    [Fact]
    public void Reset_ForgetsTheHitRegions()
    {
        var list = new DisplayList();
        list.PushClip(0, 0, 10, 10, new CornerRadii(2));
        list.HitRegion(First, 0, 0, 10, 10, default);
        list.Pop();

        list.Reset();

        Assert.Equal(0, list.HitEntries.Length);
        Assert.Equal(default, list.HitTest(new Vector2(5, 5)));
    }

    /// <summary>
    /// A DrawingContext writes each command as the display list writes it, its scopes end with using, and its hit regions are the
    /// node's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two display lists at scale 1.25 and a drawing context on the first for a node.
    ///
    /// Steps:
    /// - Through the context: in an opacity of 0.5, a clip, and a rotation, each ended with using, a solid rectangle, a rounded one
    ///   with a border, a shadow, a line, a filled and a stroked ellipse, an arc, a sector, a glyph run, and a hit region. On the
    ///   second list, the same calls directly, with the same pushes and pops. Read both.
    ///
    /// Expected:
    /// - Both hold the same commands, rectangles, states, groups, and shape count. The first holds one hit region, of the node, and
    ///   the second, written with the node directly, the same one.
    /// </remarks>
    [Fact]
    public void DrawingContext_Commands_AreTheDisplayLists()
    {
        var viaContext = new DisplayList();
        var direct = new DisplayList();
        viaContext.Reset(1.25f);
        direct.Reset(1.25f);
        var stroke = new Stroke(new Paint(Red), 2);
        var border = new Border(new BorderWidths(1), new Paint(Red));
        GlyphInstance[] glyphs = [new GlyphInstance(1, 2, 3, 4, 0, 0, 0)];

        var context = new DrawingContext(viaContext, First);
        using (context.PushOpacity(0.5f))
        using (context.PushClip(1, 2, 80, 60, new CornerRadii(6)))
        using (context.PushTransform(Matrix3x2.CreateRotation(0.3f)))
        {
            context.FillRect(1, 1, 10, 10, Red);
            context.FillRect(2, 2, 20, 10, new Paint(Red), new CornerRadii(3), border);
            context.DrawShadow(3, 3, 10, 10, new CornerRadii(2), new BoxShadow(Red, new Vector2(1, 2), 4));
            context.DrawLine(0, 0, 30, 10, stroke);
            context.FillEllipse(20, 20, 8, 5, new Paint(Red));
            context.DrawEllipse(20, 20, 8, 5, stroke);
            context.DrawArc(30, 30, 10, 0, 90, stroke);
            context.FillSector(30, 30, 10, 0, 90, new Paint(Red));
            context.GlyphRun(glyphs, Red);
            context.HitRegion(4, 4, 12, 12, new CornerRadii(3));
        }

        direct.PushOpacity(0.5f);
        direct.PushClip(1, 2, 80, 60, new CornerRadii(6));
        direct.PushTransform(Matrix3x2.CreateRotation(0.3f));
        direct.FillRect(1, 1, 10, 10, Red);
        direct.FillRect(2, 2, 20, 10, new Paint(Red), new CornerRadii(3), border);
        direct.DrawShadow(3, 3, 10, 10, new CornerRadii(2), new BoxShadow(Red, new Vector2(1, 2), 4));
        direct.DrawLine(0, 0, 30, 10, stroke);
        direct.FillEllipse(20, 20, 8, 5, new Paint(Red));
        direct.DrawEllipse(20, 20, 8, 5, stroke);
        direct.DrawArc(30, 30, 10, 0, 90, stroke);
        direct.FillSector(30, 30, 10, 0, 90, new Paint(Red));
        direct.GlyphRun(glyphs, Red);
        direct.HitRegion(First, 4, 4, 12, 12, new CornerRadii(3));
        direct.Pop();
        direct.Pop();
        direct.Pop();

        Assert.Equal(direct.CommandCount, viaContext.CommandCount);
        Assert.Equal(direct.StateCount, viaContext.StateCount);
        Assert.Equal(direct.GroupCount, viaContext.GroupCount);
        Assert.Equal(direct.ShapeCount, viaContext.ShapeCount);
        Assert.Equal(direct.GlyphCount, viaContext.GlyphCount);
        Assert.Equal(Commands(direct), Commands(viaContext));
        Assert.Equal([.. direct.HitEntries], [.. viaContext.HitEntries]);
        Assert.Equal(First, viaContext.HitEntries[0].Node);
        Assert.Throws<InvalidOperationException>(viaContext.Pop);
    }

    /// <summary>
    /// A DrawingScope that no push returned refuses to end anything.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The default DrawingScope.
    ///
    /// Steps:
    /// - Dispose it.
    ///
    /// Expected:
    /// - It throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void DrawingScope_Default_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            DrawingScope scope = default;
            scope.Dispose();
        });
    }

    /// <summary>
    /// A steady-state frame that writes hit regions in clips and transforms and tests points allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list and a drawing context for a node; one frame run before measuring, so the arrays have grown.
    ///
    /// Steps:
    /// - 1,000 frames: reset; in a rotation and a rounded clip, write 100 regions through the context; test 10 points, one that hits.
    ///
    /// Expected:
    /// - 0 bytes on this thread (SteadyStateAllocation).
    /// </remarks>
    [Fact]
    public void SteadyState_HitRegionsAndHitTest_AllocateNothing()
    {
        var list = new DisplayList();
        NodeHandle hit = default;

        void Frame(int index)
        {
            list.Reset(1.25f);
            var context = new DrawingContext(list, First);
            using (context.PushTransform(Matrix3x2.CreateRotation(0.2f)))
            using (context.PushClip(0, 0, 400, 400, new CornerRadii(20)))
            {
                for (int i = 0; i < 100; i++)
                {
                    context.HitRegion(i * 3, i * 2, 20, 20, new CornerRadii(4));
                }
            }

            for (int i = 0; i < 10; i++)
            {
                hit = list.HitTest(new Vector2(i == 0 ? 30 : 900 + i, 30));
            }
        }

        Frame(0);
        SteadyStateAllocation.AssertNone(1_000, Frame);
        Assert.Equal(default, list.HitTest(new Vector2(900, 900)));
        Assert.True(hit == default || hit == First);
    }

    // How far a point is inside (negative) or outside (positive) a rounded rectangle (x, y, width, height) with one radius, by the
    // circle's equation in the corners' squares and the distance to the edges elsewhere; null when it is outside by more than a band
    // where only the sign matters. Independent of the renderer's formula.
    private static float? RoundedMargin(Vector2 p, Vector4 rect, float radius, out bool inCorner)
    {
        float left = rect.X;
        float top = rect.Y;
        float right = rect.X + rect.Z;
        float bottom = rect.Y + rect.W;
        float cx =
            p.X < left + radius ? left + radius
            : p.X > right - radius ? right - radius
            : float.NaN;
        float cy =
            p.Y < top + radius ? top + radius
            : p.Y > bottom - radius ? bottom - radius
            : float.NaN;
        inCorner = !float.IsNaN(cx) && !float.IsNaN(cy) && p.X >= left && p.X <= right && p.Y >= top && p.Y <= bottom;
        if (inCorner)
        {
            return Vector2.Distance(p, new Vector2(cx, cy)) - radius;
        }

        float outside = MathF.Max(MathF.Max(left - p.X, p.X - right), MathF.Max(top - p.Y, p.Y - bottom));
        return outside > 0 && !float.IsNaN(cx) && !float.IsNaN(cy) ? (float?)null : outside;
    }

    private static FillRectCommand ReadRect(DisplayList list)
    {
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.FillRect)
            {
                return reader.ReadFillRect();
            }

            Skip(ref reader, tag);
        }

        throw new InvalidOperationException("The list has no rectangle.");
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

    // Each command with what the reader returns for it, as text, so two lists compare command by command.
    private static List<string> Commands(DisplayList list)
    {
        var commands = new List<string>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            commands.Add(
                tag switch
                {
                    CommandTag.FillRect => reader.ReadFillRect().ToString(),
                    CommandTag.Shape => reader.ReadShape().ToString(),
                    CommandTag.Shadow => reader.ReadShadow().ToString(),
                    CommandTag.PushState => reader.ReadState(out _).ToString(),
                    CommandTag.PushOpacity => reader.ReadGroup().ToString(),
                    CommandTag.GlyphRun => reader.ReadGlyphRun().Glyphs.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CommandTag.Pop => Pop(ref reader),
                    _ => reader.ReadCurve().ToString(),
                }
            );
        }

        return commands;
    }

    private static string Pop(ref DisplayListReader reader)
    {
        reader.ReadPop();
        return "Pop";
    }

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
