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
