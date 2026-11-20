using XylemUI.Primitives;

namespace XylemUI.Theme.Fluent;

/// <summary>
/// Makes the parts of the Fluent theme (design §13.2, "部品がテーマに付いていく形"): each makes a primitive of
/// <c>XylemUI.Primitives</c> and gives it the theme's look with one effect, which writes the look again when the theme changes.
/// Call them with the class's name, <c>Fluent.Button(...)</c>: with <c>using static</c>, C# picks the primitive of the same name.
/// </summary>
public static class Fluent
{
    /// <summary>
    /// Makes a button (<see cref="Primitive.Button(Element, Action)"/>) with the look of the theme provided here: its background,
    /// border, color of text, corners, padding, and hovered, pressed, and disabled looks follow the theme, and a value set in
    /// <paramref name="style"/> wins over the theme's.
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
        Element button = Primitive.Button(content, onClick);
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
}
