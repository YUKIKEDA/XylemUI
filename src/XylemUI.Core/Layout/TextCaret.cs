using XylemUI.Text.LineBreak;
using XylemUI.Text.Unicode;

namespace XylemUI;

/// <summary>
/// Which side of a position in the text a caret belongs to where two places on the screen show it (design §4.3): the end of one
/// line or the start of the next.
/// </summary>
internal enum Affinity : byte
{
    /// <summary>
    /// The character before the position: the end of the line it ends.
    /// </summary>
    Upstream,

    /// <summary>
    /// The character after the position: the start of the line it starts.
    /// </summary>
    Downstream,
}

/// <summary>
/// A position in a text and where its caret is drawn (design §4.3).
/// </summary>
/// <param name="TextOffset">The position, in UTF-16 code units.</param>
/// <param name="Affinity">Which side of it the caret belongs to.</param>
/// <param name="Rect">The caret: from the line's top to its bottom, of width 0, from the leaf's content box.</param>
internal readonly record struct CaretPosition(int TextOffset, Affinity Affinity, Rect Rect);

/// <summary>
/// The text a text leaf laid out, by position (design §4.3, row T12): the line at a height, the caret at a point, the point of a
/// caret, and the cluster under a point, on the lines its last layout committed. The lines are left to right (bidirectional text
/// is row P03). Positions are from the leaf's content box, its border box less its padding and border.
/// </summary>
internal static class TextCaret
{
    /// <summary>
    /// Returns the line at a height: the one whose top and bottom hold it, the first above them all, and the last below.
    /// </summary>
    /// <param name="leaf">The text leaf, laid out.</param>
    /// <param name="y">The height, from the content box's top.</param>
    /// <param name="top">The line's top.</param>
    /// <param name="height">The line's height.</param>
    /// <returns>The line's index; -1 when the leaf has no committed lines.</returns>
    public static int LineAt(TextLeaf leaf, float y, out float top, out float height)
    {
        top = 0;
        height = 0;
        if (Committed(leaf) is not { } layout)
        {
            return -1;
        }

        ReadOnlySpan<LaidLine> lines = layout.Lines;
        for (int i = 0; i < lines.Length; i++)
        {
            (height, _) = LayoutEngine.TextLine(leaf.Shaped!.Result!, lines[i].Span, leaf.Style);
            if (y < top + height || i == lines.Length - 1)
            {
                return i;
            }

            top += height;
        }

        return -1;
    }

