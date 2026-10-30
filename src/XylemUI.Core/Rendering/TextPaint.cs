using System.Numerics;

namespace XylemUI;

/// <summary>
/// The gradient of brushed text and the box its fractions are of, in physical pixels (row R4). Every glyph run of the text points
/// at it, the runs ResolveGlyphs splits included.
/// </summary>
/// <param name="Brush">The gradient, as an index into the list's brushes.</param>
/// <param name="Box">The box the gradient's fractions are of: left, top, width, and height.</param>
internal readonly record struct TextPaint(int Brush, Vector4 Box);
