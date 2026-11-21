namespace XylemUI;

/// <summary>
/// The OS's settings that a window's tree takes (design §6.0, "OS settings"): one object of the app tier, which every window core of
/// the UI thread reads. How text is drawn is not here: it is read for the monitor of each window
/// (<see cref="IPlatformWindow.TextRendering"/>).
/// </summary>
/// <remarks>
/// It belongs to the UI thread. The host reads the values when it makes the object and again when the OS tells it that a setting
/// changed; a reading that changed values raises <see cref="Changed"/> once, marked with the values that changed, and a reading that
/// changed none raises nothing.
/// </remarks>
internal interface IPlatformSettings
{
    /// <summary>
    /// Occurs on the UI thread once for each reading that changed values, with the values that changed. A host whose readings can
    /// come during a synchronous render raises it through <see cref="SynchronousRenderGuard.RunOrDefer"/>, so that it runs after the
    /// render (design §2.11.1).
    /// </summary>
    event Action<PlatformSettingsChanges>? Changed;

    /// <summary>
    /// Gets a value indicating whether the OS asks for less motion (design §2.14, "Reduced motion").
    /// </summary>
    bool ReducesMotion { get; }

    /// <summary>
    /// Gets the OS's settings a text field draws with: the caret's blink and width, the selection's colors, whether access keys
    /// are always underlined, and how far a press moves before a drag starts (design §4.9).
    /// </summary>
    TextEditingSettings TextEditing { get; }

    /// <summary>
    /// Gets the tags of the OS's preferred languages, most preferred first (design §4.11, "The OS's languages").
    /// </summary>
    IReadOnlyList<string> UiLanguages { get; }

    /// <summary>
    /// Gets a value indicating whether the OS asks for times in 24-hour form (design §4.11, "The key of the 24-hour form").
    /// </summary>
    bool Use24HourFormat { get; }
}
