using static XylemUI.Tests.ImageDoubles;

namespace XylemUI.Tests;

// The app's entries to the image caches (design §5.0.4, "API"; row IM1, 3a): the two limits, Evict, and Clear reach each open
// window of the UI thread, a window opened later uses the limits set before, and each entry checks its thread and its arguments.
// The limits belong to the UI thread, so each test puts them back.
[Collection(UiThreadGroup.Name)]
public sealed class ImageCacheTests(UiThreadFixture ui)
{
    /// <summary>
    /// Unless set, the limits are 1,000 images and 104,857,600 bytes (100 MiB), and a window's cache has them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread, on which no test left a limit set.
    ///
    /// Steps:
    /// - Read both limits. Make a window and read its cache's limits.
    ///
    /// Expected:
    /// - 1,000 and 104,857,600, from <see cref="ImageCache"/> and from the window's cache.
    /// </remarks>
    [Fact]
    public void Limits_Unset_AreTheDefaults() =>
        ui.Invoke(() =>
            WithLimits(() =>
            {
                using var scene = new ImageScene();

                Assert.Equal((1_000, 104_857_600L), (ImageCache.MaximumCount, ImageCache.MaximumBytes));
                Assert.Equal((1_000, 104_857_600L), (scene.Cache.MaximumCount, scene.Cache.MaximumBytes));
            })
        );

    /// <summary>
    /// Lowering the count limit takes the least recently used images out of each open window at once; a window opened later uses
    /// the limit; with 0, no image stays that no element shows, and one on screen stays.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two windows with no loop; the test decode. Each window showed and took away three images; the second also keeps one on
    ///   screen.
    ///
    /// Steps:
    /// - Set the count limit to 2; make a third window. Set the count limit to 0; draw the second window.
    ///
    /// Expected:
    /// - At 2: each window counts two images and holds two (in the second, the one on screen, the newest, is one of the two); the
    ///   third window's cache has the limit 2.
    /// - At 0: nothing is counted; the first window holds no image, the second the one on screen, which the frame draws.
    /// </remarks>
    [Fact]
    public void MaximumCount_Lowered_TrimsEachOpenWindowAtOnce() =>
        ui.Invoke(() =>
            WithLimits(() =>
            {
                using var first = new ImageScene();
                using var second = new ImageScene();
                for (int i = 0; i < 3; i++)
                {
                    ShowAndLeave(first, Sized(10, 10));
                    ShowAndLeave(second, Sized(10, 10));
                }

                _ = second.Show(Sized(10, 10));
                Assert.Equal((3, 4), (first.Tree.Images.Count, second.Tree.Images.Count));

                ImageCache.MaximumCount = 2;
                Assert.Equal((2, 2, 2, 2), (first.Cache.CountedCount, first.Tree.Images.Count, second.Cache.CountedCount, second.Tree.Images.Count));
                using var third = new ImageScene();
                Assert.Equal((2, 2), (ImageCache.MaximumCount, third.Cache.MaximumCount));

                ImageCache.MaximumCount = 0;
                Assert.Equal((0, 0, 0, 1), (first.Cache.CountedCount, first.Tree.Images.Count, second.Cache.CountedCount, second.Tree.Images.Count));
                _ = Assert.Single(second.Draw());
            })
        );

    /// <summary>
    /// Lowering the byte limit takes the least recently used images out of each open window at once, and a window opened later
    /// uses the limit: an image larger than it is shown and not kept.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two windows with no loop; the test decode. Each showed and took away three images of 10 x 10 (400 bytes each).
    ///
    /// Steps:
    /// - Set the byte limit to 800. Make a third window; show an image of 20 x 20 (1,600 bytes) in it, draw, and take it away.
    ///
    /// Expected:
    /// - Each of the two windows counts 800 bytes and holds two images.
    /// - The third window's cache has the limit 800; its image is drawn and not counted; taken away, the window holds no image.
    /// </remarks>
    [Fact]
    public void MaximumBytes_Lowered_TrimsEachOpenWindowAtOnce() =>
        ui.Invoke(() =>
            WithLimits(() =>
            {
                using var first = new ImageScene();
                using var second = new ImageScene();
                for (int i = 0; i < 3; i++)
                {
                    ShowAndLeave(first, Sized(10, 10));
                    ShowAndLeave(second, Sized(10, 10));
                }

                ImageCache.MaximumBytes = 800;
                Assert.Equal(
                    (800L, 2, 800L, 2),
                    (first.Cache.CountedBytes, first.Tree.Images.Count, second.Cache.CountedBytes, second.Tree.Images.Count)
                );

                using var third = new ImageScene();
                Assert.Equal((800L, 800L), (ImageCache.MaximumBytes, third.Cache.MaximumBytes));
                Shown large = third.Show(Sized(20, 20));
                _ = Assert.Single(third.Draw());
                Assert.Equal((0, 1), (third.Cache.CountedCount, third.Tree.Images.Count));

                Scope.Dispose(large.Scope);
                Assert.Equal((0, 0), (third.Tree.Images.Count, third.Cache.Count));
            })
        );

