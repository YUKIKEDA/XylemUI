using XylemUI.Primitives;

namespace XylemUI.Theme.Fluent;

/// <summary>
/// Makes the parts of the Fluent theme (design §13.2, "部品がテーマに付いていく形"): each makes a primitive of
/// <c>XylemUI.Primitives</c> and gives it the theme's look with one effect, which writes the look again when the theme changes.
/// Call them with the class's name, <c>Fluent.Button(...)</c>: with <c>using static</c>, C# picks the primitive of the same name.
/// </summary>
public static class Fluent
{
    // The tracks of the grid that centers a button's content, in both axes: free space, the content, free space.
    private static readonly GridTrack[] CenteringTracks = [Track.Fr(1), Track.Auto, Track.Fr(1)];

    /// <summary>
    /// Makes a button (<see cref="Primitive.Button(Element, Action)"/>) with the look of the theme provided here: its background,
    /// border, color of text, corners, padding, and hovered, pressed, focus-visible, and disabled looks follow the theme, and a value
    /// set in <paramref name="style"/> wins over the theme's. The button is at least 96 wide, as Fluent's medium button, and holds
    /// the content in its middle; the content's grid column and row are written for that.
    /// </summary>
    /// <param name="content">The content: a node of the current tree with no parent.</param>
    /// <param name="onClick">What a click runs, in the scope current now.</param>
    /// <param name="appearance">The look among Fluent's buttons.</param>
    /// <param name="style">The values of the look the app sets; <see langword="null"/> for the theme's alone.</param>
    /// <returns>The button.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onClick"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="appearance"/> is not a value of its enum.</exception>
    /// <exception cref="ArgumentException">The content is no node, is of another tree, or has a parent.</exception>
    /// <exception cref="InvalidOperationException">No theme is provided here, which names the attribute and the option that provide
    /// one; or no window's root function is running, or there is no current scope.</exception>
    [UiThreadOnly]
    public static Element Button(Element content, Action onClick, ButtonAppearance appearance = ButtonAppearance.Standard, ButtonStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        if (appearance is not (ButtonAppearance.Standard or ButtonAppearance.Primary))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance, "The appearance is not a value of ButtonAppearance.");
        }

        // The theme is looked for before the button is made, so that a missing theme leaves no node without a parent in the tree.
        IReadOnlySignal<FluentTheme> theme = FluentTheme.UseForControl();
        Element button = Primitive.Button(Centered(content), onClick).MinWidth(ButtonLooks.MinWidth);
        ButtonLooks? applied = null;
        _ = Effect.Create(() =>
        {
            ButtonLooks looks = ButtonLooks.Of(theme.Get(), appearance);
            _ = button
                .Background(style?.Background ?? looks.Background)
                .BorderPaint(style?.Border ?? looks.Border)
                .Foreground(style?.Foreground ?? looks.Foreground)
                .CornerRadius(style?.Radius ?? looks.Radius)
                .Hovered(style?.Hovered ?? looks.Hovered)
                .Pressed(style?.Pressed ?? looks.Pressed)
                .FocusVisible(looks.FocusVisible)
                .Disabled(style?.Disabled ?? looks.Disabled);

            // The values of the layout are written only when they change, so that a change of the colors lays nothing out again.
            if (applied is null || applied.Padding != looks.Padding || applied.BorderWidth != looks.BorderWidth)
            {
                _ = button.Padding(looks.Padding.Horizontal, looks.Padding.Vertical);
                _ = button.Border(looks.BorderWidth, style?.Border ?? looks.Border);
            }

            applied = looks;
        });
        return button;
    }

    // Puts the content in the middle of the button, as Fluent's button centers it (design §13.2, "中身の位置"): a grid of three
    // columns and three rows, 1fr / auto / 1fr, with the content in the middle cell. The grid fills the button's content box: its
    // width as a block child, its height as a fraction, which resolves only when the button's height is definite (a button a row
    // stretches), and is the content's height otherwise. The public API has no alignment until row P02h, which replaces the grid.
    private static Element Centered(Element content)
    {
        Element grid = Elements.Grid(CenteringTracks, CenteringTracks, content).HeightFraction(1);
        _ = content.GridColumn(2).GridRow(2);
        return grid;
    }
}
