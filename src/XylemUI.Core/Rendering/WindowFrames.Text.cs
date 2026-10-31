using XylemUI.Text;
using XylemUI.Text.LineBreak;

namespace XylemUI;

internal sealed partial class WindowFrames
{
    // The space between the two lines of a double decoration, in logical pixels (SkParagraph's kDoubleDecorationSpacing,
    // src/Decorations.cpp line 21).
    private const float DoubleSpacing = 3;

    // One line of a text leaf with ranges (design §4.8), in SkParagraph's order (src/TextLine.cpp lines 201 to 233): the
    // backgrounds, the text, then the decorations. The line's glyphs are cut where the look's range changes, and for the text and
    // the decorations also where the font run does, as the decorations take the metrics of their run's font. The room of an inline
    // object takes its range's background, as CSS's inline-block does and SkParagraph's placeholder; it draws no glyph, and the
    // decorations stop at it, as they do at an inline-block (Avalonia draws neither).
    private void DrawRanges(
        TextRanges ranges,
        ShapingResult result,
        ReadOnlySpan<LaidGlyph> glyphs,
        float x,
        float top,
        float height,
        float baseline,
        Color color
    )
    {
        ReadOnlySpan<PaintRun> paint = ranges.Paint;
        var pieces = new Pieces(glyphs, paint, byFont: false);
        while (pieces.Next(out int _, out int _, out PaintRun run, out float left, out float right))
        {
            if (Look(ranges, run).Background is { } background && right > left)
            {
                _list.FillRect(x + left, top, right - left, height, background);
            }
        }

        // The text: one glyph run for each piece of one color, whatever else changes along it.
        pieces = new Pieces(glyphs, paint, byFont: false);
        int from = 0;
        float at = 0;
        Color? current = null;
        while (pieces.Next(out int start, out int _, out PaintRun run, out float left, out float _))
        {
            Color ink = Look(ranges, run).Color ?? color;
            if (current is { } open && open != ink)
            {
                DrawGlyphs(ranges, result, glyphs[from..start], x + at, baseline, open);
                from = start;
                at = left;
            }

            current = ink;
        }

        if (current is { } last)
        {
            DrawGlyphs(ranges, result, glyphs[from..], x + at, baseline, last);
        }

        pieces = new Pieces(glyphs, paint, byFont: true);
        while (pieces.Next(out int start, out int end, out PaintRun run, out float left, out float right))
        {
            SpanStyle look = Look(ranges, run);
            if (look.Decoration is { Lines: not DecorationLines.None } decoration && right > left && !glyphs[start].IsPlaceholder)
            {
                int font = glyphs[start].Run;
                float size = result.Styles[font].Size * result.Runs[font].Scale;
                Color ink = decoration.Color ?? look.Color ?? color;
                DrawDecoration(decoration, result.Runs[font].Font, size, glyphs[start..end], x + left, x + right, baseline, ink);
            }
        }
    }

