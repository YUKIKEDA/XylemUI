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
    private readonly ReadOnlySpan<int> _runPaints;
    private readonly ReadOnlySpan<GlyphInstance> _glyphs;
    private readonly ReadOnlySpan<ShapeRecord> _shapes;
    private readonly ReadOnlySpan<CurveRecord> _curves;
    private readonly ReadOnlySpan<TextPaint> _textPaints;
    private readonly ReadOnlySpan<GroupRecord> _groups;
    private readonly ReadOnlySpan<StateRecord> _states;
    private readonly ReadOnlySpan<ClipCommand> _clips;
    private int _command;
    private int _rect;
    private int _color;
    private int _run;
    private int _glyph;
    private int _shape;
    private int _curve;
    private int _group;
    private int _state;
    private CommandTag? _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayListReader"/> struct before the first command of <paramref name="list"/>.
    /// </summary>
    /// <param name="list">The display list to read.</param>
    internal DisplayListReader(DisplayList list)
    {
        _commands = list.Commands;
        _rects = list.Rects;
        _colors = list.Colors;
        _runLengths = list.RunLengths;
        _runCoverages = list.RunCoverages;
        _runPaints = list.RunPaints;
        _glyphs = list.Glyphs;
        _shapes = list.Shapes;
        _curves = list.Curves;
        _textPaints = list.TextPaints;
        _groups = list.Groups;
        _states = list.States;
        _clips = list.Clips;
        Brushes = list.Brushes;
    }

    /// <summary>
    /// Gets the gradients of the list, in the order of their first use: <see cref="ShapeCommand.FillBrush"/>,
    /// <see cref="ShapeCommand.BorderBrush"/>, <see cref="CurveCommand.Brush"/>, and <see cref="GlyphRunCommand.Brush"/> index into it.
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
                || _curve != _curves.Length
                || _group != _groups.Length
                || _state != _states.Length
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
        int paint = _runPaints[_run - 1];
        Color color = Unpack(_colors[_color++]);
        if (paint < 0)
        {
            return new GlyphRunCommand(color, glyphs, coverage);
        }

        TextPaint text = _textPaints[paint];
        return new GlyphRunCommand(color, glyphs, coverage, text.Brush, text.Box);
    }

    /// <summary>
    /// Reads the current command as a line, an ellipse, an arc, or a sector (<see cref="CommandTag.Line"/> to
    /// <see cref="CommandTag.Sector"/>).
    /// </summary>
    /// <returns>The command.</returns>
    public CurveCommand ReadCurve()
    {
        if (_current is not (CommandTag.Line or CommandTag.FillEllipse or CommandTag.StrokeEllipse or CommandTag.Arc or CommandTag.Sector))
        {
            Invariant.Fail("ReadCurve was called for a command that is not a line, an ellipse, an arc, or a sector.");
        }

        _current = null;
        ReadOnlySpan<float> geometry = _rects.Slice(_rect, 4);
        _rect += 4;
        Color color = Unpack(_colors[_color++]);
        ref readonly CurveRecord record = ref _curves[_curve++];
        return new CurveCommand(
            geometry[0],
            geometry[1],
            geometry[2],
            geometry[3],
            color,
            record.Brush,
            record.Box,
            record.Width,
            record.Cap,
            record.HasDash ? record.Dash : null,
            record.StartAngle,
            record.SweepAngle
        );
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

    /// <summary>
    /// Reads the current command as the start of an opacity group.
    /// </summary>
    /// <returns>The group. An empty group (its bounds have no area) has nothing to draw.</returns>
    public GroupCommand ReadGroup()
    {
        if (_current != CommandTag.PushOpacity)
        {
            Invariant.Fail("ReadGroup was called for a command that is not a PushOpacity.");
        }

        _current = null;
        _color++;
        ref readonly GroupRecord record = ref _groups[_group++];
        float width = MathF.Max(0, record.Right - record.Left);
        float height = MathF.Max(0, record.Bottom - record.Top);
        return new GroupCommand(width > 0 ? record.Left : 0, height > 0 ? record.Top : 0, width, height, record.Alpha, record.Layer, record.Clip);
    }

    /// <summary>
    /// Reads the current command as the start of a clip or a transform (row R4d).
    /// </summary>
    /// <param name="clips">The clips the state applies per pixel, at most <see cref="DisplayList.MaxRoundedClips"/>.</param>
    /// <returns>The state the commands up to the matching <see cref="CommandTag.Pop"/> are drawn with.</returns>
    public StateCommand ReadState(out ReadOnlySpan<ClipCommand> clips)
    {
        if (_current != CommandTag.PushState)
        {
            Invariant.Fail("ReadState was called for a command that is not a PushState.");
        }

        _current = null;
        _color++;
        return GetState(_state++, out clips);
    }

    /// <summary>
    /// Returns a state read before, again: for a renderer that draws the state's commands another way inside a layer (row R4d).
    /// </summary>
    /// <param name="index">The state's <see cref="StateCommand.Index"/>.</param>
    /// <param name="clips">The clips the state applies per pixel.</param>
    /// <returns>The state.</returns>
    public readonly StateCommand GetState(int index, out ReadOnlySpan<ClipCommand> clips)
    {
        ref readonly StateRecord record = ref _states[index];
        clips = _clips.Slice(record.ClipStart, record.ClipCount);
        return new StateCommand(record.Transform, record.TextScale, record.ClipRect, record.Bounds, index);
    }

    /// <summary>
    /// Reads the current command as the end of the innermost group, clip, or transform.
    /// </summary>
    public void ReadPop()
    {
        if (_current != CommandTag.Pop)
        {
            Invariant.Fail("ReadPop was called for a command that is not a Pop.");
        }

        _current = null;
        _color++;
    }

    private static Color Unpack(uint packed) => new((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
}
