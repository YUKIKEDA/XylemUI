using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Primitives.Tests;

// The headless button (row V1d, design §2.13 and §13.1): a box that holds its content, takes clicks, and has the pressed
// pseudo-state, with no look of its own. The button of a label is tested with the counter sample in XylemUI.Desktop.Tests, whose
// text needs HarfBuzz.
[Collection(UiThreadGroup.Name)]
public sealed class ButtonTests(UiThreadFixture ui)
{
    private static readonly Color Gray = Color.FromRgb(0x808080);

    /// <summary>
    /// A button is a box that holds the content and has no look: no background, no border's paint, and no color of text.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100.
    ///
    /// Steps:
    /// - Mount a button of a box of 40 x 20.
    ///
    /// Expected:
    /// - The button's one child is the box; it has no look, or one with no background, border paint, or foreground; it writes a
    ///   hit region.
    /// </remarks>
    [Fact]
    public void Button_HoldsTheContent_AndHasNoLook() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Element content = default;
            Element button = default;
            scene.Mount(() => button = Button(content = Box().Width(40).Height(20), () => { }));
            ref UiNode node = ref scene.Tree.Nodes.GetRef(button.Node);
            Assert.Equal(content.Node, node.FirstChild);
            Assert.True(scene.Tree.Nodes.GetRef(content.Node).NextSibling.IsNone);
            Assert.True(node.Visual is null || (node.Visual.Background is null && node.Visual.BorderPaint is null && node.Visual.Foreground is null));
            Assert.True(node.Input!.WritesHitRegion);
        });

    /// <summary>
    /// A click on the button's content runs the button's action, and the button is pressed while the left button is down on it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 holding a button of a box of 40 x 20 at the top left, whose action counts.
    ///
    /// Steps:
    /// - Press the left button at (10, 10); read the pseudo-state; release.
    ///
    /// Expected:
    /// - Pressed while down, not after; the action ran once.
    /// </remarks>
    [Fact]
    public void Button_Click_RunsTheAction_AndPressesWhileDown() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            int clicks = 0;
            Element button = default;
            scene.Mount(() => button = Button(Box().Width(40).Height(20), () => clicks++));

            scene.Send(PointerInputKind.Down, 10, 10, PointerButtons.Left);
            Assert.True((scene.Tree.Nodes.GetRef(button.Node).Pseudo & PseudoState.Pressed) != 0);
            scene.Send(PointerInputKind.Up, 10, 10, PointerButtons.None);
            Assert.False((scene.Tree.Nodes.GetRef(button.Node).Pseudo & PseudoState.Pressed) != 0);
            Assert.Equal(1, clicks);
        });

    /// <summary>
    /// A button refuses no action, as the chain's click handler does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window.
    ///
    /// Steps:
    /// - Mount a root function that makes a button of a box with a null action.
    ///
    /// Expected:
    /// - <see cref="ArgumentNullException"/>.
    /// </remarks>
    [Fact]
    public void Button_NullAction_Throws() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Assert.Throws<ArgumentNullException>(() => scene.Mount(() => Button(Box(), null!)));
            Assert.Throws<ArgumentNullException>(() => scene.Mount(() => Button("label", null!)));
        });

    // A window of 200 x 100 logical pixels with no text, as the window's host makes one.
    private sealed class Scene : IDisposable
    {
        private readonly NodeHandle _root;
        private readonly GlyphAtlas _atlas = new(new NoGlyphs());
        private readonly WindowFrames _frames;
        private readonly WindowInput _input;
        private ScopeHandle _scope;

        public Scene()
        {
            Tree = new UiTree();
            _root = Tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(Tree, _root, new NoTarget(), _atlas, new NoScheduler(), Gray);
            _input = new WindowInput(Tree, _frames, new NoPlatform());
        }

        public UiTree Tree { get; }

        public void Mount(Func<Element> root)
        {
            UiTree.Current = Tree;
            try
            {
                _scope = Scope.Create(() => Tree.AppendChild(_root, root().Node));
            }
            finally
            {
                UiTree.Current = null;
            }

            _frames.Resize(new Size(200, 100), 1);
        }

        public void Send(PointerInputKind kind, int x, int y, PointerButtons buttons) =>
            _input.Handle(new PointerInput(kind, x, y, PointerButton.Left, buttons, KeyModifiers.None, 1, TimeSpan.Zero, PointerType.Mouse));

        public void Dispose()
        {
            if (Scope.IsAlive(_scope))
            {
                Scope.Dispose(_scope);
            }

            _atlas.Dispose();
        }
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() { }
    }

    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }

    private sealed class NoGlyphs : IGlyphRasterizer
    {
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn in these tests.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) =>
            throw new InvalidOperationException("No glyph is drawn in these tests.");
    }
}
