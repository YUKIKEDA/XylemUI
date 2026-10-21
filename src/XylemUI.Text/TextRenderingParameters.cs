namespace XylemUI.Text;

/// <summary>
/// How the text of one window is drawn, as the OS sets it for the window's monitor (design §5.0.3, "ClearType"): the kind of
/// coverage, the order of the subpixels, and the gamma and contrast the glyph shader corrects coverage with.
/// </summary>
/// <param name="Coverage">
/// <see cref="GlyphCoverage.Rgb"/> only when the OS asks for ClearType, the window is opaque, and the monitor's subpixels are in a
/// row of red, green, and blue (either order); <see cref="GlyphCoverage.Gray"/> otherwise.
/// </param>
/// <param name="IsBgr">Whether the monitor's subpixels are blue, green, red from the left, so ClearType coverage is swapped.</param>
/// <param name="Gamma">The gamma of the correction, 1.0 to 2.2.</param>
/// <param name="GrayscaleContrast">The contrast added to grayscale coverage.</param>
/// <param name="ClearTypeContrast">The contrast added to ClearType coverage.</param>
public readonly record struct TextRenderingParameters(
    GlyphCoverage Coverage,
    bool IsBgr,
    float Gamma,
    float GrayscaleContrast,
    float ClearTypeContrast
)
{
    /// <summary>
    /// Gets the parameters where the OS has none: grayscale, gamma 1.8, and contrast 1.0 for grayscale and 0.5 for ClearType, GPUI's defaults off
    /// Windows (Zed v1.24.3, <c>crates/gpui_wgpu/src/wgpu_renderer.rs</c>, lines 2585 to 2602).
    /// </summary>
    public static TextRenderingParameters Default { get; } =
        new(GlyphCoverage.Gray, IsBgr: false, Gamma: 1.8f, GrayscaleContrast: 1.0f, ClearTypeContrast: 0.5f);
}
