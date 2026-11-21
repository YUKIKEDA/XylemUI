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

    // Given to the resolving function as it is: with a read-only wrapper, Localization.Resolve compiled to 3,228 bytes against 1,684
    // and Counter grew 3,584 bytes (docs/benchmarks/lc2-cldr-size.md; Issue #906).
    private readonly Locale[] _supportedLocales;
    private readonly Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale> _resolve;
    private readonly Signal<LocaleData> _data;
    private readonly Signal<bool> _use24HourFormat;

    // The value of _data: the same resolved locale keeps it, so the signal does not change when tables of another locale are given
    // the resolved one (WithLocale makes a new object). The supported tables do not change, so the same locale chooses the same tables.
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
        _supportedLocales = new Locale[_supported.Length];
        for (int i = 0; i < _supportedLocales.Length; i++)
        {
            _supportedLocales[i] = _supported[i].Locale;
        }

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
    /// Refuses a list of supported tables that holds a <see langword="null"/>: the window options and the headless options check it
    /// where they check their other values, before the window is made.
    /// </summary>
    /// <param name="supported">The tables of the locales the app supports; <see langword="null"/> for none.</param>
    /// <param name="paramName">The name of the options' parameter, for the exception.</param>
    /// <exception cref="ArgumentException"><paramref name="supported"/> holds a <see langword="null"/>.</exception>
    public static void VerifySupported(IReadOnlyList<LocaleData>? supported, string paramName)
    {
        if (supported is null)
        {
            return;
        }

        for (int i = 0; i < supported.Count; i++)
        {
            if (supported[i] is null)
            {
                throw new ArgumentException("SupportedLocales holds no null element.", paramName);
            }
        }
    }

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
        if (_value is { } kept && kept.Locale == resolved)
        {
            return kept;
        }

        LocaleData chosen = TablesFor(resolved);
        _value = chosen.Locale == resolved ? chosen : chosen.WithLocale(resolved);
        return _value;
    }

    // Design §4.11, "When the replacing function returns a locale not declared": the supported tables of the locale; else the first
    // of its language whose likely script is its likely script, when that script can be inferred; else the root's.
    private LocaleData TablesFor(Locale resolved)
    {
        foreach (LocaleData tables in _supported)
        {
            if (tables.Locale == resolved)
            {
                return tables;
            }
        }

        // A script that cannot be inferred (0) is not the same script as another that cannot, as in Localization.Resolve.
        uint script = LikelyScripts.Infer(resolved);
        if (script != 0)
        {
            foreach (LocaleData tables in _supported)
            {
                if (tables.Locale.Language == resolved.Language && LikelyScripts.Infer(tables.Locale) == script)
                {
                    return tables;
                }
            }
        }

        return LocaleData.Root;
    }
}
