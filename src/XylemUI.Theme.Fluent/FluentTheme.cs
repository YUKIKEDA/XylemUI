namespace XylemUI.Theme.Fluent;

/// <summary>
/// The values of a Fluent 2 theme (design §13.2, "テーマの値"): one immutable object, replaced whole when the theme changes. The
/// variant, the density, and the accent color are set only by <see cref="Create"/>, since the colors, the spacing, and the sizes all
/// follow from them; an app that changes colors or radii replaces a group of tokens with <see langword="with"/>.
/// </summary>
public sealed record FluentTheme
{
    private FluentTheme(ThemeVariant variant, Density density, Color? accent)
    {
        Variant = variant;
        Density = density;
        Accent = accent;
    }

    /// <summary>
    /// Gets the variant the colors were made for.
    /// </summary>
    public ThemeVariant Variant { get; }

    /// <summary>
    /// Gets the density the spacing and sizes were made for.
    /// </summary>
    public Density Density { get; }

    /// <summary>
    /// Gets the accent color the brand steps were made from; <see langword="null"/> for Fluent's own steps (<c>brandWeb</c>).
    /// </summary>
    public Color? Accent { get; }

    /// <summary>
    /// Gets the alias colors: neutral, brand, status, and stroke colors.
    /// </summary>
    public required FluentColors Colors { get; init; }

    /// <summary>
    /// Gets the spacing between and inside parts.
    /// </summary>
    public required FluentSpacing Spacing { get; init; }

    /// <summary>
    /// Gets the sizes of parts and the widths of strokes.
    /// </summary>
    public required FluentSizes Sizes { get; init; }

    /// <summary>
    /// Gets the corner radii.
    /// </summary>
    public required FluentRadii Radii { get; init; }

    /// <summary>
    /// Gets the text styles.
    /// </summary>
    public required FluentTypography Typography { get; init; }

    /// <summary>
    /// Gets the elevation shadows, whose colors follow the variant.
    /// </summary>
    public required FluentShadows Shadows { get; init; }

    /// <summary>
    /// Creates the theme of a variant, a density, and an accent color, from the tokens of Fluent UI (design §13.2, "トークンの元").
    /// </summary>
    /// <param name="variant">The variant.</param>
    /// <param name="density">The density. <see cref="Density.Compact"/> takes <see cref="Density.Standard"/>'s spacing and sizes until
    /// row B1 sets its own.</param>
    /// <param name="accent">The color the 16 brand steps are made from (its alpha is not read), or <see langword="null"/> for
    /// Fluent's own steps. A high-contrast theme keeps it in <see cref="Accent"/> and uses no brand step.</param>
    /// <param name="contrast">The OS's high-contrast colors, which a <see cref="ThemeVariant.HighContrast"/> theme's colors come from;
    /// <see langword="null"/> for the other variants.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variant"/> or <paramref name="density"/> is not a value of its
    /// enum.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="variant"/> is <see cref="ThemeVariant.HighContrast"/> and
    /// <paramref name="contrast"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="variant"/> is <see cref="ThemeVariant.Light"/> or
    /// <see cref="ThemeVariant.Dark"/> and <paramref name="contrast"/> is not <see langword="null"/>.</exception>
    public static FluentTheme Create(ThemeVariant variant, Density density, Color? accent, HighContrastColors? contrast = null)
    {
        if (density is not (Density.Standard or Density.Compact))
        {
            throw new ArgumentOutOfRangeException(nameof(density), density, "The density is not a value of Density.");
        }

        FluentColors colors;
        switch (variant)
        {
            case ThemeVariant.Light or ThemeVariant.Dark:
                if (contrast is not null)
                {
                    throw new ArgumentException("High-contrast colors are given for a theme that is not high contrast.", nameof(contrast));
                }

                Color[] brand = Brand(accent);
                colors = variant == ThemeVariant.Light ? FluentColors.Light(brand) : FluentColors.Dark(brand);
                break;
            case ThemeVariant.HighContrast:
                if (contrast is not { } system)
                {
                    throw new ArgumentNullException(nameof(contrast), "A high-contrast theme needs the OS's high-contrast colors.");
                }

                colors = FluentColors.HighContrast(system);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, "The variant is not a value of ThemeVariant.");
        }

        return new FluentTheme(variant, density, accent)
        {
            Colors = colors,
            Spacing = FluentSpacing.Standard,
            Sizes = FluentSizes.Standard,
            Radii = FluentRadii.Standard,
            Typography = FluentTypography.Standard,
            Shadows = FluentShadows.Create(
                colors.ColorNeutralShadowAmbient,
                colors.ColorNeutralShadowKey,
                colors.ColorBrandShadowAmbient,
                colors.ColorBrandShadowKey
            ),
        };
    }

    // The 16 brand steps: Fluent's own (brandWeb) without an accent, or the steps made from it (design §13.2, "アクセントの色").
    private static Color[] Brand(Color? accent)
    {
        if (accent is { } key)
        {
            return BrandRamp.From(key);
        }

        ReadOnlySpan<uint> web = FluentBrand.Web;
        var steps = new Color[BrandRamp.Steps];
        for (int i = 0; i < steps.Length; i++)
        {
            steps[i] = Color.FromRgb(web[i]);
        }

        return steps;
    }
}
