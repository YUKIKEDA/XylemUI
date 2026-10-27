using XylemUI.Rendering.D3D11;
using XylemUI.Text;

namespace XylemUI.Desktop.Tests;

// Draws with the renderer and reads one pixel of each draw, before the present makes the back buffer undefined.
internal sealed class ReadingTarget(D3D11Renderer renderer) : IRenderTarget
{
    public (int X, int Y)? Point { get; set; }

    public Color Pixel { get; private set; }

    public int Draws { get; private set; }

    public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas)
    {
        renderer.Draw(list, clearColor, atlas);
        Draws++;
        if (Point is (int x, int y))
        {
            Pixel = renderer.ReadPixel(x, y);
        }
    }

    public void Present() => renderer.Present();
}
