using System.Numerics;
using static XylemUI.Elements;
using static XylemUI.Tests.ImageDoubles;

namespace XylemUI.Tests;

// The image cache of a window (design §5.0.4, "Cache"; row IM1, 3a): the key, the three sets, the rules that move an entry between
// them, the two limits, and the tree thrown away. The tests show image elements in a window with no GPU and read the cache's
// counts; a test decode stands in for the decoders, and an image of 10 x 10 counts for 400 bytes.
[Collection(UiThreadGroup.Name)]
public sealed class WindowImageCacheTests(UiThreadFixture ui)
{
    // A byte limit no test reaches.
    private const long Plenty = 1L << 30;

    /// <summary>
    /// Two elements that show the same source at the same decode size share one decode and one image.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window with no loop, so a decode runs where the element asks for it; the test decode, counting its calls.
    ///
    /// Steps:
    /// - Show two images whose sources are equal (two sources of the same array). Draw.
    ///
    /// Expected:
    /// - One decode; the cache asked for one decode and found the key once; the window holds one image, the cache one entry; the
    ///   frame draws two images with the same handle.
    /// </remarks>
    [Fact]
    public void SameKey_TwoElements_ShareOneDecodeAndOneImage() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                byte[] bytes = [40, 20];

                _ = scene.Show(ImageSource.FromBytes(bytes));
                _ = scene.Show(ImageSource.FromBytes(bytes));
                List<ImageCommand> images = scene.Draw();

