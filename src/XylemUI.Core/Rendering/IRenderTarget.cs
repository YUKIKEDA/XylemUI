using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// What a window's frames draw on (design §5.0, "The draw stage and the frame drive"): the renderer of the window. The D3D11
/// renderer implements it.
/// </summary>
internal interface IRenderTarget
{
    /// <summary>
    /// Draws display lists into the back buffer, each over the ones before it: the window's list, then its popups' (design §7.3,
    /// "Display lists").
    /// </summary>
    /// <param name="lists">The lists, back to front, their glyphs resolved.</param>
    /// <param name="clearColor">The color the buffer is cleared to first, before the first list.</param>
    /// <param name="atlas">The atlas the lists' glyphs are in.</param>
    void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas);

    /// <summary>
    /// Shows the back buffer.
    /// </summary>
    void Present();
}