    /// <summary>
    /// A negative limit throws <see cref="ArgumentOutOfRangeException"/> and changes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window with no loop that showed and took away two images; the default limits.
    ///
    /// Steps:
    /// - Set the count limit to -1; set the byte limit to -1.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentOutOfRangeException"/>; the limits are the defaults and the window still counts two
    ///   images.
    /// </remarks>
    [Fact]
    public void Limits_Negative_ThrowAndChangeNothing() =>
        ui.Invoke(() =>
            WithLimits(() =>
            {
                using var scene = new ImageScene();
                ShowAndLeave(scene, Sized(10, 10));
                ShowAndLeave(scene, Sized(10, 10));

                _ = Assert.Throws<ArgumentOutOfRangeException>(() => ImageCache.MaximumCount = -1);
                _ = Assert.Throws<ArgumentOutOfRangeException>(() => ImageCache.MaximumBytes = -1);

                Assert.Equal((1_000, 104_857_600L), (ImageCache.MaximumCount, ImageCache.MaximumBytes));
                Assert.Equal((1_000, 104_857_600L), (scene.Cache.MaximumCount, scene.Cache.MaximumBytes));
                Assert.Equal((2, 2), (scene.Cache.CountedCount, scene.Tree.Images.Count));
            })
        );

    /// <summary>
    /// <see cref="ImageCache.Evict"/> takes a source's images out of every open window, whatever their decode sizes: one that no
    /// element shows is released at once, one on screen stays until its last element goes, and an element made afterwards decodes
    /// the source again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two windows with no loop; the test decode; a source S of 40 x 20 and another source.
    /// - The first window showed and took away S with no decode size, S with the decode width 10, and the other source. The second
    ///   window shows S and keeps it; its frame is drawn and the handle kept.
    ///
    /// Steps:
    /// - Evict a source equal to S (another source of the same array). Draw the second window. Show S in the second window again;
    ///   then take away the element that showed S before.
    ///
    /// Expected:
    /// - The first window holds the other source's image only, and one entry.
    /// - The second window has no entry and counts nothing; its image is alive and drawn.
    /// - S again: a decode (five in all), and the window holds two images. The first element gone: the image it showed is dead and
    ///   the window holds one.
    /// </remarks>
    [Fact]
    public void Evict_TakesTheSourceOut_AtEverySizeInEveryWindow() =>
        ui.Invoke(() =>
            WithLimits(() =>
            {
                using var first = new ImageScene();
                using var second = new ImageScene();
                byte[] bytes = [40, 20];
                ShowAndLeave(first, ImageSource.FromBytes(bytes));
                ShowAndLeave(first, ImageSource.FromBytes(bytes), new ImageOptions { DecodeWidth = 10 });
                ShowAndLeave(first, Sized(10, 10));
                Shown shown = second.Show(ImageSource.FromBytes(bytes));
                ImageHandle image = Assert.Single(second.Draw()).Image;
                Assert.Equal((4, 3, 1), (Decodes, first.Tree.Images.Count, second.Tree.Images.Count));

                ImageCache.Evict(ImageSource.FromBytes(bytes));

                Assert.Equal((1, 1), (first.Tree.Images.Count, first.Cache.Count));
                Assert.Equal((0, 0, 1), (second.Cache.Count, second.Cache.CountedCount, second.Tree.Images.Count));
                Assert.True(second.Tree.Images.IsAlive(image));
                Assert.Equal(image, Assert.Single(second.Draw()).Image);

                _ = second.Show(ImageSource.FromBytes(bytes));
                Assert.Equal((5, 2), (Decodes, second.Tree.Images.Count));

                Scope.Dispose(shown.Scope);
                Assert.False(second.Tree.Images.IsAlive(image));
                Assert.Equal((1, 1), (second.Tree.Images.Count, second.Cache.Count));
            })
        );

