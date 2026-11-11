using System.Collections.Concurrent;
using System.Numerics;
using XylemUI.Imaging;
using XylemUI.Testing;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Tests.FrameDoubles;

namespace XylemUI.Tests;

// The stand-ins of the image elements' tests, which run no GPU and no decoder: a test decode whose image's bytes are its width and
// its height (no bytes is an image that cannot be read), a context that keeps the decodes' results until the test runs them, and a
// window with no text.
internal static class ImageDoubles
{
    private static readonly Color Gray = Color.FromRgb(0x808080);
    private static int _decodes;

    // The decodes the test decode ran since WithDecode started.
    public static int Decodes => Volatile.Read(ref _decodes);

    // The size the test decode was last asked for.
    public static (int Width, int Height) Asked { get; private set; }

    public static ImageSource Broken { get; } = ImageSource.FromBytes(Array.Empty<byte>());

    // A source of its own: an equal one is made from the same array.
    public static ImageSource Sized(int width, int height) => ImageSource.FromBytes(new byte[] { (byte)width, (byte)height });

    // The test decode: an image of the size the source's two bytes give, at the size asked for as the decoders make it.
    public static ImageResult DecodeSized(ImageSource source, int width, int height)
    {
        _ = Interlocked.Increment(ref _decodes);
        Asked = (width, height);
        ReadOnlyMemory<byte> bytes = source.Read()!.Value;
        if (bytes.Length < 2)
        {
            return default;
        }

        (int w, int h) = ImageDecoder.TargetSize(bytes.Span[0], bytes.Span[1], width, height, TextureCache.MaxSide);
        return new ImageResult(new byte[w * h * 4], w, h);
    }

