namespace XylemUI.Theme.Fluent;

/// <summary>
/// The look of a <see cref="Fluent.Button"/> among Fluent's buttons (design §13.2, "TH1 のボタンの見た目の種類"). Row B1 adds
/// the outline, subtle, and transparent buttons.
/// </summary>
public enum ButtonAppearance
{
    /// <summary>
    /// The neutral button, Fluent's <c>secondary</c>, the default: the neutral background with a border.
    /// </summary>
    Standard,

    /// <summary>
    /// The button of the main action, Fluent's <c>primary</c>: the brand background, which the accent color makes, with the text on
    /// it.
    /// </summary>
    Primary,
}
