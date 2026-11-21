using XylemUI.Testing;
using XylemUI.Testing.XUnit;
using XylemUI.Text;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The locale of a window (row LC1, design §4.11): the supported locales resolved against the OS's languages, provided around the
// root function, resolved again when the OS's languages change, and the language of the text that sets none; from row LC2, the
// tables of the declared locales, Localization.Provide, and the 24-hour form. The headless app takes the OS's languages and its
// 24-hour form from its options; a desktop window takes them from Windows through the same WindowLocale (OpenWindowLocaleTests).
public sealed class WindowLocaleTests
{
    /// <summary>
    /// The root function finds the resolved locale under <see cref="Localization.Locale"/>, and a reader of it runs again when
    /// the OS's languages change the resolved locale, and only then.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app that supports en and ja, with the OS's languages "ja-JP, en-US". An effect in the root function records the
    ///   locale each time it runs.
    ///
    /// Steps:
    /// - Change the OS's languages to "en-GB", then to "en-AU", then to "ko-KR, ja-JP".
    ///
    /// Expected:
    /// - The effect recorded ja, en, ja: the change to "en-AU" resolves to en again and does not run it.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Locale_IsResolved_AndFollowsTheSystemLanguages()
    {
        var seen = new List<string>();
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                _ = Effect.Create(() => seen.Add(locale.Get().ToLanguageTag()));
                return Box();
            },
            new HeadlessOptions { SupportedLocales = [CldrLocales.En, CldrLocales.Ja], SystemLocales = ["ja-JP", "en-US"] }
        );
        _ = app.PumpAndSettle();

        app.SetSystemLocales(["en-GB"]);
        app.SetSystemLocales(["en-AU"]);
        app.SetSystemLocales(["ko-KR", "ja-JP"]);
        _ = app.PumpAndSettle();

        Assert.Equal(["ja", "en", "ja"], seen);
    }

    /// <summary>
    /// A tag of the OS that cannot be read is dropped from the list; with no supported locale, the locale is the first language
    /// that can be read, or undetermined when none can.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app that supports no locale, with the OS's languages "ja_JP, x-private, fr-FR"; the root function keeps the locale's
    ///   signal.
    ///
    /// Steps:
    /// - Read the locale; change the OS's languages to "???" and read it again.
    ///
    /// Expected:
    /// - "fr-FR", then "und".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Locale_UnreadableSystemTags_AreDropped()
    {
        IReadOnlySignal<Locale>? locale = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                locale = Localization.Locale.Use();
                return Box();
            },
            new HeadlessOptions { SystemLocales = ["ja_JP", "x-private", "fr-FR"] }
        );
        _ = app.PumpAndSettle();
        string first = locale!.Get().ToLanguageTag();

        app.SetSystemLocales(["???"]);
        _ = app.PumpAndSettle();

        Assert.Equal("fr-FR", first);
        Assert.Equal("und", locale.Get().ToLanguageTag());
    }

    /// <summary>
    /// With the defaults of the options, the OS gives no language: the locale is the first supported one, or undetermined when
    /// the app supports none, so that a test does not depend on its machine.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One app with default options, and one that supports de and en.
    ///
    /// Steps:
    /// - Read each app's locale in its root function.
    ///
    /// Expected:
    /// - "und" and "de".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Locale_DefaultOptions_DoNotReadTheMachine()
    {
        string? plain = null;
        string? declared = null;
        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(() =>
            {
                plain = Localization.Locale.Use().Get().ToLanguageTag();
                return Box();
            })
        )
        {
            _ = app.PumpAndSettle();
        }

        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(
                () =>
                {
                    declared = Localization.Locale.Use().Get().ToLanguageTag();
                    return Box();
                },
                new HeadlessOptions { SupportedLocales = [CldrLocales.De, CldrLocales.En] }
            )
        )
        {
            _ = app.PumpAndSettle();
        }

        Assert.Equal("und", plain);
        Assert.Equal("de", declared);
    }

    /// <summary>
    /// The app's own function chooses the locale in place of the resolution, and is given the languages that could be read and
    /// the supported locales.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app that supports en and ja, with the OS's languages "ja-JP", and a function that records what it is given and
    ///   returns "fr-CA".
    ///
    /// Steps:
    /// - Read the locale in the root function.
    ///
    /// Expected:
    /// - "fr-CA"; the function was given "ja-JP" and "en, ja".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Locale_TheAppsFunction_ReplacesTheResolution()
    {
        string? locale = null;
        string? given = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                locale = Localization.Locale.Use().Get().ToLanguageTag();
                return Box();
            },
            new HeadlessOptions
            {
                SupportedLocales = [CldrLocales.En, CldrLocales.Ja],
                SystemLocales = ["ja-JP"],
                ResolveLocale = (preferred, supported) =>
                {
                    given = $"{string.Join(",", preferred)} | {string.Join(",", supported)}";
                    return Locale.Parse("fr-CA");
                },
            }
        );
        _ = app.PumpAndSettle();

        Assert.Equal("fr-CA", locale);
        Assert.Equal("ja-JP | en,ja", given);
    }

    /// <summary>
    /// Text that sets no language has the language of the nearest locale, and is given the new one when that locale changes;
    /// text that set a language keeps it, and the function that made the texts does not run again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app that supports ja and en, with the OS's language "ja-JP". A list of one row, whose function is counted, holds a
    ///   text with no language. Beside it: a text given the language "ko", and a text inside a part wrapped in a signal of
    ///   another locale, "ar". The part's text is made first, so that the last text made before a later row has the window's
    ///   locale.
    ///
    /// Steps:
    /// - Read the three languages. Change the OS's language to "en-US" and read them; add a second row, whose text is made after
    ///   the change, and read that text's language in place of the first row's; set the part's signal to "he" and read them.
    ///
    /// Expected:
    /// - ja, ko, ar; then en, ko, ar; the same with the new row's text; then en, ko, he. The row's function ran once for each
    ///   row: the change of the locale did not make the first row again.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task DefaultTextLanguage_FollowsTheNearestLocale()
    {
        Signal<Locale> part = Signal.Create(Locale.Parse("ar"));
        Signal<IReadOnlyList<int>> rows = Signal.Create<IReadOnlyList<int>>([1]);
        int made = 0;
        Element plain = default;
        Element fixedLanguage = default;
        Element inPart = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Column(
                    Localization.Locale.Provide(part, () => inPart = Elements.Text("c")),
                    Column()
                        .For(
                            rows,
                            static row => row,
                            _ =>
                            {
                                made++;
                                return plain = Elements.Text("a");
                            }
                        ),
                    fixedLanguage = Elements.Text("b").Language(TextLanguage.FromTag("ko"))
                ),
            new HeadlessOptions { SupportedLocales = [CldrLocales.Ja, CldrLocales.En], SystemLocales = ["ja-JP"] }
        );
        _ = app.PumpAndSettle();
        var seen = new List<string> { Languages() };

        app.SetSystemLocales(["en-US"]);
        _ = app.PumpAndSettle();
        seen.Add(Languages());
        rows.Set([1, 2]);
        _ = app.PumpAndSettle();
        seen.Add(Languages());
        part.Set(Locale.Parse("he"));
        _ = app.PumpAndSettle();
        seen.Add(Languages());

        Assert.Equal(["ja ko ar", "en ko ar", "en ko ar", "en ko he"], seen);
        Assert.Equal(2, made);

        string Languages() => $"{Language(plain)} {Language(fixedLanguage)} {Language(inPart)}";
        string Language(Element text)
        {
            TextLanguage language = app.Tree.Nodes.GetRef(text.Node).Text!.Style.Language;
            foreach (string tag in (string[])["ja", "en", "ko", "ar", "he", "und"])
            {
                if (language == TextLanguage.FromTag(tag))
                {
                    return tag;
                }
            }

            return "?";
        }
    }

    /// <summary>
    /// An app that declares no locale formats with the root's tables (design §4.11, "When the app declares nothing"): with the
    /// default options they are <see cref="LocaleData.Root"/> itself, and with an OS language they are the root's under that
    /// language's tag, the locale of the window.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One app with default options, and one whose OS's language is "ja-JP"; neither declares a locale.
    ///
    /// Steps:
    /// - Read <see cref="Localization.Data"/> and <see cref="Localization.Locale"/> in each app's root function.
    ///
    /// Expected:
    /// - The first app's tables are <see cref="LocaleData.Root"/>. The second app's tables have the root's plural rules and the
    ///   locale ja-JP, which is the locale key's too.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Data_NothingDeclared_IsTheRootTables()
    {
        LocaleData? plain = null;
        LocaleData? tagged = null;
        string? locale = null;
        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(() =>
            {
                plain = Localization.Data.Use().Get();
                return Box();
            })
        )
        {
            _ = app.PumpAndSettle();
        }

        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(
                () =>
                {
                    tagged = Localization.Data.Use().Get();
                    locale = Localization.Locale.Use().Get().ToLanguageTag();
                    return Box();
                },
                new HeadlessOptions { SystemLocales = ["ja-JP"] }
            )
        )
        {
            _ = app.PumpAndSettle();
        }

        Assert.Same(LocaleData.Root, plain);
        Assert.Same(LocaleData.Root.Plurals, tagged!.Plurals);
        Assert.Equal("ja-JP", tagged.Locale.ToLanguageTag());
        Assert.Equal("ja-JP", locale);
    }

    /// <summary>
    /// Tables declared under another tag (<see cref="LocaleData.WithLocale"/>) are resolved and shape the text by that tag, and
    /// format with the tables they were made from, the week included (design §4.11, "Locales that CLDR does not have").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app that declares the en-GB tables and the en tables under the tag en-RU, in that order, with the OS's language
    ///   "en-RU". The root function holds a text with no language and reads the two keys.
    ///
    /// Steps:
    /// - Read the keys and the text's language.
    ///
    /// Expected:
    /// - The locale is en-RU (by the tags en-GB and en, en-GB would be taken first); the tables have the plural rules of
    ///   <see cref="CldrLocales.En"/> and its week, which starts on Sunday; the text's language is en-RU.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task WithLocale_TheTagResolvesAndShapesTheText_AndTheTablesFormat()
    {
        LocaleData? tables = null;
        string? locale = null;
        Element text = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                tables = Localization.Data.Use().Get();
                locale = Localization.Locale.Use().Get().ToLanguageTag();
                return text = Elements.Text("a");
            },
            new HeadlessOptions { SupportedLocales = [CldrLocales.EnGB, CldrLocales.En.WithLocale(Locale.Parse("en-RU"))], SystemLocales = ["en-RU"] }
        );
        _ = app.PumpAndSettle();

        Assert.Equal("en-RU", locale);
        Assert.Same(CldrLocales.En.Plurals, tables!.Plurals);
        Assert.Equal(DayOfWeek.Sunday, tables.FirstDayOfWeek);
        Assert.Equal(TextLanguage.FromTag("en-RU"), app.Tree.Nodes.GetRef(text.Node).Text!.Style.Language);
    }

    /// <summary>
    /// An app that lets its user choose the language wraps its root function in <see cref="Localization.Provide"/> with a signal of
    /// its own: setting the signal gives the part another locale for its text and its tables, and the rest of the window keeps the
    /// window's (design §4.11, "Replacing").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app that declares en and ja, with the OS's language "ja-JP". The root function is a column of a text outside the part
    ///   and the part: the app's signal of the ja tables, provided around a text and a read of <see cref="Localization.Data"/>.
    ///
    /// Steps:
    /// - Read the two texts' languages and the part's tables; set the signal to the en tables and read them again.
    ///
    /// Expected:
    /// - First ja, ja, and <see cref="CldrLocales.Ja"/>; then ja, en, and <see cref="CldrLocales.En"/>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Provide_TheAppsOwnSignal_SwitchesTheTextAndTheTablesOfThePart()
    {
        Signal<LocaleData> choice = Signal.Create(CldrLocales.Ja);
        IReadOnlySignal<LocaleData>? data = null;
        Element outside = default;
        Element inside = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Column(
                    outside = Elements.Text("a"),
                    Localization.Provide(
                        choice,
                        () =>
                        {
                            data = Localization.Data.Use();
                            return inside = Elements.Text("b");
                        }
                    )
                ),
            new HeadlessOptions { SupportedLocales = [CldrLocales.En, CldrLocales.Ja], SystemLocales = ["ja-JP"] }
        );
        _ = app.PumpAndSettle();
        IReadOnlySignal<LocaleData> part = data!;
        (TextLanguage, TextLanguage, LocaleData) before = (Language(outside), Language(inside), part.Get());

        choice.Set(CldrLocales.En);
        _ = app.PumpAndSettle();

        TextLanguage ja = TextLanguage.FromTag("ja");
        Assert.Equal((ja, ja, CldrLocales.Ja), before);
        Assert.Equal((ja, TextLanguage.FromTag("en"), CldrLocales.En), (Language(outside), Language(inside), part.Get()));

        TextLanguage Language(Element text) => app.Tree.Nodes.GetRef(text.Node).Text!.Style.Language;
    }

    /// <summary>
    /// <see cref="HeadlessOptions.Use24HourFormat"/> is the first value of <see cref="Localization.Use24HourFormat"/>, off by
    /// default, and <see cref="HeadlessApp.SetUse24HourFormat"/> changes it: its readers run again only when the value changes, and
    /// a reader of the locale does not run.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One app with default options whose root function reads the 24-hour form. One app with the option on, whose root function
    ///   has an effect that records the 24-hour form and an effect that counts its reads of the locale.
    ///
    /// Steps:
    /// - In the second app, set the 24-hour form on, off, off, and on.
    ///
    /// Expected:
    /// - The first app read off. The second app's first effect recorded on, off, on; the second ran once.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Use24HourFormat_TheOptionIsTheFirstValue_AndSetChangesIt()
    {
        bool? plain = null;
        var seen = new List<bool>();
        int localeReads = 0;
        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(() =>
            {
                plain = Localization.Use24HourFormat.Use().Get();
                return Box();
            })
        )
        {
            _ = app.PumpAndSettle();
        }

        await using (
            HeadlessApp app = await HeadlessApp.StartAsync(
                () =>
                {
                    IReadOnlySignal<bool> use24HourFormat = Localization.Use24HourFormat.Use();
                    IReadOnlySignal<Locale> locale = Localization.Locale.Use();
                    _ = Effect.Create(() => seen.Add(use24HourFormat.Get()));
                    _ = Effect.Create(() =>
                    {
                        _ = locale.Get();
                        localeReads++;
                    });
                    return Box();
                },
                new HeadlessOptions { Use24HourFormat = true }
            )
        )
        {
            _ = app.PumpAndSettle();
            app.SetUse24HourFormat(true);
            app.SetUse24HourFormat(false);
            app.SetUse24HourFormat(false);
            app.SetUse24HourFormat(true);
            _ = app.PumpAndSettle();
        }

        Assert.False(plain);
        Assert.Equal([true, false, true], seen);
        Assert.Equal(1, localeReads);
    }

    /// <summary>
    /// The headless start refuses supported locales that hold a <see langword="null"/>, where it refuses its other options, before
    /// any part of the app is made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Options whose supported locales are the en tables and a <see langword="null"/>.
    ///
    /// Steps:
    /// - Start an app of a box with them.
    ///
    /// Expected:
    /// - It throws <see cref="ArgumentException"/> whose parameter is the options.
    /// </remarks>
    [UiFact]
    public void StartAsync_ANullSupportedLocale_Throws()
    {
        var options = new HeadlessOptions { SupportedLocales = [CldrLocales.En, null!] };

        // StartAsync checks its options before it returns its task.
        ArgumentException thrown = Assert.Throws<ArgumentException>(() =>
        {
            _ = HeadlessApp.StartAsync(() => Box(), options);
        });

        Assert.Equal("options", thrown.ParamName);
    }

    /// <summary>
    /// Setting the 24-hour form runs the work that queued on the UI thread before it returns, as a desktop window's notice is
    /// followed by the loop (design §4.11, "The key of the 24-hour form").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with the 24-hour form off, whose root function has an effect that, when it reads the form on, posts to the UI
    ///   thread's synchronization context a piece of work that counts its runs. Nothing is pumped after the start.
    ///
    /// Steps:
    /// - Set the 24-hour form on; read the count at once.
    ///
    /// Expected:
    /// - The work ran once.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SetUse24HourFormat_RunsTheWorkThatQueued()
    {
        int ran = 0;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            IReadOnlySignal<bool> use24HourFormat = Localization.Use24HourFormat.Use();
            SynchronizationContext context = SynchronizationContext.Current!;
            _ = Effect.Create(() =>
            {
                if (use24HourFormat.Get())
                {
                    context.Post(_ => ran++, null);
                }
            });
            return Box();
        });

        app.SetUse24HourFormat(true);

        Assert.Equal(1, ran);
    }

    /// <summary>
    /// Setting the 24-hour form checks the app, as <see cref="HeadlessApp.SetSystemLocales"/> does: off the UI thread it throws
    /// <see cref="InvalidOperationException"/>, and after the app is disposed <see cref="ObjectDisposedException"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a box.
    ///
    /// Steps:
    /// - Set the 24-hour form from a thread of the pool; dispose the app and set it on the UI thread.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/>, then <see cref="ObjectDisposedException"/>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SetUse24HourFormat_OffTheUiThreadOrDisposed_Throws()
    {
        HeadlessApp app = await HeadlessApp.StartAsync(() => Box());

        // The test calls the change off the UI thread on purpose, which is what XYLEM100 reports.
#pragma warning disable XYLEM100
        Exception? offThread = await Task.Run(() => Record.Exception(() => app.SetUse24HourFormat(true)), TestContext.Current.CancellationToken);
#pragma warning restore XYLEM100
        await app.DisposeAsync();
        Exception? disposed = Record.Exception(() => app.SetUse24HourFormat(true));

        Assert.IsType<InvalidOperationException>(offThread);
        Assert.IsType<ObjectDisposedException>(disposed);
    }
}
