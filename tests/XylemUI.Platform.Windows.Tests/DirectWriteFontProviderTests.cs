using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// DirectWrite's font fallback through DirectWriteFontProvider (design §4.5, roadmap row F1), against the fonts of Windows. The
// expected families were read on Windows 11 (ja-JP) and are the ones Windows ships; a test that needs a font the OS lacks
// (a server image without the East Asian fonts) is skipped with the font's name, not passed.
[Collection(UiThreadGroup.Name)]
public sealed class DirectWriteFontProviderTests(UiThreadFixture ui)
{
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");

    /// <summary>
    /// A Han character takes the font of its language when the family asked for has no Han glyphs: the same characters in
    /// Japanese, Simplified Chinese, Traditional Chinese, and Korean get four fonts.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The provider over the system fonts. The text 漢字, asked for in Segoe UI (which has no Han glyphs).
    ///
    /// Steps:
    /// - Resolve the text from 0 with the languages ja, zh-Hans, zh-Hant, and ko.
    ///
    /// Expected:
    /// - Each match covers both characters, in Yu Gothic UI, Microsoft YaHei UI, Microsoft JhengHei UI, and Malgun Gothic.
    /// </remarks>
    [Fact]
    public void HanCharacters_TakeTheFontOfTheirLanguage() =>
        ui.Invoke(() =>
        {
            using var provider = new DirectWriteFontProvider();
            var request = FontRequest.Default with { Family = "Segoe UI" };
            foreach (
                (string tag, string family) in new[]
                {
                    ("ja", "Yu Gothic UI"),
                    ("zh-Hans", "Microsoft YaHei UI"),
                    ("zh-Hant", "Microsoft JhengHei UI"),
                    ("ko", "Malgun Gothic"),
                }
            )
            {
                SkipWithout(family);
                FontMatch match = provider.ResolveFont("漢字", 0, TextLanguage.FromTag(tag), request);
                Assert.Equal((family, 2), (match.Font!.FamilyName, match.Length));
            }
        });

    /// <summary>
    /// Text in the default font, an emoji, and a character no font has split into matches at their edges, with the emoji in
    /// Segoe UI Emoji and the unknown character without a font.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The provider. The text "Ab", 😀 (two UTF-16 characters), U+0378 (unassigned), "c".
    ///
    /// Steps:
    /// - Resolve from 0, then from the end of each match, in Japanese with the default request.
    ///
    /// Expected:
    /// - Four matches: 0 + 2 in the OS's UI font (DefaultFamily), 2 + 2 in Segoe UI Emoji, 4 + 1 without a font, 5 + 1 in the UI
    ///   font. Every scale is 1.
    /// </remarks>
    [Fact]
    public void MixedText_SplitsAtTheEdgesOfEachFont() =>
        ui.Invoke(() =>
        {
            SkipWithout("Segoe UI Emoji");
            using var provider = new DirectWriteFontProvider();
            const string text = "Ab😀\u0378c";
            List<(int Start, int Length, string? Family, float Scale)> matches = Matches(provider, text);

            Assert.Equal(
                [(0, 2, provider.DefaultFamily, 1f), (2, 2, "Segoe UI Emoji", 1f), (4, 1, null, 1f), (5, 1, provider.DefaultFamily, 1f)],
                matches
            );
        });

    /// <summary>
    /// DirectWrite keeps a grapheme cluster in one match (design §4.5: run boundaries never split a cluster; until row T1 the
    /// provider relies on DirectWrite for it).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The provider. Four texts: "a", the family emoji 👨‍👩‍👧 (three emoji joined by two ZWJ, eight UTF-16 characters), "b";
    ///   👍 with the skin tone U+1F3FD (four); "e", the combining acute U+0301, "x"; ❤ with the emoji presentation selector
    ///   U+FE0F.
    ///
    /// Steps:
    /// - Resolve each text from 0 to its end in Japanese with the default request.
    ///
    /// Expected:
    /// - The family emoji is one match of eight characters (from 1), the thumbs-up with its tone one of four, "e" with its
    ///   accent and the "x" one of three, and the heart with its selector one of two.
    /// </remarks>
    [Fact]
    public void GraphemeClusters_StayInOneMatch() =>
        ui.Invoke(() =>
        {
            SkipWithout("Segoe UI Emoji");
            using var provider = new DirectWriteFontProvider();

            Assert.Equal([(0, 1), (1, 8), (9, 1)], Ranges(provider, "a👨\u200D👩\u200D👧b"));
            Assert.Equal([(0, 4)], Ranges(provider, "👍🏽"));
            Assert.Equal([(0, 3)], Ranges(provider, "e\u0301x"));
            Assert.Equal([(0, 2)], Ranges(provider, "❤\uFE0F"));
        });

