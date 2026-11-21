namespace XylemUI.Tests;

// The keys of the locale, its tables, and the 24-hour form (LC2, design §4.11, "Distribution" and "Replacing"): Localization.Provide
// provides the locale key and the tables key from one signal, a part wrapped in the locale key alone keeps the tables from outside,
// and the keys have no default. A window provides them at its root (WindowCoreTests).
[Collection(UiThreadGroup.Name)]
public sealed class LocalizationKeysTests(UiThreadFixture ui)
{
    /// <summary>
    /// Inside <see cref="Localization.Provide"/> the tables key is the signal given, and the locale key is the locale of its tables,
    /// also after the signal changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal of the ja tables.
    ///
    /// Steps:
    /// - In a root scope, provide the signal around a body that keeps both keys' signals. Read them; set the signal to the de
    ///   tables and read them again.
    ///
    /// Expected:
    /// - The tables key is the signal itself; the locale key reads ja, then de, the locale of the tables each time.
    /// </remarks>
    [Fact]
    public void Provide_BothKeys_HoldTheSameLocale() =>
        ui.Invoke(() =>
        {
            Signal<LocaleData> choice = Signal.Create(CldrLocales.Ja);
            IReadOnlySignal<LocaleData>? data = null;
            IReadOnlySignal<Locale>? locale = null;
            ScopeHandle root = Scope.Create(() =>
                _ = Localization.Provide(
                    choice,
                    () =>
                    {
                        data = Localization.Data.Use();
                        locale = Localization.Locale.Use();
                        return default;
                    }
                )
            );
            IReadOnlySignal<Locale> localeKey = locale!;
            string first = localeKey.Get().ToLanguageTag();

            choice.Set(CldrLocales.De);

            Assert.Same(choice, data);
            Assert.Equal("ja", first);
            Assert.Equal("de", localeKey.Get().ToLanguageTag());
            Assert.Equal(data!.Get().Locale, localeKey.Get());
            Scope.Dispose(root);
        });

    /// <summary>
    /// A change of the provided tables runs again the code that reads the tables, and the code that reads the locale only when
    /// the locale is another; code that reads neither does not run again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal of the ja tables provided with <see cref="Localization.Provide"/> around three effects: one counts its reads of
    ///   the tables key, one of the locale key, and one reads a signal of its own.
    ///
    /// Steps:
    /// - Set the signal to the ja tables under the tag ja (another object of the same locale), then to the en tables, then to the
    ///   en tables again.
    ///
    /// Expected:
    /// - The tables effect ran 3 times, the locale effect 2 times, the other effect once.
    /// </remarks>
    [Fact]
    public void Provide_ChangedTables_RunAgainOnlyTheirReaders() =>
        ui.Invoke(() =>
        {
            Signal<LocaleData> choice = Signal.Create(CldrLocales.Ja);
            Signal<int> other = Signal.Create(0);
            int tableRuns = 0;
            int localeRuns = 0;
            int otherRuns = 0;
            ScopeHandle root = Scope.Create(() =>
                _ = Localization.Provide(
                    choice,
                    () =>
                    {
                        IReadOnlySignal<LocaleData> data = Localization.Data.Use();
                        IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                        _ = Effect.Create(() =>
                        {
                            _ = data.Get();
                            tableRuns++;
                        });
                        _ = Effect.Create(() =>
                        {
                            _ = locale.Get();
                            localeRuns++;
                        });
                        _ = Effect.Create(() =>
                        {
                            _ = other.Get();
                            otherRuns++;
                        });
                        return default;
                    }
                )
            );

            choice.Set(CldrLocales.Ja.WithLocale(Locale.Parse("ja")));
            choice.Set(CldrLocales.En);
            choice.Set(CldrLocales.En);

            Assert.Equal((3, 2, 1), (tableRuns, localeRuns, otherRuns));
            Scope.Dispose(root);
        });

    /// <summary>
    /// Providing the same signal of tables twice gives the same locale signal, so the text of the default language keeps one entry
    /// for it; another signal of tables gives another locale signal.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two signals of the ja tables.
    ///
    /// Steps:
    /// - In a root scope, provide the first signal twice and the second once, each around a body that keeps the locale key's
    ///   signal.
    ///
    /// Expected:
    /// - The two locale signals of the first are the same object; the one of the second is another.
    /// </remarks>
    [Fact]
    public void Provide_TheSameSignalTwice_GivesOneLocaleSignal() =>
        ui.Invoke(() =>
        {
            Signal<LocaleData> first = Signal.Create(CldrLocales.Ja);
            Signal<LocaleData> second = Signal.Create(CldrLocales.Ja);
            var seen = new List<IReadOnlySignal<Locale>>();
            ScopeHandle root = Scope.Create(() =>
            {
                _ = Localization.Provide(first, Keep);
                _ = Localization.Provide(first, Keep);
                _ = Localization.Provide(second, Keep);
            });

            Assert.Same(seen[0], seen[1]);
            Assert.NotSame(seen[0], seen[2]);
            Scope.Dispose(root);

            Element Keep()
            {
                seen.Add(Localization.Locale.Use());
                return default;
            }
        });

