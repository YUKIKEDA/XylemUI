using XylemUI.Platform.Windows;
using XylemUI.Rendering.D3D11;
using XylemUI.Text;

namespace XylemUI.Testing.D3D11;

/// <summary>
/// Draws a headless app's frames on WARP, Direct3D 11's software rasterizer, into a texture with no window or swap chain, and reads
/// them back (design §12.3). Glyphs are drawn with DirectWrite, as in an app's window. Give it to
/// <see cref="HeadlessOptions.Renderer"/> as <c>Renderer = () => new WarpRenderer()</c>.
/// </summary>
/// <remarks>
/// WARP gives the same pixels for the same version of Windows; a golden made on another version may differ (design §5.0.3).
/// </remarks>
public sealed class WarpRenderer : HeadlessRenderer
{
    private D3D11Renderer? _renderer;

    /// <inheritdoc/>
    internal override IRenderTarget Target =>
        _renderer ?? throw new InvalidOperationException("The renderer is given a size before its target is read.");

    /// <inheritdoc/>
    internal override IGlyphRasterizer CreateRasterizer() => new DirectWriteGlyphRasterizer();

    /// <inheritdoc/>
    internal override void Resize(int width, int height)
    {
        if (_renderer is null)
        {
            _renderer = D3D11Renderer.CreateOffscreen(width, height);
        }
        else
        {
            _renderer.Resize(width, height);
        }
    }

    /// <inheritdoc/>
    internal override Color[] Capture(out int width, out int height)
    {
        D3D11Renderer renderer = _renderer ?? throw new InvalidOperationException("The renderer has drawn no frame.");
        width = renderer.Width;
        height = renderer.Height;
        return renderer.CaptureFrame();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer?.Dispose();
            _renderer = null;
        }

        base.Dispose(disposing);
    }
}
