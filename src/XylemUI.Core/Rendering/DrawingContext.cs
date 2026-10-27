using System.Numerics;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// What a node's drawing function writes with (design §5.0, "Hit regions and DrawingContext"; row R4e): the display list's commands,
/// under the same names, and the node's hit regions. Scopes end with <c>using</c> on the <see cref="DrawingScope"/> a push returns.
/// It is called each time the display list is built, so what it draws and where it is hit are made again together.
/// </summary>
/// <remarks>
/// The framework makes it for the node it draws (row V1); it holds the list and the node and allocates nothing.
/// </remarks>
public readonly ref struct DrawingContext
{
    private readonly DisplayList _list;
    private readonly NodeHandle _node;
    private readonly WindowFrames? _frames;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingContext"/> struct for a node.
    /// </summary>
    /// <param name="list">The display list of the frame.</param>
    /// <param name="node">The node drawn, whose hit regions it writes.</param>
    /// <param name="frames">The window's frames, whose tree's text tools and glyph atlas lay out and draw a <see cref="TextLayout"/>;
    /// <see langword="null"/> for a context with no window, which draws no text layout.</param>
    internal DrawingContext(DisplayList list, NodeHandle node, WindowFrames? frames = null)
    {
        _list = list;
        _node = node;
        _frames = frames;
    }

    /// <summary>
    /// Shapes a text layout and breaks it into lines at a width (design §3.2, "Text from a draw function"). The same text, style, and
    /// width hit its caches.
    /// </summary>
    /// <param name="layout">The text.</param>
    /// <param name="maxWidth">The width the lines break at, in logical pixels; <see cref="float.PositiveInfinity"/> for the hard
    /// breaks only.</param>
    /// <returns>The size of the lines' box, in logical pixels.</returns>
    /// <exception cref="InvalidOperationException">The window's tree has no text tools, or the context has no window.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWidth"/> is negative or not a number.</exception>
    public Size LayoutText(TextLayout layout, float maxWidth)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!(maxWidth >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "The width is a number at or above 0.");
        }

        TextContext context =
            _frames?.Tree.Text ?? throw new InvalidOperationException("The window's tree has no text tools to lay the text out with.");
        Size size = LayoutEngine.LayOutText(context, layout.Leaf, maxWidth);
        layout.Laid = true;
        return size;
    }

    /// <summary>
    /// Draws the lines <see cref="LayoutText"/> made last, with the top left of their box at a point.
    /// </summary>
    /// <param name="layout">The text.</param>
    /// <param name="x">The left edge, in logical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="color">The color of the text.</param>
    /// <exception cref="InvalidOperationException">The text was not laid out since it last changed, or the context has no window.</exception>
    public void DrawText(TextLayout layout, float x, float y, Color color)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!layout.Laid || _frames is null)
        {
            throw new InvalidOperationException("The text is laid out with LayoutText before it is drawn.");
        }

        _frames.DrawLaidText(layout.Leaf, x, y, color);
    }

    /// <summary>
    /// Appends the hit region of the node drawn: a rectangle with rounded corners, given in logical pixels, in the clips and the
    /// transform open now. A point hits it where a shape of the same rectangle covers half a pixel or more (design §7, "Hit
    /// testing").
    /// </summary>
    /// <param name="x">The left edge, in logical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="radii">The radius of each corner; when the two on a side sum past it, all are scaled down as CSS does.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An edge is not a number, or the width, the height, or a radius is negative or not a number.
    /// </exception>
    public void HitRegion(float x, float y, float width, float height, in CornerRadii radii) => _list.HitRegion(_node, x, y, width, height, radii);

    /// <inheritdoc cref="DisplayList.PushOpacity(float)"/>
    /// <returns>The scope, whose <see cref="DrawingScope.Dispose"/> ends the group.</returns>
    public DrawingScope PushOpacity(float alpha)
    {
        _list.PushOpacity(alpha);
        return new DrawingScope(_list);
    }

    /// <inheritdoc cref="DisplayList.PushClip(float, float, float, float, in CornerRadii)"/>
    /// <returns>The scope, whose <see cref="DrawingScope.Dispose"/> ends the clip.</returns>
    public DrawingScope PushClip(float x, float y, float width, float height, in CornerRadii radii)
    {
        _list.PushClip(x, y, width, height, radii);
        return new DrawingScope(_list);
    }

    /// <inheritdoc cref="DisplayList.PushTransform(in Matrix3x2)"/>
    /// <returns>The scope, whose <see cref="DrawingScope.Dispose"/> ends the transform.</returns>
    public DrawingScope PushTransform(in Matrix3x2 transform)
    {
        _list.PushTransform(transform);
        return new DrawingScope(_list);
    }

    /// <inheritdoc cref="DisplayList.FillRect(float, float, float, float, Color)"/>
    public void FillRect(float x, float y, float width, float height, Color color) => _list.FillRect(x, y, width, height, color);

    /// <inheritdoc cref="DisplayList.FillRect(float, float, float, float, in Paint, in CornerRadii, Border?)"/>
    public void FillRect(float x, float y, float width, float height, in Paint fill, in CornerRadii radii, Border? border = null) =>
        _list.FillRect(x, y, width, height, fill, radii, border);

    /// <inheritdoc cref="DisplayList.DrawShadow(float, float, float, float, in CornerRadii, in BoxShadow)"/>
    public void DrawShadow(float x, float y, float width, float height, in CornerRadii radii, in BoxShadow shadow) =>
        _list.DrawShadow(x, y, width, height, radii, shadow);

    /// <inheritdoc cref="DisplayList.DrawLine(float, float, float, float, in Stroke)"/>
    public void DrawLine(float x1, float y1, float x2, float y2, in Stroke stroke) => _list.DrawLine(x1, y1, x2, y2, stroke);

    /// <inheritdoc cref="DisplayList.FillEllipse(float, float, float, float, in Paint)"/>
    public void FillEllipse(float centerX, float centerY, float radiusX, float radiusY, in Paint fill) =>
        _list.FillEllipse(centerX, centerY, radiusX, radiusY, fill);

    /// <inheritdoc cref="DisplayList.DrawEllipse(float, float, float, float, in Stroke)"/>
    public void DrawEllipse(float centerX, float centerY, float radiusX, float radiusY, in Stroke stroke) =>
        _list.DrawEllipse(centerX, centerY, radiusX, radiusY, stroke);

    /// <inheritdoc cref="DisplayList.DrawArc(float, float, float, float, float, in Stroke)"/>
    public void DrawArc(float centerX, float centerY, float radius, float startAngle, float sweepAngle, in Stroke stroke) =>
        _list.DrawArc(centerX, centerY, radius, startAngle, sweepAngle, stroke);

    /// <inheritdoc cref="DisplayList.FillSector(float, float, float, float, float, in Paint)"/>
    public void FillSector(float centerX, float centerY, float radius, float startAngle, float sweepAngle, in Paint fill) =>
        _list.FillSector(centerX, centerY, radius, startAngle, sweepAngle, fill);

    /// <inheritdoc cref="DisplayList.GlyphRun(ReadOnlySpan{GlyphInstance}, Color, GlyphCoverage)"/>
    public void GlyphRun(ReadOnlySpan<GlyphInstance> glyphs, Color color, GlyphCoverage coverage = GlyphCoverage.Gray) =>
        _list.GlyphRun(glyphs, color, coverage);

    /// <inheritdoc cref="DisplayList.DrawText(ReadOnlySpan{ShapedGlyph}, Font, float, GlyphAtlas, float, float, Color, GlyphCoverage, bool)"/>
    public float DrawText(
        ReadOnlySpan<ShapedGlyph> glyphs,
        Font font,
        float size,
        GlyphAtlas atlas,
        float x,
        float baseline,
        Color color,
        GlyphCoverage coverage = GlyphCoverage.Gray,
        bool aliased = false
    ) => _list.DrawText(glyphs, font, size, atlas, x, baseline, color, coverage, aliased);

    /// <inheritdoc cref="DisplayList.DrawText(ReadOnlySpan{ShapedGlyph}, Font, float, GlyphAtlas, float, float, GradientBrush, float, float, float, float, GlyphCoverage, bool)"/>
    public float DrawText(
        ReadOnlySpan<ShapedGlyph> glyphs,
        Font font,
        float size,
        GlyphAtlas atlas,
        float x,
        float baseline,
        GradientBrush brush,
        float boxX,
        float boxY,
        float boxWidth,
        float boxHeight,
        GlyphCoverage coverage = GlyphCoverage.Gray,
        bool aliased = false
    ) => _list.DrawText(glyphs, font, size, atlas, x, baseline, brush, boxX, boxY, boxWidth, boxHeight, coverage, aliased);
}
