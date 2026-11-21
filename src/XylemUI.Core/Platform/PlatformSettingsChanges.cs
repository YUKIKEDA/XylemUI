namespace XylemUI;

/// <summary>
/// The values of the OS's settings that one reading changed (<see cref="IPlatformSettings.Changed"/>).
/// </summary>
[Flags]
internal enum PlatformSettingsChanges : byte
{
    /// <summary>
    /// No value changed.
    /// </summary>
    None = 0,

    /// <summary>
    /// <see cref="IPlatformSettings.ReducesMotion"/> changed.
    /// </summary>
    ReducesMotion = 1,

    /// <summary>
    /// <see cref="IPlatformSettings.TextEditing"/> changed.
    /// </summary>
    TextEditing = 2,

    /// <summary>
    /// <see cref="IPlatformSettings.UiLanguages"/> changed.
    /// </summary>
    UiLanguages = 4,

    /// <summary>
    /// <see cref="IPlatformSettings.Use24HourFormat"/> changed.
    /// </summary>
    Use24HourFormat = 8,
}
