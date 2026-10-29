using XylemUI.Text;

namespace XylemUI.Tests;

// The stand-ins a window's frames are made with in the tests that run no GPU: a target that counts its presents, a scheduler
// that runs the frame it holds when asked, and a rasterizer for trees that draw no text.
internal static class FrameDoubles
{
    internal sealed class CountingTarget : IRenderTarget
    {
        public int Presents { get; private set; }

        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() => Presents++;
    }

    // Holds the one callback a frame scheduler holds, and runs it when asked.
    internal sealed class CountingScheduler : IFrameScheduler
    {
        private Action? _callback;

        public int Requests { get; set; }

        // Whether a frame is asked for and has not run.
        public bool Pending => _callback is not null;

        public void RequestFrame(Action callback)
        {
            Requests++;
            _callback = callback;
        }

        public void CancelFrame() => _callback = null;

        public void Run()
        {
            Action? callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }

    // The trees here draw no text, so no glyph is asked for.
    internal sealed class NoGlyphs : IGlyphRasterizer
    {
        public GlyphBox Measure(in GlyphKey glyph) => throw new InvalidOperationException("No glyph is drawn in these tests.");

        public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) =>
            throw new InvalidOperationException("No glyph is drawn in these tests.");
    }
}