    // One line that a selection reaches (design §4.10, "Drawing"), with or without ranges, in DrawRanges' order: the glyphs are
    // also cut at the selection's ends; the selection's background goes over the range's, and its text color takes the place of
    // the range's. The room of an inline object is not selected. A line the selection does not reach goes through DrawRanges or
    // the plain glyph run, as before the selection (row P02b2: checking the selection in DrawRanges made the ranged leaves' draw
    // stage of rich-text 4 to 14 % longer).
    private void DrawSelected(
        TextRanges? ranges,
        LeafSelection selection,
        ShapingResult result,
        ReadOnlySpan<LaidGlyph> glyphs,
        float x,
        float top,
        float height,
        float baseline,
        Color color
    )
    {
        ReadOnlySpan<PaintRun> paint = ranges is null ? default : ranges.Paint;
        var pieces = new SelectedPieces(glyphs, paint, selection);
        while (pieces.Next(out int _, out PaintRun run, out bool selected, out float left, out float right))
        {
            if (right <= left)
            {
                continue;
            }

            if (ranges is not null && Look(ranges, run).Background is { } background)
            {
                _list.FillRect(x + left, top, right - left, height, background);
            }

            if (selected)
            {
                _list.FillRect(x + left, top, right - left, height, selection.Background);
            }
        }

        pieces = new SelectedPieces(glyphs, paint, selection);
        int from = 0;
        float at = 0;
        Color? current = null;
        while (pieces.Next(out int start, out PaintRun run, out bool selected, out float left, out float _))
        {
            Color ink = selected ? selection.Foreground : (ranges is null ? null : Look(ranges, run).Color) ?? color;
            if (current is { } open && open != ink)
            {
                DrawSelectedGlyphs(ranges, result, glyphs[from..start], x + at, baseline, open);
                from = start;
                at = left;
            }

            current = ink;
        }

        if (current is { } last)
        {
            DrawSelectedGlyphs(ranges, result, glyphs[from..], x + at, baseline, last);
        }

        if (ranges is null)
        {
            return;
        }

        var decorated = new Pieces(glyphs, paint, byFont: true);
        while (decorated.Next(out int start, out int end, out PaintRun run, out float left, out float right))
        {
            SpanStyle look = Look(ranges, run);
            if (look.Decoration is { Lines: not DecorationLines.None } decoration && right > left && !glyphs[start].IsPlaceholder)
            {
                int font = glyphs[start].Run;
                float size = result.Styles[font].Size * result.Runs[font].Scale;
                Color ink = decoration.Color ?? look.Color ?? color;
                DrawDecoration(decoration, result.Runs[font].Font, size, glyphs[start..end], x + left, x + right, baseline, ink);
            }
        }
    }

    // A glyph run of one color of a selected line: with ranges, around the rooms of the inline objects.
    private void DrawSelectedGlyphs(TextRanges? ranges, ShapingResult result, ReadOnlySpan<LaidGlyph> glyphs, float x, float baseline, Color color)
    {
        if (ranges is null)
        {
            DrawGlyphs(result, glyphs, x, baseline, color);
        }
        else
        {
            DrawGlyphs(ranges, result, glyphs, x, baseline, color);
        }
    }

    // A glyph run of one color; with inline objects, a run on each side of each one's room, which has no glyph to draw.
    private void DrawGlyphs(TextRanges ranges, ShapingResult result, ReadOnlySpan<LaidGlyph> glyphs, float x, float baseline, Color color)
    {
        if (!ranges.HasInlines)
        {
            DrawGlyphs(result, glyphs, x, baseline, color);
            return;
        }

        int from = 0;
        float at = 0;
        float pen = 0;
        for (int g = 0; g < glyphs.Length; g++)
        {
            if (glyphs[g].IsPlaceholder)
            {
                DrawGlyphs(result, glyphs[from..g], x + at, baseline, color);
                from = g + 1;
                at = pen + glyphs[g].Advance;
            }

            pen += glyphs[g].Advance;
        }

        DrawGlyphs(result, glyphs[from..], x + at, baseline, color);
    }

    private void DrawGlyphs(ShapingResult result, ReadOnlySpan<LaidGlyph> glyphs, float x, float baseline, Color color) =>
        _list.DrawLine(glyphs, result.Runs, result.Styles, _atlas, x, baseline, color, _textRendering.Coverage, _textRendering.IsAliased);

    // A range's look: its own fields, under those of its link's hovered style while a pointer is over the link.
    private static SpanStyle Look(TextRanges ranges, in PaintRun run)
    {
        var own = new SpanStyle(Color: run.Color, Background: run.Background, Decoration: run.Decoration, Shadow: run.Shadow);
        return run.Link >= 0 && run.Link == ranges.HoveredLink ? ranges.Links[run.Link].Hovered.Over(own) : own;
    }

