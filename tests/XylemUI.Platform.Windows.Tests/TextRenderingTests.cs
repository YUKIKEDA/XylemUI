using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// How a window's text is drawn (design §5.0.3, "ClearType", roadmap row T2): the three conditions of ClearType, the values read for
// the window's monitor, and reading them again when a setting of Windows changes. No test changes a setting of Windows: the
// conditions are tested on their inputs, and the reading again with a reader the test sets.
[Collection(UiThreadGroup.Name)]
public sealed class TextRenderingTests(UiThreadFixture ui)
{
    /// <summary>
    /// ClearType only when all three conditions hold: Windows smooths fonts with ClearType, the window is opaque, and the monitor's
    /// subpixels are a row of red, green, and blue in either order.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Take the coverage for each combination of the setting (ClearType or not), the window (opaque or not), and the pixel
    ///   geometry (RGB, BGR, flat).
    ///
    /// Expected:
    /// - Rgb for ClearType, opaque, and RGB or BGR (2 of the 12); Gray for the other 10.
    /// </remarks>
    /// <param name="clearType">Whether the setting is ClearType.</param>
    /// <param name="isOpaque">Whether the window is opaque.</param>
    /// <param name="geometry">The pixel geometry.</param>
    /// <param name="expected">The coverage.</param>
    [Theory]
    [InlineData(true, true, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_RGB, GlyphCoverage.Rgb)]
    [InlineData(true, true, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR, GlyphCoverage.Rgb)]
    [InlineData(true, true, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_FLAT, GlyphCoverage.Gray)]
    [InlineData(true, false, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_RGB, GlyphCoverage.Gray)]
    [InlineData(true, false, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR, GlyphCoverage.Gray)]
    [InlineData(true, false, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_FLAT, GlyphCoverage.Gray)]
    [InlineData(false, true, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_RGB, GlyphCoverage.Gray)]
    [InlineData(false, true, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR, GlyphCoverage.Gray)]
    [InlineData(false, true, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_FLAT, GlyphCoverage.Gray)]
    [InlineData(false, false, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_RGB, GlyphCoverage.Gray)]
    [InlineData(false, false, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR, GlyphCoverage.Gray)]
    [InlineData(false, false, DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_FLAT, GlyphCoverage.Gray)]
    public void CoverageOf_Conditions_IsClearTypeOnlyWhenAllHold(
        bool clearType,
        bool isOpaque,
        DWRITE_PIXEL_GEOMETRY geometry,
        GlyphCoverage expected
    ) => Assert.Equal(expected, Win32TextRendering.CoverageOf(clearType, isOpaque, geometry));

    /// <summary>
    /// A new window reads its parameters for the monitor it is on, and they are the values of that monitor.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window.
    ///
    /// Steps:
    /// - Read the window's <see cref="Win32Window.TextRendering"/>, and the monitor's rendering parameters straight from
    ///   DirectWrite (<c>CreateMonitorRenderingParams</c> for <c>MonitorFromWindow</c>) with the font smoothing setting.
    ///
    /// Expected:
    /// - The gamma, both contrasts, and whether the subpixels are BGR are DirectWrite's; the coverage is Rgb exactly when the
    ///   setting is ClearType and the geometry is RGB or BGR (the window is opaque).
    /// </remarks>
    [Fact]
    public void TextRendering_NewWindow_IsTheMonitorsParameters() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("text rendering", 200, 100);

            TextRenderingParameters read = window.TextRendering;
            (DWRITE_PIXEL_GEOMETRY geometry, float gamma, float contrast, float grayscaleContrast) = MonitorParameters(window.Hwnd);
            bool clearType = ClearTypeSetting();

