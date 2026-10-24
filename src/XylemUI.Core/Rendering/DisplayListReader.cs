using XylemUI.Text;

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
    private readonly ReadOnlySpan<int> _runLengths;
    private readonly ReadOnlySpan<GlyphCoverage> _runCoverages;
    private readonly ReadOnlySpan<GlyphInstance> _glyphs;
    private readonly ReadOnlySpan<ShapeRecord> _shapes;
    private int _command;
    private int _rect;
    private int _color;
    private int _run;
    private int _glyph;
    private int _shape;
    private CommandTag? _current;

    internal DisplayListReader(DisplayList list)
    {
        _commands = list.Commands;
        _rects = list.Rects;
        _colors = list.Colors;
        _runLengths = list.RunLengths;
        _runCoverages = list.RunCoverages;
        _glyphs = list.Glyphs;
        _shapes = list.Shapes;
        Brushes = list.Brushes;
    }

    /// <summary>
    /// Gets the gradients of the list, in the order of their first use: <see cref="ShapeCommand.FillBrush"/> and
    /// <see cref="ShapeCommand.BorderBrush"/> index into it.
    /// </summary>
    public ReadOnlySpan<GradientBrush> Brushes { get; }

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
            if (
                _rect != _rects.Length
                || _color != _colors.Length
                || _run != _runLengths.Length
                || _glyph != _glyphs.Length
                || _shape != _shapes.Length
            )
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
        return new FillRectCommand(rect[0], rect[1], rect[2], rect[3], Unpack(packed));
    }

    /// <summary>
    /// Reads the current command as a glyph run.
    /// </summary>
    /// <returns>The run; its glyphs are valid until the display list is written again.</returns>
    public GlyphRunCommand ReadGlyphRun()
    {
        if (_current != CommandTag.GlyphRun)
        {
            Invariant.Fail("ReadGlyphRun was called for a command that is not a GlyphRun.");
        }

        _current = null;
        GlyphCoverage coverage = _runCoverages[_run];
        int length = _runLengths[_run++];
        ReadOnlySpan<GlyphInstance> glyphs = _glyphs.Slice(_glyph, length);
        _glyph += length;
        return new GlyphRunCommand(Unpack(_colors[_color++]), glyphs, coverage);
    }

    /// <summary>
    /// Reads the current command as a rounded rectangle with a border.
    /// </summary>
    /// <returns>The shape.</returns>
    public ShapeCommand ReadShape()
    {
        if (_current != CommandTag.Shape)
        {
            Invariant.Fail("ReadShape was called for a command that is not a Shape.");
        }

        _current = null;
        ReadOnlySpan<float> rect = _rects.Slice(_rect, 4);
        _rect += 4;
        Color fill = Unpack(_colors[_color++]);
        ref readonly ShapeRecord record = ref _shapes[_shape++];
        return new ShapeCommand(
            rect[0],
            rect[1],
            rect[2],
            rect[3],
            fill,
            record.FillBrush,
            record.Radii,
            record.BorderWidths,
            Unpack(record.BorderColor),
            record.BorderBrush
        );
    }

    /// <summary>
    /// Reads the current command as the shadow of a rounded rectangle.
    /// </summary>
    /// <returns>The shadow.</returns>
    public ShadowCommand ReadShadow()
    {
        if (_current != CommandTag.Shadow)
        {
            Invariant.Fail("ReadShadow was called for a command that is not a Shadow.");
        }

        _current = null;
        ReadOnlySpan<float> rect = _rects.Slice(_rect, 4);
        _rect += 4;
        Color color = Unpack(_colors[_color++]);
        ref readonly ShapeRecord record = ref _shapes[_shape++];
        return new ShadowCommand(rect[0], rect[1], rect[2], rect[3], record.Radii, color, record.Offset, record.Sigma, record.Spread, record.Inset);
    }

    private static Color Unpack(uint packed) => new((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
}