    /// <summary>
    /// A part wrapped in the locale key alone has that locale and keeps the tables from outside it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal of the ja tables provided with <see cref="Localization.Provide"/>.
    ///
    /// Steps:
    /// - Inside it, provide a signal of the locale fr under <see cref="Localization.Locale"/> alone, around a body that reads both
    ///   keys.
    ///
    /// Expected:
    /// - The locale is fr, and the tables key is the outer signal, whose tables are ja's.
    /// </remarks>
    [Fact]
    public void LocaleProvide_Alone_KeepsTheTablesFromOutside() =>
        ui.Invoke(() =>
        {
            Signal<LocaleData> choice = Signal.Create(CldrLocales.Ja);
            Signal<Locale> french = Signal.Create(Locale.Parse("fr"));
            IReadOnlySignal<LocaleData>? data = null;
            string? locale = null;
            ScopeHandle root = Scope.Create(() =>
                _ = Localization.Provide(
                    choice,
                    () =>
                        Localization.Locale.Provide(
                            french,
                            () =>
                            {
                                data = Localization.Data.Use();
                                locale = Localization.Locale.Use().Get().ToLanguageTag();
                                return default;
                            }
                        )
                )
            );

            Assert.Equal("fr", locale);
            Assert.Same(choice, data);
            Assert.Same(CldrLocales.Ja, data!.Get());
            Scope.Dispose(root);
        });

    /// <summary>
    /// The tables key and the 24-hour key have no default: outside a window, where nothing provides them, a lookup throws, as one
    /// of the locale key does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root scope with nothing provided.
    ///
    /// Steps:
    /// - Look up each of the three keys with <c>Use</c> and with <c>TryUse</c>.
    ///
    /// Expected:
    /// - Each <c>Use</c> throws <see cref="InvalidOperationException"/>, and each <c>TryUse</c> returns <see langword="false"/>.
    /// </remarks>
    [Fact]
    public void Use_OutsideAWindow_Throws() =>
        ui.Invoke(() =>
        {
            Exception? data = null;
            Exception? use24HourFormat = null;
            Exception? locale = null;
            bool found = true;
            ScopeHandle root = Scope.Create(() =>
            {
                data = Record.Exception(() => Localization.Data.Use());
                use24HourFormat = Record.Exception(() => Localization.Use24HourFormat.Use());
                locale = Record.Exception(() => Localization.Locale.Use());
                found = Localization.Data.TryUse(out _);
                found |= Localization.Use24HourFormat.TryUse(out _);
                found |= Localization.Locale.TryUse(out _);
            });

            Assert.IsType<InvalidOperationException>(data);
            Assert.IsType<InvalidOperationException>(use24HourFormat);
            Assert.IsType<InvalidOperationException>(locale);
            Assert.False(found);
            Scope.Dispose(root);
        });

    /// <summary>
    /// <see cref="Localization.Provide"/> takes neither a null signal nor a null body.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal of the ja tables.
    ///
    /// Steps:
    /// - In a root scope, call Provide with a null signal, then with a null body.
    ///
    /// Expected:
    /// - Both throw <see cref="ArgumentNullException"/>.
    /// </remarks>
    [Fact]
    public void Provide_NullArguments_Throw() =>
        ui.Invoke(() =>
        {
            Signal<LocaleData> choice = Signal.Create(CldrLocales.Ja);
            Exception? noData = null;
            Exception? noContent = null;
            ScopeHandle root = Scope.Create(() =>
            {
                noData = Record.Exception(() => Localization.Provide(null!, static () => default));
                noContent = Record.Exception(() => Localization.Provide(choice, null!));
            });

            Assert.IsType<ArgumentNullException>(noData);
            Assert.IsType<ArgumentNullException>(noContent);
            Scope.Dispose(root);
        });

    /// <summary>
    /// <see cref="Localization.Provide"/> throws <see cref="InvalidOperationException"/> off the UI thread and while a computed
    /// value computes, as its documentation says: for a signal of tables provided before and for one never provided.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - On the UI thread: two signals of the ja tables; the second was provided once in a root scope, so its locale signal is kept.
    ///
    /// Steps:
    /// - On the UI thread, provide the second signal from inside a computed value's computation. From a thread of the pool,
    ///   provide each signal.
    ///
    /// Expected:
    /// - The three calls throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Provide_OffTheUiThreadOrInAComputation_Throws()
    {
        Signal<LocaleData>? fresh = null;
        Signal<LocaleData>? provided = null;
        Exception? inComputation = null;
        ui.Invoke(() =>
        {
            fresh = Signal.Create(CldrLocales.Ja);
            provided = Signal.Create(CldrLocales.Ja);
            Signal<LocaleData> kept = provided;
            ScopeHandle root = Scope.Create(() => _ = Localization.Provide(kept, static () => default));
            Scope.Dispose(root);
            Computed<int> computed = Computed.Create(() =>
            {
                inComputation = Record.Exception(() => Localization.Provide(kept, static () => default));
                return 0;
            });
            _ = computed.Get();
        });

        // The test calls Provide off the UI thread on purpose, which is what XYLEM100 reports.
#pragma warning disable XYLEM100
        Exception? freshOffThread = null;
        Exception? providedOffThread = null;
        var worker = new Thread(() =>
        {
            freshOffThread = Record.Exception(() => Localization.Provide(fresh!, static () => default));
            providedOffThread = Record.Exception(() => Localization.Provide(provided!, static () => default));
        });
        worker.Start();
        worker.Join();
#pragma warning restore XYLEM100

        Assert.IsType<InvalidOperationException>(inComputation);
        Assert.IsType<InvalidOperationException>(freshOffThread);
        Assert.IsType<InvalidOperationException>(providedOffThread);
    }
}
