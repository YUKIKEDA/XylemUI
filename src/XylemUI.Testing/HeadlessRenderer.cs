using XylemUI.Text;

namespace XylemUI.Testing;

/// <summary>
/// Draws a headless app's frames into pixels (design §12.3, "The plug-in"): a renderer that a package of its own provides, such as
/// <c>XylemUI.Testing.D3D11</c>'s WARP renderer, given to <see cref="HeadlessOptions.Renderer"/>. An app with none lays its frames
/// out and draws nothing.
/// </summary>
/// <remarks>
/// What a renderer hands the app is internal to the packages that ship with this one: the target its frames draw into, the
/// rasterizer of its glyphs, and the read-back of the frame. A renderer belongs to one app, which disposes it.
/// </remarks>
public abstract class HeadlessRenderer : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HeadlessRenderer"/> class; only the packages that ship with this one derive
    /// from it.
    /// </summary>
    private protected HeadlessRenderer() { }

    /// <summary>
    /// Gets the target the app's frames draw into and present to; read after the first <see cref="Resize"/>.
    /// </summary>
    internal abstract IRenderTarget Target { get; }

    /// <summary>
    /// Makes the rasterizer of the glyphs the target draws; the app's glyph atlas owns it.
    /// </summary>
    /// <returns>The rasterizer.</returns>
    internal abstract IGlyphRasterizer CreateRasterizer();

    /// <summary>
    /// Sets the size of the frames, in physical pixels; the app calls it first, right after making the renderer.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    internal abstract void Resize(int width, int height);

    /// <summary>
    /// Reads back the frame drawn last: its colors from the top left, row by row.
    /// </summary>
    /// <param name="width">The width of the frame.</param>
    /// <param name="height">The height of the frame.</param>
    /// <returns>The colors, <paramref name="width"/> a row.</returns>
    internal abstract Color[] Capture(out int width, out int height);

    /// <summary>
    /// Releases what the renderer holds.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases what the renderer holds.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing) { }
}
