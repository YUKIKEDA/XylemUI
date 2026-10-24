using System.Numerics;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// A run of glyphs in one color or one gradient, read from a <see cref="DisplayList"/>. The coverage of each glyph, from the
/// glyph atlas, is the alpha of the color over the target: one alpha per pixel (grayscale) or one per subpixel (ClearType, design
/// §5.0.3). A run with a gradient takes the gradient's color at each pixel (design §5.0, "The API of lines, ellipses, arcs, and the
/// paint of text").
/// </summary>
/// <param name="color">The color of the text; with a gradient, the brush's first color made opaque.</param>
/// <param name="glyphs">The glyphs; the span is valid until the display list is written again.</param>
/// <param name="coverage">The kind of the glyphs' coverage, which says among which atlas pages their pages are.</param>
/// <param name="brush">The gradient, as an index into <see cref="DisplayListReader.Brushes"/>, or −1 for the color.</param>
/// <param name="box">The box the gradient's fractions are of, in physical pixels: left, top, width, and height.</param>
public readonly ref struct GlyphRunCommand(
    Color color,
    ReadOnlySpan<GlyphInstance> glyphs,
    GlyphCoverage coverage,
    int brush = -1,
    Vector4 box = default
)
{
    /// <summary>
    /// Gets the color of the text.
    /// </summary>
    public Color Color { get; } = color;

    /// <summary>
    /// Gets the glyphs, in the order they were written.
    /// </summary>
    public ReadOnlySpan<GlyphInstance> Glyphs { get; } = glyphs;

    /// <summary>
    /// Gets the kind of the glyphs' coverage.
    /// </summary>
    public GlyphCoverage Coverage { get; } = coverage;

    /// <summary>
    /// Gets the gradient, as an index into <see cref="DisplayListReader.Brushes"/>, or −1 when the run is in <see cref="Color"/>.
    /// </summary>
    public int Brush { get; } = brush;

    /// <summary>
    /// Gets the box the gradient's fractions are of, in physical pixels: left, top, width, and height.
    /// </summary>
    public Vector4 Box { get; } = box;
}