    /// <summary>
    /// A request without a family gets the OS's UI font, the one <see cref="DirectWriteFontProvider.DefaultFamily"/> names.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The provider.
    ///
    /// Steps:
    /// - Resolve "Abc" from 0 with the default request.
    ///
    /// Expected:
    /// - One match of three characters, whose font's family is DefaultFamily, which is not empty.
    /// </remarks>
    [Fact]
    public void DefaultRequest_TakesTheUiFont() =>
        ui.Invoke(() =>
        {
            using var provider = new DirectWriteFontProvider();

            FontMatch match = provider.ResolveFont("Abc", 0, Japanese, FontRequest.Default);

            Assert.NotEmpty(provider.DefaultFamily);
            Assert.Equal((provider.DefaultFamily, 3), (match.Font!.FamilyName, match.Length));
        });

    /// <summary>
    /// The split of a text puts what no font of the OS has into the bundled font, which draws it as its .notdef rectangle.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The provider. The text "A", U+0378, U+0379 (both unassigned), "B".
    ///
    /// Steps:
    /// - Split the text with FontFallback.Split in Japanese with the default request.
    ///
    /// Expected:
    /// - Three runs: 0 + 1 in the UI font, 1 + 2 in the bundled font (the two unknown characters join one run), 3 + 1 in the UI
    ///   font.
    /// </remarks>
    [Fact]
    public void Split_PutsUnknownCharactersInTheBundledFont() =>
        ui.Invoke(() =>
        {
            using var provider = new DirectWriteFontProvider();
            var runs = new List<FontRun>();

            FontFallback.Split(provider, "A\u0378\u0379B", Japanese, FontRequest.Default, runs);

            Assert.Equal(
                [(0, 1, provider.DefaultFamily), (1, 2, "XylemUI Fallback"), (3, 1, provider.DefaultFamily)],
                runs.Select(run => (run.Start, run.Length, run.Font.FamilyName))
            );
        });

    /// <summary>
    /// A face other than the first of an OS font collection opens (S8a's review): the second face of Yu Gothic's .ttc is another
    /// family than the first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - C:\Windows\Fonts\YuGothR.ttc, whose faces are Yu Gothic and Yu Gothic UI Semilight (fontTools' TTCollection, name 1).
    ///
    /// Steps:
    /// - Open its bytes as a Font with face 0 and with face 1.
    ///
    /// Expected:
    /// - Face 0 is Yu Gothic; face 1's family starts with "Yu Gothic UI".
    /// </remarks>
    [Fact]
    public void FontCollection_OpensItsSecondFace()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "YuGothR.ttc");
        if (!File.Exists(path))
        {
            Assert.Skip($"{path} is not installed.");
        }

        byte[] data = File.ReadAllBytes(path);
        using var first = new Font(data, 0);
        using var second = new Font(data, 1);

        Assert.Equal("Yu Gothic", first.FamilyName);
        Assert.StartsWith("Yu Gothic UI", second.FamilyName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolving a text again whose fonts are open allocates nothing: the fonts, the locale, and the text-analysis source are
    /// kept.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The provider, warmed with ten splits of "Ab日本😀\u0378c" in Japanese into a reused list.
    ///
    /// Steps:
    /// - Split the same text 1,000 more times, measuring the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes.
    /// </remarks>
    [Fact]
    public void ResolvingAgain_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var provider = new DirectWriteFontProvider();
            var runs = new List<FontRun>(8);
            const string text = "Ab日本😀\u0378c";
            for (int i = 0; i < 10; i++)
            {
                FontFallback.Split(provider, text, Japanese, FontRequest.Default, runs);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                FontFallback.Split(provider, text, Japanese, FontRequest.Default, runs);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        });

    private static void SkipWithout(string family)
    {
        if (!Installed(family))
        {
            Assert.Skip($"{family} is not installed.");
        }
    }

    private static bool Installed(string family) =>
        family switch
        {
            "Yu Gothic UI" => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "YuGothR.ttc")),
            "Microsoft YaHei UI" => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc")),
            "Microsoft JhengHei UI" => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msjh.ttc")),
            "Malgun Gothic" => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf")),
            "Segoe UI Emoji" => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf")),
            _ => true,
        };

    private static List<(int Start, int Length, string? Family, float Scale)> Matches(DirectWriteFontProvider provider, string text)
    {
        var matches = new List<(int, int, string?, float)>();
        for (int start = 0; start < text.Length; )
        {
            FontMatch match = provider.ResolveFont(text, start, Japanese, FontRequest.Default);
            matches.Add((start, match.Length, match.Font?.FamilyName, match.Scale));
            start += match.Length;
        }

        return matches;
    }

    private static List<(int Start, int Length)> Ranges(DirectWriteFontProvider provider, string text) =>
        Matches(provider, text).Select(match => (match.Start, match.Length)).ToList();
}
