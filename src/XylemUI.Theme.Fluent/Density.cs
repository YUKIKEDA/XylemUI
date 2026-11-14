namespace XylemUI.Theme.Fluent;

/// <summary>
/// How much room a <see cref="FluentTheme"/> gives its parts (design §13.2, "密度").
/// </summary>
public enum Density
{
    /// <summary>
    /// Fluent's spacing and sizes; the default.
    /// </summary>
    Standard,

    /// <summary>
    /// Smaller spacing and parts. Row B1 sets its values; until then they are <see cref="Standard"/>'s.
    /// </summary>
    Compact,
}
