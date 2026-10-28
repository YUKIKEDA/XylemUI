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
    /// A button takes the focus: Tab stops at it, and a press on its content gives it the focus (row K1a).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 holding a row of two buttons of boxes of 40 x 20; the window is active.
    ///
    /// Steps:
    /// - Send Tab; then press and release the left button at (50, 10), on the second button's content.
    ///
    /// Expected:
    /// - Tab focuses the first button and is handled; the press focuses the second.
    /// </remarks>
    [Fact]
    public void Button_TakesTheFocus_FromTabAndAPress() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Element first = default;
            Element second = default;
            scene.Mount(() => Row(first = Button(Box().Width(40).Height(20), () => { }), second = Button(Box().Width(40).Height(20), () => { })));
            scene.Input.SetActive(true);

            Assert.True(scene.Key(Key.Tab));
            Assert.Equal(first.Node, scene.Tree.Focus.Focused);
            scene.Send(PointerInputKind.Down, 50, 10, PointerButtons.Left);
            scene.Send(PointerInputKind.Up, 50, 10, PointerButtons.None);
            Assert.Equal(second.Node, scene.Tree.Focus.Focused);
        });

    /// <summary>
    /// The chain's focus and keys: a box takes no focus until it is made focusable; <c>Focus</c> gives it the focus, <c>IsFocused</c>
    /// follows it, and the key and focus handlers of the chain run in the scope they were added in.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window of 200 x 100 holding a column of a plain box and a focusable box with a key-down handler and a focus-in handler that
    ///   record the current scope; the window is active.
    ///
    /// Steps:
    /// - Call Focus on the plain box, then on the focusable box; read IsFocused; send a down of A.
    ///
    /// Expected:
    /// - False for the plain box, true for the other; IsFocused true; both handlers ran once, in the window's root scope.
    /// </remarks>
    [Fact]
    public void Chain_FocusAndKeyHandlers_RunInTheirScope() =>
        ui.Invoke(() =>
        {
            using var scene = new Scene();
            Element plain = default;
            Element box = default;
            var scopes = new List<ScopeHandle>();
            scene.Mount(() =>
            {
                plain = Box().Width(40).Height(20);
                box = Box()
                    .Width(40)
                    .Height(20)
                    .Focusable()
                    .On(KeyEventKind.Down, EventPhase.Bubble, (in KeyEvent _) => scopes.Add(Scope.Current))
                    .On(FocusEventKind.FocusIn, EventPhase.Bubble, (in FocusEvent _) => scopes.Add(Scope.Current));
                return Column(plain, box);
            });
            scene.Input.SetActive(true);

            Assert.False(plain.Focus());
            Assert.True(box.Focus());
            Assert.True(box.IsFocused().Get());
            _ = scene.Key(Key.A);
            Assert.Equal([scene.RootScope, scene.RootScope], scopes);
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
        private ScopeHandle _scope;

        public Scene()
        {
            Tree = new UiTree();
            _root = Tree.CreateNode(new LayoutStyle());
            _frames = new WindowFrames(Tree, _root, new NoTarget(), _atlas, new NoScheduler(), Gray);
            Input = new WindowInput(Tree, _frames, new NoPlatform());
        }

        public UiTree Tree { get; }

        public WindowInput Input { get; }

        public ScopeHandle RootScope => _scope;

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
            Input.Handle(new PointerInput(kind, x, y, PointerButton.Left, buttons, KeyModifiers.None, 1, TimeSpan.Zero, PointerType.Mouse));

        public bool Key(Key key) =>
            Input.Handle(new KeyInput(KeyEventKind.Down, key, PhysicalKey.None, default, KeyModifiers.None, false, TimeSpan.Zero));

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
