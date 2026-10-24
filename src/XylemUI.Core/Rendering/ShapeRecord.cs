using System.Numerics;

namespace XylemUI;

// The values of a Shape or a Shadow command beyond its rectangle and color, in physical pixels (row R4). A Shape uses the radii,
// the border, and the brushes; a Shadow the radii and the shadow's values.
internal struct ShapeRecord
{
    public CornerRadii Radii;
    public BorderWidths BorderWidths;
    public uint BorderColor;
    public int FillBrush;
    public int BorderBrush;
    public Vector2 Offset;
    public float Sigma;
    public float Spread;
    public bool Inset;
}
