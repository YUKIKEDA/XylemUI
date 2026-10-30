using System.Numerics;

namespace XylemUI;

/// <summary>
/// A state of clips and a transform (row R4d), as <see cref="StateCommand"/> gives it to a renderer: the transform of the commands'
/// positions to physical pixels, the text's scale, the rectangle of the straight clips, the bounds of what it lets be drawn, and its
/// clips in the clip stream.
/// </summary>
internal struct StateRecord
{
    /// <summary>The transform of the commands' positions to physical pixels.</summary>
    public Matrix3x2 Transform;

    /// <summary>The scale the glyph runs of the state are drawn at.</summary>
    public float TextScale;

    /// <summary>The rectangle of the straight clips: its left, top, right, and bottom edges.</summary>
    public Vector4 ClipRect;

    /// <summary>The bounds of what the state lets be drawn: left, top, right, and bottom.</summary>
    public Vector4 Bounds;

    /// <summary>The index of the state's first clip in the clip stream.</summary>
    public int ClipStart;

    /// <summary>The number of the state's clips in the clip stream.</summary>
    public int ClipCount;
}