    /// <summary>
    /// <see cref="ImageCache.Evict"/> takes a source's decode in flight out too: the elements that wait still show its result,
    /// which does not enter the cache, and a decode nobody waits for is dropped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose context keeps the posts; the test decode. A second window and the gated test decode for the last step.
    ///
    /// Steps:
    /// - Show an image of S. Evict S before its result is run. Wait and run the posts; draw. Show S again; wait and run the posts.
    ///   Take away the first element.
    /// - In the second window: show an image of T; wait until its decode started; take it away; evict T; open the gate; wait and run
    ///   the posts.
    ///
    /// Expected:
    /// - Evicted while decoding: no entry. After the result: the element draws the image; still no entry, nothing counted.
    /// - S again: a second decode asked for; after it, two images. The first element gone: one image, the one entry's.
    /// - T: no entry and no image.
    /// </remarks>
    [Fact]
    public void Evict_WhileDecoding_ShowsTheResultToThoseWaiting_AndKeepsNone() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            WithLimits(
                () =>
                {
                    using var scene = new ImageScene();
                    ImageSource source = Sized(10, 10);
                    Shown shown = scene.Show(source);
                    Assert.Equal(1, scene.Cache.Count);

                    ImageCache.Evict(source);
                    Assert.Equal(0, scene.Cache.Count);

                    posts.Settle();
                    _ = Assert.Single(scene.Draw());
                    Assert.Equal((0, 0, 1), (scene.Cache.Count, scene.Cache.CountedCount, scene.Tree.Images.Count));

                    _ = scene.Show(source);
                    Assert.Equal(2, scene.Cache.Decodes);
                    posts.Settle();
                    Assert.Equal((2, 2, 1), (Decodes, scene.Tree.Images.Count, scene.Cache.Count));

                    Scope.Dispose(shown.Scope);
                    Assert.Equal((1, 1), (scene.Tree.Images.Count, scene.Cache.Count));
                },
                posts
            );

