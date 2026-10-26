using System.Numerics;
using System.Runtime.CompilerServices;
using XylemUI.Text;
using XylemUI.Text.LineBreak;

namespace XylemUI;

public sealed partial class DisplayList
{
    /// <summary>
    /// Appends one laid-out line of a text leaf (design §5.0, "The draw stage and the frame drive"): its glyphs as the layout's
    /// Pass2 placed them, each in the font of its run, from <paramref name="x"/> along the baseline. The glyphs are placed as
    /// <see cref="DrawText(ReadOnlySpan{ShapedGlyph}, Font, float, GlyphAtlas, float, float, Color, GlyphCoverage, bool)"/> places
    /// them, and wait for <see cref="ResolveGlyphs"/> where the atlas does not hold them yet.
    /// </summary>
    /// <param name="glyphs">The line's glyphs, with advances and offsets in logical pixels.</param>
    /// <param name="runs">The text's font runs, which the glyphs' <see cref="LaidGlyph.Run"/> index.</param>
    /// <param name="size">The text's size in logical pixels per em; a run's scale multiplies it.</param>
    /// <param name="atlas">The glyph atlas the renderer draws from.</param>
    /// <param name="x">The line's left edge, in logical pixels.</param>
    /// <param name="baseline">The line's baseline, in logical pixels from the top.</param>
    /// <param name="color">The text's color.</param>
    /// <param name="coverage">Grayscale, or ClearType where the window allows it.</param>
    /// <param name="aliased">Whether the glyphs are bilevel, because the OS does not smooth fonts.</param>
    /// <exception cref="ArgumentException"><paramref name="aliased"/> is set with ClearType coverage, as for DrawText.</exception>
    internal void DrawLine(
        ReadOnlySpan<LaidGlyph> glyphs,
        ReadOnlySpan<FontRun> runs,
        float size,
        GlyphAtlas atlas,
        float x,
        float baseline,
        Color color,
        GlyphCoverage coverage,
        bool aliased
    )
    {
        if (aliased && coverage == GlyphCoverage.Rgb)
        {
            throw new ArgumentException("Bilevel text has no ClearType coverage.", nameof(aliased));
        }

        if (_mode != 0)
        {
            DrawLineInScope(glyphs, runs, size, atlas, x, baseline, color, coverage, aliased);
        }
        else
        {
            DrawLine<Outside>(glyphs, runs, size, atlas, x, baseline, color, coverage, aliased);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawLineInScope(
        ReadOnlySpan<LaidGlyph> glyphs,
        ReadOnlySpan<FontRun> runs,
        float size,
        GlyphAtlas atlas,
        float x,
        float baseline,
        Color color,
        GlyphCoverage coverage,
        bool aliased
    ) => DrawLine<Inside>(glyphs, runs, size, atlas, x, baseline, color, coverage, aliased);

    // As DrawText<TScope>, with the pen in logical pixels and the font and size taken from each glyph's run.
    private void DrawLine<TScope>(
        ReadOnlySpan<LaidGlyph> glyphs,
        ReadOnlySpan<FontRun> runs,
        float size,
        GlyphAtlas atlas,
        float x,
        float baseline,
        Color color,
        GlyphCoverage coverage,
        bool aliased
    )
        where TScope : struct, IScope
    {
        if (glyphs.IsEmpty || (TScope.In && !(_textScale > 0)))
        {
            return;
        }

        if (TScope.In && coverage == GlyphCoverage.Rgb && TextIsGray)
        {
            coverage = GlyphCoverage.Gray;
        }

        // From logical pixels to the pixels the glyphs are placed in: physical, and inside a transform the text's.
        float toPixels = TScope.In ? Scale * _textScale : Scale;
        Vector2 shift = TScope.In ? _shift : default;
        float originX = (x * toPixels) + shift.X;
        float originY = (baseline * toPixels) + shift.Y;
        Span<GlyphInstance> destination = ReserveGlyphs(glyphs.Length);
        int first = GlyphCount;
        int written = 0;
        int runStart = 0;
        GlyphCoverage runCoverage = coverage;
        float pen = 0;
        foreach (LaidGlyph laid in glyphs)
        {
            FontRun run = runs[laid.Run];
            float pixelSize = size * run.Scale * toPixels;
            float position = originX + ((pen + laid.OffsetX) * toPixels);
            int subpixel = 0;
            int pixel = aliased ? GlyphPosition.Round(position) : GlyphPosition.Snap(position, out subpixel);
            var key = new GlyphKey(run.Font, laid.GlyphId, pixelSize, subpixel, coverage, Aliased: aliased);
            int row = (int)MathF.Round(originY - (laid.OffsetY * toPixels));
            GlyphLookup lookup = atlas.Lookup(key, color.Packed, out AtlasGlyph glyph);
            if (lookup == GlyphLookup.Found && glyph.Width > 0)
            {
                if (glyph.Coverage != runCoverage)
                {
                    if (written > runStart)
                    {
                        EndGlyphRun<TScope>(written - runStart, color, runCoverage, -1);
                        runStart = written;
                    }

                    runCoverage = glyph.Coverage;
                }

                destination[written++] = Place(glyph, position, pixel, row);
            }
            else if (lookup == GlyphLookup.Missing)
            {
                if (runCoverage != coverage)
                {
                    if (written > runStart)
                    {
                        EndGlyphRun<TScope>(written - runStart, color, runCoverage, -1);
                        runStart = written;
                    }

                    runCoverage = coverage;
                }

                destination[written] = Pend(first + written, key, color, position, pixel, row);
                written++;
            }

            pen += laid.Advance;
        }

        if (written > runStart)
        {
            EndGlyphRun<TScope>(written - runStart, color, runCoverage, -1);
        }
    }
}
