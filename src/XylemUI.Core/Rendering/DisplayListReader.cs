namespace XylemUI;

/// <summary>
/// Reads the commands of a <see cref="DisplayList"/> in order, each stream with its own cursor (design §5.0.1).
/// </summary>
/// <remarks>
/// Call <see cref="TryNext"/>, then the read method of the tag it returned. Reading past a stream, reading the wrong kind, or a
/// stream with data left at the end is a bug in the encoding and fails the invariant.
/// </remarks>
public ref struct DisplayListReader
{
    private readonly ReadOnlySpan<CommandTag> _commands;
    private readonly ReadOnlySpan<float> _rects;
    private readonly ReadOnlySpan<uint> _colors;
    private int _command;
    private int _rect;
    private int _color;
    private CommandTag? _current;

    internal DisplayListReader(DisplayList list)
    {
        _commands = list.Commands;
        _rects = list.Rects;
        _colors = list.Colors;
    }

    /// <summary>
    /// Moves to the next command.
    /// </summary>
    /// <param name="tag">The kind of the command.</param>
    /// <returns><see langword="false"/> when every command was read.</returns>
    public bool TryNext(out CommandTag tag)
    {
        if (_current is not null)
        {
            Invariant.Fail("A display list command was skipped without being read.");
        }

        if (_command == _commands.Length)
        {
            if (_rect != _rects.Length || _color != _colors.Length)
            {
                Invariant.Fail("A display list stream has data left after its last command.");
            }

            tag = default;
            return false;
        }

        tag = _commands[_command++];
        _current = tag;
        return true;
    }

    /// <summary>
    /// Reads the current command as a solid rectangle.
    /// </summary>
    /// <returns>The rectangle.</returns>
    public FillRectCommand ReadFillRect()
    {
        if (_current != CommandTag.FillRect)
        {
            Invariant.Fail("ReadFillRect was called for a command that is not a FillRect.");
        }

        _current = null;
        ReadOnlySpan<float> rect = _rects.Slice(_rect, 4);
        _rect += 4;
        uint packed = _colors[_color++];
        var color = new Color((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
        return new FillRectCommand(rect[0], rect[1], rect[2], rect[3], color);
    }
}