    // The lines of a decoration under a piece of a line from `left` to `right`, as SkParagraph places them (src/Decorations.cpp
    // lines 139 to 204): the font's thickness and positions, or a fourteenth of the size and its defaults where the font has
    // none. An underline and an overline skip the glyphs' outlines; a line through does not.
    private void DrawDecoration(
        in TextDecoration decoration,
        Font font,
        float size,
        ReadOnlySpan<LaidGlyph> glyphs,
        float left,
        float right,
        float baseline,
        Color color
    )
    {
        DecorationMetrics metrics = font.Decorations;
        float units = size / font.UnitsPerEm;
        float underline = metrics.UnderlineSize is > 0 and int u ? u * units : size / 14;
        foreach (
            DecorationLines line in (ReadOnlySpan<DecorationLines>)[DecorationLines.Underline, DecorationLines.Overline, DecorationLines.LineThrough]
        )
        {
            if ((decoration.Lines & line) == 0)
            {
                continue;
            }

            // The line is centered on the font's offset (y up), as SkParagraph and Avalonia center theirs (Avalonia's
            // TextDecoration.cs lines 193, 196, and 258 to 261), though OpenType calls post.underlinePosition and
            // OS/2.yStrikeoutPosition the line's top. GPUI does not read the font's offset.
            float own = line == DecorationLines.LineThrough && metrics.StrikeoutSize is > 0 and int s ? s * units : underline;
            float thickness = own * decoration.Thickness;
            float center = line switch
            {
                DecorationLines.Underline => metrics.UnderlineOffset is < 0 and int offset ? -offset * units : thickness,
                DecorationLines.Overline => -font.Ascent * units,
                _ => metrics.StrikeoutOffset is int offset ? -offset * units : -(metrics.XHeight ?? 0) * units / 2,
            };
            bool skips = line != DecorationLines.LineThrough;
            DrawDecorationLine(decoration.Style, font, size, glyphs, left, right, baseline, center, thickness, skips, color);
            if (decoration.Style == DecorationStyle.Double)
            {
                DrawDecorationLine(decoration.Style, font, size, glyphs, left, right, baseline, center + DoubleSpacing, thickness, skips, color);
            }
        }
    }

    // One line of a decoration, its center `center` below the baseline, cut where the glyphs' outlines cross it when it skips them: each crossing widened by
    // the line's thickness on both sides, and a piece shorter than a twelfth of an em before a crossing left out (Avalonia's
    // Avalonia.Base/Media/TextDecoration.cs lines 213 to 252, whose comment says the math is Chrome's).
    private void DrawDecorationLine(
        DecorationStyle style,
        Font font,
        float size,
        ReadOnlySpan<LaidGlyph> glyphs,
        float left,
        float right,
        float baseline,
        float center,
        float thickness,
        bool skips,
        Color color
    )
    {
        float y = baseline + center;
        if (!skips || _tree.Text is not { } text)
        {
            DrawDecorationPiece(style, size, left, left, right, y, thickness, color);
            return;
        }

        float top = (center - (thickness / 2)) / size;
        float bottom = (center + (thickness / 2)) / size;
        float last = left;
        float pen = left;
        foreach (LaidGlyph glyph in glyphs)
        {
            // The band from the glyph's own baseline: a glyph raised by its offset meets the line lower in its outline.
            float lift = glyph.OffsetY / size;
            if (text.Intercepts.Intercept(font, glyph.GlyphId, top + lift, bottom + lift, out float from, out float to))
            {
                float origin = pen + glyph.OffsetX;
                float start = MathF.Min(origin + (from * size) - thickness, right);
                float end = origin + (to * size) + thickness;
                if (start > last && last + (size / 12) < start)
                {
                    DrawDecorationPiece(style, size, left, last, start, y, thickness, color);
                }

                last = MathF.Max(last, end);
            }

            pen += glyph.Advance;
        }

        if (last < right)
        {
            DrawDecorationPiece(style, size, left, last, right, y, thickness, color);
        }
    }

