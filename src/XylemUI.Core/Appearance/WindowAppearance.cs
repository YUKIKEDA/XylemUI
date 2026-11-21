namespace XylemUI;

/// <summary>
/// The OS's appearance as one window sees it (design §13.2, "OS の見た目"): kept in a signal that the window provides around its
/// root function under <see cref="SystemAppearance.Current"/>, as <see cref="WindowLocale"/> keeps the locale. The window core makes
/// one for each window (<see cref="WindowCore.Mount"/>), and the host tells the core when the OS's appearance changes.
/// </summary>
internal sealed class WindowAppearance
{
    private readonly Signal<SystemAppearance> _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowAppearance"/> class.
    /// </summary>
    /// <param name="value">The OS's appearance now.</param>
    /// <exception cref="ArgumentException">Only one of <see cref="SystemAppearance.HighContrast"/> and
    /// <see cref="SystemAppearance.Contrast"/> says high contrast is on.</exception>
    public WindowAppearance(SystemAppearance value) => _value = Signal.Create(Check(value, nameof(value)));

    /// <summary>
    /// Gets the OS's appearance.
    /// </summary>
    public IReadOnlySignal<SystemAppearance> Value => _value;

    /// <summary>
    /// Returns <paramref name="value"/> when its high contrast and its high-contrast colors agree: the colors are there exactly
    /// while high contrast is on.
    /// </summary>
    /// <param name="value">The OS's appearance.</param>
    /// <param name="parameterName">The name of the caller's parameter, for the exception.</param>
    /// <returns><paramref name="value"/>.</returns>
    /// <exception cref="ArgumentException">They disagree.</exception>
    public static SystemAppearance Check(SystemAppearance value, string parameterName)
    {
        if (value.HighContrast != value.Contrast.HasValue)
        {
            ThrowHelper.ThrowContrastColorsDisagree(parameterName);
        }

        return value;
    }

    /// <summary>
    /// Takes the OS's appearance again. The signal changes only when the value does, so that the several notices one change of
    /// Windows sends run the readers once (design §13.2, "同じ値はまとめる").
    /// </summary>
    /// <param name="value">The OS's appearance now.</param>
    /// <exception cref="ArgumentException">Only one of <see cref="SystemAppearance.HighContrast"/> and
    /// <see cref="SystemAppearance.Contrast"/> says high contrast is on.</exception>
    public void Update(SystemAppearance value) => _value.Set(Check(value, nameof(value)));

    /// <summary>
    /// Runs a window's root function with the OS's appearance provided around it.
    /// </summary>
    /// <param name="root">The root function.</param>
    /// <returns>The element the root function made.</returns>
    public Element Provide(Func<Element> root) => SystemAppearance.Current.Provide(_value, root);
}
