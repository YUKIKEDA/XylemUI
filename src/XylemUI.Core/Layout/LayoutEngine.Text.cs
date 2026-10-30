using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XylemUI.Text;
using XylemUI.Text.LineBreak;

namespace XylemUI;

internal static partial class LayoutEngine
{
    // A text leaf's content (design §3.2, "Text leaves"): its lines at the width the input gives, from the two caches. A known
    // width breaks the text there; otherwise a definite space does, a min-content space breaks it at its min-content width, and a
    // max-content space only at its hard line breaks. A commit marks the entry it used, which is then not overwritten.
    private static MeasureResult MeasureText(TextContext context, TextLeaf leaf, OptionalSize known, AvailableSize available, bool commit)
    {
        ShapedTextCache shaped = Shape(context, leaf);
        if (shaped.Result is null)
        {
            (float height, float baseline) = Line(shaped.EmptyLine, leaf.Style);
            return new MeasureResult(new Size(0, height), baseline);
        }

        float width =
            !float.IsNaN(known.Width) ? known.Width
            : available.Width.Kind == AvailableSpaceKind.MinContent ? shaped.MinWidth
            : available.Width.Kind == AvailableSpaceKind.MaxContent ? float.PositiveInfinity
            : available.Width.Value;

        // A width no narrower than the longest line breaks only at the hard line breaks, as a max-content space does: one entry
        // serves both (a row measures its item at max-content and lays it out at that width). The width may come back a bit
        // under the longest line after a padding is added and taken off, which the tolerance of the line breaking covers.
        if (width >= shaped.MaxWidth - ShapingResult.Tolerance)
        {
            width = float.PositiveInfinity;
        }

        LineBreakCache cache = leaf.LineBreaks ??= new LineBreakCache();
        if (!cache.TryGet(width, out int index))
        {
            LineBreakEntry entry = cache.Take(width, out index);
            cache.SetResult(index, Fill(context, leaf, shaped.Result, entry));
        }

        if (commit)
        {
            cache.Commit(index);
        }

        return cache.ResultOf(index);
    }

    /// <summary>
    /// Lays a text leaf's lines out at a width, as the layout would for a leaf of that width, and commits the entry for drawing: a
    /// <see cref="TextLayout"/> of a draw function (design §3.2, "Text from a draw function").
    /// </summary>
    /// <param name="context">The text tools.</param>
    /// <param name="leaf">The text and its caches.</param>
    /// <param name="maxWidth">The width the lines break at; <see cref="float.PositiveInfinity"/> for the hard breaks only.</param>
    /// <returns>The size of the lines' box.</returns>
    internal static Size LayOutText(TextContext context, TextLeaf leaf, float maxWidth)
    {
        AvailableSpace width = float.IsPositiveInfinity(maxWidth) ? AvailableSpace.MaxContent : AvailableSpace.Definite(maxWidth);
        return MeasureText(context, leaf, OptionalSize.None, new AvailableSize(width, AvailableSpace.MaxContent), commit: true).Size;
    }

#if DEBUG
    // The width of a text leaf's lines at a min-content or max-content space, broken into an entry of the context's own: the
    // Debug check of the content widths (VerifyIntrinsicWidths) leaves the leaf's cache as the layout made it.
    private static float MeasureTextUncached(TextContext context, TextLeaf leaf, AvailableSpace space)
    {
        ShapedTextCache shaped = Shape(context, leaf);
        if (shaped.Result is null)
        {
            return 0;
        }

        LineBreakEntry entry = context.CheckEntry;
        entry.Width = space.Kind == AvailableSpaceKind.MinContent ? shaped.MinWidth : float.PositiveInfinity;
        return Fill(context, leaf, shaped.Result, entry).Size.Width;
    }
#endif

    // A text leaf's content widths; they do not depend on the height.
    private static IntrinsicWidths TextIntrinsicWidths(TextContext context, TextLeaf leaf)
    {
        ShapedTextCache shaped = Shape(context, leaf);
        return new IntrinsicWidths(shaped.MinWidth, shaped.MaxWidth, DependsOnHeight: false);
    }

