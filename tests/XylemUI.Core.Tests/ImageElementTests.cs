using System.Numerics;
using XylemUI.Imaging;
using XylemUI.Testing;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The image element (design §5.0.4, "API", "Size", "Display", "Screen readers", "Owner and lifetime"; row IM1, 1b-2), on a tree
// with no GPU and no loop: the decode runs where the element asks for it (ImageBlock with no SynchronizationContext), and a test
// decode stands in for the decoders. An image's bytes here are its width and its height; no bytes is an image that cannot be read.
[Collection(UiThreadGroup.Name)]
public sealed class ImageElementTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static int _decodes;
    private static (int Width, int Height) _asked;

    /// <summary>
    /// A box given no size takes the image's pixels as logical pixels; given its width, its height follows the image's ratio; given
    /// both, both; and a box whose image could not be read takes the error part's size, or 0 with none.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 at scale 1 whose root lines its children up at the start; the test decode.
    ///
    /// Steps:
    /// - Mount, each in the root (which does not stretch them): an image of 40 x 20; the same with the width 80; the same with the width 30 and the height 30; an image
    ///   that cannot be read with an error part of 12 x 7; one with none.
    ///
    /// Expected:
    /// - The boxes are 40 x 20, 80 x 40, 30 x 30, 12 x 7, and 0 x 0; the window holds three images.
    /// </remarks>
    [Fact]
    public void Size_FollowsTheImage_OrTheErrorPart() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new Scene();
                Element[] made = new Element[5];
                scene.MountEach(() =>
                    [
                        made[0] = Image(Sized(40, 20)),
                        made[1] = Image(Sized(40, 20)).Width(80),
                        made[2] = Image(Sized(40, 20)).Width(30).Height(30),
                        made[3] = Image(Broken, new ImageOptions { Error = () => Box().Width(12).Height(7) }),
                        made[4] = Image(Broken),
                    ]
                );

                Assert.Equal(
                    [(40f, 20f), (80f, 40f), (30f, 30f), (12f, 7f), (0f, 0f)],
                    made.Select(e => (scene.Tree.GetRect(e.Node).Width, scene.Tree.GetRect(e.Node).Height))
                );
                Assert.Equal(3, scene.Tree.Images.Count);
            })
        );

    /// <summary>
    /// The frame draws the image as its fit asks, inside the box less its padding: Contain centers the whole image, Cover draws the
    /// box with the part that covers it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window above; images of 40 x 20 in boxes of 60 x 60 with padding 5 (a content box of 50 x 50 at (5, 5) in the box).
    ///
    /// Steps:
    /// - Mount two such boxes in a row: Contain, then Cover. Read the frame's image commands.
    ///
    /// Expected:
    /// - Contain: (5, 17.5, 50, 25), its edges on the nearest pixels as a rectangle's (17.5 to 18, 42.5 to 42): (5, 18, 50, 24), the
    ///   whole image. Cover: (65, 5, 50, 50), the part (10, 0, 20, 20).
    /// </remarks>
    [Fact]
    public void Draw_FollowsTheFit_InTheContentBox() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new Scene();
                scene.Mount(() =>
                    Row(
                        Image(Sized(40, 20)).Width(60).Height(60).Padding(5),
                        Image(Sized(40, 20), new ImageOptions { Fit = ImageFit.Cover }).Width(60).Height(60).Padding(5)
                    )
                );

                List<ImageCommand> images = Images(scene.Frames.List);
                Assert.Equal(2, images.Count);
                Assert.Equal((5f, 18f, 50f, 24f, new Vector4(0, 0, 40, 20)), Of(images[0]));
                Assert.Equal((65f, 5f, 50f, 50f, new Vector4(10, 0, 20, 20)), Of(images[1]));
            })
        );

    /// <summary>
    /// A source that changes lets the image shown go and reads the new one; a source that changes to an equal one reads nothing; the
    /// element's scope disposed lets its image go.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window above; a signal of a source of 40 x 20; the test decode, counting its calls.
    ///
    /// Steps:
    /// - Mount an image of the signal's source. Set the signal to another source of 10 x 10; to an equal source of 10 x 10 (other
    ///   bytes, the same memory). Dispose the root scope.
    ///
    /// Expected:
    /// - Mounted: one decode, one image, the box 40 x 20. After the change: two decodes, one image (the first released), the box
    ///   10 x 10. After the equal source: still two decodes. After the dispose: no image.
    /// </remarks>
    [Fact]
    public void Source_Changes_LetTheOldImageGo() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new Scene();
                byte[] small = [10, 10];

                // Compared by reference, so that the equal source reaches the element, which compares the values itself.
                var source = new Signal<ImageSource>(Sized(40, 20), ReferenceEqualityComparer.Instance);
                Element image = default;
                scene.Mount(() => Row(image = Image(() => source.Get())));
                ImageHandle first = Shown(image);
                Assert.Equal((1, 1), (_decodes, scene.Tree.Images.Count));

                source.Set(ImageSource.FromBytes(small));
                scene.Frames.RunFrame();

                Assert.Equal((2, 1), (_decodes, scene.Tree.Images.Count));
                Assert.False(scene.Tree.Images.IsAlive(first));
                Assert.Equal((10f, 10f), (scene.Tree.GetRect(image.Node).Width, scene.Tree.GetRect(image.Node).Height));

                source.Set(ImageSource.FromBytes(small));
                Assert.Equal(2, _decodes);

                Scope.Dispose(scene.RootScope);
                Assert.Equal(0, scene.Tree.Images.Count);
            })
        );

    /// <summary>
    /// While its decode runs on the thread pool, an image shows its loading part at the part's size; the result, posted to the UI
    /// thread, replaces it with the image. An element disposed before its result arrives adds no image.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window above; the test decode; a context that keeps what is posted to it until the test runs it.
    ///
    /// Steps:
    /// - Mount an image of 40 x 20 whose loading part is a 15 x 5 box, and a second image; wait until no decode is in flight. Dispose
    ///   the second's scope. Run what was posted; run a frame.
    ///
    /// Expected:
    /// - Before the posts run: the first element's child is the loading part, 15 x 5, and the window holds no image.
    /// - After: the first element has no child and is 40 x 20; the window holds one image (the disposed element's result dropped).
    /// </remarks>
    [Fact]
    public void Loading_ShowsThePart_UntilThePostedResultArrives() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                var posts = new KeepingContext();
                SynchronizationContext.SetSynchronizationContext(posts);
                using var scene = new Scene();
                Element first = default;
                Element loading = default;
                ScopeHandle second = default;
                scene.MountEach(() =>
                {
                    Element inner = default;
                    second = Scope.Create(() => inner = Image(Sized(10, 10)));
                    first = Image(Sized(40, 20), new ImageOptions { Loading = () => loading = Box().Width(15).Height(5) });
                    return [first, Box(inner)];
                });
                Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));

                Assert.Equal(first.Node, scene.Tree.Nodes.GetRef(loading.Node).Parent);
                Rect part = scene.Tree.GetRect(loading.Node);
                Assert.Equal((15f, 5f, 0), (part.Width, part.Height, scene.Tree.Images.Count));

                Scope.Dispose(second);
                posts.RunAll();
                scene.Frames.RunFrame();

                Rect image = scene.Tree.GetRect(first.Node);
                Assert.True(scene.Tree.Nodes.GetRef(first.Node).FirstChild.IsNone);
                Assert.Equal((40f, 20f, 1), (image.Width, image.Height, scene.Tree.Images.Count));
            })
        );

    /// <summary>
    /// The decode is asked for the size the options give, in the image's pixels.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window above; the test decode, keeping the size it was asked for.
    ///
    /// Steps:
    /// - Mount an image of 40 x 20 with DecodeWidth 10.
    ///
    /// Expected:
    /// - The decode was asked for 10 x 0; the image is 10 x 5, and so is its box.
    /// </remarks>
    [Fact]
    public void DecodeWidth_IsAskedOfTheDecode() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new Scene();
                Element image = default;
                scene.Mount(() => Row(image = Image(Sized(40, 20), new ImageOptions { DecodeWidth = 10 })));

                Assert.Equal((10, 0), _asked);
                Assert.Equal((10f, 5f), (scene.Tree.GetRect(image.Node).Width, scene.Tree.GetRect(image.Node).Height));
            })
        );

    /// <summary>
    /// An image with a label is an image to a screen reader, named by it; one with none is decoration, left out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window above.
    ///
    /// Steps:
    /// - Mount an image labeled "Logo" and one with no label.
    ///
    /// Expected:
    /// - The first has the role Image and the name "Logo" and is not excluded; the second is excluded.
    /// </remarks>
    [Fact]
    public void Label_MakesAnImageNode_OrNone() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new Scene();
                Element labeled = default;
                Element plain = default;
                scene.Mount(() => Row(labeled = Image(Sized(4, 4), new ImageOptions { Label = "Logo" }), plain = Image(Sized(4, 4))));

                AccessibilityInfo info = scene.Tree.AccessibilityInfoOf(labeled.Node)!;
                Assert.Equal((SemanticsRole.Image, "Logo"), (info.Role, info.Name));
                Assert.False(scene.Tree.IsAccessibilityExcluded(labeled.Node));
                Assert.True(scene.Tree.IsAccessibilityExcluded(plain.Node));
            })
        );

    /// <summary>
    /// Frames that draw images already shown allocate nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The window above with 10 images of 40 x 20 in a row, shown.
    ///
    /// Steps:
    /// - In the windows of PerformanceAssert.DoesNotAllocate: mark the first image to paint again, and run the frame.
    ///
    /// Expected:
    /// - No allocation; each frame drew the 10 images.
    /// </remarks>
    [Fact]
    public void Frames_WithImages_AllocateNothing() =>
        ui.Invoke(() =>
            WithDecode(() =>
            {
                using var scene = new Scene();
                Element[] made = new Element[10];
                scene.Mount(() =>
                {
                    for (int i = 0; i < made.Length; i++)
                    {
                        made[i] = Image(Sized(40, 20)).Width(18);
                    }

                    return Row(made);
                });

                _ = PerformanceAssert.DoesNotAllocate(
                    200,
                    _ =>
                    {
                        Invalidation.Invalidate(scene.Tree, made[0].Node, ChangeKind.Color);
                        scene.Frames.RunFrame();
                    }
                );
                Assert.Equal(10, Images(scene.Frames.List).Count);
            })
        );

    private static ImageSource Broken { get; } = ImageSource.FromBytes(Array.Empty<byte>());

    private static ImageSource Sized(int width, int height) => ImageSource.FromBytes(new byte[] { (byte)width, (byte)height });

    // Runs `test` with the test decode in place of the decoders, and no SynchronizationContext, so that an element decodes as it is
    // made; both are put back after.
    private static void WithDecode(Action test)
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        _decodes = 0;
        ImageDecodeQueue.DecodeForTests = static (source, width, height) =>
        {
            _decodes++;
            _asked = (width, height);
            ReadOnlyMemory<byte> bytes = source.Read()!.Value;
            if (bytes.Length < 2)
            {
                return default;
            }

            (int w, int h) = ImageDecoder.TargetSize(bytes.Span[0], bytes.Span[1], width, height, TextureCache.MaxSide);
            return new ImageResult(new byte[w * h * 4], w, h);
        };
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            test();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
            ImageDecodeQueue.DecodeForTests = null;
        }
    }

    // Keeps what is posted to it, from any thread, until RunAll runs it on the calling thread.
    private sealed class KeepingContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        public void RunAll()
        {
            while (_posted.TryDequeue(out (SendOrPostCallback Callback, object? State) post))
            {
                post.Callback(post.State);
            }
        }
    }

    private static List<ImageCommand> Images(DisplayList list)
    {
        var images = new List<ImageCommand>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            switch (tag)
            {
                case CommandTag.Image:
                    images.Add(reader.ReadImage());
                    break;
                case CommandTag.FillRect:
                    _ = reader.ReadFillRect();
                    break;
                case CommandTag.PushState:
                    _ = reader.ReadState(out _);
                    break;
                case CommandTag.PushOpacity:
                    _ = reader.ReadGroup();
                    break;
                case CommandTag.Pop:
                    reader.ReadPop();
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected command {tag}.");
            }
        }

        return images;
    }

    private static (float X, float Y, float Width, float Height, Vector4 Part) Of(ImageCommand image) =>
        (image.X, image.Y, image.Width, image.Height, image.Source);

    private static ImageHandle Shown(Element image)
    {
        UiTree tree = image.Tree!;
        for (int i = 0; i < tree.Images.SlotCount; i++)
        {
            if (tree.Images.GetSlot(i, out _, out _, out _) != 0)
            {
                return new ImageHandle(i, tree.Images.GetSlot(i, out _, out _, out _));
            }
        }

        return default;
    }

    // A window of 200 x 100 logical pixels with no text, whose root lines its children up at the start.
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            Tree = new UiTree();
            Root = Tree.CreateNode(
                new LayoutStyle { Algorithm = new LayoutAlgorithm.Flex(new FlexProps { AlignItems = new AlignItems(AlignItemsKeyword.Start) }) }
            );
            Atlas = new GlyphAtlas(new NoGlyphs());
            Frames = new WindowFrames(Tree, Root, new CountingTarget(), Atlas, new CountingScheduler(), Gray);
        }

        public UiTree Tree { get; }

        public NodeHandle Root { get; }

        public GlyphAtlas Atlas { get; }

        public WindowFrames Frames { get; }

        public ScopeHandle RootScope { get; private set; }

        public void Mount(Func<Element> root) => MountEach(() => [root()]);

        // Runs the root function, puts each element it returns in the root, and draws the first frame.
        public void MountEach(Func<Element[]> root)
        {
            UiTree.Current = Tree;
            try
            {
                RootScope = Scope.Create(() =>
                {
                    foreach (Element element in root())
                    {
                        Tree.AppendChild(Root, element.Node);
                    }
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            Frames.Resize(new Size(200, 100), 1);
        }

        public void Dispose()
        {
            if (Scope.IsAlive(RootScope))
            {
                Scope.Dispose(RootScope);
            }

            Atlas.Dispose();
        }
    }
}