            Assert.Equal(gamma, read.Gamma);
            Assert.Equal(contrast, read.ClearTypeContrast);
            Assert.Equal(grayscaleContrast, read.GrayscaleContrast);
            Assert.Equal(geometry == DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR, read.IsBgr);
            bool row = geometry is DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_RGB or DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_BGR;
            Assert.Equal(clearType && row ? GlyphCoverage.Rgb : GlyphCoverage.Gray, read.Coverage);
        });

    /// <summary>
    /// <c>WM_SETTINGCHANGE</c> reads the parameters again; the window raises <see cref="Win32Window.TextRenderingChanged"/> only
    /// when they changed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window, then a reader set by the test that returns grayscale with gamma 1.8, and a handler that counts the
    ///   event. The reader is put back after the test.
    ///
    /// Steps:
    /// - Send <c>WM_SETTINGCHANGE</c>; send it again; set the reader to return ClearType, BGR; send it again.
    ///
    /// Expected:
    /// - After the first: the window's value is the reader's, and the event was raised once if that differs from the value read
    ///   when the window was created, else not at all. After the second: no new event. After the third: the ClearType value, and
    ///   one more event.
    /// </remarks>
    [Fact]
    public void SettingChange_ReadsAgainAndRaisesWhenChanged() =>
        ui.Invoke(() =>
        {
            Func<HWND, TextRenderingParameters> previous = Win32Window.TextRenderingReader;
            try
            {
                using var window = new Win32Window("setting change", 200, 100);
                TextRenderingParameters created = window.TextRendering;
                var gray = new TextRenderingParameters(GlyphCoverage.Gray, IsBgr: false, Gamma: 1.8f, GrayscaleContrast: 1, ClearTypeContrast: 0.5f);
                var clearType = gray with { Coverage = GlyphCoverage.Rgb, IsBgr = true };
                int raised = 0;
                window.TextRenderingChanged += (_, _) => raised++;
                TextRenderingParameters next = gray;
                Win32Window.TextRenderingReader = _ => next;

                _ = SendMessageW(window.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                int first = raised;
                _ = SendMessageW(window.Hwnd, WM.WM_SETTINGCHANGE, default, default);
                int second = raised;
                next = clearType;
                _ = SendMessageW(window.Hwnd, WM.WM_SETTINGCHANGE, default, default);

                Assert.Equal(created == gray ? 0 : 1, first);
                Assert.Equal(first, second);
                Assert.Equal(first + 1, raised);
                Assert.Equal(clearType, window.TextRendering);
            }
            finally
            {
                Win32Window.TextRenderingReader = previous;
            }
        });

    private static unsafe (DWRITE_PIXEL_GEOMETRY Geometry, float Gamma, float Contrast, float GrayscaleContrast) MonitorParameters(HWND hwnd)
    {
        IDWriteFactory* factory;
        Guid factoryId = IID.IID_IDWriteFactory;
        Assert.True(DirectX.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &factoryId, (IUnknown**)&factory) >= 0);
        IDWriteRenderingParams* parameters;
        Assert.True(factory->CreateMonitorRenderingParams(MonitorFromWindow(hwnd, MONITOR.MONITOR_DEFAULTTONEAREST), &parameters) >= 0);
        IDWriteRenderingParams1* parameters1;
        Guid parametersId = IID.IID_IDWriteRenderingParams1;
        Assert.True(parameters->QueryInterface(&parametersId, (void**)&parameters1) >= 0);
        (DWRITE_PIXEL_GEOMETRY, float, float, float) values = (
            parameters->GetPixelGeometry(),
            parameters->GetGamma(),
            parameters->GetEnhancedContrast(),
            parameters1->GetGrayscaleEnhancedContrast()
        );
        _ = parameters1->Release();
        _ = parameters->Release();
        _ = factory->Release();
        return values;
    }

    private static unsafe bool ClearTypeSetting()
    {
        uint type = 0;
        return SystemParametersInfoW(SPI.SPI_GETFONTSMOOTHINGTYPE, 0, &type, 0) == BOOL.FALSE || type == FE_FONTSMOOTHINGCLEARTYPE;
    }
}
