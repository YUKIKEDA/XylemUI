using System.Collections.ObjectModel;

namespace XylemUI;

/// <summary>
/// The locale of one window (design §4.11): the locales the app supports resolved against the languages the OS gives, the tables
/// chosen for the result, and whether the OS asks for times in 24-hour form, kept in signals that the window provides around its
/// root function. The window core makes one for each window (<see cref="WindowCore.Mount"/>), and the host tells the core when the
/// OS's languages or its time format change.
/// </summary>
internal sealed class WindowLocale
{
    private readonly LocaleData[] _supported;
    private readonly ReadOnlyCollection<Locale> _supportedLocales;
    private readonly Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale> _resolve;
    private readonly Signal<LocaleData> _data;
    private readonly Signal<bool> _use24HourFormat;

    // The tables the value of _data was chosen from, and that value: the same choice for the same locale keeps the value, so the
    // signal does not change when tables of another locale are given the resolved one (WithLocale makes a new object).
    private LocaleData? _chosen;
    private LocaleData? _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowLocale"/> class, resolved against the OS's languages.
    /// </summary>
    /// <param name="supported">The tables of the locales the app supports; <see langword="null"/> for none.</param>
    /// <param name="resolve">What resolves them; <see langword="null"/> for <see cref="Localization.Resolve"/>.</param>
    /// <param name="systemTags">The tags of the OS's languages, most preferred first.</param>
    /// <param name="use24HourFormat">Whether the OS asks for times in 24-hour form.</param>
    public WindowLocale(
        IReadOnlyList<LocaleData>? supported,
        Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? resolve,
        IReadOnlyList<string> systemTags,
        bool use24HourFormat
    )
    {
        _supported = supported is null ? [] : [.. supported];
        var locales = new Locale[_supported.Length];
        for (int i = 0; i < locales.Length; i++)
        {
            locales[i] = _supported[i].Locale;
        }

        _supportedLocales = Array.AsReadOnly(locales);
        _resolve = resolve ?? Localization.Resolve;
        _data = Signal.Create(Choose(Resolve(systemTags)));
        _use24HourFormat = Signal.Create(use24HourFormat);
    }

    /// <summary>
    /// Gets the tables of the resolved locale; their <see cref="LocaleData.Locale"/> is the resolved locale.
    /// </summary>
    public IReadOnlySignal<LocaleData> Data => _data;

    /// <summary>
    /// Gets whether the OS asks for times in 24-hour form.
    /// </summary>
    public IReadOnlySignal<bool> Use24HourFormat => _use24HourFormat;

    /// <summary>
    /// Resolves again against other languages of the OS. The signal changes only when the resolved locale or its tables do.
    /// </summary>
    /// <param name="systemTags">The tags of the OS's languages, most preferred first.</param>
    public void Update(IReadOnlyList<string> systemTags) => _data.Set(Choose(Resolve(systemTags)));

    /// <summary>
    /// Sets whether the OS asks for times in 24-hour form. The signal changes only when the value does.
    /// </summary>
    /// <param name="use24HourFormat">Whether times are in 24-hour form.</param>
    public void SetUse24HourFormat(bool use24HourFormat) => _use24HourFormat.Set(use24HourFormat);

    /// <summary>
    /// Runs a window's root function with the tables, their locale, and the 24-hour form provided around it
    /// (<see cref="Localization.Provide"/>), and keeps the root scope for the tree: the effects that shape the text of the default
    /// language again live there.
    /// </summary>
    /// <param name="tree">The window's tree.</param>
    /// <param name="root">The root function.</param>
    /// <returns>The element the root function made.</returns>
    public Element Provide(UiTree tree, Func<Element> root)
    {
        tree.LocaleScope = Scope.Current;
        return Localization.Use24HourFormat.Provide(_use24HourFormat, () => Localization.Provide(_data, root));
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

        return _resolve(preferred, _supportedLocales);
    }

    // The tables of the resolved locale under its tag, so that the locale key and the tables' locale agree (design §4.11, "The key
    // of the tables"): tables of another locale, the root's included, are given the resolved one with WithLocale.
    private LocaleData Choose(Locale resolved)
    {
        LocaleData chosen = TablesFor(resolved);
        if (ReferenceEquals(chosen, _chosen) && _value is { } kept && kept.Locale == resolved)
        {
            return kept;
        }

        _chosen = chosen;
        _value = chosen.Locale == resolved ? chosen : chosen.WithLocale(resolved);
        return _value;
    }

    // Design §4.11, "When the replacing function returns a locale not declared": the supported tables of the locale; else the first
    // of its language whose likely script is its likely script; else the root's.
    private LocaleData TablesFor(Locale resolved)
    {
        foreach (LocaleData tables in _supported)
        {
            if (tables.Locale == resolved)
            {
                return tables;
            }
        }

        uint script = LikelyScripts.Infer(resolved);
        foreach (LocaleData tables in _supported)
        {
            if (tables.Locale.Language == resolved.Language && LikelyScripts.Infer(tables.Locale) == script)
            {
                return tables;
            }
        }

        return LocaleData.Root;
    }
}
