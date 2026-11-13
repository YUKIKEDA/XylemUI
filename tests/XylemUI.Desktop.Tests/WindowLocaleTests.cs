using XylemUI.Testing;
using XylemUI.Testing.XUnit;
using XylemUI.Text;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The locale of a window (row LC1, design §4.11): the supported locales resolved against the OS's languages, provided around the
// root function, resolved again when the OS's languages change, and the language of the text that sets none. The headless app
// takes the OS's languages from its options; a desktop window takes them from Windows through the same WindowLocale.
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
            new HeadlessOptions { SupportedLocales = [Locale.Parse("en"), Locale.Parse("ja")], SystemLocales = ["ja-JP", "en-US"] }
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
                new HeadlessOptions { SupportedLocales = [Locale.Parse("de"), Locale.Parse("en")] }
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
                SupportedLocales = [Locale.Parse("en"), Locale.Parse("ja")],
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
            new HeadlessOptions { SupportedLocales = [Locale.Parse("ja"), Locale.Parse("en")], SystemLocales = ["ja-JP"] }
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
}
