using XylemUI.Imaging;
using XylemUI.Testing.D3D11;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The image element on WARP in a headless app (design §5.0.4; row IM1, 1b-2): each format read from a file on the thread pool, and
// a JPEG from bytes and from a resource, drawn at its own size, against the pixels the decoder reads from the same file (the decoder
// is tested against Pillow and WIC in XylemUI.Imaging.Tests).
public sealed class ImagePixelTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Images");

    /// <summary>
    /// An image of each format, read from its file on a worker and drawn at its own size, shows the pixels its file decodes to, over
    /// the window's white.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 100 x 80 on WARP, at scale 1.
    ///
    /// Steps:
    /// - Show the image from its file, its box the image's size; wait until no decode is in flight; pump; read the frame.
    ///
    /// Expected:
    /// - Each pixel of the box is the decoder's pixel over white, each channel within 1.
    /// </remarks>
    /// <param name="name">The image's file.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [UiTheory]
    [InlineData("basn2c08.png")]
    [InlineData("baseline.jpg")]
    [InlineData("lossless.webp")]
    [InlineData("first-frame.gif")]
    [InlineData("rgb24.bmp")]
    [InlineData("png.ico")]
    public async Task Image_EachFormatFromAFile_ShowsItsPixels(string name)
    {
        string path = Path.Combine(Folder, name);
        (byte[] expected, int width, int height) = Decoded(File.ReadAllBytes(path));
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Image(ImageSource.FromFile(path)).Width(width).Height(height), Options());

        Settle(app);

        AssertPixels(app, expected, width, height);
    }

    /// <summary>
    /// A JPEG read from bytes and from a resource shows the same pixels as from its file.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 100 x 80 on WARP; baseline.jpg, and the same file embedded as the resource "image.jpg".
    ///
    /// Steps:
    /// - Show the image from the file's bytes, then in another app from the resource; settle each and read its frame.
    ///
    /// Expected:
    /// - Both frames hold the decoder's pixels, each channel within 1.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Image_FromBytesAndFromAResource_ShowsItsPixels()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Folder, "baseline.jpg"));
        (byte[] expected, int width, int height) = Decoded(bytes);
        foreach (
            ImageSource source in new[] { ImageSource.FromBytes(bytes), ImageSource.FromResource(typeof(ImagePixelTests).Assembly, "image.jpg") }
        )
        {
            await using HeadlessApp app = await HeadlessApp.StartAsync(() => Image(source).Width(width).Height(height), Options());
            Settle(app);
            AssertPixels(app, expected, width, height);
        }
    }

    /// <summary>
    /// A file that is not there shows the error part, and its image is not added.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 100 x 80 on WARP.
    ///
    /// Steps:
    /// - Show an image from a path that does not exist, its error part a 20 x 10 box; settle.
    ///
    /// Expected:
    /// - The error part is the element's child, 20 x 10, and the element is 10 high (its width is the window's: the app's root
    ///   stretches it); the window holds no image.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Image_MissingFile_ShowsTheErrorPart()
    {
        Element image = default;
        Element error = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                image = Image(
                    ImageSource.FromFile(Path.Combine(Folder, "missing.png")),
                    new ImageOptions { Error = () => error = Box().Width(20).Height(10) }
                ),
            Options()
        );

        Settle(app);

        Assert.Equal(image.Node, app.Tree.Nodes.GetRef(error.Node).Parent);
        Rect part = app.Tree.GetRect(error.Node);
        Assert.Equal((20f, 10f, 10f), (part.Width, part.Height, app.Tree.GetRect(image.Node).Height));
        Assert.Equal(0, app.Tree.Images.Count);
    }

    /// <summary>
    /// Disposing the app throws its tree away: the image its cache kept is released with it (design §5.0.4, "Cache").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 100 x 80 on WARP that shows baseline.jpg from its bytes; settled.
    ///
    /// Steps:
    /// - Dispose the app.
    ///
    /// Expected:
    /// - The window held one image before, and holds none after.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task App_Disposed_ReleasesTheImagesOfItsCache()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Folder, "baseline.jpg"));
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Image(ImageSource.FromBytes(bytes)), Options());
        Settle(app);
        Assert.Equal(1, app.Tree.Images.Count);

        await app.DisposeAsync();

        Assert.Equal(0, app.Tree.Images.Count);
    }

    private static HeadlessOptions Options() =>
        new()
        {
            Width = 100,
            Height = 80,
            Renderer = () => new WarpRenderer(),
        };

    // Waits until no decode is in flight, then runs the UI queue (where the results were posted) and the frames.
    private static void Settle(HeadlessApp app)
    {
        Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));
        _ = app.PumpAndSettle();
    }

    // The pixels the decoder reads for an image element, its colors converted to sRGB: basn2c08.png's gAMA of 1.0 changes them
    // (IM1's second PR, design §5.0.4, Q24).
    private static (byte[] Pixels, int Width, int Height) Decoded(byte[] bytes)
    {
        var decoder = new ImageDecoder();
        Assert.True(decoder.TryDecodeTo(bytes, 0, 0, 4096, out byte[]? pixels, out int width, out int height));
        return (pixels!, width, height);
    }

    // Each pixel of the box at the top left, against the decoder's premultiplied pixel over white.
    private static void AssertPixels(HeadlessApp app, byte[] expected, int width, int height)
    {
        Color[] frame = app.Capture(out int frameWidth, out _);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                int under = 255 - expected[i + 3];
                (int b, int g, int r) = (expected[i] + under, expected[i + 1] + under, expected[i + 2] + under);
                Color actual = frame[(y * frameWidth) + x];
                int difference = Math.Max(Math.Abs(actual.R - r), Math.Max(Math.Abs(actual.G - g), Math.Abs(actual.B - b)));
                Assert.True(difference <= 1, $"pixel ({x}, {y}): expected ({r}, {g}, {b}), got {actual}");
            }
        }
    }
}
