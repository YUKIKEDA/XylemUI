namespace XylemUI.Testing;

/// <summary>
/// The OS's settings of a headless app (design §6.0, "Headless"): made from <see cref="HeadlessOptions"/>, so that a test does not
/// depend on the machine it runs on, and changed by the app (<see cref="HeadlessApp.SetSystemLocales"/>,
/// <see cref="HeadlessApp.SetUse24HourFormat"/>) as an OS's change is read. A change that makes a value differ raises
/// <see cref="Changed"/> once, marked with that value; one that does not raises nothing.
/// </summary>
/// <remarks>
/// A headless app runs no synchronous render, so the notice is raised at once, where the Windows host raises it through
/// <see cref="SynchronousRenderGuard.RunOrDefer"/>.
/// </remarks>
/// <param name="options">The options whose settings the app has at the start.</param>
internal sealed class HeadlessSettings(HeadlessOptions options) : IPlatformSettings
{
    private string[] _uiLanguages = [.. options.SystemLocales];

    /// <inheritdoc/>
    public event Action<PlatformSettingsChanges>? Changed;

    /// <inheritdoc/>
    public bool ReducesMotion { get; } = options.ReduceMotion;

    /// <inheritdoc/>
    /// <remarks>
    /// The defaults, with no blink unless <see cref="HeadlessOptions.CaretBlink"/>, and the keyboard cues of
    /// <see cref="HeadlessOptions.KeyboardCues"/>.
    /// </remarks>
    public TextEditingSettings TextEditing { get; } =
        (options.CaretBlink ? TextEditingSettings.Default : TextEditingSettings.Default with { CaretBlink = null }) with
        {
            KeyboardCues = options.KeyboardCues,
        };

    /// <inheritdoc/>
    public IReadOnlyList<string> UiLanguages => _uiLanguages;

    /// <inheritdoc/>
    public bool Use24HourFormat { get; private set; } = options.Use24HourFormat;

    /// <summary>
    /// Sets the OS's languages; tells the change when the list differs from the one before, in its tags or their order.
    /// </summary>
    /// <param name="tags">The tags of the languages, most preferred first; copied.</param>
    public void SetUiLanguages(IReadOnlyList<string> tags)
    {
        string[] next = [.. tags];
        if (!next.AsSpan().SequenceEqual(_uiLanguages))
        {
            _uiLanguages = next;
            Changed?.Invoke(PlatformSettingsChanges.UiLanguages);
        }
    }

    /// <summary>
    /// Sets whether the OS asks for times in 24-hour form; tells the change when the value differs from the one before.
    /// </summary>
    /// <param name="use24HourFormat">Whether times are in 24-hour form.</param>
    public void SetUse24HourFormat(bool use24HourFormat)
    {
        if (use24HourFormat != Use24HourFormat)
        {
            Use24HourFormat = use24HourFormat;
            Changed?.Invoke(PlatformSettingsChanges.Use24HourFormat);
        }
    }
}
