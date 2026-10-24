using System.Numerics;

namespace XylemUI;

// The values of a line, an ellipse, an arc, or a sector beyond its geometry (in the rectangle stream) and its color, in physical
// pixels (row R4b). A stream of their own, so that the records of shapes and shadows do not grow with them: the shapes scene's
// display list stage took 3 µs longer with these fields in ShapeRecord.
internal struct CurveRecord
{
    public int Brush;
    public Vector4 Box;
    public float Width;
    public LineCap Cap;
    public bool HasDash;
    public Dash Dash;
    public float StartAngle;
    public float SweepAngle;
}
