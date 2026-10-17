using System.Numerics;
using System.Runtime.InteropServices;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// The per-instance data of one quad, as the vertex shader reads it (design §5.0.1): a solid rectangle, or a glyph whose
/// coverage is copied from the atlas. Both kinds share one instance buffer and one draw, so the display list's order is the
/// painting order. Written straight into the mapped instance buffer; no managed copy in between.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct QuadInstance
{
    /// <summary>
    /// The page of a solid rectangle: no atlas.
    /// </summary>
    public const uint SolidPage = uint.MaxValue;

    /// <summary>
    /// The left, top, width, and height, in physical pixels (whole pixels for a glyph).
    /// </summary>
    public Vector4 Rect;

    /// <summary>
    /// The color, <see cref="Color.Packed"/>.
    /// </summary>
    public uint Color;

    /// <summary>
    /// The top-left texel of a glyph's coverage in its page: x in the low 16 bits, y in the high 16 bits.
    /// </summary>
    public uint AtlasOrigin;

    /// <summary>
    /// The atlas page (the slice of the atlas texture array) of a glyph; <see cref="SolidPage"/> for a solid rectangle.
    /// </summary>
    public uint Page;
}
