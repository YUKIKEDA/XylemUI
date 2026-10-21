namespace XylemUI.Text;

/// <summary>
/// A rectangle of an atlas page whose pixels changed and must be uploaded to the GPU.
/// </summary>
/// <param name="Page">The page, among the pages of <paramref name="Coverage"/>.</param>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Coverage">The kind of the page.</param>
public readonly record struct AtlasRegion(int Page, int X, int Y, int Width, int Height, GlyphCoverage Coverage = GlyphCoverage.Gray);
