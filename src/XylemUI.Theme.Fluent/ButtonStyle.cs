namespace XylemUI.Theme.Fluent;

/// <summary>
/// What an app sets of a <see cref="Fluent.Button"/>'s look (design §13.2, "アプリが重ねた見た目"): a value that is set wins over
/// the theme's, also after the theme changes; <see langword="null"/> takes the theme's. A look set with the method chain after the
/// button is made does not stay: the button's effect writes the theme's again at the next change of the theme.
/// </summary>
public sealed record ButtonStyle
{
    /// <summary>
    /// Gets the background; <see langword="null"/> for the theme's.
    /// </summary>
    public Paint? Background { get; init; }

    /// <summary>
    /// Gets the paint of the border; <see langword="null"/> for the theme's. The width is the theme's.
    /// </summary>
    public Paint? Border { get; init; }

    /// <summary>
    /// Gets the color of the text; <see langword="null"/> for the theme's.
    /// </summary>
    public Color? Foreground { get; init; }

    /// <summary>
    /// Gets the radius of the corners; <see langword="null"/> for the theme's.
    /// </summary>
    public float? Radius { get; init; }

    /// <summary>
    /// Gets what the hovered state lays over the look; <see langword="null"/> for the theme's.
    /// </summary>
    public PaintStyle? Hovered { get; init; }

    /// <summary>
    /// Gets what the pressed state lays over the look; <see langword="null"/> for the theme's.
    /// </summary>
    public PaintStyle? Pressed { get; init; }

    /// <summary>
    /// Gets what the disabled state lays over the look; <see langword="null"/> for the theme's.
    /// </summary>
    public PaintStyle? Disabled { get; init; }
}
