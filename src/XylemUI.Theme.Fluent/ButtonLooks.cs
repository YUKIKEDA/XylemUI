using System.Runtime.CompilerServices;

namespace XylemUI.Theme.Fluent;

/// <summary>
/// The look a <see cref="Fluent.Button"/> takes from a theme, made once for each theme and appearance (design §13.2, "setter"), so
/// that a button's effect allocates nothing when the theme changes. The values are those of Fluent's medium button
/// (<c>useButtonStyles.styles.ts</c> at microsoft/fluentui 1f59fc2: the base lines 40 to 75 without line 64's smallest width, which
/// is <see cref="MinWidth"/>, and without lines 67 to 69's text, which <see cref="Fluent.Button"/> writes on the row around the
/// content from <see cref="FluentTypography.Body1Strong"/>; the focus-visible state of lines 105 to 114, of which only the
/// border's color is drawn; the primary lines 151 to 166; the disabled lines 322 to 369 and 420 to 430), read through the theme's
/// tokens.
/// </summary>
internal sealed class ButtonLooks
{
    /// <summary>
    /// The smallest width of the medium button, a constant of the button's styles, not a token (<c>minWidth: '96px'</c>, line 64).
    /// It is the same in every theme, so the button writes it once, when it is made.
    /// </summary>
    public const float MinWidth = 96;

    // The top and bottom padding of the medium button, a constant of the button's styles, not a token (buttonSpacingMedium, line 19).
    private const float VerticalPadding = 5;

    // The looks made for a theme, one for each appearance. The theme is a weak key, so that the entry goes with it.
    private static readonly ConditionalWeakTable<FluentTheme, ButtonLooks[]> _made = new();

    private ButtonLooks(
        FluentTheme theme,
        Paint background,
        Paint border,
        Color foreground,
        PaintStyle hovered,
        PaintStyle pressed,
        Paint disabledBorder
    )
    {
        FluentColors colors = theme.Colors;
        Background = background;
        Border = border;
        Foreground = foreground;
        Hovered = hovered;
        Pressed = pressed;
        Radius = theme.Radii.BorderRadiusMedium;
        Padding = new Insets(theme.Spacing.SpacingHorizontalM, VerticalPadding);
        BorderWidth = theme.Sizes.StrokeWidths.StrokeWidthThin;
        Disabled = new PaintStyle
        {
            Background = colors.ColorNeutralBackgroundDisabled,
            BorderPaint = disabledBorder,
            Foreground = colors.ColorNeutralForegroundDisabled,
        };

        // Fluent's focus indicator (lines 105 to 114) also lays a ring of 1 inside the border in the same color and a transparent
        // outline of 2 outside it; a pseudo-state's look holds no shadow and Core draws no outline (design §13.2, "TH1 のボタンの値").
        FocusVisible = new PaintStyle { BorderPaint = colors.ColorStrokeFocus2 };
    }

    /// <summary>
    /// Gets the background.
    /// </summary>
    public Paint Background { get; }

    /// <summary>
    /// Gets the paint of the border.
    /// </summary>
    public Paint Border { get; }

    /// <summary>
    /// Gets the color of the text.
    /// </summary>
    public Color Foreground { get; }

    /// <summary>
    /// Gets the radius of the corners.
    /// </summary>
    public float Radius { get; }

    /// <summary>
    /// Gets what the hovered state lays over the look.
    /// </summary>
    public PaintStyle Hovered { get; }

    /// <summary>
    /// Gets what the pressed state lays over the look.
    /// </summary>
    public PaintStyle Pressed { get; }

    /// <summary>
    /// Gets what the disabled state lays over the look.
    /// </summary>
    public PaintStyle Disabled { get; }

    /// <summary>
    /// Gets what the focus-visible state lays over the look: the border's color of Fluent's focus indicator.
    /// </summary>
    public PaintStyle FocusVisible { get; }

    /// <summary>
    /// Gets the padding, which changes the layout.
    /// </summary>
    public Insets Padding { get; }

    /// <summary>
    /// Gets the width of the border, which changes the layout.
    /// </summary>
    public float BorderWidth { get; }

    /// <summary>
    /// Returns the look of an appearance in a theme: the one made the first time it was asked for, with no allocation after that.
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <param name="appearance">The appearance, a value of its enum.</param>
    /// <returns>The look.</returns>
    public static ButtonLooks Of(FluentTheme theme, ButtonAppearance appearance)
    {
        if (!_made.TryGetValue(theme, out ButtonLooks[]? looks))
        {
            looks = [Standard(theme), Primary(theme)];
            _made.AddOrUpdate(theme, looks);
        }

        return looks[(int)appearance];
    }

    // Fluent's secondary button: the neutral background, the neutral border, and the neutral text, each with its hover and press.
    private static ButtonLooks Standard(FluentTheme theme)
    {
        FluentColors c = theme.Colors;
        return new ButtonLooks(
            theme,
            c.ColorNeutralBackground1,
            c.ColorNeutralStroke1,
            c.ColorNeutralForeground1,
            new PaintStyle
            {
                Background = c.ColorNeutralBackground1Hover,
                BorderPaint = c.ColorNeutralStroke1Hover,
                Foreground = c.ColorNeutralForeground1Hover,
            },
            new PaintStyle
            {
                Background = c.ColorNeutralBackground1Pressed,
                BorderPaint = c.ColorNeutralStroke1Pressed,
                Foreground = c.ColorNeutralForeground1Pressed,
            },
            c.ColorNeutralStrokeDisabled
        );
    }

    // Fluent's primary button: the brand background with the text on brand, and a border that Fluent writes as 'transparent', here
    // the token of a transparent stroke, which high contrast gives the window's text color.
    private static ButtonLooks Primary(FluentTheme theme)
    {
        FluentColors c = theme.Colors;
        return new ButtonLooks(
            theme,
            c.ColorBrandBackground,
            c.ColorTransparentStroke,
            c.ColorNeutralForegroundOnBrand,
            new PaintStyle
            {
                Background = c.ColorBrandBackgroundHover,
                BorderPaint = c.ColorTransparentStroke,
                Foreground = c.ColorNeutralForegroundOnBrand,
            },
            new PaintStyle
            {
                Background = c.ColorBrandBackgroundPressed,
                BorderPaint = c.ColorTransparentStroke,
                Foreground = c.ColorNeutralForegroundOnBrand,
            },
            c.ColorTransparentStroke
        );
    }

    /// <summary>
    /// The padding on the left and right, and on the top and bottom.
    /// </summary>
    /// <param name="Horizontal">The left and right padding.</param>
    /// <param name="Vertical">The top and bottom padding.</param>
    internal readonly record struct Insets(float Horizontal, float Vertical);
}
