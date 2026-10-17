using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The paint commands of one frame, in reused arrays of one stream per kind of data (design §5.0, §5.0.1). A renderer reads it
/// with <see cref="GetReader"/>; it never sees the UI tree.
/// </summary>
/// <remarks>
/// <see cref="Reset"/> keeps the arrays and only forgets how much of them is used, so a steady-state frame writes into the
/// slots of the last one and allocates nothing. A stream grows, to twice its size, only when a frame needs more.
/// </remarks>
public sealed class DisplayList
{
    private const int FloatsPerRect = 4;

    private CommandTag[] _commands;
    private float[] _rects;
    private uint[] _colors;
    private int _usedRects;
    private int _usedColors;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayList"/> class.
    /// </summary>
    /// <param name="commandCapacity">The number of commands the streams hold before they first grow.</param>
    public DisplayList(int commandCapacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandCapacity);
        _commands = new CommandTag[commandCapacity];
        _rects = new float[commandCapacity * FloatsPerRect];
        _colors = new uint[commandCapacity];
    }

    /// <summary>
    /// Gets the number of commands written since the last <see cref="Reset"/>.
    /// </summary>
    public int CommandCount { get; private set; }

    /// <summary>
    /// Gets the number of times a stream had to grow. A steady state does not raise it.
    /// </summary>
    public int GrowthCount { get; private set; }

    /// <summary>
    /// Forgets the commands, keeping the arrays for the next frame.
    /// </summary>
    public void Reset()
    {
        CommandCount = 0;
        _usedRects = 0;
        _usedColors = 0;
    }

    /// <summary>
    /// Appends a solid rectangle.
    /// </summary>
    /// <param name="x">The left edge, in physical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="color">The color.</param>
    public void FillRect(float x, float y, float width, float height, Color color)
    {
        if (CommandCount == _commands.Length)
        {
            Grow();
        }

        _commands[CommandCount++] = CommandTag.FillRect;
        Span<float> rect = _rects.AsSpan(_usedRects, FloatsPerRect);
        rect[0] = x;
        rect[1] = y;
        rect[2] = width;
        rect[3] = height;
        _usedRects += FloatsPerRect;
        _colors[_usedColors++] = color.Packed;
    }

    /// <summary>
    /// Returns a reader over the commands written since the last <see cref="Reset"/>.
    /// </summary>
    /// <returns>The reader. It must not outlive the next write.</returns>
    public DisplayListReader GetReader() => new(this);

    internal ReadOnlySpan<CommandTag> Commands => _commands.AsSpan(0, CommandCount);

    internal ReadOnlySpan<float> Rects => _rects.AsSpan(0, _usedRects);

    internal ReadOnlySpan<uint> Colors => _colors.AsSpan(0, _usedColors);

    // Every stream grows with the command array, so one check per command is enough (the fast path stays one comparison).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow()
    {
        int capacity = _commands.Length * 2;
        Array.Resize(ref _commands, capacity);
        Array.Resize(ref _rects, capacity * FloatsPerRect);
        Array.Resize(ref _colors, capacity);
        GrowthCount++;
    }
}
