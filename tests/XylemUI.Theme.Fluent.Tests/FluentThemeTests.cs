using System.Numerics;
using XylemUI.Text;

namespace XylemUI.Theme.Fluent.Tests;

// The values of a Fluent theme (design §13.2, "テーマの値" and "トークンの元"). The expected colors are those of the TypeScript files of
// @fluentui/tokens at microsoft/fluentui 1f59fc2 (packages/tokens/src), read by hand, and the brand steps those of the record
// docs/benchmarks/th1-accent-ramp.md.
public sealed class FluentThemeTests
{
    private static readonly Color Transparent = new(0, 0, 0, 0);

    // Eight colors that differ from each other and from Fluent's own high-contrast colors.
    private static readonly HighContrastColors Os = new(
        Window: new Color(0x10, 0x00, 0x00),
        WindowText: new Color(0x20, 0x00, 0x00),
        Hotlight: new Color(0x30, 0x00, 0x00),
        GrayText: new Color(0x40, 0x00, 0x00),
        Highlight: new Color(0x50, 0x00, 0x00),
        HighlightText: new Color(0x60, 0x00, 0x00),
        ButtonFace: new Color(0x70, 0x00, 0x00),
        ButtonText: new Color(0x80, 0x00, 0x00)
    );

    /// <summary>
    /// A theme keeps its variant, density, and accent color, and takes the colors of its variant and the steps of its accent.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Each variant, with each density, without an accent and with the measured blue #0078D4; the high-contrast theme with
    ///   eight OS colors that differ from each other.
    ///
    /// Steps:
    /// - Create the theme.
    ///
    /// Expected:
    /// - Variant, Density, and Accent are the arguments.
    /// - colorNeutralBackground1 is white (light), grey[16] #292929 (dark), or the OS's Window (high contrast).
    /// - colorBrandBackground is brand[80] in light (#0f6cbd without an accent, #006cc0 from the blue), brand[70] in dark
    ///   (#115ea3, #005ea8), and the OS's ButtonFace in high contrast whatever the accent.
    /// </remarks>
    /// <param name="variant">The variant.</param>
    /// <param name="density">The density.</param>
    /// <param name="withAccent">Whether the accent is the measured blue.</param>
    [Theory]
    [InlineData(ThemeVariant.Light, Density.Standard, false)]
    [InlineData(ThemeVariant.Light, Density.Compact, true)]
    [InlineData(ThemeVariant.Light, Density.Standard, true)]
    [InlineData(ThemeVariant.Dark, Density.Standard, false)]
    [InlineData(ThemeVariant.Dark, Density.Compact, true)]
    [InlineData(ThemeVariant.Dark, Density.Compact, false)]
    [InlineData(ThemeVariant.HighContrast, Density.Standard, false)]
    [InlineData(ThemeVariant.HighContrast, Density.Compact, true)]
    public void Create_EachVariantDensityAndAccent_KeepsThemAndTakesTheirColors(ThemeVariant variant, Density density, bool withAccent)
    {
        Color? accent = withAccent ? Color.FromRgb(0x0078D4) : null;
        HighContrastColors? contrast = variant == ThemeVariant.HighContrast ? Os : null;

        FluentTheme theme = FluentTheme.Create(variant, density, accent, contrast);

        Assert.Equal((variant, density, accent), (theme.Variant, theme.Density, theme.Accent));
        (Color background, Color brand) = variant switch
        {
            ThemeVariant.Light => (Color.FromRgb(0xFFFFFF), Color.FromRgb(withAccent ? 0x006CC0u : 0x0F6CBDu)),
            ThemeVariant.Dark => (Color.FromRgb(0x292929), Color.FromRgb(withAccent ? 0x005EA8u : 0x115EA3u)),
            _ => (Os.Window, Os.ButtonFace),
        };
        Assert.Equal(background, theme.Colors.ColorNeutralBackground1);
        Assert.Equal(brand, theme.Colors.ColorBrandBackground);
    }

