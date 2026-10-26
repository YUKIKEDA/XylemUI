using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// What a window's frames draw on (design §5.0, "The draw stage and the frame drive"): the renderer of the window. The D3D11
/// renderer implements it.
/// </summary>
internal interface IRenderTarget
{
    /// <summary>
    /// Draws a display list into the back buffer.
    /// </summary>
    /// <param name="list">The list, its glyphs resolved.</param>
    /// <param name="clearColor">The color the buffer is cleared to first.</param>
    /// <param name="atlas">The atlas the list's glyphs are in.</param>
    void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas);

    /// <summary>
    /// Shows the back buffer.
    /// </summary>
    void Present();
}