    // The first cache: the text split into font runs and shaped once, or the line of an empty text.
    private static ShapedTextCache Shape(TextContext context, TextLeaf leaf)
    {
        ShapedTextCache shaped = leaf.Shaped ??= new ShapedTextCache();
        if (shaped.Valid)
        {
            return shaped;
        }

        TextStyle style = leaf.Style;
        ReadOnlySpan<char> text = leaf.Text;
        if (text.IsEmpty)
        {
            FontMatch match = FontFallback.ResolveWithFallback(context.Fonts, " ", 0, style.Language, style.Font);
            shaped.FillEmpty(MetricsOf(match.Font!, style.Size * match.Scale, style.Size));
            return shaped;
        }

        // The font runs of each range of the layout (design §4.8), each with the range's fields over the leaf's style; a leaf with
        // no ranges is one range of its style. An inline object's room is a run of its own.
        context.Runs.Clear();
        context.RunStyles.Clear();
        ClearRooms(context);
        if (leaf.Ranges is { } ranges)
        {
            ReadOnlySpan<InlineObject> inlines = ranges.Inlines;
            int room = 0;
            foreach (LayoutRun range in ranges.Layout)
            {
                var runStyle = new RunStyle(range.Size ?? style.Size, range.Language ?? style.Language, range.LetterSpacing ?? 0, range.Features);
                FontRequest font = range.Font ?? style.Font;
                int start = range.Start;
                int end = range.Start + range.Length;
                for (; room < inlines.Length && inlines[room].Position < end; room++)
                {
                    int at = inlines[room].Position;
                    if (at > start)
                    {
                        AddRuns(context, text, start, at, font, runStyle);
                    }

                    AddRoom(context, at, font, runStyle);
                    start = at + 1;
                }

                if (start < end)
                {
                    AddRuns(context, text, start, end, font, runStyle);
                }
            }

            FillRooms(context, ranges);
        }
        else
        {
            AddRuns(context, text, 0, text.Length, style.Font, new RunStyle(style.Size, style.Language, 0, null));
        }

        ShapingResult result = shaped.ResultToFill();
        result.Shape(
            text,
            CollectionsMarshal.AsSpan(context.Runs),
            CollectionsMarshal.AsSpan(context.RunStyles),
            context.Shaper,
            context.Breaker,
            CollectionsMarshal.AsSpan(context.Rooms),
            CollectionsMarshal.AsSpan(context.RoomWidths)
        );
        (float min, float max) = result.IntrinsicWidths(context.Shaper);
        shaped.Fill(min, max);
        return shaped;
    }

    // The room of an inline object: a font run of one code unit, in the font a space of its range takes, which gives the line the
    // metrics of the text around it (SkParagraph's placeholder run takes the font of its style, src/OneLineShaper.cpp).
    private static void AddRoom(TextContext context, int at, in FontRequest font, in RunStyle style)
    {
        FontMatch match = FontFallback.ResolveWithFallback(context.Fonts, " ", 0, style.Language, font);
        context.Runs.Add(new FontRun(match.Font!, at, 1, match.Scale));
        context.RunStyles.Add(style);
    }

    // The positions and widths of the inline objects' rooms, from their last measurement, into the context's lists, which held
    // another leaf's before.
    private static void FillRooms(TextContext context, TextRanges ranges)
    {
        ClearRooms(context);
        ReadOnlySpan<InlineObject> inlines = ranges.Inlines;
        Span<InlineBox> boxes = ranges.Boxes;
        for (int i = 0; i < inlines.Length; i++)
        {
            context.Rooms.Add(inlines[i].Position);
            context.RoomWidths.Add(boxes[i].Width);
        }
    }

    private static void ClearRooms(TextContext context)
    {
        context.Rooms.Clear();
        context.RoomWidths.Clear();
    }

