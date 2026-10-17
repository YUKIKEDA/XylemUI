using System.Numerics;
using System.Runtime.InteropServices;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// The per-instance data of one solid rectangle, as the vertex shader reads it (design §5.0.1). Written straight into the mapped
/// instance buffer; no managed copy in between.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct QuadInstance
{
    /// <summary>
    /// The left, top, width, and height, in physical pixels.
    /// </summary>
    public Vector4 Rect;

    /// <summary>
    /// The color, <see cref="Color.Packed"/>.
    /// </summary>
    public uint Color;
}
