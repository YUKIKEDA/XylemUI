namespace XylemUI.Tests;

// The display list's reused streams and its reader (S7, design §5.0.1). It has no thread affinity and no static state.
public sealed class DisplayListTests
{
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Blue = Color.FromRgb(0x3366CC);

    /// <summary>
    /// The reader returns the commands in the order they were written, with their values.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list with a red and a semi-transparent blue rectangle.
    ///
    /// Steps:
    /// - Read every command.
    ///
    /// Expected:
    /// - Two FillRect commands with the same rectangles and colors, then the end.
    /// </remarks>
    [Fact]
    public void Reader_ReturnsTheCommandsInOrder()
    {
        var list = new DisplayList();
        var translucent = Blue with { A = 128 };
        list.FillRect(1, 2, 3, 4, Red);
        list.FillRect(5, 6, 7, 8, translucent);

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag first));
        Assert.Equal(CommandTag.FillRect, first);
        Assert.Equal(new FillRectCommand(1, 2, 3, 4, Red), reader.ReadFillRect());
        Assert.True(reader.TryNext(out _));
        Assert.Equal(new FillRectCommand(5, 6, 7, 8, translucent), reader.ReadFillRect());
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// Reset keeps the arrays: the next frame overwrites the same slots, and only a frame larger than the capacity grows them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list with a capacity of 4 commands.
    ///
    /// Steps:
    /// - Write 4 commands, reset, write 4 again; then write 9 (more than the capacity); reset and write 9 again.
    ///
    /// Expected:
    /// - No growth for the two frames of 4. The first frame of 9 grows twice (4 → 8 at the 5th command, 8 → 16 at the 9th); the
    ///   repeated frame of 9 grows none. The last frame reads back all 9 commands.
    /// </remarks>
    [Fact]
    public void Reset_KeepsTheArraysAndGrowsOnlyWhenNeeded()
    {
        var list = new DisplayList(commandCapacity: 4);
        Write(list, 4);
        list.Reset();
        Write(list, 4);
        Assert.Equal(0, list.GrowthCount);

        list.Reset();
        Write(list, 9);
        int afterFirstLargeFrame = list.GrowthCount;
        list.Reset();
        Write(list, 9);

        Assert.Equal(2, afterFirstLargeFrame);
        Assert.Equal(2, list.GrowthCount);
        DisplayListReader reader = list.GetReader();
        int count = 0;
        while (reader.TryNext(out _))
        {
            FillRectCommand rect = reader.ReadFillRect();
            Assert.Equal(count, rect.X);
            count++;
        }

        Assert.Equal(9, count);
    }

    /// <summary>
    /// Moving past a command without reading it is an encoding bug and fails the invariant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release ends the process). A display list with two commands.
    ///
    /// Steps:
    /// - Call TryNext twice without reading the first command.
    ///
    /// Expected:
    /// - <see cref="InvariantViolationException"/>.
    /// </remarks>
    [Fact]
    public void Reader_SkippingACommand_FailsTheInvariant()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
        var list = new DisplayList();
        Write(list, 2);

        Assert.Throws<InvariantViolationException>(() =>
        {
            DisplayListReader reader = list.GetReader();
            _ = reader.TryNext(out _);
            _ = reader.TryNext(out _);
        });
    }

    /// <summary>
    /// Glyph runs and rectangles read back in the order they were written, each run with its own glyphs and color; an empty run
    /// appends nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An empty display list.
    ///
    /// Steps:
    /// - Write a rectangle, a run of 2 glyphs in red, an empty run, a run of 1 glyph in blue, and a rectangle; read every command.
    ///
    /// Expected:
    /// - 4 commands and 3 glyphs: FillRect, GlyphRun (the 2 glyphs, red), GlyphRun (the 1 glyph, blue), FillRect, then the end.
    /// </remarks>
    [Fact]
    public void Reader_GlyphRunsBetweenRectangles_ReadBackInOrder()
    {
        var list = new DisplayList();
        GlyphInstance[] first = [new(10, 20, 5, 6, 0, 1, 1), new(16, 20, 4, 6, 1, 7, 1)];
        GlyphInstance[] second = [new(30, 40, 8, 9, 2, 3, 4)];
        list.FillRect(0, 0, 1, 1, Red);
        list.GlyphRun(first, Red);
        list.GlyphRun([], Blue);
        list.GlyphRun(second, Blue);
        list.FillRect(2, 2, 1, 1, Blue);

        Assert.Equal((4, 3), (list.CommandCount, list.GlyphCount));
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag tag));
        Assert.Equal(CommandTag.FillRect, tag);
        Assert.Equal(new FillRectCommand(0, 0, 1, 1, Red), reader.ReadFillRect());
        Assert.True(reader.TryNext(out tag));
        Assert.Equal(CommandTag.GlyphRun, tag);
        GlyphRunCommand run = reader.ReadGlyphRun();
        Assert.Equal(Red, run.Color);
        Assert.Equal(first, run.Glyphs.ToArray());
        Assert.True(reader.TryNext(out tag));
        Assert.Equal(CommandTag.GlyphRun, tag);
        run = reader.ReadGlyphRun();
        Assert.Equal(Blue, run.Color);
        Assert.Equal(second, run.Glyphs.ToArray());
        Assert.True(reader.TryNext(out tag));
        Assert.Equal(new FillRectCommand(2, 2, 1, 1, Blue), reader.ReadFillRect());
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// The glyph stream grows on its own, when a frame has more glyphs than it holds, and not again for the same frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list with room for 4 glyphs.
    ///
    /// Steps:
    /// - Write a run of 3 glyphs, reset, write it again; then a run of 3 and a run of 6; reset and write those two again.
    ///
    /// Expected:
    /// - No growth for the frames of 3. The frame of 3 + 6 grows the glyph stream once (to 9: twice 4 is less than needed), the
    ///   repeated one not at all; its runs read back with their own glyphs.
    /// </remarks>
    [Fact]
    public void GlyphRun_MoreGlyphsThanTheCapacity_GrowsOnce()
    {
        var list = new DisplayList(glyphCapacity: 4);
        GlyphInstance[] three = [.. Enumerable.Range(0, 3).Select(i => new GlyphInstance(i, 0, 1, 1, 0, 0, 0))];
        GlyphInstance[] six = [.. Enumerable.Range(10, 6).Select(i => new GlyphInstance(i, 0, 1, 1, 0, 0, 0))];
        list.GlyphRun(three, Red);
        list.Reset();
        list.GlyphRun(three, Red);
        Assert.Equal(0, list.GrowthCount);

        list.Reset();
        list.GlyphRun(three, Red);
        list.GlyphRun(six, Blue);
        list.Reset();
        list.GlyphRun(three, Red);
        list.GlyphRun(six, Blue);

        Assert.Equal(1, list.GrowthCount);
        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out _));
        Assert.Equal(three, reader.ReadGlyphRun().Glyphs.ToArray());
        Assert.True(reader.TryNext(out _));
        Assert.Equal(six, reader.ReadGlyphRun().Glyphs.ToArray());
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// Reading a glyph run as a rectangle, or a rectangle as a glyph run, is an encoding bug and fails the invariant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release ends the process). A display list with a glyph run, and one with a rectangle.
    ///
    /// Steps:
    /// - Read the glyph run with ReadFillRect; read the rectangle with ReadGlyphRun.
    ///
    /// Expected:
    /// - <see cref="InvariantViolationException"/> both times.
    /// </remarks>
    [Fact]
    public void Reader_WrongKind_FailsTheInvariant()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
        var glyphs = new DisplayList();
        glyphs.GlyphRun([new GlyphInstance(0, 0, 1, 1, 0, 0, 0)], Red);
        var rects = new DisplayList();
        Write(rects, 1);

        Assert.Throws<InvariantViolationException>(() =>
        {
            DisplayListReader reader = glyphs.GetReader();
            _ = reader.TryNext(out _);
            _ = reader.ReadFillRect();
        });
        Assert.Throws<InvariantViolationException>(() =>
        {
            DisplayListReader reader = rects.GetReader();
            _ = reader.TryNext(out _);
            _ = reader.ReadGlyphRun();
        });
    }

    /// <summary>
    /// A color packs red into the lowest byte, and FromRgb reads a 0xRRGGBB value as opaque.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Pack (0x11, 0x22, 0x33, 0x44); convert 0x3366CC.
    ///
    /// Expected:
    /// - 0x44332211; (0x33, 0x66, 0xCC, 0xFF).
    /// </remarks>
    [Fact]
    public void Color_PacksRedLowestAndReadsRgb()
    {
        Assert.Equal(0x44332211u, new Color(0x11, 0x22, 0x33, 0x44).Packed);
        Assert.Equal(new Color(0x33, 0x66, 0xCC, 0xFF), Color.FromRgb(0x3366CC));
    }

    private static void Write(DisplayList list, int count)
    {
        for (int i = 0; i < count; i++)
        {
            list.FillRect(i, 0, 1, 1, Red);
        }
    }
}