    // Runs `test` with a decode in place of the decoders (the test decode unless one is given) and with `context` as the thread's
    // SynchronizationContext: with none, an element decodes as it is made. Both are put back after.
    public static void WithDecode(Action test, SynchronizationContext? context = null, Func<ImageSource, int, int, ImageResult>? decode = null)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        _decodes = 0;
        ImageDecodeQueue.DecodeForTests = decode ?? DecodeSized;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            test();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            ImageDecodeQueue.DecodeForTests = null;
        }
    }

    // The image commands of a display list, in order.
    public static List<ImageCommand> Images(DisplayList list)
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

    // Keeps what is posted to it, from any thread, until RunAll runs it on the calling thread.
    internal sealed class KeepingContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        public void RunAll()
        {
            while (_posted.TryDequeue(out (SendOrPostCallback Callback, object? State) post))
            {
                post.Callback(post.State);
            }
        }

        // Waits until no decode is in flight, then runs what the decodes posted.
        public void Settle()
        {
            Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));
            RunAll();
        }
    }

    // The test decode behind a gate: each decode is counted as started, then waits on its worker until the test opens the gate. A
    // test that needs a decode to be running, or the workers to be busy, waits for the count.
    internal sealed class GatedDecode : IDisposable
    {
        private readonly ManualResetEventSlim _gate = new();
        private int _started;

        // The sources the decodes were asked for.
        public ConcurrentBag<ImageSource> Seen { get; } = [];

        public ImageResult Decode(ImageSource source, int width, int height)
        {
            Seen.Add(source);
            _ = Interlocked.Increment(ref _started);
            _ = _gate.Wait(TimeSpan.FromSeconds(30));
            return DecodeSized(source, width, height);
        }

        // Waits until at least `count` decodes started.
        public void WaitForStarted(int count) =>
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref _started) >= count, TimeSpan.FromSeconds(30)));

        public void Open() => _gate.Set();

        // Opens the gate and waits for the workers to leave it, so that none waits on a disposed gate after a failed test.
        public void Dispose()
        {
            _gate.Set();
            _ = SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30));
            _gate.Dispose();
        }
    }

    // The frames of an animation in place of a decoder: frame i fills its pixels with the byte i and shows for durations[i]
    // milliseconds; after `plays` plays (0 for no end) the frames end. The decode that gives it gives frame 0.
    internal sealed class FakeFrames(int plays, params int[] durations) : IAnimationFrames
    {
        private int _index;
        private int _played;

        // The calls of Next so far, and the call that gives a broken frame (0 for none).
        public int Calls { get; private set; }

        public int BreakAt { get; set; }

        public bool Disposed { get; private set; }

        // A gate the next call waits behind on its worker, when set.
        public ManualResetEventSlim? Gate { get; set; }

        public long HeldBytes => 1000;

        public AnimationStep Next(Span<byte> pixels, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            Assert.False(Disposed);
            Calls++;
            _ = Gate?.Wait(TimeSpan.FromSeconds(30));
            if (Calls == BreakAt)
            {
                return AnimationStep.Broken;
            }

            if (++_index == durations.Length)
            {
                _index = 0;
                if (plays != 0 && ++_played >= plays)
                {
                    return AnimationStep.End;
                }
            }

            pixels.Fill((byte)_index);
            duration = TimeSpan.FromMilliseconds(durations[_index]);
            return AnimationStep.Frame;
        }

        public void Dispose()
        {
            Assert.False(Disposed);
            Disposed = true;
        }

        // The result of the decode of the animation's image: frame 0, of 2 x 2 pixels, with the frames after it.
        public ImageResult First(long sourceBytes = 0) =>
            new(new byte[16], 2, 2)
            {
                Frames = this,
                Duration = TimeSpan.FromMilliseconds(durations[0]),
                SourceBytes = sourceBytes,
            };
    }

    // An element a test added to a scene, in a scope of its own: disposing the scope takes the element away.
    internal readonly record struct Shown(ScopeHandle Scope, Element Element);

    // A window of 200 logical pixels by `height` with no text, whose root lines its children up at the start.
    internal sealed class ImageScene : IDisposable
    {
        private readonly List<ScopeHandle> _scopes = [];
        private readonly NodeHandle _root;
        private readonly float _height;

        public ImageScene(float height = 100)
        {
            _height = height;
            _root = Tree.CreateNode(
                new LayoutStyle { Algorithm = new LayoutAlgorithm.Flex(new FlexProps { AlignItems = new AlignItems(AlignItemsKeyword.Start) }) }
            );
            Frames = new WindowFrames(Tree, _root, Target, Atlas, Scheduler, Gray, time: Time);
            Input = new WindowInput(Tree, Frames, new NoPlatform());
        }

        public UiTree Tree { get; } = new();

        public GlyphAtlas Atlas { get; } = new(new NoGlyphs());

        public CountingTarget Target { get; } = new();

        public CountingScheduler Scheduler { get; } = new();

        // The clock of the frames and of the tree's timers.
        public TestTime Time { get; } = new();

        public WindowFrames Frames { get; }

        public WindowInput Input { get; }

        public WindowImageCache Cache => Tree.CachedImages;

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
                        Tree.AppendChild(_root, element.Node);
                    }
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            _scopes.Add(RootScope);
            Frames.Resize(new Size(200, _height), 1);
        }

        // Adds an image element to the root, in a scope of its own.
        public Shown Show(ImageSource source, ImageOptions? options = null) => Show(() => options is null ? Image(source) : Image(source, options));

        // Adds the element `make` returns to the root, in a scope of its own.
        public Shown Show(Func<Element> make)
        {
            Element element = default;
            ScopeHandle scope;
            UiTree.Current = Tree;
            try
            {
                scope = Scope.Create(() =>
                {
                    element = make();
                    Tree.AppendChild(_root, element.Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            _scopes.Add(scope);
            return new Shown(scope, element);
        }

        // Draws the window (its first frame, or the next) and returns the images of its list.
        public List<ImageCommand> Draw()
        {
            Frames.Resize(new Size(200, _height), 1);
            Frames.RunFrame();
            return Images(Frames.List);
        }

        public void Wheel(int x, int y, Vector2 delta) =>
            Input.Handle(
                new PointerInput(
                    PointerInputKind.Wheel,
                    x,
                    y,
                    PointerButton.None,
                    PointerButtons.None,
                    KeyModifiers.None,
                    0,
                    TimeSpan.Zero,
                    PointerType.Mouse,
                    delta
                )
            );

        public void Dispose()
        {
            foreach (ScopeHandle scope in _scopes)
            {
                if (Scope.IsAlive(scope))
                {
                    Scope.Dispose(scope);
                }
            }

            Atlas.Dispose();
        }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
