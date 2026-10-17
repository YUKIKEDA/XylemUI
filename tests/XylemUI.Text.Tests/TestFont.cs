namespace XylemUI.Text.Tests;

// The test font (Fonts/README.md): a Noto Sans JP subset with 1000 units per em.
internal static class TestFont
{
    private static readonly byte[] Bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));

    // The language the tests shape in, unless a test is about the language.
    public static TextLanguage Japanese { get; } = TextLanguage.FromTag("ja");

    public static Font Open() => new(Bytes);
}
