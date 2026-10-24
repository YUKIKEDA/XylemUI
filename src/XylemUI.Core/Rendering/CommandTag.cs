namespace XylemUI;

/// <summary>
/// The kind of a command in a <see cref="DisplayList"/> (design §5.0.1). The other kinds of the design come with their rows.
/// </summary>
public enum CommandTag : byte
{
    /// <summary>A solid rectangle (<see cref="FillRectCommand"/>).</summary>
    FillRect,

    /// <summary>A run of glyphs in one color (<see cref="GlyphRunCommand"/>).</summary>
    GlyphRun,

    /// <summary>A rounded rectangle with a border and a paint (<see cref="ShapeCommand"/>, row R4).</summary>
    Shape,

    /// <summary>The shadow of a rounded rectangle (<see cref="ShadowCommand"/>, row R4).</summary>
    Shadow,

    /// <summary>A straight line (<see cref="CurveCommand"/>, row R4).</summary>
    Line,

    /// <summary>A filled ellipse (<see cref="CurveCommand"/>, row R4).</summary>
    FillEllipse,

    /// <summary>The outline of an ellipse (<see cref="CurveCommand"/>, row R4).</summary>
    StrokeEllipse,

    /// <summary>An arc of a circle, or a dashed circle (<see cref="CurveCommand"/>, row R4).</summary>
    Arc,

    /// <summary>A filled sector of a circle (<see cref="CurveCommand"/>, row R4).</summary>
    Sector,

    /// <summary>The start of an opacity group (<see cref="GroupCommand"/>, row R4c).</summary>
    PushOpacity,

    /// <summary>The end of the innermost group (row R4c).</summary>
    Pop,
}
