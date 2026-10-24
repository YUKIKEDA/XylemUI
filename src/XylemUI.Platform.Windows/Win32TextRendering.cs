using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Platform.Windows.Com;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Reads how a window's text is drawn (design §5.0.3, "ClearType" and "When fonts are not smoothed"): the font smoothing settings
/// of Windows, and the pixel geometry, gamma, and contrast of the window's monitor.
/// </summary>
/// <remarks>
/// GPUI reads the same values once at startup, without naming a monitor (Zed v1.24.3, <c>crates/gpui_windows/src/direct_write.rs</c>,
/// line 184, and <c>directx_renderer.rs</c>, lines 853 to 865). XylemUI reads them for the window's monitor, when the window is
/// created and when a setting changes; when the window moves to another monitor, from roadmap row W1.
/// </remarks>
internal static unsafe class Win32TextRendering
{
    /// <summary>
    /// Returns the parameters of a monitor.
    /// </summary>
    /// <param name="monitor">The monitor: the window's, or the one it is moving to.</param>
    /// <param name="isOpaque">Whether the window's background is opaque, which ClearType needs.</param>
    /// <returns>The parameters.</returns>
    /// <exception cref="InvalidOperationException">DirectWrite could not create its factory or the monitor's parameters.</exception>
    public static TextRenderingParameters Read(HMONITOR monitor, bool isOpaque)
    {
        IDWriteFactory* factory = null;
        IDWriteRenderingParams* parameters = null;
        IDWriteRenderingParams1* parameters1 = null;
        try
        {
            Guid factoryId = IID.IID_IDWriteFactory;
            ThrowIfFailed(
                DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &factoryId, (IUnknown**)&factory),
                "DWriteCreateFactory"
            );
            ThrowIfFailed(factory->CreateMonitorRenderingParams(monitor, &parameters), "IDWriteFactory::CreateMonitorRenderingParams");
            Guid parametersId = IID.IID_IDWriteRenderingParams1;
            ThrowIfFailed(parameters->QueryInterface(&parametersId, (void**)&parameters1), "IDWriteRenderingParams::QueryInterface");
            DWRITE_PIXEL_GEOMETRY geometry = parameters->GetPixelGeometry();
            BOOL smooths = BOOL.FALSE;
            bool aliased = IsAliasedOf(SystemParametersInfoW(SPI.SPI_GETFONTSMOOTHING, 0, &smooths, 0) != BOOL.FALSE, smooths != BOOL.FALSE);
            return new TextRenderingParameters(
                aliased ? GlyphCoverage.Gray : CoverageOf(SmoothsWithClearType(), isOpaque, geometry),
                geometry == DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR,
                parameters->GetGamma(),
                parameters1->GetGrayscaleEnhancedContrast(),
                parameters->GetEnhancedContrast(),
                aliased
            );
        }
        finally
        {
            Release(ref parameters1);
            Release(ref parameters);
            Release(ref factory);
        }
    }

    /// <summary>
    /// Returns the kind of coverage for the three conditions of design §5.0.3: ClearType only when all three hold. The first two
    /// are GPUI's; the third is XylemUI's own, so that a monitor without a row of subpixels gets no colored fringes.
    /// </summary>
    /// <param name="clearType">Whether the font smoothing setting of Windows is ClearType.</param>
    /// <param name="isOpaque">Whether the window's background is opaque.</param>
    /// <param name="geometry">The pixel geometry of the window's monitor.</param>
    /// <returns><see cref="GlyphCoverage.Rgb"/> or <see cref="GlyphCoverage.Gray"/>.</returns>
    public static GlyphCoverage CoverageOf(bool clearType, bool isOpaque, DWRITE_PIXEL_GEOMETRY geometry) =>
        clearType && isOpaque && (geometry is DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_RGB or DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR)
            ? GlyphCoverage.Rgb
            : GlyphCoverage.Gray;

    /// <summary>
    /// Returns whether glyphs are drawn bilevel: only when the setting "Smooth edges of screen fonts" was read and is off
    /// (<c>SPI_GETFONTSMOOTHING</c>), whatever the kind of smoothing. A setting that cannot be read counts as on, the default of
    /// Windows; Chromium, Firefox, and WPF, which follow the setting, draw bilevel then (design §5.0.3, "When fonts are not
    /// smoothed", PR #200).
    /// </summary>
    /// <param name="read">Whether Windows returned the setting.</param>
    /// <param name="smooths">Whether the setting is on.</param>
    /// <returns>Whether glyphs are bilevel.</returns>
    public static bool IsAliasedOf(bool read, bool smooths) => read && !smooths;

    // GPUI takes ClearType when the setting cannot be read (direct_write.rs, lines 1882 to 1897); so does this.
    private static bool SmoothsWithClearType()
    {
        uint type = 0;
        return SystemParametersInfoW(SPI.SPI_GETFONTSMOOTHINGTYPE, 0, &type, 0) == BOOL.FALSE || type == FE_FONTSMOOTHINGCLEARTYPE;
    }
}
