using XylemUI.Testing.XUnit;
using XylemUI.Text;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// The PNG files and the goldens (design §12.3): a renderer of this file's own gives the frame, so these run on every OS. The goldens
// and the failures' images go to a folder of each test's own, deleted after it, so the failures these tests mean stay out of the
// folder the CI uploads; the update is set through the comparison's switch rather than the environment, so the tests do not run
// beside each other.
[CollectionDefinition(nameof(GoldenTests), DisableParallelization = true)]
[Collection(nameof(GoldenTests))]
public sealed class GoldenTests : IDisposable
{
    private static readonly Color Gray = new(0x80, 0x80, 0x80);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "XylemUI.GoldenTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="GoldenTests"/> class: the goldens of these tests go to a new folder.
    /// </summary>
    public GoldenTests()
    {
        Goldens.RootForTests = Path.Combine(_root, "Goldens");
        Goldens.FailureFolderForTests = Path.Combine(_root, "GoldenFailures");
        Goldens.UpdateForTests = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Goldens.RootForTests = null;
        Goldens.FailureFolderForTests = null;
        Goldens.UpdateForTests = null;
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A PNG written and read back gives the same colors, translucent ones included.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An image of 3 × 2: opaque red, green, and blue, white, black, and a half-transparent gray.
    ///
    /// Steps:
    /// - Encode it; decode the file.
    ///
    /// Expected:
    /// - 3 × 2 and the same colors, the gray within 1 of each channel (a premultiplied round trip).
    /// </remarks>
    [Fact]
    public void Png_WrittenAndRead_GivesTheSameColors()
    {
        Color[] image = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 255), new(0, 0, 0), new(0x80, 0x80, 0x80, 0x80)];
        byte[] file = Png.Encode(image, 3, 2);
        Color[]? read = Png.Decode(file, out int width, out int height);

