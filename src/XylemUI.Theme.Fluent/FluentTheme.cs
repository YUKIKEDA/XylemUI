using System.Runtime.CompilerServices;

namespace XylemUI.Theme.Fluent;

/// <summary>
/// The values of a Fluent 2 theme (design §13.2, "テーマの値"): one immutable object, replaced whole when the theme changes. The
/// variant, the density, and the accent color are set only by <see cref="Create"/>, since the colors, the spacing, and the sizes all
/// follow from them; an app that changes colors or radii replaces a group of tokens with <see langword="with"/>.
/// </summary>
public sealed record FluentTheme
{
    // What the root wrapper is missing, named where a theme is looked for and none is there (design §13.2, "テーマの鍵").
    private const string NoWrapper =
        "add [assembly: WindowTheme(typeof(FluentRoot))] to the app, or set HeadlessOptions.RootWrapper to FluentRoot.Wrap in a test.";

    // The themes ProvideVariant made from a parent theme, one slot for light and one for dark (design §13.2, "同じテーマを返す"). The
    // parent is a weak key, so that the entry goes with it and switching themes again and again does not grow the table.
    private static readonly ConditionalWeakTable<FluentTheme, FluentTheme?[]> _variants = new();

    private FluentTheme(ThemeVariant variant, Density density, Color? accent)
    {
        Variant = variant;
        Density = density;
        Accent = accent;
    }

    /// <summary>
    /// Gets the key of the theme (design §13.2, "テーマの鍵"): <see cref="FluentRoot.Wrap"/> provides the window's theme at its root,
    /// and a part of the tree may provide another, which reaches the parts in it and not the window's background or title bar. The
    /// key has no default, so <see cref="Context{T}.Use"/> throws <see cref="InvalidOperationException"/> where no theme is provided.
    /// </summary>
    public static Context<IReadOnlySignal<FluentTheme>> Current { get; } = new();

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

    /// <summary>
    /// Asks for the theme of the window the calling code belongs to (design §13.2, "窓のテーマ"): call it in the window's root
    /// function, or in a handler or an effect of its tree. The theme reaches the root box's background and color of text, the title
    /// bar, and every part of the window. A second call replaces the theme of the first; <see langword="null"/> goes back to the
    /// theme made from the OS's appearance. Each window has its own.
    /// </summary>
    /// <param name="theme">The theme, or <see langword="null"/> for the one made from the OS's appearance.</param>
    /// <exception cref="InvalidOperationException">The window is not wrapped by <see cref="FluentRoot.Wrap"/>; or there is no
    /// current scope, or the calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    public static void RequestForWindow(IReadOnlySignal<FluentTheme>? theme)
    {
        if (!FluentRoot.Request.TryUse(out Signal<IReadOnlySignal<FluentTheme>?>? request))
        {
            throw new InvalidOperationException("The window has no root wrapper of the Fluent theme: " + NoWrapper);
        }

        request.Set(theme);
    }

    /// <summary>
    /// Provides, around <paramref name="body"/>, the theme of the tree around it in another variant, with its density and accent
    /// color (design §13.2, "一部だけ別のテーマ"): a dark card in a light window. The parts in <paramref name="body"/> follow the
    /// theme around as it changes; the window's background and title bar do not change. Under a high-contrast theme the parts keep
    /// that theme, and a group of tokens that the theme around replaced with <see langword="with"/> is not carried into a theme of
    /// another variant. The same theme around and variant give the same instance.
    /// </summary>
    /// <param name="variant"><see cref="ThemeVariant.Light"/> or <see cref="ThemeVariant.Dark"/>.</param>
    /// <param name="body">The code that makes the elements of the part.</param>
    /// <returns>The element <paramref name="body"/> returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variant"/> is neither <see cref="ThemeVariant.Light"/> nor
    /// <see cref="ThemeVariant.Dark"/>.</exception>
    /// <exception cref="InvalidOperationException">No theme is provided here; or there is no current scope, or the calling thread
    /// is not the UI thread.</exception>
    [UiThreadOnly]
    public static Element ProvideVariant(ThemeVariant variant, Func<Element> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (variant is not (ThemeVariant.Light or ThemeVariant.Dark))
        {
            throw new ArgumentOutOfRangeException(nameof(variant), variant, "A part of the tree is given a light or a dark theme only.");
        }

        IReadOnlySignal<FluentTheme> around = UseForControl();
        Computed<FluentTheme> theme = Computed.Create(() => InVariant(around.Get(), variant));
        return Current.Provide(theme, body);
    }

    /// <summary>
    /// Returns the theme a part of Theme.Fluent follows: the nearest one provided (design §13.2, "テーマの鍵"). A part calls it once,
    /// when it is made.
    /// </summary>
    /// <returns>The theme's signal.</returns>
    /// <exception cref="InvalidOperationException">No theme is provided here, which names the attribute and the option that provide
    /// one; or there is no current scope, or the calling thread is not the UI thread.</exception>
    [UiThreadOnly]
    internal static IReadOnlySignal<FluentTheme> UseForControl()
    {
        if (!Current.TryUse(out IReadOnlySignal<FluentTheme>? theme))
        {
            throw new InvalidOperationException("No Fluent theme is provided here: " + NoWrapper);
        }

        return theme;
    }

    /// <summary>
    /// Returns <paramref name="around"/> in <paramref name="variant"/>, the instance made the first time for the two (design §13.2,
    /// "一部だけ別のテーマ"): <paramref name="around"/> itself when it is high contrast or already of that variant.
    /// </summary>
    /// <param name="around">The theme of the tree around.</param>
    /// <param name="variant"><see cref="ThemeVariant.Light"/> or <see cref="ThemeVariant.Dark"/>.</param>
    /// <returns>The theme.</returns>
    internal static FluentTheme InVariant(FluentTheme around, ThemeVariant variant)
    {
        if (around.Variant == ThemeVariant.HighContrast || around.Variant == variant)
        {
            return around;
        }

        FluentTheme?[] made = _variants.GetValue(around, static _ => new FluentTheme?[2]);
        return made[(int)variant] ??= Create(variant, around.Density, around.Accent);
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
