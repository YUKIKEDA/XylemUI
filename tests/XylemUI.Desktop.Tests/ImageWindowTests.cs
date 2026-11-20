using XylemUI.Platform.Windows;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The image cache of a real window (row IM1, 3a; design §5.0.4, "Cache"): what closing the window leaves of it. The cache's rules
// are tested with no window (WindowImageCacheTests), and the pixels headless (ImagePixelTests).
[Collection(UiThreadGroup.Name)]
public sealed class ImageWindowTests(UiThreadFixture ui)
{
    /// <summary>
    /// Closing the window throws its tree away: the images of its cache are released, the one on screen and the one kept for a
    /// later element.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The Windows host, made first, as <c>XylemApp.Run</c> makes it. A window of 200 x 200 on WARP, hidden, counted as an open
    ///   window of the host, that shows an image whose source is a signal; the window is made with no synchronization context, so
    ///   each image is decoded where the element asks for it. The message loop runs under a watchdog.
    ///
    /// Steps:
    /// - Set the signal to a second image, so that the cache keeps the first for a later element. Ask the window to close; run the
    ///   loop until it returns.
    ///
    /// Expected:
    /// - The window holds two images before, and none after the loop returned before the watchdog.
    /// </remarks>
    [Fact]
    public void Close_ReleasesTheImagesOfTheCache() =>
        ui.Invoke(() =>
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Images");
            ImageSource first = ImageSource.FromFile(Path.Combine(folder, "baseline.jpg"));
            ImageSource second = ImageSource.FromFile(Path.Combine(folder, "basn2c08.png"));
            Signal<ImageSource> source = Signal.Create(first);

            // The order of XylemApp.Run: the host before the window, so that the window is disposed before the host.
            using var app = new Win32PlatformApp();
            using TestWindow window = WithNoLoop(() =>
                new TestWindow(() => Image(source.Get), new WindowOptions("Image test", 200, 200), useWarp: true)
            );
            source.Set(second);
            Assert.Equal(2, window.Tree.Images.Count);
            XylemApp.Open(window.Core, app);

            using (var watchdog = new LoopWatchdog())
            {
                LoopTestHelpers.RunLoop(watchdog, () => watchdog.Expired, window.Window.RequestClose);
                Assert.False(watchdog.Expired);
            }

            Assert.Equal(0, window.Tree.Images.Count);
        });

    // Makes the window with no SynchronizationContext: an image element made then keeps none, and decodes where it is asked to.
    private static TestWindow WithNoLoop(Func<TestWindow> make)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return make();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
