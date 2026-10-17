namespace XylemUI.Text.Tests;

// The test font (Fonts/README.md): a Noto Sans JP subset with 1000 units per em.
internal static class TestFont
{
    private static readonly byte[] Bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));

    public static Font Open() => new(Bytes);
}