    // Measures a text leaf's inline objects under the width its text is measured at (design §4.8): each child with a width-only
    // constraint, the content box's width less its margins, or the same min-content or max-content width, so that the text's content
    // widths take the child's (Flutter measures the children's intrinsic widths into the paragraph's); its first baseline from
    // a layout at that size when it aligns by it, as a flex container reads its items' (CalculateChildrenBaselines). Rooms of a new
    // size give the text shaped once their widths and drop the lines broken at the old ones (Flutter's
    // TextPainter.setPlaceholderDimensions); the same sizes keep both. A child that is gone takes no room.
    private static void MeasureInlines(UiTree tree, NodeHandle handle, TextLeaf leaf, TextRanges ranges, float knownWidth, AvailableSpace width)
    {
        AvailableSpace space = !float.IsNaN(knownWidth) ? AvailableSpace.Definite(knownWidth) : width;
        float parentWidth = space.IsDefinite ? space.Value : float.NaN;
        var parentSize = new OptionalSize(parentWidth, float.NaN);
        ReadOnlySpan<InlineObject> inlines = ranges.Inlines;
        bool changed = false;
        for (int i = 0; i < inlines.Length; i++)
        {
            NodeHandle child = inlines[i].Child;
            InlineBox box = default;
            if (tree.Nodes.IsAlive(child) && tree.Nodes.GetRef(child).Parent == handle)
            {
                Edges margin = EdgeInsets.ResolveOrZero(tree.Nodes.GetRef(child).Layout.Style.Margin, parentWidth);
                AvailableSpace childSpace = space.IsDefinite ? AvailableSpace.Definite(Math.Max(0, space.Value - margin.Horizontal)) : space;
                var input = new LayoutInput(
                    OptionalSize.None,
                    true,
                    true,
                    parentSize,
                    new AvailableSize(childSpace, AvailableSpace.MaxContent),
                    SizingMode.InherentSize,
                    RequestedAxis.Both
                );
                Size size = LayoutChild(tree, child, in input, commit: false);
                float baseline = float.NaN;
                if (inlines[i].Alignment == PlaceholderAlignment.Baseline)
                {
                    LayoutInput exact = input with { KnownDimensions = new OptionalSize(size.Width, size.Height) };
                    _ = LayoutChild(tree, child, in exact, commit: true);
                    baseline = ReadBaseline(tree, child) + margin.Top;
                }

                box = new InlineBox(size.Width + margin.Horizontal, size.Height + margin.Vertical, baseline);
            }

            if (!ranges.Boxes[i].Equals(box))
            {
                ranges.Boxes[i] = box;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        if (leaf.Shaped is { Valid: true, Result: { } result } shaped)
        {
            TextContext context = tree.Text!;
            FillRooms(context, ranges);
            result.SetPlaceholderWidths(CollectionsMarshal.AsSpan(context.Rooms), CollectionsMarshal.AsSpan(context.RoomWidths));
            (float min, float max) = result.IntrinsicWidths(context.Shaper);
            shaped.Fill(min, max);
        }

        leaf.LineBreaks?.Drop();
    }

    // Lays a committed text leaf's inline objects out at the sizes they were measured to, each in its room: right of the glyphs
    // before it on its line, and up or down by its alignment (design §4.8).
    private static void PlaceInlines(UiTree tree, NodeHandle handle, TextLeaf leaf, TextRanges ranges, Edges paddingBorder)
    {
        if (leaf.Shaped?.Result is not { } result || leaf.LineBreaks is not { Committed: >= 0 } cache)
        {
            return;
        }

        LineBreakEntry entry = cache[cache.Committed];
        float parentWidth = float.IsFinite(entry.Width) ? entry.Width : float.NaN;
        ReadOnlySpan<LaidGlyph> glyphs = entry.Layout.Glyphs;
        ReadOnlySpan<InlineObject> inlines = ranges.Inlines;
        int next = 0;
        float top = 0;
        foreach (LaidLine line in entry.Layout.Lines)
        {
            (float height, float baseline) = TextLine(leaf, result, line.Span);
            LineMetrics metrics = LineMetricsOf(result, line.Span);
            float pen = 0;
            foreach (LaidGlyph glyph in glyphs.Slice(line.GlyphStart, line.GlyphCount))
            {
                if (glyph.IsPlaceholder)
                {
                    while (next < inlines.Length && inlines[next].Position < glyph.Cluster)
                    {
                        next++;
                    }

                    if (next < inlines.Length && inlines[next].Position == glyph.Cluster)
                    {
                        InlineBox box = ranges.Boxes[next];
                        (float above, _) = Extent(inlines[next].Alignment, in box, in metrics);
                        PlaceInline(
                            tree,
                            handle,
                            inlines[next].Child,
                            in box,
                            parentWidth,
                            paddingBorder.Left + pen,
                            paddingBorder.Top + top + baseline - above
                        );
                    }
                }

                pen += glyph.Advance;
            }

            top += height;
        }
    }

    // One inline object laid out at its measured size, its margin box's corner at (x, y).
    private static void PlaceInline(UiTree tree, NodeHandle handle, NodeHandle child, in InlineBox box, float parentWidth, float x, float y)
    {
        if (!tree.Nodes.IsAlive(child) || tree.Nodes.GetRef(child).Parent != handle)
        {
            return;
        }

        Edges margin = EdgeInsets.ResolveOrZero(tree.Nodes.GetRef(child).Layout.Style.Margin, parentWidth);
        var size = new OptionalSize(Math.Max(0, box.Width - margin.Horizontal), Math.Max(0, box.Height - margin.Vertical));
        var input = new LayoutInput(
            size,
            true,
            true,
            new OptionalSize(parentWidth, float.NaN),
            new AvailableSize(AvailableSpace.Definite(size.Width), AvailableSpace.Definite(size.Height)),
            SizingMode.InherentSize,
            RequestedAxis.Both
        );
        _ = LayoutChild(tree, child, in input, commit: true);
        Place(tree, child, x + margin.Left, y + margin.Top);
    }

    // How far an inline object's room reaches above and below its line's baseline, by its alignment (SkParagraph's
    // Run::updateMetrics, src/Run.cpp lines 236 to 290): from the line's text, its largest ascent and descent.
    private static (float Above, float Below) Extent(PlaceholderAlignment alignment, in InlineBox box, in LineMetrics text)
    {
        float height = box.Height;
        float above = alignment switch
        {
            PlaceholderAlignment.Baseline => float.IsNaN(box.Baseline) ? height : box.Baseline,
            PlaceholderAlignment.AboveBaseline => height,
            PlaceholderAlignment.BelowBaseline => 0,
            PlaceholderAlignment.Top => text.Ascent,
            PlaceholderAlignment.Bottom => height - text.Descent,
            _ => (height / 2) + ((text.Ascent - text.Descent) / 2),
        };
        return (above, height - above);
    }

    // The font runs of a part of the text, each with the part's style.
    private static void AddRuns(TextContext context, ReadOnlySpan<char> text, int start, int end, in FontRequest font, in RunStyle style)
    {
        FontFallback.SplitRange(context.Fonts, text, start, end, style.Language, font, context.Runs);
        while (context.RunStyles.Count < context.Runs.Count)
        {
            context.RunStyles.Add(style);
        }
    }

    // An entry of the second cache: the lines at its width (Pass1 and Pass2). Returns the size and first baseline they make.
    private static MeasureResult Fill(TextContext context, TextLeaf leaf, ShapingResult result, LineBreakEntry entry)
    {
        // The style is read once: the lines of a leaf with no inline objects take the fonts' metrics alone, as before row T13. Read
        // for each line, the miss of a wrapped leaf in resize-text was 10.53 to 11.08 ms against main's 9.02 to 9.99.
        TextStyle style = leaf.Style;
        TextRanges? rooms = leaf.Ranges is { HasInlines: true } ranges ? ranges : null;
        result.Wrap(entry.Width, context.Shaper, entry.Lines);
        result.Lay(entry.Lines, context.Shaper, entry.Layout);
        float width = 0;
        float height = 0;
        float firstBaseline = float.NaN;
        foreach (LaidLine line in entry.Layout.Lines)
        {
            width = MathF.Max(width, line.Width);
            (float lineHeight, float baseline) = rooms is null
                ? Line(LineMetricsOf(result, line.Span), style)
                : RoomLine(rooms, result, line.Span, style);
            if (float.IsNaN(firstBaseline))
            {
                firstBaseline = height + baseline;
            }

            height += lineHeight;
        }

        return new MeasureResult(new Size(width, height), firstBaseline);
    }

    /// <summary>
    /// Returns the height of one line of a text leaf and its baseline from the line's top, as the measurement stacks them: the draw
    /// stage places the committed lines with it (design §5.0, "The draw stage and the frame drive"). The rooms of the inline objects
    /// on the line count (design §4.8).
    /// </summary>
    internal static (float Height, float Baseline) TextLine(TextLeaf leaf, ShapingResult result, LineSpan span) =>
        leaf.Ranges is { HasInlines: true } ranges ? RoomLine(ranges, result, span, leaf.Style) : Line(LineMetricsOf(result, span), leaf.Style);

    // A line with the rooms of the inline objects on it, which it grows to hold (SkParagraph's TextLine metrics, src/Run.h lines 432
    // to 443). Apart from the lines of the other leaves, which take none of it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (float Height, float Baseline) RoomLine(TextRanges ranges, ShapingResult result, LineSpan span, in TextStyle style)
    {
        LineMetrics metrics = LineMetricsOf(result, span);
        (float height, float baseline) = Line(metrics, style);
        float above = baseline;
        float below = height - baseline;
        ReadOnlySpan<InlineObject> inlines = ranges.Inlines;
        Span<InlineBox> boxes = ranges.Boxes;
        for (int i = 0; i < inlines.Length; i++)
        {
            if (inlines[i].Position >= span.Start && inlines[i].Position < span.End)
            {
                (float up, float down) = Extent(inlines[i].Alignment, in boxes[i], in metrics);
                above = MathF.Max(above, up);
                below = MathF.Max(below, down);
            }
        }

        return (above + below, above);
    }

    // The height of a line and its baseline from its top. The fonts' line gap is split half above and half below (half-leading).
    // A line height of the style replaces the fonts' height: the factor times the largest size on the line (design §4.8), and the
    // difference with ascent and descent is split the same way.
    private static (float Height, float Baseline) Line(LineMetrics metrics, in TextStyle style)
    {
        float content = metrics.Ascent + metrics.Descent;
        float height = float.IsNaN(style.LineHeight) ? content + metrics.LineGap : metrics.Size * style.LineHeight;
        return (height, ((height - content) / 2) + metrics.Ascent);
    }

    // The largest ascent, descent, line gap, and size of the font runs a line takes, its hard line break included. An empty span
    // (none is made today) takes the run at its position.
    private static LineMetrics LineMetricsOf(ShapingResult result, LineSpan span)
    {
        float ascent = 0;
        float descent = 0;
        float gap = 0;
        float size = 0;
        int end = Math.Max(span.End, span.Start + 1);
        ReadOnlySpan<FontRun> runs = result.Runs;
        ReadOnlySpan<RunStyle> styles = result.Styles;
        for (int r = 0; r < runs.Length; r++)
        {
            FontRun run = runs[r];
            if (run.Start >= end || run.Start + run.Length <= span.Start)
            {
                continue;
            }

            LineMetrics metrics = MetricsOf(run.Font, styles[r].Size * run.Scale, styles[r].Size);
            ascent = MathF.Max(ascent, metrics.Ascent);
            descent = MathF.Max(descent, metrics.Descent);
            gap = MathF.Max(gap, metrics.LineGap);
            size = MathF.Max(size, metrics.Size);
        }

        return new LineMetrics(ascent, descent, gap, size);
    }

    // The metrics of a font at an em size (the size asked for times the fallback's scale), for a range of a size.
    private static LineMetrics MetricsOf(Font font, float emSize, float size)
    {
        float scale = emSize / font.UnitsPerEm;
        return new LineMetrics(font.Ascent * scale, font.Descent * scale, font.LineGap * scale, size);
    }
}
