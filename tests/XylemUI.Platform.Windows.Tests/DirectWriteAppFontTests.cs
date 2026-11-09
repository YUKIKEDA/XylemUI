using XylemUI.Text;

namespace XylemUI.Platform.Windows.Tests;

// The fonts an app registers (design §4.5, "App fonts", row IC1) through DirectWriteFontProvider: a collection of their own, the
// base collection of a request for their family. The test fonts are the bundled fallback font renamed, ASCII only
// (tests/XylemUI.Text.Tests/Fonts/README.md). A registered font stays for the process; "Gabriola" is registered by one test alone.
[Collection(UiThreadGroup.Name)]
public sealed class DirectWriteAppFontTests(UiThreadFixture ui)
{
    private const string Family = "XylemUI Test App";
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");

    /// <summary>
    /// A registered family resolves to the registered face nearest the weight asked for, and the characters it lacks go to the
    /// system's fallback.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The regular (400) and bold (700) test app fonts are registered; the provider over the system fonts.
    ///
    /// Steps:
    /// - Resolve "Ab日本" in "XylemUI Test App" from 0 and from 2 at weight 400, and from 0 at weight 700.
    ///
    /// Expected:
    /// - From 0: the registered regular font for 2 characters. From 2: Yu Gothic UI for the 2 kanji, which the registered font
    ///   lacks. At 700: the registered bold font.
    /// </remarks>
    [Fact]
    public void RegisteredFamily_TakesItsFont_AndTheFallbackForWhatItLacks() =>
        ui.Invoke(() =>
        {
            AppFonts.Add(Read("XylemUITestApp-Regular.ttf"));
            AppFonts.Add(Read("XylemUITestApp-Bold.ttf"));
            using var provider = new DirectWriteFontProvider();
            var request = new FontRequest(Family, FontWeight.Normal, FontSlant.Normal, FontStretch.Normal);

            FontMatch latin = provider.ResolveFont("Ab日本", 0, Japanese, request);
            FontMatch bold = provider.ResolveFont("Ab日本", 0, Japanese, request with { Weight = FontWeight.Bold });

            Assert.Same(Face(400), latin.Font);
            Assert.Equal(2, latin.Length);
            Assert.Same(Face(700), bold.Font);
            if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "YuGothR.ttc")))
            {
                Assert.Skip("Yu Gothic UI is not installed.");
            }

            FontMatch kanji = provider.ResolveFont("Ab日本", 2, Japanese, request);
            Assert.Equal(("Yu Gothic UI", 2), (kanji.Font!.FamilyName, kanji.Length));
        });

    /// <summary>
    /// A font registered with the name of a family the OS has comes before the OS's family, also in a provider made before it was
    /// registered.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The provider over the system fonts; Windows has Gabriola (gabriola.ttf). No test of this process registers that family
    ///   before.
    ///
    /// Steps:
    /// - Resolve "Ab" in Gabriola; register the test font named "Gabriola"; resolve "Ab" in Gabriola again with the same provider.
    ///
    /// Expected:
    /// - First the OS's Gabriola (its file in the fonts folder); then the registered font.
    /// </remarks>
    [Fact]
    public void FamilyOfTheOs_RegisteredByTheApp_ComesFirst() =>
        ui.Invoke(() =>
        {
            if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "Gabriola.ttf")))
            {
                Assert.Skip("Gabriola is not installed.");
            }

            using var provider = new DirectWriteFontProvider();
            var request = new FontRequest("Gabriola", FontWeight.Normal, FontSlant.Normal, FontStretch.Normal);
            FontMatch before = provider.ResolveFont("Ab", 0, Japanese, request);

            AppFonts.Add(Read("XylemUITestGabriola.ttf"));
            FontMatch after = provider.ResolveFont("Ab", 0, Japanese, request);

            Assert.EndsWith("gabriola.ttf", before.Font!.FilePath, StringComparison.OrdinalIgnoreCase);
            Assert.Same(AppFontSet.Find("Gabriola", FontWeight.Normal, FontSlant.Normal, FontStretch.Normal)!.Font, after.Font);
            Assert.Equal(2, after.Length);
        });

    private static Font Face(int weight) => AppFontSet.Find(Family, new FontWeight(weight), FontSlant.Normal, FontStretch.Normal)!.Font;

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", name));
}
