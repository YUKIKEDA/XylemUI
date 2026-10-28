using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The pointer's input repeated a given number of times, for counting the instructions and cycles of one input with VTune
// (benchmarks/counters/instructions.ps1 -Command input-loop). It uses only what the input of row V1c had, so that the same file
// builds on a branch's base. `wheel` is the wheel of the `input` scene: a scroll container 300 high whose content is 3,000 high,
// alternately down and up by 50 pixels. `move` is a move over a 10 x 10 leaf of the root.
internal static class InputLoop
{
    public static int Run(string kind, int count)
    {
        UiThread.Initialize();
        using var atlas = new GlyphAtlas(new NoGlyphs());
        var tree = new UiTree();
        NodeHandle root = tree.CreateNode(new LayoutStyle { OverflowY = kind == "wheel" ? Overflow.Scroll : Overflow.Visible });
        NodeHandle content = tree.CreateNode(new LayoutStyle { Height = Length.Px(3_000) });
        NodeHandle leaf = tree.CreateNode(new LayoutStyle { Width = Length.Px(10), Height = Length.Px(10) });
        tree.AppendChild(root, content);
        tree.AppendChild(content, leaf);
        tree.AddPointerHandler(leaf, PointerEventKind.Move, EventPhase.Bubble, static (in PointerEvent _) => { });
        var frames = new WindowFrames(tree, root, new NoTarget(), atlas, new NoScheduler(), default);
        frames.Resize(new Size(400, 300), 1);
        var input = new WindowInput(tree, frames, new NoPlatform());
        for (int i = 0; i < count; i++)
        {
            if (kind == "wheel")
            {
                var delta = new System.Numerics.Vector2(0, (i & 1) == 0 ? 50 : -50);
                input.Handle(new PointerInput(PointerInputKind.Wheel, 5, 5, PointerButton.None, 0, 0, 0, TimeSpan.Zero, PointerType.Mouse, delta));
            }
            else
            {
                input.Handle(new PointerInput(PointerInputKind.Move, 5, 5, PointerButton.None, 0, 0, 0, TimeSpan.Zero, PointerType.Mouse));
            }
        }

        return tree.ScrollOffsetOf(root).Y == 0 ? 0 : 1;
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
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) => throw new InvalidOperationException("No glyph is drawn.");
    }
}