    // A piece of a decoration's line, a line with flat ends: the rectangle SkParagraph fills (draw_line_as_rect, src/Decorations.cpp lines 11 to 19),
    // at its thickness. A FillRect would put its edges on pixels, and the 0.7 px underline of a 14 px text would be 0 or 1 px. A
    // dotted or dashed one keeps the pattern of the whole line, which starts at `origin` (SkParagraph's intervals: a dot of a
    // fourteenth of the size and a gap of 1.5 dots; a dash of four and a gap of two).
    private void DrawDecorationPiece(
        DecorationStyle style,
        float size,
        float origin,
        float from,
        float to,
        float center,
        float thickness,
        Color color
    )
    {
        float unit = size / 14;
        Dash? dash = style switch
        {
            DecorationStyle.Dotted => new Dash(unit, 1.5f * unit, from - origin),
            DecorationStyle.Dashed => new Dash(4 * unit, 2 * unit, from - origin),
            _ => null,
        };
        _list.DrawLine(from, center, to, center, new Stroke(new Paint(color), thickness, LineCap.Flat, dash));
    }

    // The pieces of a line's glyphs that share a range of the look, and with byFont a font run too: their glyphs, their range,
    // and where they start and end from the line's left edge. The glyphs of a line are left to right, so their clusters do not go
    // back, and the ranges are walked once.
    private ref struct Pieces(ReadOnlySpan<LaidGlyph> glyphs, ReadOnlySpan<PaintRun> paint, bool byFont)
    {
        private readonly ReadOnlySpan<LaidGlyph> _glyphs = glyphs;
        private readonly ReadOnlySpan<PaintRun> _paint = paint;
        private int _next;
        private int _range;
        private float _pen;

        public bool Next(out int start, out int end, out PaintRun run, out float left, out float right)
        {
            start = _next;
            left = _pen;
            if (_next >= _glyphs.Length)
            {
                end = start;
                run = default;
                right = left;
                return false;
            }

            _range = RangeOf(_glyphs[_next].Cluster);
            int font = _glyphs[_next].Run;
            while (_next < _glyphs.Length && RangeOf(_glyphs[_next].Cluster) == _range && (!byFont || _glyphs[_next].Run == font))
            {
                _pen += _glyphs[_next].Advance;
                _next++;
            }

            end = _next;
            run = _paint[_range];
            right = _pen;
            return true;
        }

        // The range of a cluster, from the one the last piece was in on.
        private int RangeOf(int cluster)
        {
            int range = _range;
            while (range + 1 < _paint.Length && cluster >= _paint[range + 1].Start)
            {
                range++;
            }

            return range;
        }
    }

    // The pieces of a line that a selection reaches: the glyphs that share a range of the look (when there are ranges) and whether
    // the selection holds them, with their range, and where they start and end from the line's left edge.
    private ref struct SelectedPieces(ReadOnlySpan<LaidGlyph> glyphs, ReadOnlySpan<PaintRun> paint, LeafSelection selection)
    {
        private readonly ReadOnlySpan<LaidGlyph> _glyphs = glyphs;
        private readonly ReadOnlySpan<PaintRun> _paint = paint;
        private int _next;
        private int _range;
        private float _pen;

        public bool Next(out int start, out PaintRun run, out bool selected, out float left, out float right)
        {
            start = _next;
            left = _pen;
            if (_next >= _glyphs.Length)
            {
                run = default;
                selected = false;
                right = left;
                return false;
            }

            _range = RangeOf(_glyphs[_next].Cluster);
            selected = Selected(_glyphs[_next]);
            while (_next < _glyphs.Length && RangeOf(_glyphs[_next].Cluster) == _range && Selected(_glyphs[_next]) == selected)
            {
                _pen += _glyphs[_next].Advance;
                _next++;
            }

            run = _paint.IsEmpty ? default : _paint[_range];
            right = _pen;
            return true;
        }

        // Whether the selection holds a glyph: the one of a cluster it holds, not the room of an inline object.
        private readonly bool Selected(in LaidGlyph glyph) => !glyph.IsPlaceholder && selection.Holds(glyph.Cluster);

        // The range of a cluster, from the one the last piece was in on; 0 with no ranges.
        private int RangeOf(int cluster)
        {
            int range = _range;
            while (range + 1 < _paint.Length && cluster >= _paint[range + 1].Start)
            {
                range++;
            }

            return range;
        }
    }
}
