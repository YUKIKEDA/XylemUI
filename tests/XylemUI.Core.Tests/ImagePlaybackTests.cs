using static XylemUI.Elements;
using static XylemUI.Tests.ImageDoubles;

namespace XylemUI.Tests;

// The playing of animated images in a window's image cache (design §5.0.4, "Animations", row IM1's 3c): how a frame follows
// another, when the playing stops and goes on, what ends it, what it counts for, and when the decoder goes. The frames are a
// stand-in's (FakeFrames): frame i is filled with the byte i, which the target records when it takes the upload.
[Collection(UiThreadGroup.Name)]
public sealed class ImagePlaybackTests(UiThreadFixture ui)
{
    /// <summary>
    /// A frame shows for its duration from the frame of the window that showed it, and the next shows in the first frame of the
    /// window after both its timer rang and its pixels arrived ("How it advances"); time that passed meanwhile is not caught up.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 3 frames of 100, 50, and 200 ms with no end, shown by one element; a target that takes the uploads; a
    ///   test clock.
    ///
    /// Steps:
    /// - Draw. Let the worker's decode arrive, advance the clock by 99 ms, and draw; advance by 1 ms and draw.
    /// - Advance by 1 s with the decode arrived, and draw; advance by 49 ms and draw; by 1 ms and draw.
    /// - Advance by 200 ms before the decode's result is run; draw; run the result; draw.
    ///
    /// Expected:
    /// - The first draw uploads frame 0. At 99 ms nothing is uploaded and no frame of the window is asked for; at 100 ms frame 1.
    ///   After 1 s only frame 2 is not skipped to: frame 1 shows until 50 ms after its own frame, so the draw after 1 s uploads
    ///   frame 2 (its timer of 50 ms rang) and one frame only; 49 ms later nothing; at 200 ms... frame 0 of the next play only
    ///   once its pixels arrived: the draw before the result uploads nothing, the one after it frame 0.
    /// </remarks>
    [Fact]
    public void Frame_FollowsAnother_WhenItsTimerRangAndItsPixelsArrived() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var frames = new FakeFrames(0, 100, 50, 200);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Target.TakesUploads = true;
                    _ = scene.Show(Sized(2, 2));
                    posts.Settle();
                    _ = scene.Draw();
                    Assert.Equal([0], scene.Target.Uploads);

                    posts.Settle();
                    scene.Time.Advance(TimeSpan.FromMilliseconds(99));
                    scene.Scheduler.Requests = 0;
                    _ = scene.Draw();
                    Assert.Equal([0], scene.Target.Uploads);
                    Assert.Equal(0, scene.Scheduler.Requests);
                    scene.Time.Advance(TimeSpan.FromMilliseconds(1));
                    Assert.Equal(1, scene.Scheduler.Requests);
                    _ = scene.Draw();
                    Assert.Equal([0, 1], scene.Target.Uploads);

