namespace XylemUI.Text.Tests;

// The outlines an underline skips (row T12, design §4.8): where a glyph's outline crosses a band, kept per glyph and band under a
// cap; and the decoration metrics a font gives. The test font is a Noto Sans JP subset of 1000 units per em.
public sealed class GlyphInterceptsTests : IDisposable
{
    private readonly Font _font = TestFont.Open();
    private readonly TextShaper _shaper = new();

    public void Dispose()
    {
        _shaper.Dispose();
        _font.Dispose();
    }

    /// <summary>
    /// A band through the stems of "n" is crossed between its left and right stems; a band below the baseline is not crossed by
    /// "n"; the same band again is the kept answer.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The glyph of "n", which advances 0.61 em and stands on the baseline.
    ///
    /// Steps:
    /// - Intercept the band from 0.3 to 0.2 em above the baseline, twice; and the band from 0.2 to 0.3 em below it.
    ///
    /// Expected:
    /// - The first band is crossed from more than 0 to less than 0.61 em, wider than 0.3 em, and the same twice, with one outline
    ///   kept. The second band is not crossed.
    /// </remarks>
    [Fact]
    public void Intercept_BandThroughTheStems_IsCrossedBetweenThem()
    {
        var intercepts = new GlyphIntercepts();
        uint n = _shaper.Shape(_font, "n", TestFont.Japanese)[0].GlyphId;

        Assert.True(intercepts.Intercept(_font, n, -0.3f, -0.2f, out float left, out float right));
        Assert.InRange(left, 0.001f, 0.2f);
        Assert.InRange(right, 0.4f, 0.609f);
        Assert.True(right - left > 0.3f);
        Assert.True(intercepts.Intercept(_font, n, -0.3f, -0.2f, out float again, out float againRight));
        Assert.Equal((left, right), (again, againRight));
        Assert.Equal(1, intercepts.Count);

        Assert.False(intercepts.Intercept(_font, n, 0.2f, 0.3f, out _, out _));
    }

    /// <summary>
    /// A band is kept by its top and its bottom: one with the same top and another bottom is worked out apart.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The glyph of "n", whose top is about half an em above the baseline.
    ///
    /// Steps:
    /// - Intercept the band from 0.9 to 0.8 em above the baseline, then the band from 0.9 to 0.2 em above it.
    ///
    /// Expected:
    /// - The first is not crossed; the second is.
    /// </remarks>
    [Fact]
    public void Intercept_SameTopOtherBottom_IsAnotherBand()
    {
        var intercepts = new GlyphIntercepts();
        uint n = _shaper.Shape(_font, "n", TestFont.Japanese)[0].GlyphId;

        Assert.False(intercepts.Intercept(_font, n, -0.9f, -0.8f, out _, out _));
        Assert.True(intercepts.Intercept(_font, n, -0.9f, -0.2f, out _, out _));
    }

    /// <summary>
    /// The cache keeps no more outlines than its capacity, and a new one takes the place of the one used longest ago.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A cache of 2 outlines; the glyphs of "n", "T", and "-".
    ///
    /// Steps:
    /// - Intercept "n", then "T", then "n" again, then "-".
    ///
    /// Expected:
    /// - Two outlines kept: "n" and "-"; "T", used longest ago, is not.
    /// </remarks>
    [Fact]
    public void Intercept_Full_DropsTheOutlineUsedLongestAgo()
    {
        var intercepts = new GlyphIntercepts(capacity: 2);
        uint n = _shaper.Shape(_font, "n", TestFont.Japanese)[0].GlyphId;
        uint t = _shaper.Shape(_font, "T", TestFont.Japanese)[0].GlyphId;
        uint hyphen = _shaper.Shape(_font, "-", TestFont.Japanese)[0].GlyphId;

        _ = intercepts.Intercept(_font, n, -0.3f, -0.2f, out _, out _);
        _ = intercepts.Intercept(_font, t, -0.3f, -0.2f, out _, out _);
        _ = intercepts.Intercept(_font, n, -0.3f, -0.2f, out _, out _);
        _ = intercepts.Intercept(_font, hyphen, -0.3f, -0.2f, out _, out _);

        Assert.Equal(2, intercepts.Count);
        Assert.True(intercepts.Holds(_font, n));
        Assert.True(intercepts.Holds(_font, hyphen));
        Assert.False(intercepts.Holds(_font, t));
    }

    /// <summary>
    /// Intercepting kept glyphs and bands allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A cache that has read "n" and "T" at two bands.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: intercept both glyphs at both bands.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    [Fact]
    public void SteadyState_KeptGlyphsAndBands_AllocateNothing()
    {
        var intercepts = new GlyphIntercepts();
        uint n = _shaper.Shape(_font, "n", TestFont.Japanese)[0].GlyphId;
        uint t = _shaper.Shape(_font, "T", TestFont.Japanese)[0].GlyphId;

        void Iteration(int index)
        {
            _ = intercepts.Intercept(_font, n, -0.3f, -0.2f, out _, out _);
            _ = intercepts.Intercept(_font, t, -0.3f, -0.2f, out _, out _);
            _ = intercepts.Intercept(_font, n, 0.1f, 0.15f, out _, out _);
            _ = intercepts.Intercept(_font, t, 0.1f, 0.15f, out _, out _);
        }

        Iteration(0);
        XylemUI.Tests.SteadyStateAllocation.AssertNone(1_000, Iteration);
    }

    /// <summary>
    /// The test font gives its underline below the baseline, its line through above it, both thicknesses, and its x height.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font.
    ///
    /// Steps:
    /// - Read its decoration metrics.
    ///
    /// Expected:
    /// - The underline's top is below the baseline and the line through's above it; the thicknesses and the x height are greater
    ///   than 0 and less than an em.
    /// </remarks>
    [Fact]
    public void Font_DecorationMetrics_AreReadFromItsTables()
    {
        DecorationMetrics metrics = _font.Decorations;
        Assert.InRange(metrics.UnderlineOffset!.Value, -_font.UnitsPerEm, -1);
        Assert.InRange(metrics.StrikeoutOffset!.Value, 1, _font.UnitsPerEm);
        Assert.InRange(metrics.UnderlineSize!.Value, 1, _font.UnitsPerEm);
        Assert.InRange(metrics.StrikeoutSize!.Value, 1, _font.UnitsPerEm);
        Assert.InRange(metrics.XHeight!.Value, 1, _font.UnitsPerEm);
    }
}
