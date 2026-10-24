using System.Numerics;

namespace XylemUI.Core.Tests;

// What the display list writes for lines, ellipses, arcs, and sectors (roadmap row R4b, design §5.0, "The API of lines, ellipses,
// arcs, and the paint of text"): the values in physical pixels, the box of a gradient, an arc made to run clockwise, the shapes
// that draw nothing, and the values it refuses.
public sealed class CurveListTests
{
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Blue = Color.FromRgb(0x3366CC);

    /// <summary>
    /// A line is written in physical pixels, not put on pixels, with its stroke times the scale and the box of its two ends grown
    /// by half the width for its gradient.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list reset at scale 1.25 and a linear gradient.
    ///
    /// Steps:
    /// - Draw a line from (10, 20) to (50, 20.3), 4 wide, with the gradient, round caps, and dashes 6 and 2 with offset 1; read it.
    ///
    /// Expected:
    /// - A Line from (12.5, 25) to (62.5, 25.375), brush 0, box (10, 22.5, 55, 5.375), width 5, round caps, dashes 7.5 and 2.5
    ///   with offset 1.25; one shape row.
    /// </remarks>
    [Fact]
    public void DrawLine_AtAScale_IsInPhysicalPixelsWithItsBox()
    {
        GradientBrush brush = GradientBrush.Linear(90, [new(0, Red), new(1, Blue)]);
        var list = new DisplayList();
        list.Reset(1.25f);

        list.DrawLine(10, 20, 50, 20.3f, new Stroke(brush, 4, LineCap.Round, new Dash(6, 2, 1)));

        Assert.Equal(1, list.ShapeCount);
        CurveCommand line = ReadOne(list, CommandTag.Line);
        Assert.Equal((12.5f, 25f, 62.5f), (line.A, line.B, line.C));
        Assert.Equal(25.375f, line.D, 0.0001f);
        Assert.Equal(0, line.Brush);
        AssertNear(new Vector4(10, 22.5f, 55, 5.375f), line.Box);
        Assert.Equal((5f, LineCap.Round), (line.Width, line.Cap));
        Assert.Equal(new Dash(7.5f, 2.5f, 1.25f), line.Dash);
    }

