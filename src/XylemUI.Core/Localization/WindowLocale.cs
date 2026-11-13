namespace XylemUI;

/// <summary>
/// The locale of one window (design §4.11): the locales the app supports resolved against the languages the OS gives, kept in a
/// signal that the window provides around its root function. A host makes one for each window and tells it when the OS's
/// languages change.
/// </summary>
internal sealed class WindowLocale
{
    private readonly Signal<Locale> _resolved;
    private readonly IReadOnlyList<Locale> _supported;
    private readonly Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale> _resolve;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowLocale"/> class, resolved against the OS's languages.
    /// </summary>
    /// <param name="supported">The locales the app supports; <see langword="null"/> for none.</param>
    /// <param name="resolve">What resolves them; <see langword="null"/> for <see cref="Localization.Resolve"/>.</param>
    /// <param name="systemTags">The tags of the OS's languages, most preferred first.</param>
    public WindowLocale(
        IReadOnlyList<Locale>? supported,
        Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? resolve,
        IReadOnlyList<string> systemTags
    )
    {
        _supported = supported ?? [];
        _resolve = resolve ?? Localization.Resolve;
        _resolved = Signal.Create(Resolve(systemTags));
    }

    /// <summary>
    /// Gets the resolved locale.
    /// </summary>
    public IReadOnlySignal<Locale> Resolved => _resolved;

    /// <summary>
    /// Resolves again against other languages of the OS. The signal changes only when the resolved locale does.
    /// </summary>
    /// <param name="systemTags">The tags of the OS's languages, most preferred first.</param>
    public void Update(IReadOnlyList<string> systemTags) => _resolved.Set(Resolve(systemTags));

    /// <summary>
    /// Runs a window's root function with the resolved locale provided around it, and keeps the root scope for the tree: the
    /// effects that shape the text of the default language again live there.
    /// </summary>
    /// <param name="tree">The window's tree.</param>
    /// <param name="root">The root function.</param>
    /// <returns>The element the root function made.</returns>
    public Element Provide(UiTree tree, Func<Element> root)
    {
        tree.LocaleScope = Scope.Current;
        return Localization.Locale.Provide(_resolved, root);
    }

    // A tag the OS gives that cannot be read is dropped from the list (design §4.11, "Malformed tags").
    private Locale Resolve(IReadOnlyList<string> systemTags)
    {
        var preferred = new List<Locale>(systemTags.Count);
        foreach (string tag in systemTags)
        {
            if (Locale.TryParse(tag, out Locale locale))
            {
                preferred.Add(locale);
            }
        }

        return _resolve(preferred, _supported);
    }
}
