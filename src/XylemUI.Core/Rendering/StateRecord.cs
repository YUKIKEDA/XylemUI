using System.Numerics;

namespace XylemUI;

// A state of clips and a transform (row R4d), as StateCommand gives it to a renderer: the transform of the commands' positions to
// physical pixels, the text's scale, the rectangle of the straight clips, the bounds of what it lets be drawn, and its clips in the
// clip stream.
internal struct StateRecord
{
    public Matrix3x2 Transform;
    public float TextScale;
    public Vector4 ClipRect;
    public Vector4 Bounds;
    public int ClipStart;
    public int ClipCount;
}