    /// <summary>
    /// A filled ellipse's box is the ellipse's; an outline's is that grown by half the width; both in physical pixels.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list reset at scale 2.
    ///
    /// Steps:
    /// - Fill an ellipse at (20, 30) with radii 10 and 5; outline one at (60, 30) with radii 8 and 4, 2 wide; read both.
    ///
    /// Expected:
    /// - A FillEllipse at (40, 60) with radii 20 and 10, width 0, box (20, 50, 40, 20); a StrokeEllipse at (120, 60) with radii
    ///   16 and 8, width 4, box (102, 50, 36, 20).
    /// </remarks>
    [Fact]
    public void Ellipse_AtAScale_IsItsCenterRadiiAndBox()
    {
        var list = new DisplayList();
        list.Reset(2);

        list.FillEllipse(20, 30, 10, 5, Red);
        list.DrawEllipse(60, 30, 8, 4, new Stroke(Blue, 2));

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag first));
        Assert.Equal(CommandTag.FillEllipse, first);
        CurveCommand fill = reader.ReadCurve();
        Assert.Equal((40f, 60f, 20f, 10f, 0f), (fill.A, fill.B, fill.C, fill.D, fill.Width));
        AssertNear(new Vector4(20, 50, 40, 20), fill.Box);
        Assert.True(reader.TryNext(out CommandTag second));
        Assert.Equal(CommandTag.StrokeEllipse, second);
        CurveCommand outline = reader.ReadCurve();
        Assert.Equal((120f, 60f, 16f, 8f, 4f), (outline.A, outline.B, outline.C, outline.D, outline.Width));
        AssertNear(new Vector4(102, 50, 36, 20), outline.Box);
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// A dashed outline of a circle is a dashed arc of a whole turn from the top.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Outline a circle at (50, 50) with radius 20, 3 wide, with dashes 5 and 3; read it.
    ///
    /// Expected:
    /// - An Arc at (50, 50) with radius 20, start 0, sweep 2π, the dashes, and the box (28.5, 28.5, 43, 43).
    /// </remarks>
    [Fact]
    public void DrawEllipse_DashedCircle_IsAWholeArcFromTheTop()
    {
        var list = new DisplayList();

        list.DrawEllipse(50, 50, 20, 20, new Stroke(Red, 3, LineCap.Flat, new Dash(5, 3)));

        CurveCommand arc = ReadOne(list, CommandTag.Arc);
        Assert.Equal((50f, 50f, 20f), (arc.A, arc.B, arc.C));
        Assert.Equal((0f, MathF.Tau), (arc.StartAngle, arc.SweepAngle));
        Assert.Equal(new Dash(5, 3), arc.Dash);
        AssertNear(new Vector4(28.5f, 28.5f, 43, 43), arc.Box);
    }

    /// <summary>
    /// A counterclockwise arc is written running clockwise from its other end, its dashes moved to lie where they would from its
    /// own start; a sweep of a turn or more is a whole turn from the start.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Draw an arc at (0, 0), radius 40, from 90 degrees for -120 with dashes 10 and 6 (offset 0); one from 200 degrees for 450;
    ///   read both.
    ///
    /// Expected:
    /// - The first starts at 330 degrees and sweeps 120, with the offset (10 − 40 × 2π / 3) modulo 16 (6.224); the second starts at
    ///   200 degrees and sweeps 2π.
    /// </remarks>
    [Fact]
    public void DrawArc_Counterclockwise_RunsClockwiseFromItsOtherEnd()
    {
        var list = new DisplayList();

        list.DrawArc(0, 0, 40, 90, -120, new Stroke(Red, 4, LineCap.Round, new Dash(10, 6)));
        list.DrawArc(0, 0, 40, 200, 450, new Stroke(Red, 4));

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out _));
        CurveCommand reversed = reader.ReadCurve();
        Assert.Equal(330 * MathF.PI / 180, reversed.StartAngle, 0.0001f);
        Assert.Equal(120 * MathF.PI / 180, reversed.SweepAngle, 0.0001f);
        float moved = (10 - (40 * 2 * MathF.PI / 3)) % 16;
        Assert.Equal(moved + 16, reversed.Dash!.Value.Offset, 0.001f);
        Assert.True(reader.TryNext(out _));
        CurveCommand whole = reader.ReadCurve();
        Assert.Equal(200 * MathF.PI / 180, whole.StartAngle, 0.0001f);
        Assert.Equal(MathF.Tau, whole.SweepAngle);
    }

    /// <summary>
    /// A sector of a whole turn or more is the filled circle; a counterclockwise one runs clockwise from its other end.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Fill a sector at (30, 30), radius 10, from 0 degrees for 360; one at (60, 30), radius 10, from 60 degrees for -150; read both.
    ///
    /// Expected:
    /// - A FillEllipse at (30, 30) with radii 10 and 10; a Sector starting at 270 degrees (60 − 150 + 360) that sweeps 150, with the
    ///   whole circle's box (50, 20, 20, 20).
    /// </remarks>
    [Fact]
    public void FillSector_WholeTurnAndCounterclockwise_AreTheCircleAndAClockwiseSector()
    {
        var list = new DisplayList();

        list.FillSector(30, 30, 10, 0, 360, Red);
        list.FillSector(60, 30, 10, 60, -150, Red);

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag first));
        Assert.Equal(CommandTag.FillEllipse, first);
        CurveCommand circle = reader.ReadCurve();
        Assert.Equal((30f, 30f, 10f, 10f), (circle.A, circle.B, circle.C, circle.D));
        Assert.True(reader.TryNext(out CommandTag second));
        Assert.Equal(CommandTag.Sector, second);
        CurveCommand sector = reader.ReadCurve();
        Assert.Equal(270 * MathF.PI / 180, sector.StartAngle, 0.0001f);
        Assert.Equal(150 * MathF.PI / 180, sector.SweepAngle, 0.0001f);
        AssertNear(new Vector4(50, 20, 20, 20), sector.Box);
    }

    /// <summary>
    /// A width or a radius of 0 draws nothing, and so does a line of length 0 or an arc of sweep 0 with flat caps, and a sector of
    /// sweep 0; with round or square caps, a line of length 0 and an arc of sweep 0 are their caps.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Draw a line 0 wide, an ellipse with a radius 0 filled and outlined, a line of length 0 and an arc of sweep 0 with flat caps,
    ///   an arc with radius 0, a sector of sweep 0, and a dashed line 0 wide; then a line of length 0 with round caps and an arc of
    ///   sweep 0 with square caps.
    ///
    /// Expected:
    /// - The first writes nothing; the last two write a Line and an Arc.
    /// </remarks>
    [Fact]
    public void Degenerate_FlatOrEmpty_DrawsNothingAndCapsStay()
    {
        var list = new DisplayList();

        list.DrawLine(0, 0, 10, 10, new Stroke(Red, 0));
        list.FillEllipse(10, 10, 0, 5, Red);
        list.DrawEllipse(10, 10, 5, 0, new Stroke(Red, 2));
        list.DrawLine(5, 5, 5, 5, new Stroke(Red, 4));
        list.DrawArc(10, 10, 5, 30, 0, new Stroke(Red, 4));
        list.DrawArc(10, 10, 0, 30, 90, new Stroke(Red, 4));
        list.FillSector(10, 10, 5, 30, 0, Red);
        list.DrawLine(0, 0, 10, 10, new Stroke(Red, 0, LineCap.Round, new Dash(2, 2)));
        Assert.Equal(0, list.CommandCount);

        list.DrawLine(5, 5, 5, 5, new Stroke(Red, 4, LineCap.Round));
        list.DrawArc(10, 10, 5, 30, 0, new Stroke(Red, 4, LineCap.Square));

        Assert.Equal([CommandTag.Line, CommandTag.Arc], Tags(list));
    }

    /// <summary>
    /// A width or a radius that is negative or not a number, an angle that is not a number, a dash pattern with a negative or
    /// unnumbered length or both lengths 0, and a dashed outline of an ellipse that is not a circle are refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Make the case's call.
    ///
    /// Expected:
    /// - It throws the case's exception and writes nothing.
    /// </remarks>
    /// <param name="call">The call.</param>
    [Theory]
    [InlineData("width -1")]
    [InlineData("width NaN")]
    [InlineData("width Infinity")]
    [InlineData("dash on -1")]
    [InlineData("dash off -1")]
    [InlineData("dash both 0")]
    [InlineData("dash offset NaN")]
    [InlineData("radius -1")]
    [InlineData("radius NaN")]
    [InlineData("angle NaN")]
    [InlineData("dashed ellipse")]
    public void InvalidValues_AreRefused(string call)
    {
        var list = new DisplayList();
        Action act = call switch
        {
            "width -1" => () => list.DrawLine(0, 0, 1, 1, new Stroke(Red, -1)),
            "width NaN" => () => list.DrawArc(0, 0, 5, 0, 90, new Stroke(Red, float.NaN)),
            "width Infinity" => () => list.DrawEllipse(0, 0, 5, 5, new Stroke(Red, float.PositiveInfinity)),
            "dash on -1" => () => list.DrawLine(0, 0, 1, 1, new Stroke(Red, 1, LineCap.Flat, new Dash(-1, 2))),
            "dash off -1" => () => list.DrawLine(0, 0, 1, 1, new Stroke(Red, 1, LineCap.Flat, new Dash(1, -2))),
            "dash both 0" => () => list.DrawArc(0, 0, 5, 0, 90, new Stroke(Red, 1, LineCap.Round, new Dash(0, 0))),
            "dash offset NaN" => () => list.DrawLine(0, 0, 1, 1, new Stroke(Red, 1, LineCap.Flat, new Dash(1, 1, float.NaN))),
            "radius -1" => () => list.FillEllipse(0, 0, -1, 5, Red),
            "radius NaN" => () => list.FillSector(0, 0, float.NaN, 0, 90, Red),
            "angle NaN" => () => list.DrawArc(0, 0, 5, float.NaN, 90, new Stroke(Red, 1)),
            _ => () => list.DrawEllipse(0, 0, 10, 5, new Stroke(Red, 1, LineCap.Flat, new Dash(2, 2))),
        };

        bool argument = call.StartsWith("dash", StringComparison.Ordinal);
        if (argument)
        {
            Assert.Throws<ArgumentException>(act);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(act);
        }

        Assert.Equal(0, list.CommandCount);
    }

    private static CurveCommand ReadOne(DisplayList list, CommandTag expected)
    {
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag tag));
        Assert.Equal(expected, tag);
        CurveCommand command = reader.ReadCurve();
        Assert.False(reader.TryNext(out _));
        return command;
    }

    private static List<CommandTag> Tags(DisplayList list)
    {
        var tags = new List<CommandTag>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            tags.Add(tag);
            _ = reader.ReadCurve();
        }

        return tags;
    }

    private static void AssertNear(Vector4 expected, Vector4 actual) =>
        Assert.True(Vector4.Distance(expected, actual) < 0.001f, $"expected {expected}, got {actual}");
}