                Assert.Equal((1, 1, 1), (Decodes, scene.Cache.Decodes, scene.Cache.Hits));
                Assert.Equal((1, 1), (scene.Tree.Images.Count, scene.Cache.Count));
                Assert.Equal(2, images.Count);
                Assert.Equal(images[0].Image, images[1].Image);
            })
        );

    /// <summary>
    /// An element that asks for a key whose decode is in flight shares that decode: both show their loading parts, and the one
    /// result reaches both.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose context keeps what is posted to it; the test decode; a loading part that counts how often it is made.
    ///
    /// Steps:
    /// - Show two images whose sources are equal. Wait until no decode is in flight and run what was posted. Draw.
    ///
    /// Expected:
    /// - Before the result: two loading parts, one decode asked for, the key found once, no image.
    /// - After: one decode ran; neither element has a child; the frame draws two images with the same handle.
    /// </remarks>
    [Fact]
    public void SameKey_WhileDecoding_SharesTheDecode() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    byte[] bytes = [40, 20];
                    int loading = 0;
                    var options = new ImageOptions { Loading = () => Counted(ref loading) };

                    Shown first = scene.Show(ImageSource.FromBytes(bytes), options);
                    Shown second = scene.Show(ImageSource.FromBytes(bytes), options);
                    Assert.Equal((2, 1, 1, 0), (loading, scene.Cache.Decodes, scene.Cache.Hits, scene.Tree.Images.Count));

                    posts.Settle();
                    List<ImageCommand> images = scene.Draw();

                    Assert.Equal((1, 1), (Decodes, scene.Tree.Images.Count));
                    Assert.True(scene.Tree.Nodes.GetRef(first.Element.Node).FirstChild.IsNone);
                    Assert.True(scene.Tree.Nodes.GetRef(second.Element.Node).FirstChild.IsNone);
                    Assert.Equal(2, images.Count);
                    Assert.Equal(images[0].Image, images[1].Image);
                },
                posts
            );
        });

    /// <summary>
    /// The key is the source, the decode width, and the decode height: a difference in any one is a decode of its own, and a decode
    /// width larger than the image is its own key too.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode; a source of 40 x 20.
    ///
    /// Steps:
    /// - Show and take away an image of the source with: no decode size; width 10; height 10; width 10 and height 10; width 80. The
    ///   same of another source of 40 x 20 with no decode size. Then all six again.
    ///
    /// Expected:
    /// - Six decodes and six entries after the first round, none found; the second round decodes nothing and finds six.
    /// </remarks>
    [Fact]
    public void Key_IsTheSourceTheWidthAndTheHeight() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                byte[] bytes = [40, 20];
                byte[] other = [40, 20];
                ImageOptions[] sizes =
                [
                    new(),
                    new() { DecodeWidth = 10 },
                    new() { DecodeHeight = 10 },
                    new() { DecodeWidth = 10, DecodeHeight = 10 },
                    new() { DecodeWidth = 80 },
                ];

                void Round()
                {
                    foreach (ImageOptions size in sizes)
                    {
                        ShowAndLeave(scene, ImageSource.FromBytes(bytes), size);
                    }

                    ShowAndLeave(scene, ImageSource.FromBytes(other));
                }

                Round();
                Assert.Equal((6, 6, 6, 0), (Decodes, scene.Cache.Count, scene.Tree.Images.Count, scene.Cache.Hits));

                Round();
                Assert.Equal((6, 6, 6, 6), (Decodes, scene.Cache.Count, scene.Tree.Images.Count, scene.Cache.Hits));
            })
        );

    /// <summary>
    /// Two keys are equal when the source, the width, and the height are, whatever their hashes: the index compares the hashes
    /// first, so two keys that differ in one value meet this comparison only when their hashes collide.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two sources of one array, which are equal, and a source of another array.
    ///
    /// Steps:
    /// - Compare the key of the first source at 10 x 20 with the keys of: the equal source at 10 x 20, at 11 x 20, and at 10 x 21,
    ///   and the other source at 10 x 20.
    ///
    /// Expected:
    /// - Equal to the first, with the same hash; not equal to the other three.
    /// </remarks>
    [Fact]
    public void Key_Equals_ComparesTheSourceTheWidthAndTheHeight()
    {
        byte[] bytes = [40, 20];
        byte[] other = [40, 20];
        var key = new ImageKey(ImageSource.FromBytes(bytes), 10, 20);
        var same = new ImageKey(ImageSource.FromBytes(bytes), 10, 20);

        Assert.True(key.Equals(same));
        Assert.Equal(key.GetHashCode(), same.GetHashCode());
        Assert.False(key.Equals(new ImageKey(ImageSource.FromBytes(bytes), 11, 20)));
        Assert.False(key.Equals(new ImageKey(ImageSource.FromBytes(bytes), 10, 21)));
        Assert.False(key.Equals(new ImageKey(ImageSource.FromBytes(other), 10, 20)));
    }

    /// <summary>
    /// The counted set is in the order of use: an element that asks for a counted image makes it the newest, and the limit takes
    /// the oldest out first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode; a count limit of 3.
    ///
    /// Steps:
    /// - Show and take away images of the sources A, B, C, in that order; A again; then D. Then show C, A, and B.
    ///
    /// Expected:
    /// - After D: four decodes, one image taken out, three entries. C and A are found (no decode): B, the least recently used, was
    ///   the one taken out, and showing it decodes again.
    /// </remarks>
    [Fact]
    public void CountedSet_OverTheLimit_LosesTheLeastRecentlyUsed() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                scene.Cache.Limit(3, Plenty);
                ImageSource a = Sized(10, 10);
                ImageSource b = Sized(10, 10);
                ImageSource c = Sized(10, 10);

                ShowAndLeave(scene, a);
                ShowAndLeave(scene, b);
                ShowAndLeave(scene, c);
                ShowAndLeave(scene, a);
                ShowAndLeave(scene, Sized(10, 10));
                Assert.Equal((4, 1, 3, 3), (Decodes, scene.Cache.EvictedCount, scene.Cache.Count, scene.Tree.Images.Count));

                _ = scene.Show(c);
                _ = scene.Show(a);
                Assert.Equal(4, Decodes);

                _ = scene.Show(b);
                Assert.Equal(5, Decodes);
            })
        );

    /// <summary>
    /// After each decode the counted set holds at most the count limit and at most the byte limit, the images on screen among
    /// them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode; images of 10 x 10 (400 bytes each).
    ///
    /// Steps:
    /// - With a byte limit of 1,000 and a count limit of 1,000: show and take away five images, reading the counts after each.
    /// - With a count limit of 2 and no byte limit in reach: show three more images and keep them.
    ///
    /// Expected:
    /// - Bytes: within the limits after each; at the end two entries of 800 bytes, three taken out, two images in the window.
    /// - Count: two counted of the three shown; the window holds three images and the index three entries (the one taken out while
    ///   an element shows it lives on).
    /// </remarks>
    [Fact]
    public void CountedSet_AfterEachDecode_IsWithinBothLimits() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                scene.Cache.Limit(1_000, 1_000);
                for (int i = 0; i < 5; i++)
                {
                    ShowAndLeave(scene, Sized(10, 10));
                    Assert.InRange(scene.Cache.CountedBytes, 0, 1_000);
                    Assert.InRange(scene.Cache.CountedCount, 0, 1_000);
                }

                Assert.Equal(
                    (2, 800L, 3, 2),
                    (scene.Cache.CountedCount, scene.Cache.CountedBytes, scene.Cache.EvictedCount, scene.Tree.Images.Count)
                );

                scene.Cache.Clear();
                scene.Cache.Limit(2, Plenty);
                for (int i = 0; i < 3; i++)
                {
                    _ = scene.Show(Sized(10, 10));
                    Assert.InRange(scene.Cache.CountedCount, 0, 2);
                }

                Assert.Equal((2, 3, 3), (scene.Cache.CountedCount, scene.Tree.Images.Count, scene.Cache.Count));
            })
        );

    /// <summary>
    /// An image the limit takes out of the counted set while an element shows it stays alive and is still found by its key; an
    /// element that asks for it puts it through the check again; it is released when its last element goes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode; a count limit of 2.
    ///
    /// Steps:
    /// - Show images of A, B, C and keep them; draw, and keep the handles. Show a second image of A. Take away both images of A;
    ///   then the image of B. Show B again.
    ///
    /// Expected:
    /// - After C: A is out of the counted set (one taken out) and its handle is alive.
    /// - The second image of A decodes nothing; A is counted again and B is taken out in its turn (two taken out), alive.
    /// - A's elements gone: A stays (counted). B's element gone: B's handle is dead, and the window and the index hold two.
    /// - B again: a new decode.
    /// </remarks>
    [Fact]
    public void TakenOutWhileShown_StaysAlive_AndIsFoundByItsKey() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                scene.Cache.Limit(2, Plenty);
                ImageSource a = Sized(10, 10);
                ImageSource b = Sized(10, 10);
                Shown first = scene.Show(a);
                Shown second = scene.Show(b);
                _ = scene.Show(Sized(10, 10));
                List<ImageCommand> drawn = scene.Draw();
                ImageHandle imageOfA = drawn[0].Image;
                ImageHandle imageOfB = drawn[1].Image;
                Assert.Equal((1, 2, 3), (scene.Cache.EvictedCount, scene.Cache.CountedCount, scene.Cache.Count));
                Assert.True(scene.Tree.Images.IsAlive(imageOfA));

                Shown again = scene.Show(a);
                Assert.Equal((3, 1, 2, 2), (Decodes, scene.Cache.Hits, scene.Cache.EvictedCount, scene.Cache.CountedCount));
                Assert.True(scene.Tree.Images.IsAlive(imageOfB));

                Scope.Dispose(first.Scope);
                Scope.Dispose(again.Scope);
                Assert.True(scene.Tree.Images.IsAlive(imageOfA));
                Assert.Equal(3, scene.Tree.Images.Count);

                Scope.Dispose(second.Scope);
                Assert.False(scene.Tree.Images.IsAlive(imageOfB));
                Assert.Equal((2, 2), (scene.Tree.Images.Count, scene.Cache.Count));

                _ = scene.Show(b);
                Assert.Equal(4, Decodes);
            })
        );

    /// <summary>
    /// When its last element goes, an image in the counted set stays for the next element, and one outside it is released.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode; a count limit of 1.
    ///
    /// Steps:
    /// - Show images of A, then B, and keep them (B takes A out of the counted set). Take away B's element; then A's. Show B again;
    ///   show A again.
    ///
    /// Expected:
    /// - B's element gone: the window still holds two images. A's element gone: one image and one entry.
    /// - B again: no decode. A again: a decode.
    /// </remarks>
    [Fact]
    public void LastElementGoes_CountedStays_UncountedIsReleased() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                scene.Cache.Limit(1, Plenty);
                ImageSource a = Sized(10, 10);
                ImageSource b = Sized(10, 10);
                Shown first = scene.Show(a);
                Shown second = scene.Show(b);

                Scope.Dispose(second.Scope);
                Assert.Equal((2, 2), (scene.Tree.Images.Count, scene.Cache.Count));

                Scope.Dispose(first.Scope);
                Assert.Equal((1, 1, 1), (scene.Tree.Images.Count, scene.Cache.Count, scene.Cache.CountedCount));

                _ = scene.Show(b);
                Assert.Equal(2, Decodes);

                _ = scene.Show(a);
                Assert.Equal(3, Decodes);
            })
        );

    /// <summary>
    /// An image larger than the byte limit is shown without entering the counted set: it takes no other image out, and it is
    /// released when its element goes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode; a byte limit of 500; an image of 10 x 10 (400 bytes) in the cache that no
    ///   element shows.
    ///
    /// Steps:
    /// - Show an image of 20 x 10 (800 bytes); draw. Take it away. Show the small image again.
    ///
    /// Expected:
    /// - Shown: the frame draws it at 20 x 10; the counted set still holds the small image alone (400 bytes); none taken out, one
    ///   not taken in; the window holds two images.
    /// - Taken away: one image, one entry. The small image is found: no decode.
    /// </remarks>
    [Fact]
    public void LargerThanTheByteLimit_IsShown_NotCounted_AndTakesNoneOut() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                scene.Cache.Limit(1_000, 500);
                ImageSource small = Sized(10, 10);
                ShowAndLeave(scene, small);

                Shown large = scene.Show(Sized(20, 10));
                ImageCommand drawn = Assert.Single(scene.Draw());

                Assert.Equal((20f, 10f), (drawn.Width, drawn.Height));
                Assert.Equal(
                    (1, 400L, 0, 1),
                    (scene.Cache.CountedCount, scene.Cache.CountedBytes, scene.Cache.EvictedCount, scene.Cache.RefusedCount)
                );
                Assert.Equal(2, scene.Tree.Images.Count);

                Scope.Dispose(large.Scope);
                Assert.Equal((1, 1), (scene.Tree.Images.Count, scene.Cache.Count));

                _ = scene.Show(small);
                Assert.Equal(2, Decodes);
            })
        );

    /// <summary>
    /// With a count limit of 0, an image is shown and no image stays that no element shows.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode; a count limit of 0.
    ///
    /// Steps:
    /// - Show an image; draw. Take it away. Show it again.
    ///
    /// Expected:
    /// - Shown: the frame draws it; the counted set is empty. Taken away: no image, no entry. Again: a second decode.
    /// </remarks>
    [Fact]
    public void CountLimitOfZero_KeepsNoImageThatNoElementShows() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                scene.Cache.Limit(0, Plenty);
                ImageSource source = Sized(10, 10);

                Shown shown = scene.Show(source);
                _ = Assert.Single(scene.Draw());
                Assert.Equal((0, 1), (scene.Cache.CountedCount, scene.Tree.Images.Count));

                Scope.Dispose(shown.Scope);
                Assert.Equal((0, 0), (scene.Tree.Images.Count, scene.Cache.Count));

                _ = scene.Show(source);
                Assert.Equal(2, Decodes);
            })
        );

    /// <summary>
    /// A decode that fails leaves no entry: the elements that waited show their error parts, and an element that asks later
    /// decodes again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test decode, which cannot read a source of no bytes; an error part that counts how often it is made.
    ///
    /// Steps:
    /// - In a window with no loop: show an image of the source that cannot be read, twice.
    /// - In a window whose context keeps the posts: show two images of the source; wait and run the posts; show a third; wait and
    ///   run the posts.
    ///
    /// Expected:
    /// - No loop: two decodes, two error parts, no entry and no image.
    /// - Posts: the two elements share one decode, and each shows its error part; the third decodes again; no entry and no image.
    /// </remarks>
    [Fact]
    public void DecodeFails_LeavesNoEntry_AndIsDecodedAgain() =>
        ui.Invoke(() =>
        {
            int errors = 0;
            var options = new ImageOptions { Error = () => Counted(ref errors) };
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                _ = scene.Show(Broken, options);
                _ = scene.Show(Broken, options);

                Assert.Equal((2, 2, 0, 0), (Decodes, errors, scene.Cache.Count, scene.Tree.Images.Count));
            });

            errors = 0;
            var posts = new KeepingContext();
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    _ = scene.Show(Broken, options);
                    _ = scene.Show(Broken, options);
                    Assert.Equal((1, 0), (scene.Cache.Count, errors));

                    posts.Settle();
                    Assert.Equal((1, 2, 0, 0), (Decodes, errors, scene.Cache.Count, scene.Tree.Images.Count));

                    _ = scene.Show(Broken, options);
                    posts.Settle();
                    Assert.Equal((2, 3, 0), (Decodes, errors, scene.Cache.Count));
                },
                posts
            );
        });

    /// <summary>
    /// A decode whose last element goes before it starts is skipped, and its entry goes with it; one that runs by then ends as an
    /// image of the cache.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose context keeps the posts; a test decode that waits behind a gate, so that no decode ends before the gate
    ///   opens and the decodes past the ones the queue runs at once wait in it.
    ///
    /// Steps:
    /// - Show one image more than the queue runs at once, each of a source of its own, and wait until a decode started. Take all
    ///   the images away. Open the gate; wait and run the posts. Show every source again.
    ///
    /// Expected:
    /// - Each is an entry while its element waits. With the elements gone, the entries are those of the decodes that run: at most
    ///   as many as the queue runs at once, so at least one decode was skipped.
    /// - At the end the decodes that ran are the entries, all counted, and the images; the test decode was asked for no other.
    /// - Shown again, the sources that were decoded are found, and each skipped one is asked for a decode.
    /// </remarks>
    [Fact]
    public void DecodeNotStarted_WithNoElement_IsSkipped() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            using var decode = new GatedDecode();
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    int count = ImageDecodeQueue.Limit + 1;
                    var sources = new ImageSource[count];
                    var shown = new Shown[count];
                    for (int i = 0; i < count; i++)
                    {
                        sources[i] = Sized(10, 10);
                        shown[i] = scene.Show(sources[i]);
                    }

                    decode.WaitForStarted(1);
                    Assert.Equal(count, scene.Cache.Count);

                    foreach (Shown one in shown)
                    {
                        Scope.Dispose(one.Scope);
                    }

                    Assert.InRange(scene.Cache.Count, 1, ImageDecodeQueue.Limit);

                    decode.Open();
                    posts.Settle();
                    int ran = decode.Seen.Count;
                    Assert.InRange(ran, 1, ImageDecodeQueue.Limit);
                    Assert.Equal((ran, ran, ran, ran), (Decodes, scene.Cache.Count, scene.Cache.CountedCount, scene.Tree.Images.Count));

                    foreach (ImageSource source in sources)
                    {
                        _ = scene.Show(source);
                    }

                    Assert.Equal((ran, count + (count - ran)), (scene.Cache.Hits, scene.Cache.Decodes));
                    posts.Settle();
                },
                posts,
                decode.Decode
            );
        });

    /// <summary>
    /// A decode whose last element goes while it runs ends as an image that no element shows: counted when the limits take it,
    /// and dropped without becoming an image when they do not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose context keeps the posts; the gated test decode.
    ///
    /// Steps:
    /// - Show an image; wait until its decode started; take it away; open the gate; wait and run the posts. Show the source again.
    /// - The same in a second window with a count limit of 0, without the last step.
    ///
    /// Expected:
    /// - Default limits: the entry stays while the decode runs; after, one entry, counted, and one image; the source is found: no
    ///   second decode.
    /// - Count limit of 0: no entry, no image, nothing queued for upload; one image not taken in.
    /// </remarks>
    [Fact]
    public void DecodeRunning_WithNoElement_EndsAsAnImageNoElementShows() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            using var decode = new GatedDecode();
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    ImageSource source = Sized(10, 10);
                    Shown shown = scene.Show(source);
                    decode.WaitForStarted(1);
                    Scope.Dispose(shown.Scope);
                    Assert.Equal(1, scene.Cache.Count);

                    decode.Open();
                    posts.Settle();
                    Assert.Equal((1, 1, 1), (scene.Cache.Count, scene.Cache.CountedCount, scene.Tree.Images.Count));

                    _ = scene.Show(source);
                    Assert.Equal((1, 1), (Decodes, scene.Cache.Hits));
                },
                posts,
                decode.Decode
            );

            using var second = new GatedDecode();
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Cache.Limit(0, Plenty);
                    Shown shown = scene.Show(Sized(10, 10));
                    second.WaitForStarted(1);
                    Scope.Dispose(shown.Scope);

                    second.Open();
                    posts.Settle();
                    Assert.Equal(
                        (0, 0, 0, 1),
                        (scene.Cache.Count, scene.Tree.Images.Count, scene.Tree.Images.PendingUploads.Length, scene.Cache.RefusedCount)
                    );
                },
                posts,
                second.Decode
            );
        });

    /// <summary>
    /// A source the cache holds is shown at once, with no loading part in between: when an element's source changes to it, and
    /// when a new element asks for it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose context keeps the posts; the test decode; a loading part that counts how often it is made; a signal of a
    ///   source A of 40 x 20.
    ///
    /// Steps:
    /// - Show an image of the signal's source; wait and run the posts. Set the signal to B of 10 x 10; draw; wait and run the
    ///   posts. Set the signal to A; draw, running no post. Show a second image of B; draw.
    ///
    /// Expected:
    /// - A, then B: a loading part each (two).
    /// - Back to A: still two loading parts, and the frame draws the image at 40 x 20 with nothing posted.
    /// - The second image of B: still two loading parts, and the frame draws two images; two decodes in all.
    /// </remarks>
    [Fact]
    public void CachedSource_IsShown_WithoutTheLoadingPart() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    ImageSource a = Sized(40, 20);
                    ImageSource b = Sized(10, 10);
                    int loading = 0;
                    var options = new ImageOptions { Loading = () => Counted(ref loading) };
                    Signal<ImageSource> source = Signal.Create(a);

                    _ = scene.Show(() => Image(() => source.Get(), options));
                    posts.Settle();
                    source.Set(b);
                    _ = scene.Draw();
                    Assert.Equal(2, loading);
                    posts.Settle();

                    source.Set(a);
                    ImageCommand drawn = Assert.Single(scene.Draw());
                    Assert.Equal((2, 40f, 20f), (loading, drawn.Width, drawn.Height));

                    _ = scene.Show(b, options);
                    Assert.Equal((2, 2, 2), (loading, scene.Draw().Count, Decodes));
                },
                posts
            );
        });

    /// <summary>
    /// The tree thrown away releases every image of its cache, whatever set the entry is in, and a decode's result that arrives
    /// afterwards adds none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose context keeps the posts; the test decode. The cache holds: an image no element shows; an image an element
    ///   shows; an image an element shows that Evict took out of the index; and an entry whose decode ran and whose result is
    ///   posted and not run yet.
    ///
    /// Steps:
    /// - Let every entry go, as the host does when its window closes. Run the posts. Let every entry go again; dispose the
    ///   elements.
    ///
    /// Expected:
    /// - Before: three images, three entries in the index.
    /// - After: no image, no entry, nothing counted. The posted result adds no image. The second call and the elements' disposal
    ///   throw nothing and change nothing.
    /// </remarks>
    [Fact]
    public void TreeThrownAway_ReleasesEveryImage_AndDropsLateResults() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    ImageSource outside = Sized(10, 10);
                    Shown unshown = scene.Show(Sized(10, 10));
                    Shown shown = scene.Show(Sized(10, 10));
                    Shown evicted = scene.Show(outside);
                    posts.Settle();
                    Scope.Dispose(unshown.Scope);
                    scene.Cache.Evict(outside);
                    Shown pending = scene.Show(Sized(10, 10));
                    Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));
                    Assert.Equal((3, 3), (scene.Tree.Images.Count, scene.Cache.Count));

                    scene.Cache.ReleaseAll();
                    Assert.Equal((0, 0, 0, 0L), (scene.Tree.Images.Count, scene.Cache.Count, scene.Cache.CountedCount, scene.Cache.CountedBytes));

                    posts.RunAll();
                    Assert.Equal(0, scene.Tree.Images.Count);

                    scene.Cache.ReleaseAll();
                    Scope.Dispose(shown.Scope);
                    Scope.Dispose(evicted.Scope);
                    Scope.Dispose(pending.Scope);
                    Assert.Equal((0, 0), (scene.Tree.Images.Count, scene.Cache.Count));
                },
                posts
            );
        });

    /// <summary>
    /// The elements that waited for a decode when the tree let every entry go wait for nothing afterwards: when they then ask for
    /// other sources, each decode's result reaches every element that waits for it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window whose context keeps the posts; the test decode; a loading part for each element. Two image elements whose
    ///   sources are signals, both of the source X: the second waits behind the first for X's decode. The decode ran, and its
    ///   result is posted and not run.
    ///
    /// Steps:
    /// - Let every entry go. Set the first element's source to Y of 40 x 20; draw; wait until no decode is in flight, so that Y's
    ///   result is posted before Z's. Set the second element's source to Z of 20 x 10; draw; show a third image of Z, which waits
    ///   behind the second. Wait and run the posts. Draw.
    ///
    /// Expected:
    /// - No element has a child. The frame draws three images: the first element's at 40 x 20, and the second's and the third's
    ///   at 20 x 10 with the same handle.
    /// - Three decodes ran, and the window holds two images: X's result added none.
    /// </remarks>
    [Fact]
    public void TreeThrownAway_WaitingElementsThatAskAgain_EachGetTheirOwnResult() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    ImageSource x = Sized(10, 10);
                    ImageSource z = Sized(20, 10);
                    int loading = 0;
                    var options = new ImageOptions { Loading = () => Counted(ref loading) };
                    Signal<ImageSource> firstSource = Signal.Create(x);
                    Signal<ImageSource> secondSource = Signal.Create(x);
                    Shown first = scene.Show(() => Image(() => firstSource.Get(), options));
                    Shown second = scene.Show(() => Image(() => secondSource.Get(), options));
                    Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));

                    scene.Cache.ReleaseAll();
                    firstSource.Set(Sized(40, 20));
                    _ = scene.Draw();
                    Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));
                    secondSource.Set(z);
                    _ = scene.Draw();
                    Shown third = scene.Show(z, options);
                    posts.Settle();
                    List<ImageCommand> images = scene.Draw();

                    Assert.True(scene.Tree.Nodes.GetRef(first.Element.Node).FirstChild.IsNone);
                    Assert.True(scene.Tree.Nodes.GetRef(second.Element.Node).FirstChild.IsNone);
                    Assert.True(scene.Tree.Nodes.GetRef(third.Element.Node).FirstChild.IsNone);
                    Assert.Equal(3, images.Count);
                    Assert.Equal((40f, 20f), (images[0].Width, images[0].Height));
                    Assert.Equal((20f, 10f, 20f, 10f), (images[1].Width, images[1].Height, images[2].Width, images[2].Height));
                    Assert.Equal(images[1].Image, images[2].Image);
                    Assert.Equal((3, 2), (Decodes, scene.Tree.Images.Count));
                },
                posts
            );
        });

    /// <summary>
    /// Finding a key, moving its entry to the newest end, and an element leaving an entry allocate nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode; one image element whose source is a signal; the sources A and B both in the
    ///   cache; warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: set the signal to B, the frame, set it to A,
    ///   the frame.
    ///
    /// Expected:
    /// - 0 bytes in every window; each iteration found a key twice; two decodes in all.
    /// </remarks>
    [Fact]
    public void SteadyState_SwitchingBetweenCachedSources_AllocatesNothing() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();
                ImageSource a = Sized(40, 20);
                ImageSource b = Sized(10, 10);
                Signal<ImageSource> source = Signal.Create(a);
                scene.Mount(() => Image(() => source.Get()));

                void Iteration(int index)
                {
                    source.Set(b);
                    scene.Frames.RunFrame();
                    source.Set(a);
                    scene.Frames.RunFrame();
                }

                for (int i = 0; i < 20; i++)
                {
                    Iteration(i);
                }

                int hits = scene.Cache.Hits;
                int runs = PerformanceAssert.DoesNotAllocate(1_000, Iteration);

                Assert.Equal(hits + (2 * runs), scene.Cache.Hits);
                Assert.Equal((2, 2), (Decodes, scene.Cache.CountedCount));
            })
        );

    /// <summary>
    /// A limit that takes an image out of the counted set and releases it allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window with no loop; the test decode. Before each run, not measured: an image shown and taken away, which the cache
    ///   keeps, and the window's queues of uploads and of released images emptied as a frame's renderer does.
    ///
    /// Steps:
    /// - 1,000 runs in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: set the count limit to 0, then back to 1,000.
    ///
    /// Expected:
    /// - 0 bytes in every window; each run took one image out; the window holds no image at the end.
    /// </remarks>
    [Fact]
    public void SteadyState_TheLimitTakesAnImageOut_AllocatesNothing() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene();

                void Prepare(int index)
                {
                    ShowAndLeave(scene, Sized(10, 10));
                    scene.Tree.Images.TakeUploads();
                    scene.Tree.Images.TakeReleased();
                }

                void Iteration(int index)
                {
                    scene.Cache.Limit(0, Plenty);
                    scene.Cache.Limit(1_000, Plenty);
                }

                int evicted = scene.Cache.EvictedCount;
                int runs = PerformanceAssert.DoesNotAllocate(1_000, Prepare, Iteration);

                Assert.Equal(evicted + runs, scene.Cache.EvictedCount);
                Assert.Equal((0, 0), (scene.Tree.Images.Count, scene.Cache.Count));
            })
        );

    /// <summary>
    /// The scene of 100 images (design §11.3) scrolls with no allocation in the steady state under the default limits
    /// (design §5.0.4, "Limits").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 200; a scroll view of 200 x 150 holding ten rows of ten images of 16 x 16, each of a source of its own:
    ///   content 160 high, so every image is at least partly in the viewport at each offset. The limits <c>ImageCache</c> gives a
    ///   new window. The test decode. Warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>: a wheel of (0, 50), the frame, a wheel of
    ///   (0, −50), the frame.
    ///
    /// Expected:
    /// - The limits are 1,000 images and 104,857,600 bytes.
    /// - 0 bytes in every window; each iteration presented twice; the last frame draws 100 images.
    /// - 100 decodes in all, 100 images counted, none taken out.
    /// </remarks>
    [Fact]
    public void SteadyState_ScrollingAHundredImages_AllocatesNothing() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new ImageScene(200);
                scene.Mount(() => ScrollView(Column(Rows(10, 10))).Width(200).Height(150));
                Assert.Equal((1_000, 104_857_600L), (scene.Cache.MaximumCount, scene.Cache.MaximumBytes));

                void Iteration(int index)
                {
                    scene.Wheel(10, 10, new Vector2(0, 50));
                    scene.Scheduler.Run();
                    scene.Wheel(10, 10, new Vector2(0, -50));
                    scene.Scheduler.Run();
                }

                for (int i = 0; i < 20; i++)
                {
                    Iteration(i);
                }

                int presents = scene.Target.Presents;
                int runs = PerformanceAssert.DoesNotAllocate(1_000, Iteration);

                Assert.Equal(presents + (2 * runs), scene.Target.Presents);
                Assert.Equal(100, Images(scene.Frames.List).Count);
                Assert.Equal((100, 100, 0, 100), (Decodes, scene.Cache.CountedCount, scene.Cache.EvictedCount, scene.Tree.Images.Count));
            })
        );

    // Shows an image and takes it away: with room in the limits, the cache keeps it as an image no element shows.
    private static void ShowAndLeave(ImageScene scene, ImageSource source, ImageOptions? options = null) =>
        Scope.Dispose(scene.Show(source, options).Scope);

    // A part of 15 x 5 that counts how often it is made.
    private static Element Counted(ref int count)
    {
        count++;
        return Box().Width(15).Height(5);
    }

    private static Element[] Rows(int rows, int columns)
    {
        var made = new Element[rows];
        for (int row = 0; row < rows; row++)
        {
            var images = new Element[columns];
            for (int column = 0; column < columns; column++)
            {
                images[column] = Image(Sized(16, 16));
            }

            made[row] = Row(images);
        }

        return made;
    }
}
