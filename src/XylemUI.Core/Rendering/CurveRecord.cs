using System.Numerics;

namespace XylemUI;

/// <summary>
/// The values of a line, an ellipse, an arc, or a sector beyond its geometry (in the rectangle stream) and its color, in physical
/// pixels (row R4b). <see cref="CurveCommand"/> has the meaning of each.
/// </summary>
/// <remarks>
/// A stream of their own, so that the records of shapes and shadows do not grow with them: the shapes scene's display list stage
/// took 3 µs longer with these fields in <see cref="ShapeRecord"/>.
/// </remarks>
internal struct CurveRecord
{
    /// <summary>The gradient, as an index into the list's brushes, or -1.</summary>
    public int Brush;

    /// <summary>The box the gradient's fractions are of: left, top, width, and height.</summary>
    public Vector4 Box;

    /// <summary>The width of the line; 0 for a filled ellipse or a sector.</summary>
    public float Width;

    /// <summary>The shape of the ends.</summary>
    public LineCap Cap;

    /// <summary>Whether <see cref="Dash"/> holds a dash pattern.</summary>
    public bool HasDash;

    /// <summary>The dash pattern, its lengths in physical pixels, when <see cref="HasDash"/> is <see langword="true"/>.</summary>
    public Dash Dash;

    /// <summary>Where an arc or a sector starts, in radians clockwise from up.</summary>
    public float StartAngle;

    /// <summary>How far an arc or a sector runs clockwise from <see cref="StartAngle"/>, in radians.</summary>
    public float SweepAngle;
}
