using System.Numerics;

namespace XylemUI;

// The gradient of brushed text and the box its fractions are of, in physical pixels (row R4). Every glyph run of the text points
// at it, the runs ResolveGlyphs splits included.
internal readonly record struct TextPaint(int Brush, Vector4 Box);