                    posts.Settle();
                    scene.Time.Advance(TimeSpan.FromSeconds(1));
                    _ = scene.Draw();
                    Assert.Equal([0, 1, 2], scene.Target.Uploads);
                    posts.Settle();
                    scene.Time.Advance(TimeSpan.FromMilliseconds(199));
                    _ = scene.Draw();
                    Assert.Equal([0, 1, 2], scene.Target.Uploads);

                    Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));
                    scene.Time.Advance(TimeSpan.FromMilliseconds(1));
                    _ = scene.Draw();
                    Assert.Equal([0, 1, 2, 0], scene.Target.Uploads);
                    posts.Settle();
                    Assert.Equal(4, frames.Calls);
                },
                posts,
                (_, _, _) => frames.First()
            );
        });

    /// <summary>
    /// A frame whose timer rang does not show before its pixels arrived.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above, with frames of 100 ms.
    ///
    /// Steps:
    /// - Draw. Advance the clock by 100 ms before the decode's result is run, and draw. Run the result and draw.
    ///
    /// Expected:
    /// - The draw before the result uploads nothing more, and the one after it frame 1.
    /// </remarks>
    [Fact]
    public void Frame_WhoseTimerRang_WaitsForItsPixels() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var frames = new FakeFrames(0, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Target.TakesUploads = true;
                    _ = scene.Show(Sized(2, 2));
                    posts.Settle();
                    _ = scene.Draw();

                    Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));
                    scene.Time.Advance(TimeSpan.FromMilliseconds(100));
                    _ = scene.Draw();
                    Assert.Equal([0], scene.Target.Uploads);

                    posts.RunAll();
                    _ = scene.Draw();
                    Assert.Equal([0, 1], scene.Target.Uploads);
                },
                posts,
                (_, _, _) => frames.First()
            );
        });

    /// <summary>
    /// Elements of the same key share one playing and show the same frame; an animation no element draws stops after the frame
    /// it had asked for, keeps its decoder while the cache holds it, and goes on from that frame when an element shows it again
    /// ("Animations in the cache", "When it stops").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 4 frames of 100 ms with no end; two elements of its source; a target that takes the uploads.
    ///
    /// Steps:
    /// - Draw, and step twice (let the decode arrive, advance 100 ms, draw). Dispose both elements; step three times.
    /// - Show the source again and step twice.
    ///
    /// Expected:
    /// - One decode of the image and one upload a step: frames 0, 1, 2 for two elements, which the playing counts until they are
    ///   disposed. With no element, the frame asked for
    ///   before (3) is uploaded once and nothing after it, and the decoder is not disposed. Shown again, the steps upload frames
    ///   0 and 1 of the next play: the decoder went on from where it stopped, and the image was not decoded again.
    /// </remarks>
    [Fact]
    public void AnimationNoElementDraws_Stops_AndGoesOnWhenShownAgain() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var frames = new FakeFrames(0, 100, 100, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Target.TakesUploads = true;
                    ImageSource source = Sized(2, 2);
                    Shown first = scene.Show(source);
                    Shown second = scene.Show(source);
                    posts.Settle();
                    Assert.Equal(2, scene.Draw().Count);
                    Step(scene, posts, 100);
                    Step(scene, posts, 100);
                    Assert.Equal([0, 1, 2], scene.Target.Uploads);
                    Assert.Equal((1, 2), (scene.Cache.Decodes, scene.Cache.ShowingElements));

                    Scope.Dispose(first.Scope);
                    Scope.Dispose(second.Scope);
                    Assert.Equal(0, scene.Cache.ShowingElements);
                    Step(scene, posts, 100);
                    Step(scene, posts, 100);
                    Step(scene, posts, 100);
                    Assert.Equal([0, 1, 2, 3], scene.Target.Uploads);
                    Assert.Equal((false, 3), (frames.Disposed, frames.Calls));

                    _ = scene.Show(source);
                    _ = scene.Draw();
                    Step(scene, posts, 100);
                    Step(scene, posts, 100);
                    Assert.Equal([0, 1, 2, 3, 0, 1], scene.Target.Uploads);
                    Assert.Equal(1, scene.Cache.Decodes);
                },
                posts,
                (_, _, _) => frames.First()
            );
        });

    /// <summary>
    /// With less motion asked for, an animation shows its first frame and asks for no next one; when the setting comes in the
    /// middle, it stays on the frame it shows, and when the setting goes, it goes on from that frame ("When it stops").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 3 frames of 100 ms with no end, shown by one element; a target that takes the uploads.
    ///
    /// Steps:
    /// - Ask for less motion; draw; advance the clock by 1 s and draw. Take the setting away, run the frame it asks for, and
    ///   the one the next frame of the animation asks for 100 ms later.
    /// - Let the next decode arrive, ask for less motion, advance by 100 ms, and draw. Take the setting away and draw.
    ///
    /// Expected:
    /// - With the setting from the start, only frame 0 is uploaded and the frames' decoder is not asked. Taking it away asks
    ///   for one frame of the window, after which the decoder is asked once, and the frame 100 ms later uploads frame 1. With the setting again, the frame that was due is not uploaded; without it, the next draw uploads it
    ///   (frame 2).
    /// </remarks>
    [Fact]
    public void LessMotion_StopsOnTheFrameThatShows_AndGoesOnWithoutIt() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var frames = new FakeFrames(0, 100, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Target.TakesUploads = true;
                    scene.Tree.Animations.ReduceMotion = true;
                    _ = scene.Show(Sized(2, 2));
                    posts.Settle();
                    _ = scene.Draw();
                    scene.Time.Advance(TimeSpan.FromSeconds(1));
                    _ = scene.Draw();
                    Assert.Equal([0], scene.Target.Uploads);
                    Assert.Equal(0, frames.Calls);

                    // The setting's going asks for a frame, which draws the image and so asks for the next frame of the animation.
                    scene.Scheduler.Requests = 0;
                    scene.Tree.Animations.ReduceMotion = false;
                    Assert.Equal(1, scene.Scheduler.Requests);
                    scene.Scheduler.Run();
                    posts.Settle();
                    Assert.Equal(1, frames.Calls);
                    scene.Time.Advance(TimeSpan.FromMilliseconds(100));
                    scene.Scheduler.Run();
                    Assert.Equal([0, 1], scene.Target.Uploads);

                    posts.Settle();
                    scene.Tree.Animations.ReduceMotion = true;
                    scene.Time.Advance(TimeSpan.FromMilliseconds(100));
                    _ = scene.Draw();
                    Assert.Equal([0, 1], scene.Target.Uploads);

                    scene.Tree.Animations.ReduceMotion = false;
                    _ = scene.Draw();
                    Assert.Equal([0, 1, 2], scene.Target.Uploads);
                },
                posts,
                (_, _, _) => frames.First()
            );
        });

    /// <summary>
    /// An animation that played its plays stops on its last frame, lets its decoder go, and counts as a still image from then on;
    /// until then it counts for what it holds ("The bytes counted", "Animations in the cache").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 2 frames of 100 ms that plays once, 2 x 2 pixels, whose decoder holds 1,000 bytes and whose decode read
    ///   300 bytes of file; one element; a target that takes the uploads.
    ///
    /// Steps:
    /// - Draw and read the bytes counted. Step twice, then advance the clock by 1 s and draw.
    ///
    /// Expected:
    /// - Playing, the entry counts 2 x 2 x 4 x 2 + 1,000 + 300 = 1,332 bytes. The steps upload frame 1 and then nothing: the
    ///   second finds the end, disposes the decoder, and the entry counts 16 bytes; the window's images let go of the buffer the
    ///   frames were uploaded from. Nothing is asked of the clock after it.
    /// </remarks>
    [Fact]
    public void AnimationThatPlayedItsPlays_StopsOnItsLastFrame_AndCountsAsAStillImage() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var frames = new FakeFrames(1, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Target.TakesUploads = true;
                    _ = scene.Show(Sized(2, 2));
                    posts.Settle();
                    ImageHandle image = scene.Draw()[0].Image;
                    Assert.Equal((1, 1332L), (scene.Cache.CountedCount, scene.Cache.CountedBytes));

                    Step(scene, posts, 100);
                    Assert.False(frames.Disposed);
                    Step(scene, posts, 100);
                    Assert.Equal((true, 16L), (frames.Disposed, scene.Cache.CountedBytes));
                    _ = scene.Tree.Images.GetSlot(image.Index, out _, out _, out byte[]? kept);
                    Assert.Null(kept);
                    scene.Time.Advance(TimeSpan.FromSeconds(1));
                    _ = scene.Draw();
                    Assert.Equal([0, 1], scene.Target.Uploads);
                    Assert.Equal((2, 0, 0), (frames.Calls, scene.Tree.Animations.Count, scene.Time.PendingTimers));
                },
                posts,
                (_, _, _) => frames.First(sourceBytes: 300)
            );
        });

    /// <summary>
    /// An animation whose next frame cannot be decoded is replaced by the error part: the decoder and the image go, and the
    /// source is decoded again for an element made later ("Untrusted input").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 3 frames of 100 ms whose second call for a frame gives a broken one; two elements of its source with an
    ///   error part each.
    ///
    /// Steps:
    /// - Draw and step twice. Show the source in a third element.
    ///
    /// Expected:
    /// - After the first step the image shows frame 1. After the second, both elements show their error parts and draw no image,
    ///   the decoder is disposed, the window holds no image and the cache no entry. The third element asks for a second decode.
    /// </remarks>
    [Fact]
    public void AnimationWithABrokenFrame_IsReplacedByTheErrorPart() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var frames = new FakeFrames(0, 100, 100, 100) { BreakAt = 2 };
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Target.TakesUploads = true;
                    ImageSource source = Sized(2, 2);
                    var options = new ImageOptions { Error = () => Box().Width(5).Height(5) };
                    Shown first = scene.Show(source, options);
                    Shown second = scene.Show(source, options);
                    posts.Settle();
                    _ = scene.Draw();
                    Step(scene, posts, 100);
                    Assert.Equal([0, 1], scene.Target.Uploads);

                    posts.Settle();
                    scene.Time.Advance(TimeSpan.FromMilliseconds(100));
                    Assert.Empty(scene.Draw());
                    Assert.False(scene.Tree.Nodes.GetRef(first.Element.Node).FirstChild.IsNone);
                    Assert.False(scene.Tree.Nodes.GetRef(second.Element.Node).FirstChild.IsNone);
                    Assert.Equal((true, 0, 0, 0), (frames.Disposed, scene.Tree.Images.Count, scene.Cache.Count, scene.Cache.CountedCount));

                    frames.BreakAt = 1;
                    _ = scene.Show(source);
                    Assert.Equal(2, scene.Cache.Decodes);
                    posts.Settle();
                },
                posts,
                (_, _, _) => frames.Disposed ? default : frames.First()
            );
        });

    /// <summary>
    /// The decoder goes when its entry does: an animation no element shows that is evicted, one the tree lets go, and one whose
    /// decode result arrives after its entry went; a worker that is in the decoder ends first ("The decoder's owner and life").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Animations of 2 frames of 100 ms with no end, each in a scene of its own.
    ///
    /// Steps:
    /// - First: draw, let the next decode arrive and its timer ring so that the playing is in the frame clock's list; dispose the
    ///   element and evict the source.
    /// - Second: draw with the next decode held on its worker behind a gate; let every entry go; open the gate and run the result.
    /// - Third: with a cache that keeps no image no element shows, draw, and dispose the element.
    /// - Fourth: let every entry go before the image's decode result is run; run it.
    ///
    /// Expected:
    /// - First: the decoder is disposed at once, and the frame clock's list and the clock's timers are empty.
    /// - Second: the frame's timer is stopped at once; the decoder is not disposed while the worker is in it, and is once the
    ///   result ran.
    /// - Third: the decoder is disposed with its image when the element leaves, and no timer is left.
    /// - Fourth: the result's decoder is disposed, and the window holds no image.
    /// </remarks>
    [Fact]
    public void Decoder_GoesWithItsEntry_AfterTheWorkerThatIsInIt() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var evicted = new FakeFrames(0, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    ImageSource source = Sized(2, 2);
                    Shown shown = scene.Show(source);
                    posts.Settle();
                    _ = scene.Draw();
                    posts.Settle();
                    scene.Time.Advance(TimeSpan.FromMilliseconds(100));
                    Assert.Equal(1, scene.Tree.Animations.Count);

                    Scope.Dispose(shown.Scope);
                    scene.Cache.Evict(source);
                    Assert.Equal((true, 0, 0, 0), (evicted.Disposed, scene.Tree.Animations.Count, scene.Time.PendingTimers, scene.Tree.Images.Count));
                },
                posts,
                (_, _, _) => evicted.First()
            );

            using var gate = new ManualResetEventSlim();
            var held = new FakeFrames(0, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    _ = scene.Show(Sized(2, 2));
                    posts.Settle();
                    held.Gate = gate;
                    _ = scene.Draw();
                    Assert.True(SpinWait.SpinUntil(() => held.Calls == 1, TimeSpan.FromSeconds(30)));

                    Assert.Equal(1, scene.Time.PendingTimers);
                    scene.Cache.ReleaseAll();
                    Assert.Equal((false, 0), (held.Disposed, scene.Time.PendingTimers));
                    gate.Set();
                    posts.Settle();
                    Assert.True(held.Disposed);
                },
                posts,
                (_, _, _) => held.First()
            );

            var left = new FakeFrames(0, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Cache.Limit(0, long.MaxValue);
                    Shown shown = scene.Show(Sized(2, 2));
                    posts.Settle();
                    _ = scene.Draw();
                    posts.Settle();
                    Assert.False(left.Disposed);

                    Scope.Dispose(shown.Scope);
                    Assert.Equal((true, 0, 0), (left.Disposed, scene.Tree.Images.Count, scene.Time.PendingTimers));
                },
                posts,
                (_, _, _) => left.First()
            );

            var late = new FakeFrames(0, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    _ = scene.Show(Sized(2, 2));
                    Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));
                    scene.Cache.ReleaseAll();
                    posts.RunAll();
                    Assert.Equal((true, 0), (late.Disposed, scene.Tree.Images.Count));
                },
                posts,
                (_, _, _) => late.First()
            );
        });

    /// <summary>
    /// Where images do not play, an animation is the still image of its first frame, and its decoder goes at once: a tree whose
    /// images do not play (a headless app's default), and an element made where no loop runs ("Headless").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Animations of 2 frames of 100 ms: one in a scene whose tree's images do not play, one in a scene with no
    ///   synchronization context.
    ///
    /// Steps:
    /// - Show each, draw, advance the clock by 1 s, and draw.
    ///
    /// Expected:
    /// - Each decoder is disposed and never asked for a frame; each entry counts 16 bytes; the clock holds no timer.
    /// </remarks>
    [Fact]
    public void WhereImagesDoNotPlay_AnAnimationIsItsFirstFrame() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var switched = new FakeFrames(0, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Tree.AnimatesImages = false;
                    _ = scene.Show(Sized(2, 2));
                    posts.Settle();
                    _ = scene.Draw();
                    scene.Time.Advance(TimeSpan.FromSeconds(1));
                    _ = scene.Draw();
                    Assert.Equal((true, 0, 16L, 0), (switched.Disposed, switched.Calls, scene.Cache.CountedBytes, scene.Time.PendingTimers));
                },
                posts,
                (_, _, _) => switched.First()
            );

            var inline = new FakeFrames(0, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    _ = scene.Show(Sized(2, 2));
                    _ = scene.Draw();
                    scene.Time.Advance(TimeSpan.FromSeconds(1));
                    _ = scene.Draw();
                    Assert.Equal((true, 0, 16L, 0), (inline.Disposed, inline.Calls, scene.Cache.CountedBytes, scene.Time.PendingTimers));
                },
                null,
                (_, _, _) => inline.First()
            );
        });

    /// <summary>
    /// A frame that ended with no renderer having taken an image's upload leaves no upload waiting when the next frame's pixels are
    /// decoded into the same buffer ("The decoder's owner and life").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 2 frames of 100 ms with no end, shown by one element; a target that takes no upload.
    ///
    /// Steps:
    /// - Draw; read the uploads that wait. Step once; read them again.
    ///
    /// Expected:
    /// - After each draw no upload waits, and the next frame was asked for (one call, then two).
    /// </remarks>
    [Fact]
    public void UploadNoRendererTook_IsWithdrawnBeforeTheBufferIsWrittenAgain() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var frames = new FakeFrames(0, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    _ = scene.Show(Sized(2, 2));
                    posts.Settle();
                    _ = scene.Draw();
                    Assert.Equal(0, scene.Tree.Images.PendingUploads.Length);
                    posts.Settle();
                    Assert.Equal(1, frames.Calls);

                    scene.Time.Advance(TimeSpan.FromMilliseconds(100));
                    _ = scene.Draw();
                    Assert.Equal(0, scene.Tree.Images.PendingUploads.Length);
                    posts.Settle();
                    Assert.Equal(2, frames.Calls);
                },
                posts,
                (_, _, _) => frames.First()
            );
        });

    /// <summary>
    /// Playing allocates nothing on the UI thread ("Allocation").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An animation of 3 frames of 100 ms with no end, shown by one element; a target that takes the uploads; 4 steps run to warm
    ///   the lists up.
    ///
    /// Steps:
    /// - Run 60 steps: with the decode arrived and the UI queue's entries taken before the measured part, measure the bytes the
    ///   thread allocates while the timer rings (the clock advanced by 100 ms) and the frame is drawn, which shows the next frame
    ///   and asks for the one after it.
    ///
    /// Expected:
    /// - 0 bytes in every step.
    /// </remarks>
    [Fact]
    public void Playing_AllocatesNothingOnTheUiThread() =>
        ui.Invoke(() =>
        {
            var posts = new KeepingContext();
            var frames = new FakeFrames(0, 100, 100, 100);
            WithDecode(
                () =>
                {
                    using var scene = new ImageScene();
                    scene.Target.TakesUploads = true;
                    _ = scene.Show(Sized(2, 2));
                    posts.Settle();
                    _ = scene.Draw();
                    for (int i = 0; i < 4; i++)
                    {
                        Step(scene, posts, 100);
                    }

                    scene.Target.Uploads.Clear();
                    scene.Target.Uploads.Capacity = 128;
                    long most = 0;
                    for (int i = 0; i < 60; i++)
                    {
                        posts.Settle();
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        scene.Time.Advance(TimeSpan.FromMilliseconds(100));
                        scene.Frames.RunFrame();
                        most = Math.Max(most, GC.GetAllocatedBytesForCurrentThread() - before);
                    }

                    Assert.Equal((0L, 60), (most, scene.Target.Uploads.Count));
                },
                posts,
                (_, _, _) => frames.First()
            );
        });

    // One step of an animation: the decode asked for arrives, the clock advances, and the window draws.
    private static void Step(ImageScene scene, KeepingContext posts, int milliseconds)
    {
        posts.Settle();
        scene.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        _ = scene.Draw();
    }
}