    /// <summary>
    /// Returns the caret nearest a point of a line: before a cluster when the point is in its left half, after it in its right
    /// half (design §4.3). Before the line, its start; after it, its end, before a hard line break that ends it.
    /// </summary>
    /// <param name="leaf">The text leaf, laid out.</param>
    /// <param name="lineIndex">The line, from <see cref="LineAt"/>.</param>
    /// <param name="x">The point, from the content box's left.</param>
    /// <returns>The caret.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The leaf has no such line.</exception>
    public static CaretPosition HitTestText(TextLeaf leaf, int lineIndex, float x)
    {
        LineLayoutResult layout = Committed(leaf) ?? throw new ArgumentOutOfRangeException(nameof(lineIndex), "The text leaf has no lines.");
        ReadOnlySpan<LaidLine> lines = layout.Lines;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)lineIndex, (uint)lines.Length, nameof(lineIndex));
        LaidLine line = lines[lineIndex];
        float top = Top(leaf, lines, lineIndex, out float height);
        ReadOnlySpan<LaidGlyph> glyphs = layout.Glyphs.Slice(line.GlyphStart, line.GlyphCount);
        int end = VisibleEnd(leaf, line.Span);
        float pen = 0;
        for (int g = 0; g < glyphs.Length; )
        {
            int cluster = glyphs[g].Cluster;
            float width = 0;
            int next = g;
            while (next < glyphs.Length && glyphs[next].Cluster == cluster)
            {
                width += glyphs[next].Advance;
                next++;
            }

            int after = next < glyphs.Length ? glyphs[next].Cluster : end;
            if (cluster >= end)
            {
                break;
            }

            if (x < pen + (width / 2))
            {
                return new CaretPosition(cluster, Affinity.Downstream, new Rect(pen, top, 0, height));
            }

            if (x < pen + width)
            {
                return new CaretPosition(after, after == end ? Affinity.Upstream : Affinity.Downstream, new Rect(pen + width, top, 0, height));
            }

            pen += width;
            g = next;
        }

        return new CaretPosition(end, Affinity.Upstream, new Rect(pen, top, 0, height));
    }

    /// <summary>
    /// Returns where the caret of a position is drawn (design §4.3): on the line that holds it, at the left of the cluster it
    /// starts. A position where one line ends and the next starts is on the first with <see cref="Affinity.Upstream"/>, on the
    /// second with <see cref="Affinity.Downstream"/>.
    /// </summary>
    /// <param name="leaf">The text leaf, laid out.</param>
    /// <param name="textOffset">The position, from 0 to the text's length.</param>
    /// <param name="affinity">Which side of it the caret belongs to.</param>
    /// <returns>The caret.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the text, or the leaf has no lines.</exception>
    public static CaretPosition CaretGeometry(TextLeaf leaf, int textOffset, Affinity affinity)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)textOffset, (uint)leaf.Length, nameof(textOffset));
        LineLayoutResult layout = Committed(leaf) ?? throw new ArgumentOutOfRangeException(nameof(textOffset), "The text leaf has no lines.");
        ReadOnlySpan<LaidLine> lines = layout.Lines;
        int index = lines.Length - 1;
        for (int i = 0; i < lines.Length; i++)
        {
            LineSpan span = lines[i].Span;
            bool before = affinity == Affinity.Upstream ? textOffset <= VisibleEnd(leaf, span) : textOffset < span.End;
            if (before)
            {
                index = i;
                break;
            }
        }

        LaidLine line = lines[index];
        float top = Top(leaf, lines, index, out float height);
        float pen = 0;
        foreach (LaidGlyph glyph in layout.Glyphs.Slice(line.GlyphStart, line.GlyphCount))
        {
            if (glyph.Cluster >= textOffset)
            {
                break;
            }

            pen += glyph.Advance;
        }

        return new CaretPosition(textOffset, affinity, new Rect(pen, top, 0, height));
    }

    /// <summary>
    /// Returns the cluster whose box holds a point: from its line's top to its bottom, and from its left to its advance's end, as
    /// Flutter's paragraph hit-tests its spans (<c>rendering/paragraph.dart</c> lines 851 to 870).
    /// </summary>
    /// <param name="leaf">The text leaf, laid out.</param>
    /// <param name="x">The point's x, from the content box's left.</param>
    /// <param name="y">The point's y, from the content box's top.</param>
    /// <returns>The cluster's first code unit; -1 when no cluster holds the point.</returns>
    public static int ClusterAt(TextLeaf leaf, float x, float y)
    {
        int index = LineAt(leaf, y, out float top, out float height);
        if (index < 0 || y < top || y >= top + height || x < 0)
        {
            return -1;
        }

        LineLayoutResult layout = Committed(leaf)!;
        LaidLine line = layout.Lines[index];
        float pen = 0;
        foreach (LaidGlyph glyph in layout.Glyphs.Slice(line.GlyphStart, line.GlyphCount))
        {
            pen += glyph.Advance;
            if (x < pen)
            {
                return glyph.Cluster;
            }
        }

        return -1;
    }

    private static LineLayoutResult? Committed(TextLeaf leaf) =>
        leaf.Shaped?.Result is not null && leaf.LineBreaks is { Committed: >= 0 } cache ? cache[cache.Committed].Layout : null;

    // The top and the height of a line, the lines above it stacked as the measurement stacks them.
    private static float Top(TextLeaf leaf, ReadOnlySpan<LaidLine> lines, int index, out float height)
    {
        ShapingResult result = leaf.Shaped!.Result!;
        float top = 0;
        for (int i = 0; i < index; i++)
        {
            top += LayoutEngine.TextLine(result, lines[i].Span, leaf.Style).Height;
        }

        height = LayoutEngine.TextLine(result, lines[index].Span, leaf.Style).Height;
        return top;
    }

    // A line's end before the hard line break that ends it (UAX #14's BK, CR, LF, and NL, as ShapingResult trims them): a caret
    // goes before the break, not after it.
    private static int VisibleEnd(TextLeaf leaf, LineSpan span)
    {
        ReadOnlySpan<char> text = leaf.Text;
        int end = span.End;
        while (
            end > span.Start
            && UnicodeData.LineBreak(UnicodeData.Properties(text[end - 1]))
                is LineBreakClass.BK
                    or LineBreakClass.CR
                    or LineBreakClass.LF
                    or LineBreakClass.NL
        )
        {
            end--;
        }

        return end;
    }
}
