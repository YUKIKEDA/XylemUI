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
            cache.SetResult(index, Fill(context, leaf.Style, shaped.Result, entry));
        }

        if (commit)
        {
            cache.Commit(index);
        }

        return cache.ResultOf(index);
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
        return Fill(context, leaf.Style, shaped.Result, entry).Size.Width;
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
            shaped.FillEmpty(MetricsOf(match.Font!, style.Size * match.Scale));
            return shaped;
        }

        FontFallback.Split(context.Fonts, text, style.Language, style.Font, context.Runs);
        ShapingResult result = shaped.ResultToFill();
        result.Shape(
            text,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(context.Runs),
            style.Size,
            style.Language,
            context.Shaper,
            context.Breaker
        );
        (float min, float max) = result.IntrinsicWidths(context.Shaper);
        shaped.Fill(min, max);
        return shaped;
    }

    // An entry of the second cache: the lines at its width (Pass1 and Pass2). Returns the size and first baseline they make.
    private static MeasureResult Fill(TextContext context, in TextStyle style, ShapingResult result, LineBreakEntry entry)
    {
        result.Wrap(entry.Width, context.Shaper, entry.Lines);
        result.Lay(entry.Lines, context.Shaper, entry.Layout);
        float width = 0;
        float height = 0;
        float firstBaseline = float.NaN;
        foreach (LaidLine line in entry.Layout.Lines)
        {
            width = MathF.Max(width, line.Width);
            (float lineHeight, float baseline) = Line(LineMetricsOf(result, line.Span), style);
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
    /// stage places the committed lines with it (design §5.0, "The draw stage and the frame drive").
    /// </summary>
    internal static (float Height, float Baseline) TextLine(ShapingResult result, LineSpan span, in TextStyle style) =>
        Line(LineMetricsOf(result, span), style);

    // The height of a line and its baseline from its top. The fonts' line gap is split half above and half below (half-leading).
    // A line height of the style replaces the fonts' height, and the difference with ascent and descent is split the same way.
    private static (float Height, float Baseline) Line(LineMetrics metrics, in TextStyle style)
    {
        float content = metrics.Ascent + metrics.Descent;
        float height = float.IsNaN(style.LineHeight) ? content + metrics.LineGap : style.Size * style.LineHeight;
        return (height, ((height - content) / 2) + metrics.Ascent);
    }

    // The largest ascent, descent, and line gap of the font runs a line takes, its hard line break included. An empty span (none
    // is made today) takes the run at its position.
    private static LineMetrics LineMetricsOf(ShapingResult result, LineSpan span)
    {
        float ascent = 0;
        float descent = 0;
        float gap = 0;
        int end = Math.Max(span.End, span.Start + 1);
        foreach (FontRun run in result.Runs)
        {
            if (run.Start >= end || run.Start + run.Length <= span.Start)
            {
                continue;
            }

            LineMetrics metrics = MetricsOf(run.Font, result.Size * run.Scale);
            ascent = MathF.Max(ascent, metrics.Ascent);
            descent = MathF.Max(descent, metrics.Descent);
            gap = MathF.Max(gap, metrics.LineGap);
        }

        return new LineMetrics(ascent, descent, gap);
    }

    private static LineMetrics MetricsOf(Font font, float size)
    {
        float scale = size / font.UnitsPerEm;
        return new LineMetrics(font.Ascent * scale, font.Descent * scale, font.LineGap * scale);
    }
}