    /// <summary>
    /// Without an accent color, the brand tokens take Fluent's own steps (brandWeb); with one, the steps made from it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The light and dark themes without an accent, and the light theme with the measured blue #0078D4.
    ///
    /// Steps:
    /// - Read brand tokens that name steps 30, 40, 70, 80, 100, 110, and 160 (alias/lightColor.ts and darkColor.ts).
    ///
    /// Expected:
    /// - Without an accent: brandWeb's steps (#0a2e4a, #0c3b5e, #115ea3, #0f6cbd, #479ef5, #62abf5, #ebf3fc); dark
    ///   colorBrandForeground1 is brand[100] #479ef5.
    /// - With the blue: the record's steps of #0078D4 (#002d55, #00396b, #005ea8, #006cc0, #5e9cf0, #75a8f6, #edf3ff).
    /// </remarks>
    [Fact]
    public void Create_WithoutAccent_TakesBrandWeb_AndWithOneTakesItsSteps()
    {
        FluentColors web = FluentTheme.Create(ThemeVariant.Light, Density.Standard, null).Colors;
        FluentColors dark = FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null).Colors;
        FluentColors blue = FluentTheme.Create(ThemeVariant.Light, Density.Standard, Color.FromRgb(0x0078D4)).Colors;

        Assert.Equal(new uint[] { 0x0A2E4A, 0x0C3B5E, 0x115EA3, 0x0F6CBD, 0x479EF5, 0x62ABF5, 0xEBF3FC }, BrandTokens(web).Select(Rgb));
        Assert.Equal(Color.FromRgb(0x479EF5), dark.ColorBrandForeground1);
        Assert.Equal(new uint[] { 0x002D55, 0x00396B, 0x005EA8, 0x006CC0, 0x5E9CF0, 0x75A8F6, 0xEDF3FF }, BrandTokens(blue).Select(Rgb));
    }

    /// <summary>
    /// Fluent's neutral, translucent, status, and palette colors come in, with the patches the palette files make to each variant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The light and dark themes without an accent.
    ///
    /// Steps:
    /// - Read tokens of each kind.
    ///
    /// Expected:
    /// - Light: colorNeutralForeground1 grey[14] #242424; colorNeutralForegroundInvertedDisabled whiteAlpha[40], white at alpha 102;
    ///   colorNeutralShadowAmbient rgba(0,0,0,0.12), black at 31 (30.6 rounded); colorNeutralBackground7 #00000000; colorStatusDanger
    ///   Background1 cranberry.tint60 #fdf3f4; colorStatusDangerBackground3Hover cranberry.shade10 #b10e1c (a patch);
    ///   colorStatusWarningForegroundInverted orange.tint30 #faa06b (the template); colorPaletteRedBackground1 red.tint60 #fdf6f6;
    ///   colorPaletteYellowForeground1 yellow.shade30 #817400 (a patch).
    /// - Dark: colorNeutralForeground1 white; colorStatusWarningForegroundInverted orange.shade20 #bc4b09 (dark's patch);
    ///   colorPaletteRedForeground3 red.tint30 #e37d80 (dark's patch).
    /// </remarks>
    [Fact]
    public void Create_LightAndDark_TakeFluentsNeutralTranslucentStatusAndPaletteColors()
    {
        FluentColors light = FluentTheme.Create(ThemeVariant.Light, Density.Standard, null).Colors;
        FluentColors dark = FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null).Colors;

        Assert.Equal(Color.FromRgb(0x242424), light.ColorNeutralForeground1);
        Assert.Equal(new Color(255, 255, 255, 102), light.ColorNeutralForegroundInvertedDisabled);
        Assert.Equal(new Color(0, 0, 0, 31), light.ColorNeutralShadowAmbient);
        Assert.Equal(Transparent, light.ColorNeutralBackground7);
        Assert.Equal(Color.FromRgb(0xFDF3F4), light.ColorStatusDangerBackground1);
        Assert.Equal(Color.FromRgb(0xB10E1C), light.ColorStatusDangerBackground3Hover);
        Assert.Equal(Color.FromRgb(0xFAA06B), light.ColorStatusWarningForegroundInverted);
        Assert.Equal(Color.FromRgb(0xFDF6F6), light.ColorPaletteRedBackground1);
        Assert.Equal(Color.FromRgb(0x817400), light.ColorPaletteYellowForeground1);
        Assert.Equal(Color.FromRgb(0xFFFFFF), dark.ColorNeutralForeground1);
        Assert.Equal(Color.FromRgb(0xBC4B09), dark.ColorStatusWarningForegroundInverted);
        Assert.Equal(Color.FromRgb(0xE37D80), dark.ColorPaletteRedForeground3);
    }

    /// <summary>
    /// A high-contrast theme takes each of the OS's eight colors where Fluent names its CSS system color, keeps Fluent's fixed
    /// values, and uses no brand step.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Eight OS colors that differ from each other; the high-contrast theme without an accent and with red #E81123.
    ///
    /// Steps:
    /// - Read a token of each system color, of each fixed value, and of the palette.
    ///
    /// Expected:
    /// - colorNeutralBackground1 is Window (Canvas), colorNeutralForeground1 WindowText (CanvasText), colorBrandForegroundLink
    ///   Hotlight (LinkText), colorNeutralForegroundDisabled GrayText, colorSubtleBackgroundHover Highlight,
    ///   colorNeutralForeground1Hover HighlightText, colorBrandBackground ButtonFace, colorNeutralForegroundOnBrand ButtonText.
    /// - colorSubtleBackground is transparent, colorBackgroundOverlay blackAlpha[50] (black at 128), colorNeutralShadowAmbient
    ///   rgba(0,0,0,0.24) (black at 61).
    /// - colorPaletteRedBackground1 is Window, colorStatusDangerBackground3Hover and colorStatusSuccessBorderActive Highlight.
    /// - The colors with the red accent equal those without.
    /// </remarks>
    [Fact]
    public void Create_HighContrast_TakesTheOsColorsAndFluentsFixedValues()
    {
        FluentColors colors = FluentTheme.Create(ThemeVariant.HighContrast, Density.Standard, null, Os).Colors;
        FluentColors red = FluentTheme.Create(ThemeVariant.HighContrast, Density.Standard, Color.FromRgb(0xE81123), Os).Colors;

        Assert.Equal(Os.Window, colors.ColorNeutralBackground1);
        Assert.Equal(Os.WindowText, colors.ColorNeutralForeground1);
        Assert.Equal(Os.Hotlight, colors.ColorBrandForegroundLink);
        Assert.Equal(Os.GrayText, colors.ColorNeutralForegroundDisabled);
        Assert.Equal(Os.Highlight, colors.ColorSubtleBackgroundHover);
        Assert.Equal(Os.HighlightText, colors.ColorNeutralForeground1Hover);
        Assert.Equal(Os.ButtonFace, colors.ColorBrandBackground);
        Assert.Equal(Os.ButtonText, colors.ColorNeutralForegroundOnBrand);
        Assert.Equal(Transparent, colors.ColorSubtleBackground);
        Assert.Equal(new Color(0, 0, 0, 128), colors.ColorBackgroundOverlay);
        Assert.Equal(new Color(0, 0, 0, 61), colors.ColorNeutralShadowAmbient);
        Assert.Equal(Os.Window, colors.ColorPaletteRedBackground1);
        Assert.Equal(Os.Highlight, colors.ColorStatusDangerBackground3Hover);
        Assert.Equal(Os.Highlight, colors.ColorStatusSuccessBorderActive);
        Assert.Equal(colors, red);
    }

    /// <summary>
    /// Create refuses a high-contrast theme without the OS's colors, OS colors for another variant, and values outside the enums.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Create a high-contrast theme with null colors; a light and a dark theme with the OS's colors; a theme of variant 3 and one
    ///   of density 2.
    ///
    /// Expected:
    /// - ArgumentNullException, ArgumentException (twice), and ArgumentOutOfRangeException (twice), each naming its parameter.
    /// </remarks>
    [Fact]
    public void Create_WrongContrastOrEnum_Throws()
    {
        Assert.Equal(
            "contrast",
            Assert.Throws<ArgumentNullException>(() => FluentTheme.Create(ThemeVariant.HighContrast, Density.Standard, null)).ParamName
        );
        Assert.Equal(
            "contrast",
            Assert.Throws<ArgumentException>(() => FluentTheme.Create(ThemeVariant.Light, Density.Standard, null, Os)).ParamName
        );
        Assert.Equal("contrast", Assert.Throws<ArgumentException>(() => FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null, Os)).ParamName);
        Assert.Equal(
            "variant",
            Assert.Throws<ArgumentOutOfRangeException>(() => FluentTheme.Create((ThemeVariant)3, Density.Standard, null)).ParamName
        );
        Assert.Equal("density", Assert.Throws<ArgumentOutOfRangeException>(() => FluentTheme.Create(ThemeVariant.Light, (Density)2, null)).ParamName);
    }

    /// <summary>
    /// The spacing, sizes, radii, and text styles are Fluent's, the same for every variant and, until row B1, for both densities;
    /// the shadows take the shadow colors of their variant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The light theme, Standard and Compact, and the dark theme.
    ///
    /// Steps:
    /// - Read tokens of each group.
    ///
    /// Expected:
    /// - spacingHorizontalNone 0, spacingHorizontalM 12, spacingVerticalXXXL 32; strokeWidthThickest 4; borderRadiusMedium 4,
    ///   borderRadiusCircular 10000; body1 14/20/400, title1 32/40/600, display 68/92/600 (global/fonts.ts).
    /// - Compact's spacing, sizes, radii, and typography equal Standard's.
    /// - Light shadow4: ambient black at 31 (0.12), offset 0, blur 2; key black at 36 (0.14), offset 0 2, blur 4. Light shadow64Brand:
    ///   ambient black at 77 (0.30), blur 8; key black at 64 (0.25), offset 0 32, blur 64. Dark shadow4: black at 61 (0.24) and 71
    ///   (0.28).
    /// </remarks>
    [Fact]
    public void Create_SpacingSizesRadiiTypographyAndShadows_AreFluents()
    {
        FluentTheme standard = FluentTheme.Create(ThemeVariant.Light, Density.Standard, null);
        FluentTheme compact = FluentTheme.Create(ThemeVariant.Light, Density.Compact, null);
        FluentTheme dark = FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null);

        Assert.Equal(
            (0f, 12f, 32f),
            (standard.Spacing.SpacingHorizontalNone, standard.Spacing.SpacingHorizontalM, standard.Spacing.SpacingVerticalXXXL)
        );
        Assert.Equal(4f, standard.Sizes.StrokeWidthThickest);
        Assert.Equal((4f, 10000f), (standard.Radii.BorderRadiusMedium, standard.Radii.BorderRadiusCircular));
        Assert.Equal(new FluentTextStyle(14, 20, new FontWeight(400)), standard.Typography.Body1);
        Assert.Equal(new FluentTextStyle(32, 40, new FontWeight(600)), standard.Typography.Title1);
        Assert.Equal(new FluentTextStyle(68, 92, new FontWeight(600)), standard.Typography.Display);
        Assert.Equal(
            (standard.Spacing, standard.Sizes, standard.Radii, standard.Typography),
            (compact.Spacing, compact.Sizes, compact.Radii, compact.Typography)
        );
        Assert.Equal(
            new FluentShadow(new BoxShadow(new Color(0, 0, 0, 31), Vector2.Zero, 2), new BoxShadow(new Color(0, 0, 0, 36), new Vector2(0, 2), 4)),
            standard.Shadows.Shadow4
        );
        Assert.Equal(
            new FluentShadow(new BoxShadow(new Color(0, 0, 0, 77), Vector2.Zero, 8), new BoxShadow(new Color(0, 0, 0, 64), new Vector2(0, 32), 64)),
            standard.Shadows.Shadow64Brand
        );
        Assert.Equal((new Color(0, 0, 0, 61), new Color(0, 0, 0, 71)), (dark.Shadows.Shadow4.Ambient.Color, dark.Shadows.Shadow4.Key.Color));
    }

    /// <summary>
    /// An app replaces a group of tokens with <see langword="with"/>, and the rest of the theme stays.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The dark theme of the measured blue.
    ///
    /// Steps:
    /// - Replace its radii with a copy whose borderRadiusMedium is 8.
    ///
    /// Expected:
    /// - The new theme's borderRadiusMedium is 8; its variant, density, accent, and colors are the old theme's; the old theme's
    ///   borderRadiusMedium is still 4.
    /// </remarks>
    [Fact]
    public void With_ReplacesAGroupOfTokens_AndKeepsTheRest()
    {
        FluentTheme theme = FluentTheme.Create(ThemeVariant.Dark, Density.Standard, Color.FromRgb(0x0078D4));

        FluentTheme rounder = theme with { Radii = theme.Radii with { BorderRadiusMedium = 8 } };

        Assert.Equal(8f, rounder.Radii.BorderRadiusMedium);
        Assert.Equal((theme.Variant, theme.Density, theme.Accent), (rounder.Variant, rounder.Density, rounder.Accent));
        Assert.Same(theme.Colors, rounder.Colors);
        Assert.Equal(4f, theme.Radii.BorderRadiusMedium);
    }

    // Brand tokens of the light alias colors, steps 30, 40, 70, 80, 100, 110, and 160 (alias/lightColor.ts).
    private static Color[] BrandTokens(FluentColors colors) =>
        [
            colors.ColorBrandForeground2Pressed,
            colors.ColorBrandBackgroundPressed,
            colors.ColorBrandBackgroundHover,
            colors.ColorBrandBackground,
            colors.ColorBrandForegroundInverted,
            colors.ColorBrandForegroundInvertedHover,
            colors.ColorBrandBackground2,
        ];

    private static uint Rgb(Color color) => ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
