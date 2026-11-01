using static XylemUI.Elements;

namespace XylemUI.Text.Tests;

// The glyphs of a popup whose list alone is built (design §7.3, "Display lists"): such a frame starts no frame of the glyph atlas.
[Collection(UiThreadGroup.Name)]
public sealed class PopupGlyphTests(UiThreadFixture ui) : IDisposable
{
    private readonly Font _font = TestFont.Open();

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// When the atlas at its page limit refuses a new glyph in a frame that built a popup's list alone, the next frame builds the
    /// window's list too, which starts a frame of the atlas, and the popup's glyph is drawn.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An atlas of one 64 px page with 32 px plots, at most 1 page (PageLimit 2: 8 plots); each Japanese glyph of the test font at
    ///   24 px takes a plot (GlyphAtlasTests).
    /// - The window draws 7 of the 8 Japanese glyphs; an anchor's open popup draws a text that follows a signal, at first the 8th.
    ///   Settled: the 8 plots are full and used in the atlas's current frame.
    ///
    /// Steps:
    /// - Set the popup's text to "W", and run one frame; then run the frame it asked for.
    ///
    /// Expected:
    /// - The first frame builds the popup's list alone: "W" is refused, the popup draws no glyph, and a frame is asked for.
    /// - The second builds the window's list, and the popup draws one glyph; no more is refused.
    /// </remarks>
    [Fact]
    public void PopupAloneBuilt_AtlasRefusesAGlyph_TheNextFrameDrawsIt() =>
        ui.Invoke(() =>
        {
            var atlas = new GlyphAtlas(new TestRasterizer(), pageSize: 64, plotSize: 32, maxPages: 1);
            using var scene = new TextScene(_font, atlas);
            scene.Tree.UiLanguage = TestFont.Japanese;
            var open = Signal.Create(true);
            var popupText = Signal.Create("ト");
            scene.Mount(() =>
                Column(
                    Elements.Text("日本語のテキス").FontSize(24),
                    Box().Width(10).Height(10).Popup(open, () => Elements.Text(() => popupText.Get()).FontSize(24))
                )
            );
            Settle(scene);
            Assert.Equal(1, PopupGlyphs(scene));
            Assert.Equal(2, atlas.GetPageCount(GlyphCoverage.Gray));
            int builds = scene.Window.MainBuilds;
            int refused = atlas.RefusedCount;

            popupText.Set("W");
            scene.Draw();

            Assert.Equal(builds, scene.Window.MainBuilds);
            Assert.Equal(refused + 1, atlas.RefusedCount);
            Assert.Equal(0, PopupGlyphs(scene));
            Assert.True(scene.Scheduler.Pending);

            scene.Draw();

            Assert.Equal(builds + 1, scene.Window.MainBuilds);
            Assert.Equal(refused + 1, atlas.RefusedCount);
            Assert.Equal(1, PopupGlyphs(scene));
        });

    private static void Settle(TextScene scene)
    {
        for (int i = 0; i < 10 && scene.Scheduler.Pending; i++)
        {
            scene.Draw();
        }

        Assert.False(scene.Scheduler.Pending);
    }

    // The glyphs the open popup's list draws: its text's runs are its only commands.
    private static int PopupGlyphs(TextScene scene)
    {
        int glyphs = 0;
        DisplayListReader reader = Assert.Single(scene.Tree.Popups.Opened).List.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            Assert.Equal(CommandTag.GlyphRun, tag);
            glyphs += reader.ReadGlyphRun().Glyphs.Length;
        }

        return glyphs;
    }
}
