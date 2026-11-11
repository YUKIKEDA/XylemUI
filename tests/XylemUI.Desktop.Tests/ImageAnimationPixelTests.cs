using XylemUI.Testing.D3D11;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// Animated images in a headless app on WARP (design §5.0.4, "Animations" and "Headless"; row IM1's 3c): the real decoders on the
// thread pool, the frames written into the image's texture, and the headless switch. The frames' expected pixels are Pillow's
// (XylemUI.Imaging.Tests/Images/README.md).
public sealed class ImageAnimationPixelTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Images");

    /// <summary>
    /// By default a headless app shows an animated image's first frame and keeps no timer for it: the pump settles, time does not
    /// change the picture, and the app ends with nothing left ("Headless").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 100 x 80 on WARP with the default options, showing an animation at its own size (48 x 32).
    ///
    /// Steps:
    /// - Wait for the decode and pump until settled; read the frame. Advance the clock by 1 s, pump, and read the frame again.
    ///
    /// Expected:
    /// - Both frames hold the animation's first frame, each channel within 1; the second pump runs no iteration that draws.
    /// </remarks>
    /// <param name="name">The animation: a GIF, and a WebP, which the decoders did not read before the animations.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [UiTheory]
    [InlineData("anim-previous.gif")]
    [InlineData("anim-lossless.webp")]
    public async Task AnimatedImage_ByDefault_ShowsItsFirstFrame(string name)
    {
        string path = Path.Combine(Folder, name);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Image(ImageSource.FromFile(path)).Width(48).Height(32), Options(false));

        Settle(app);
        AssertFrame(app, name, 0);
        int presents = app.PresentCount;

        app.Time.Advance(TimeSpan.FromSeconds(1));
        Settle(app);
        AssertFrame(app, name, 0);
        Assert.Equal(presents, app.PresentCount);
    }

    /// <summary>
    /// With <see cref="HeadlessOptions.ImageAnimation"/>, an animated image plays on the virtual clock: its frames are decoded on
    /// the thread pool and written into its texture, and an animation that plays once ends on its last frame with no timer left.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 100 x 80 on WARP whose images play, showing anim-previous.gif (4 frames, one play) at its own size.
    ///
    /// Steps:
    /// - 10 times, wait until no decode is in flight and pump until settled (the pump waits for the frames' timers on the virtual
    ///   clock); read the frame.
    ///
    /// Expected:
    /// - The frame read is the animation's frame 3, each channel within 1, and at least 4 frames were presented, one for each
    ///   frame of the animation. The app ends with no timer left (its disposal does not throw).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AnimatedImage_WithTheOption_PlaysToItsLastFrame()
    {
        const string Name = "anim-previous.gif";
        string path = Path.Combine(Folder, Name);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Image(ImageSource.FromFile(path)).Width(48).Height(32), Options(true));

        int presents = app.PresentCount;

        for (int i = 0; i < 10; i++)
        {
            Settle(app);
        }

        AssertFrame(app, Name, 3);
        Assert.True(app.PresentCount - presents >= 4);
    }

    private static HeadlessOptions Options(bool animate) =>
        new()
        {
            Width = 100,
            Height = 80,
            Renderer = () => new WarpRenderer(),
            ImageAnimation = animate,
        };

    // Waits until no decode is in flight, then runs the UI queue (where the results were posted), the timers, and the frames.
    private static void Settle(HeadlessApp app)
    {
        Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));
        _ = app.PumpAndSettle();
    }

    // Each pixel of the box at the top left, against the animation's expected frame over white.
    private static void AssertFrame(HeadlessApp app, string name, int index)
    {
        byte[] expected = File.ReadAllBytes(Path.Combine(Folder, $"{name}.{index}.bgra"));
        Color[] frame = app.Capture(out int frameWidth, out _);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 48; x++)
            {
                int i = ((y * 48) + x) * 4;
                int under = 255 - expected[i + 3];
                (int b, int g, int r) = (expected[i] + under, expected[i + 1] + under, expected[i + 2] + under);
                Color actual = frame[(y * frameWidth) + x];
                int difference = Math.Max(Math.Abs(actual.R - r), Math.Max(Math.Abs(actual.G - g), Math.Abs(actual.B - b)));
                Assert.True(difference <= 1, $"frame {index}, pixel ({x}, {y}): expected ({r}, {g}, {b}), got {actual}");
            }
        }
    }
}