            using var decode = new GatedDecode();
            WithLimits(
                () =>
                {
                    using var scene = new ImageScene();
                    ImageSource source = Sized(10, 10);
                    Shown shown = scene.Show(source);
                    decode.WaitForStarted(1);
                    Scope.Dispose(shown.Scope);
                    Assert.Equal(1, scene.Cache.Count);

                    ImageCache.Evict(source);
                    Assert.Equal(0, scene.Cache.Count);

                    decode.Open();
                    posts.Settle();
                    Assert.Equal((0, 0, 0), (scene.Cache.Count, scene.Tree.Images.Count, scene.Tree.Images.PendingUploads.Length));
                },
                posts,
                decode.Decode
            );
        });

    /// <summary>
    /// A decode that <see cref="ImageCache.Evict"/> took out while an element waited, and whose element then goes before the
    /// result, leaves nothing: the result does not enter the cache, and no element is left to show it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose context keeps the posts; the gated test decode; the default limits, which a 10 x 10 image fits.
    ///
    /// Steps:
    /// - Show an image of S; wait until its decode started. Evict S, then take the element away. Open the gate; wait and run the
    ///   posts. Show S again; wait and run the posts.
    ///
    /// Expected:
    /// - After the result: no entry, no image, and no upload waiting.
    /// - S again: a second decode, and one image, the one entry's.
    /// </remarks>
    [Fact]
    public void Evict_WhileDecoding_ThenTheElementGoes_KeepsNoImage() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            using var decode = new GatedDecode();
            WithLimits(
                () =>
                {
                    using var scene = new ImageScene();
                    ImageSource source = Sized(10, 10);
                    Shown shown = scene.Show(source);
                    decode.WaitForStarted(1);

                    ImageCache.Evict(source);
                    Scope.Dispose(shown.Scope);
                    Assert.Equal(0, scene.Cache.Count);

                    decode.Open();
                    posts.Settle();
                    Assert.Equal((0, 0, 0), (scene.Cache.Count, scene.Tree.Images.Count, scene.Tree.Images.PendingUploads.Length));

                    _ = scene.Show(source);
                    posts.Settle();
                    Assert.Equal((2, 1, 1), (scene.Cache.Decodes, scene.Tree.Images.Count, scene.Cache.Count));
                },
                posts,
                decode.Decode
            );
        });

    /// <summary>
    /// <see cref="ImageCache.Evict"/> throws <see cref="ArgumentNullException"/> for no source.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread.
    ///
    /// Steps:
    /// - Evict <see langword="null"/>.
    ///
    /// Expected:
    /// - <see cref="ArgumentNullException"/>.
    /// </remarks>
    [Fact]
    public void Evict_Null_Throws() => ui.Invoke(() => _ = Assert.Throws<ArgumentNullException>(() => ImageCache.Evict(null!)));

    /// <summary>
    /// <see cref="ImageCache.Clear"/> takes every image out of every open window, as <see cref="ImageCache.Evict"/> does for one
    /// source.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two windows with no loop; the test decode. The first showed and took away two images; the second showed and took away one
    ///   and keeps another on screen.
    ///
    /// Steps:
    /// - Clear. Draw the second window. Show the second window's kept source again.
    ///
    /// Expected:
    /// - Neither window has an entry; the first holds no image, the second the one on screen, which the frame draws.
    /// - The source again: a decode (five in all).
    /// </remarks>
    [Fact]
    public void Clear_TakesEveryImageOut_OfEveryWindow() =>
        ui.Invoke(() =>
            WithLimits(() =>
            {
                using var first = new ImageScene();
                using var second = new ImageScene();
                ImageSource kept = Sized(10, 10);
                ShowAndLeave(first, Sized(10, 10));
                ShowAndLeave(first, Sized(10, 10));
                ShowAndLeave(second, Sized(10, 10));
                _ = second.Show(kept);
                Assert.Equal((2, 2, 4), (first.Tree.Images.Count, second.Tree.Images.Count, Decodes));

                ImageCache.Clear();

                Assert.Equal((0, 0, 0, 1), (first.Cache.Count, first.Tree.Images.Count, second.Cache.Count, second.Tree.Images.Count));
                _ = Assert.Single(second.Draw());

                _ = second.Show(kept);
                Assert.Equal(5, Decodes);
            })
        );

    /// <summary>
    /// Each entry of <see cref="ImageCache"/> throws <see cref="InvalidOperationException"/> off the UI thread (design §2.12).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A UI thread exists; the test's own thread is another.
    ///
    /// Steps:
    /// - On the test's thread: read and set the count limit, read and set the byte limit, evict a source, and clear.
    ///
    /// Expected:
    /// - Each of the six throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void EachEntry_OffTheUiThread_Throws()
    {
        ui.Invoke(() => { });
        ImageSource source = Sized(10, 10);

        _ = Assert.Throws<InvalidOperationException>(() => _ = ImageCache.MaximumCount);
        _ = Assert.Throws<InvalidOperationException>(() => ImageCache.MaximumCount = 10);
        _ = Assert.Throws<InvalidOperationException>(() => _ = ImageCache.MaximumBytes);
        _ = Assert.Throws<InvalidOperationException>(() => ImageCache.MaximumBytes = 10);
        _ = Assert.Throws<InvalidOperationException>(() => ImageCache.Evict(source));
        _ = Assert.Throws<InvalidOperationException>(ImageCache.Clear);
    }

    // Runs `test` as WithDecode does, and puts the UI thread's limits back after it.
    private static void WithLimits(Action test, SynchronizationContext? context = null, Func<ImageSource, int, int, ImageResult>? decode = null)
    {
        int count = ImageCache.MaximumCount;
        long bytes = ImageCache.MaximumBytes;
        try
        {
            WithDecode(test, context, decode);
        }
        finally
        {
            ImageCache.MaximumCount = count;
            ImageCache.MaximumBytes = bytes;
        }
    }

    private static void ShowAndLeave(ImageScene scene, ImageSource source, ImageOptions? options = null) =>
        Scope.Dispose(scene.Show(source, options).Scope);
}
