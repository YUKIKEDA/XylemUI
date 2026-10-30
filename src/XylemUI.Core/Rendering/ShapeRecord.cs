using System.Numerics;

namespace XylemUI;

/// <summary>
/// The values of a Shape or a Shadow command beyond its rectangle and color, in physical pixels (row R4). A Shape uses the radii,
/// the border, and the brushes; a Shadow the radii and the shadow's values.
/// </summary>
internal struct ShapeRecord
{
    /// <summary>The radius of each corner.</summary>
    public CornerRadii Radii;

    /// <summary>The width of each edge of a Shape's border; zero for no border.</summary>
    public BorderWidths BorderWidths;

    /// <summary>The packed color of a Shape's border, when <see cref="BorderBrush"/> is -1.</summary>
    public uint BorderColor;

    /// <summary>A Shape's fill gradient, as an index into the list's brushes, or -1.</summary>
    public int FillBrush;

    /// <summary>A Shape's border gradient, as an index into the list's brushes, or -1.</summary>
    public int BorderBrush;

    /// <summary>How far a Shadow is moved from the shape.</summary>
    public Vector2 Offset;

    /// <summary>The standard deviation of a Shadow's blur: half of CSS's blur radius.</summary>
    public float Sigma;

    /// <summary>How much a Shadow grows before it is blurred.</summary>
    public float Spread;

    /// <summary>Whether a Shadow is drawn inside the shape.</summary>
    public bool Inset;
}