        Assert.Equal((3, 2), (width, height));
        Assert.NotNull(read);
        Assert.Equal(image[..5], read[..5]);
        Assert.InRange(Math.Abs(read[5].R - 0x80), 0, 1);
        Assert.Equal(0x80, read[5].A);
    }

    /// <summary>
    /// The update writes the golden; a frame equal to it, or within the tolerance, passes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app with a renderer whose frame is 4 × 4 of gray.
    ///
    /// Steps:
    /// - Compare with the update on; then with it off; then with every pixel 2 lighter.
    ///
    /// Expected:
    /// - The update writes Goldens' "gray.png"; both comparisons pass.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MatchesGolden_UpdatedThenEqualOrWithinTheTolerance_Passes()
    {
        var renderer = new StillRenderer(4, 4, Gray);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box(), new HeadlessOptions { Renderer = () => renderer });

        Goldens.UpdateForTests = true;
        await app.MatchesGoldenAsync("gray.png");
        Assert.True(File.Exists(Path.Combine(_root, "Goldens", "gray.png")));

        Goldens.UpdateForTests = false;
        await app.MatchesGoldenAsync("gray.png");
        renderer.Fill(new Color(0x82, 0x82, 0x82));
        await app.MatchesGoldenAsync("gray.png");
    }

    /// <summary>
    /// A pixel beyond the channel's tolerance fails and writes the frame and the difference; the pixel limit lets that many pass.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The golden of a 4 × 4 gray frame; the frame then has one pixel 3 lighter and one 40 lighter.
    ///
    /// Steps:
    /// - Compare with the default; with 1 pixel allowed; with 2 allowed; with a channel of 40 allowed.
    ///
    /// Expected:
    /// - The default and 1 pixel throw <see cref="GoldenMismatchException"/> naming 2 pixels and at most 40, and the frame and its
    ///   difference are in GoldenFailures; 2 pixels and a channel of 40 pass, and the pass removes the failure's images.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MatchesGolden_BeyondTheTolerance_FailsAndWritesTheDifference()
    {
        var renderer = new StillRenderer(4, 4, Gray);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box(), new HeadlessOptions { Renderer = () => renderer });
        Goldens.UpdateForTests = true;
        await app.MatchesGoldenAsync("beyond.png");
        Goldens.UpdateForTests = false;

        renderer.Pixels[5] = new Color(0x83, 0x83, 0x83);
        renderer.Pixels[10] = new Color(0xA8, 0x80, 0x80);
        GoldenMismatchException error = await Assert.ThrowsAsync<GoldenMismatchException>(() => app.MatchesGoldenAsync("beyond.png"));
        Assert.Contains("2 pixels of 16", error.Message, StringComparison.Ordinal);
        Assert.Contains("at most 40", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Goldens.FailureFolder, "beyond.png")));
        Assert.True(File.Exists(Path.Combine(Goldens.FailureFolder, "beyond.diff.png")));

        _ = await Assert.ThrowsAsync<GoldenMismatchException>(() => app.MatchesGoldenAsync("beyond.png", new GoldenTolerance(2, 1)));
        await app.MatchesGoldenAsync("beyond.png", new GoldenTolerance(2, 2));
        Assert.False(File.Exists(Path.Combine(Goldens.FailureFolder, "beyond.png")));
        Assert.False(File.Exists(Path.Combine(Goldens.FailureFolder, "beyond.diff.png")));
        await app.MatchesGoldenAsync("beyond.png", new GoldenTolerance(40, 0));
    }

    /// <summary>
    /// A missing golden fails, writes nothing to Goldens, and puts the frame in GoldenFailures; a golden of another size fails.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 4 × 4 gray frame; no golden "missing.png"; a golden "small.png" of 2 × 2.
    ///
    /// Steps:
    /// - Compare with "missing.png", then with "small.png".
    ///
    /// Expected:
    /// - Both throw <see cref="GoldenMismatchException"/>: the first names the variable that writes the goldens, and Goldens has no
    ///   "missing.png"; the second names both sizes.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MatchesGolden_MissingOrOfAnotherSize_Fails()
    {
        var renderer = new StillRenderer(4, 4, Gray);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box(), new HeadlessOptions { Renderer = () => renderer });

        GoldenMismatchException missing = await Assert.ThrowsAsync<GoldenMismatchException>(() => app.MatchesGoldenAsync("missing.png"));
        Assert.Contains(Goldens.UpdateVariable, missing.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "Goldens", "missing.png")));
        Assert.True(File.Exists(Path.Combine(Goldens.FailureFolder, "missing.png")));

        _ = Directory.CreateDirectory(Path.Combine(_root, "Goldens"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "Goldens", "small.png"), Png.Encode([Gray, Gray, Gray, Gray], 2, 2));
        GoldenMismatchException size = await Assert.ThrowsAsync<GoldenMismatchException>(() => app.MatchesGoldenAsync("small.png"));
        Assert.Contains("4 × 4", size.Message, StringComparison.Ordinal);
        Assert.Contains("2 × 2", size.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An app with no renderer has no pixels: the screenshot and the comparison throw.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app with no renderer.
    ///
    /// Steps:
    /// - Save a screenshot; compare with a golden.
    ///
    /// Expected:
    /// - Both throw <see cref="InvalidOperationException"/> naming HeadlessOptions.Renderer.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ScreenshotAndGolden_NoRenderer_Throw()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box());
        InvalidOperationException shot = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.SaveScreenshotAsync(Path.Combine(_root, "shot.png"))
        );
        Assert.Contains("HeadlessOptions.Renderer", shot.Message, StringComparison.Ordinal);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => app.MatchesGoldenAsync("none.png"));
    }

    /// <summary>
    /// The screenshot is a PNG of the frame, and the renderer is sized to the window in physical pixels and disposed with the app.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 30 × 20 at a scale of 1.5, with a renderer that fills its frame with gray.
    ///
    /// Steps:
    /// - Save a screenshot; read it; dispose the app.
    ///
    /// Expected:
    /// - The renderer was sized 45 × 30; the file is 45 × 30 of gray; the renderer is disposed.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task SaveScreenshot_WritesTheFrame_AtThePhysicalSize()
    {
        StillRenderer? renderer = null;
        HeadlessApp app = await HeadlessApp.StartAsync(
            () => Box(),
            new HeadlessOptions
            {
                Width = 30,
                Height = 20,
                Scale = 1.5f,
                Renderer = () => renderer = new StillRenderer(0, 0, Gray),
            }
        );
        string path = Path.Combine(_root, "shot.png");
        await app.SaveScreenshotAsync(path);
        Color[]? read = Png.Decode(await File.ReadAllBytesAsync(path), out int width, out int height);
        await app.DisposeAsync();

        Assert.Equal((45, 30), (width, height));
        Assert.All(read!, color => Assert.Equal(Gray, color));
        Assert.True(renderer!.Disposed);
    }

    // A renderer whose frame is a picture this test sets: the frames it is handed are counted, not drawn.
    private sealed class StillRenderer(int width, int height, Color fill) : HeadlessRenderer
    {
        private int _width = width;
        private int _height = height;

        public Color[] Pixels { get; private set; } = Enumerable.Repeat(fill, width * height).ToArray();

        public bool Disposed { get; private set; }

        internal override IRenderTarget Target { get; } = new HeadlessTarget(null);

        public void Fill(Color color) => Array.Fill(Pixels, color);

        internal override IGlyphRasterizer CreateRasterizer() => new HeadlessGlyphs();

        internal override void Resize(int width, int height)
        {
            if (_width == 0)
            {
                (_width, _height) = (width, height);
                Pixels = Enumerable.Repeat(fill, width * height).ToArray();
            }
        }

        internal override Color[] Capture(out int width, out int height)
        {
            (width, height) = (_width, _height);
            return (Color[])Pixels.Clone();
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
